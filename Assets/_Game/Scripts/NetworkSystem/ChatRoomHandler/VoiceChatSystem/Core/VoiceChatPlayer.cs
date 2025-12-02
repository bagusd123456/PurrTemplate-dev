using Concentus;
using Concentus.Structs;
using NyxMachina.Shared.EventFramework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace NyxMachina.Multiplayer
{
    [RequireComponent(typeof(AudioSource))]
    public class VoiceChatPlayer : MonoBehaviour
    {
        private VoiceConfig _config;
        private AudioSource _audioSource;

        // Buffers
        private float[] _audioRingBuffer;
        private int _head, _tail, _bufferCapacity;
        private object _bufferLock = new object();

        // ZERO GC ASYNC
        private Queue<byte[]> _jitterBuffer;
        private object _jitterLock = new object();
        private ArrayPool<byte> _receivePool; // Pool for incoming packets

        private CancellationTokenSource _cts;
        private Task _decodeTask;

        // Visuals
        private struct VolumeFrame { public double TargetDspTime; public float MaxVolume; }
        private VolumeFrame[] _syncFrames;
        private int _syncHead, _syncTail, _syncCapacity = 64;
        private object _syncLock = new object();

        private bool _isInitialized;
        private ulong _clientId;

        // Stats
        private bool _wasSpeaking;
        private float _speakTimer;
        private const float SPEAK_THRESHOLD = 0.001f;
        private const float SPEAK_COOLDOWN = 0.05f;
        private double _bufferLatency;
        private float _lastSample;

        void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            _audioSource.loop = true;
            _audioSource.spatialBlend = 1.0f;
            _audioSource.dopplerLevel = 0f;
        }

        public void Initialize(VoiceConfig config, ulong clientId)
        {
            if (config == null || config.SampleRate <= 0)
                return;

            _clientId = clientId;
            _config = config;

            _bufferCapacity = _config.SampleRate * 2;
            _audioRingBuffer = new float[_bufferCapacity];
            _syncFrames = new VolumeFrame[_syncCapacity];

            // POOL SETUP
            _receivePool = new ArrayPool<byte>(100, 1275);
            _jitterBuffer = new Queue<byte[]>(100);

            _cts = new CancellationTokenSource();
            _decodeTask = Task.Run(() => DecodeWorker(_cts.Token));

            if (_audioSource.clip != null)
                Destroy(_audioSource.clip);

            _lastSample = 0f;
            _audioSource.clip = AudioClip.Create("VoiceStream", _config.SampleRate, 1, _config.SampleRate, true, OnPcmRead);
            
            if (isActiveAndEnabled)
                _audioSource.Play();

            AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
            _bufferLatency = (double)bufferLength / _config.SampleRate;

            _isInitialized = true;
        }

        private void OnDestroy()
        {
            if (_cts != null)
                _cts.Cancel();

            _isInitialized = false;

            if (_audioSource)
                _audioSource.Stop();
        }

        // --- MAIN THREAD (Network Recv) ---
        public void OnVoiceDataReceived(byte[] data, int sequenceId)
        {
            if (!_isInitialized)
                return;

            // RENT Buffer
            byte[] pooledPacket = _receivePool.Rent();

            // Copy Data (data length is variable, copy only what's needed)
            // Store the Length in the first 2 bytes or handle struct.
            // Simplification: We assume data fits in 1275. 
            // We actually need to pass the LENGTH to the worker.
            // A cleaner way is to use a class wrapper, but that's GC.
            // TRICK: Store Length in first 2 bytes of pooledPacket.

            int payloadLen = data.Length;
            pooledPacket[0] = (byte)(payloadLen & 0xFF);
            pooledPacket[1] = (byte)((payloadLen >> 8) & 0xFF);

            Buffer.BlockCopy(data, 0, pooledPacket, 2, payloadLen);

            lock (_jitterLock)
            {
                _jitterBuffer.Enqueue(pooledPacket);
            }
        }

        // --- BACKGROUND DECODER ---
        private void DecodeWorker(CancellationToken token)
        {
            var decoder = (OpusDecoder)OpusCodecFactory.CreateDecoder(_config.SampleRate, _config.Channels);
            short[] decodeBufferShort = new short[_config.FrameSize * _config.Channels];
            const float SHORT_TO_FLOAT = 1.0f / 32768.0f;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    byte[] packet = null;
                    bool hasData = false;

                    lock (_jitterLock)
                    {
                        if (_jitterBuffer.Count > 0)
                        {
                            packet = _jitterBuffer.Dequeue();
                            hasData = true;
                        }
                    }

                    if (hasData)
                    {
                        try
                        {
                            // Read Length header
                            int length = packet[0] | (packet[1] << 8);

                            // Decode (skip first 2 bytes)
                            ReadOnlySpan<byte> payload = packet.AsSpan(2, length);
                            Span<short> output = decodeBufferShort.AsSpan();

                            // Decode using Span
                            int samplesDecoded = decoder.Decode(payload, output, _config.FrameSize, false);
                            WriteToRingBuffer(decodeBufferShort, samplesDecoded, SHORT_TO_FLOAT);
                        }
                        catch { }
                        finally
                        {
                            // RETURN to Pool
                            _receivePool.Return(packet);
                        }
                    }
                    else
                    {
                        Thread.Sleep(2);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private void WriteToRingBuffer(short[] pcmShorts, int samplesPerChannel, float scale)
        {
            int totalSamples = _config.Channels == 2 ? samplesPerChannel * 2 : samplesPerChannel;
            lock (_bufferLock)
            {
                for (int i = 0; i < totalSamples; i++)
                {
                    float val;
                    if (_config.Channels == 2)
                    {
                        if (i % 2 == 0)
                        {
                            val = (pcmShorts[i] * scale + pcmShorts[i + 1] * scale) * 0.5f;
                            i++;
                        }
                        else
                            continue;
                    }
                    else
                        val = pcmShorts[i] * scale;

                    int nextHead = (_head + 1) % _bufferCapacity;
                    if (nextHead == _tail) _tail = (_tail + 1) % _bufferCapacity;
                    _audioRingBuffer[_head] = val;
                    _head = nextHead;
                }
            }
        }

        // --- AUDIO THREAD ---
        private void OnPcmRead(float[] data)
        {
            if (!_isInitialized)
                return;
            float framePeak = 0f;
            int length = data.Length;

            lock (_bufferLock)
            {
                for (int i = 0; i < length; i++)
                {
                    if (_head != _tail)
                    {
                        float sample = _audioRingBuffer[_tail];
                        _tail = (_tail + 1) % _bufferCapacity;
                        data[i] = sample;
                        _lastSample = sample;
                        float abs = sample > 0 ? sample : -sample;
                        if (abs > framePeak) framePeak = abs;
                    }
                    else
                    {
                        _lastSample *= 0.95f;
                        if (_lastSample < 1e-5f && _lastSample > -1e-5f) _lastSample = 0f;
                        data[i] = _lastSample;
                    }
                }
            }

            lock (_syncLock)
            {
                int nextHead = (_syncHead + 1) % _syncCapacity;
                if (nextHead != _syncTail)
                {
                    _syncFrames[_syncHead] = new VolumeFrame
                    {
                        TargetDspTime = AudioSettings.dspTime + _bufferLatency,
                        MaxVolume = framePeak
                    };
                    _syncHead = nextHead;
                }
            }
        }

        private void Update()
        {
            if (_isInitialized)
                ProcessSyncQueue();
        }

        private void ProcessSyncQueue()
        {
            double currentTime = AudioSettings.dspTime;
            float currentFrameVolume = 0f;
            bool hasNewData = false;
            lock (_syncLock)
            {
                while (_syncTail != _syncHead)
                {
                    VolumeFrame frame = _syncFrames[_syncTail];
                    if (frame.TargetDspTime <= currentTime)
                    {
                        if (frame.MaxVolume > currentFrameVolume) currentFrameVolume = frame.MaxVolume;
                        hasNewData = true;
                        _syncTail = (_syncTail + 1) % _syncCapacity;
                    }
                    else break;
                }
            }
            if (hasNewData)
                HandleSpeakingEvent(currentFrameVolume);
        }

        private void HandleSpeakingEvent(float volume)
        {
            bool isTechnicallySpeaking = volume > SPEAK_THRESHOLD;
            if (isTechnicallySpeaking)
                _speakTimer = SPEAK_COOLDOWN;
            else _speakTimer -= Time.deltaTime;
            bool isVisuallySpeaking = _speakTimer > 0;
            if (isVisuallySpeaking != _wasSpeaking)
            {
                _wasSpeaking = isVisuallySpeaking;
                EVENT.Publish(new VoiceChatEvent.OnPlayerTalk(_clientId, isVisuallySpeaking, volume));
            }
        }

        public void StopPlayer()
        {
            if (_audioSource)
            {
                _audioSource.Stop();
                _wasSpeaking = false;
            }
        }

        public void StartPlayer()
        {
            if (_audioSource)
                _audioSource.Play();
        }

        public void SetVolume(float v)
        {
            if (_audioSource)
                _audioSource.volume = v;
        }

        public void SetMute(bool m)
        {
            if (_audioSource)
                _audioSource.mute = m;
        }
    }
}