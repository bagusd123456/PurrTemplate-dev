using Concentus;
using Concentus.Structs;
using NyxMachina.Shared.EventFramework;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class VoiceChatPlayer : MonoBehaviour
{
    private VoiceConfig _config;
    private AudioSource _audioSource;
    
    // Buffers
    private float[] _audioRingBuffer;
    private int _head, _tail, _bufferCapacity;
    private object _bufferLock = new object();
    
    // Async
    private ConcurrentQueue<byte[]> _jitterBuffer;
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
        if (config == null || config.SampleRate <= 0) return;

        _clientId = clientId;
        _config = config;
        
        _bufferCapacity = _config.SampleRate * 2; 
        _audioRingBuffer = new float[_bufferCapacity];

        _syncFrames = new VolumeFrame[_syncCapacity];

        // Store pure byte arrays in buffer (Simpler than struct for batching)
        _jitterBuffer = new ConcurrentQueue<byte[]>();
        _cts = new CancellationTokenSource();
        _decodeTask = Task.Run(() => DecodeWorker(_cts.Token));

        if (_audioSource.clip != null) Destroy(_audioSource.clip);
        
        _lastSample = 0f;
        _audioSource.clip = AudioClip.Create("VoiceStream", _config.SampleRate, 1, _config.SampleRate, true, OnPcmRead);
        _audioSource.Play();

        AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
        _bufferLatency = (double)bufferLength / _config.SampleRate;

        _isInitialized = true;
    }

    private void OnDestroy()
    {
        if (_cts != null) _cts.Cancel();
        _isInitialized = false;
        if(_audioSource) _audioSource.Stop();
    }

    // --- MAIN THREAD (Network Recv) ---
    public void OnVoiceDataReceived(byte[] data, int sequenceId)
    {
        if (!_isInitialized || data == null || data.Length < 2) return;

        // --- BATCH UNPACKING ---
        // Protocol: [Len(2)][Data...][Len(2)][Data...]
        int offset = 0;
        int maxLen = data.Length;

        while (offset < maxLen)
        {
            // Safety: Ensure we can read length
            if (offset + 2 > maxLen) break;

            // Read Length (Little Endian logic from Recorder)
            int frameLen = (data[offset]) | (data[offset + 1] << 8);
            offset += 2;

            // Safety: Ensure we can read data
            if (offset + frameLen > maxLen) break;

            // Extract Frame
            // Optimization: If PurrNet recycles 'data', we MUST copy.
            // If data is fresh every time, we could use ArraySegment, but Queue<byte[]> is safest.
            byte[] frameData = new byte[frameLen];
            Buffer.BlockCopy(data, offset, frameData, 0, frameLen);
            
            _jitterBuffer.Enqueue(frameData);

            offset += frameLen;
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
                if (_jitterBuffer.TryDequeue(out byte[] frameData))
                {
                    try
                    {
                        // Standard Decode (Batching makes FEC sequence tracking harder, 
                        // so we rely on standard PLC (Packet Loss Concealment) from Opus internal logic
                        // or just skip FEC for simplicity in batching mode).
                        int samplesDecoded = decoder.Decode(frameData, 0, frameData.Length, decodeBufferShort, 0, _config.FrameSize, false);
                        WriteToRingBuffer(decodeBufferShort, samplesDecoded, SHORT_TO_FLOAT);
                    }
                    catch { }
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
                    if (i % 2 == 0) // Mix Stereo
                    {
                        val = (pcmShorts[i] * scale + pcmShorts[i + 1] * scale) * 0.5f;
                        i++; 
                    }
                    else continue;
                }
                else val = pcmShorts[i] * scale;

                int nextHead = (_head + 1) % _bufferCapacity;
                if (nextHead == _tail) _tail = (_tail + 1) % _bufferCapacity; // Drop Oldest
                _audioRingBuffer[_head] = val;
                _head = nextHead;
            }
        }
    }

    // --- AUDIO THREAD ---
    private void OnPcmRead(float[] data)
    {
        if (!_isInitialized) return; 
        float framePeak = 0f;
        int len = data.Length;

        lock (_bufferLock)
        {
            for (int i = 0; i < len; i++)
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
                    _lastSample *= 0.95f; // Soft fade
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

    // --- VISUALS ---
    private void Update()
    {
        if (!_isInitialized) return;
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

        if (hasNewData) HandleSpeakingEvent(currentFrameVolume);
    }

    private void HandleSpeakingEvent(float volume)
    {
        bool isTechnicallySpeaking = volume > SPEAK_THRESHOLD;
        if (isTechnicallySpeaking) _speakTimer = SPEAK_COOLDOWN;
        else _speakTimer -= Time.deltaTime;

        bool isVisuallySpeaking = _speakTimer > 0;
        if (isVisuallySpeaking != _wasSpeaking)
        {
            _wasSpeaking = isVisuallySpeaking;
            EVENT.Publish(new VoiceChatEvent.OnPlayerTalk(_clientId, isVisuallySpeaking, volume));
        }
    }
    
    // API
    public void StopPlayer() { if(_audioSource) _audioSource.Stop(); _wasSpeaking=false; }
    public void StartPlayer() { if(_audioSource) _audioSource.Play(); }
    public void SetVolume(float v) { if(_audioSource) _audioSource.volume = v; }
    public void SetMute(bool m) { if(_audioSource) _audioSource.mute = m; }
}