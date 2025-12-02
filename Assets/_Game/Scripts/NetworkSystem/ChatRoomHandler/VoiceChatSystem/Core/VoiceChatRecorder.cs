using Concentus;
using Concentus.Enums;
using Concentus.Structs;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace NyxMachina.Multiplayer
{
    public class VoiceChatRecorder : MonoBehaviour
    {
        [Header("Settings")]
        public KeyCode pushToTalkKey = KeyCode.V;
        public bool usePushToTalk = true;
        [Range(0, 10)] public int OpusComplexity = 2;

        public float CurrentVolume { get; private set; }

        private VoiceConfig _config;

        // --- BUFFERS ---
        private float[] _micBuffer;
        private float[] _frameBufferFloat;
        private AudioRingBuffer _ringBuffer;

        // --- NATIVE (Burst) ---
        private NativeArray<float> _nativeInput;
        private NativeArray<short> _nativeOutput;
        private NativeArray<float> _nativeVolume;

        // --- ZERO GC POOLING ---
        // Queue logic: Standard Queue is Zero-Alloc if capacity is set high enough in ctor
        private Queue<short[]> _encodeQueue;
        private object _queueLock = new object();

        // Pools: Recycle arrays instead of 'new'
        private ArrayPool<short> _pcmPool;  // For passing audio to thread
        private ArrayPool<byte> _packetPool; // For passing packets to network

        // --- THREADING ---
        private CancellationTokenSource _cancellationTokenSource;
        private Task _encodeTask;

        private AudioClip _micClip;
        private string _device;
        private int _lastPos;
        private bool _isRecording;
        private IVoiceNetworkTransport _transport;
        private int _packetSequence = 0;

        private void OnDestroy()
        {
            StopRecording();
            if (_nativeInput.IsCreated) _nativeInput.Dispose();
            if (_nativeOutput.IsCreated) _nativeOutput.Dispose();
            if (_nativeVolume.IsCreated) _nativeVolume.Dispose();
        }

        public void Initialize(IVoiceNetworkTransport transport, VoiceConfig config)
        {
            _transport = transport;
            _config = config;

            int frameSize = _config.FrameSize * _config.Channels;
            int micBufferSize = _config.SampleRate * _config.Channels;

            _frameBufferFloat = new float[frameSize];
            _micBuffer = new float[micBufferSize];
            _ringBuffer = new AudioRingBuffer(micBufferSize);

            // Native
            if (_nativeInput.IsCreated) _nativeInput.Dispose();
            _nativeInput = new NativeArray<float>(frameSize, Allocator.Persistent);

            if (_nativeOutput.IsCreated) _nativeOutput.Dispose();
            _nativeOutput = new NativeArray<short>(frameSize, Allocator.Persistent);

            if (_nativeVolume.IsCreated) _nativeVolume.Dispose();
            _nativeVolume = new NativeArray<float>(1, Allocator.Persistent);

            // --- POOL SETUP ---
            // PcmPool: Holds 50 frames (approx 1 second of buffer)
            _pcmPool = new ArrayPool<short>(50, frameSize);
            // PacketPool: Holds 50 packets (Max MTU size ~1275)
            _packetPool = new ArrayPool<byte>(50, 1275);

            // Pre-allocate Queue capacity to avoid resizing
            _encodeQueue = new Queue<short[]>(64);

            _cancellationTokenSource = new CancellationTokenSource();
            _encodeTask = Task.Run(() => EncoderWorker(_cancellationTokenSource.Token));

            if (_isRecording)
            {
                StopRecording();
                StartRecording();
            }
        }

        private void EncoderWorker(CancellationToken token)
        {
            var encoder = (OpusEncoder)OpusCodecFactory.CreateEncoder(_config.SampleRate, _config.Channels, OpusApplication.OPUS_APPLICATION_VOIP);
            encoder.Complexity = OpusComplexity;
            encoder.Bitrate = _config.Bitrate;
            encoder.UseInbandFEC = _config.UseInbandFEC;

            int frameSize = _config.FrameSize;
            byte[] tempEncodeBuffer = new byte[1275];

            try
            {
                while (!token.IsCancellationRequested)
                {
                    short[] pcmData = null;
                    bool hasData = false;

                    // Lock-based Queue (Cheaper than Allocating ConcurrentQueue Nodes)
                    lock (_queueLock)
                    {
                        if (_encodeQueue.Count > 0)
                        {
                            pcmData = _encodeQueue.Dequeue();
                            hasData = true;
                        }
                    }

                    if (hasData)
                    {
                        try
                        {
                            ReadOnlySpan<short> inputSpan = pcmData.AsSpan(0, frameSize);
                            Span<byte> outputSpan = tempEncodeBuffer.AsSpan();

                            // Encode
                            int length = encoder.Encode(inputSpan, frameSize, outputSpan, outputSpan.Length);

                            // Recycle PCM buffer immediately
                            _pcmPool.Return(pcmData);

                            if (length > 0)
                            {
                                // Rent a Packet Buffer
                                byte[] netPacket = _packetPool.Rent();

                                // Copy encoded data
                                Buffer.BlockCopy(tempEncodeBuffer, 0, netPacket, 0, length);

                                int seq = Interlocked.Increment(ref _packetSequence);

                                // Send (Pass ArraySegment to indicate real length)
                                _transport.SendVoiceData(new ArraySegment<byte>(netPacket, 0, length), seq);

                                // RECYCLE PACKET:
                                // CRITICAL: We assume Transport copies data or sends synchronously.
                                // If Transport queues the byte[] reference for later, do NOT return here.
                                // PurrNet typically serializes immediately, so this is safe.
                                _packetPool.Return(netPacket);
                            }
                        }
                        catch (Exception ex) { Debug.LogError($"[VoiceWorker] {ex.Message}"); }
                    }
                    else
                    {
                        Thread.Sleep(2);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private void Update()
        {
            if (!isActiveAndEnabled) return;
            HandleInput();
            if (_isRecording) ProcessMicrophone();
        }

        private void HandleInput()
        {
            bool wantsToTalk = !usePushToTalk || Input.GetKey(pushToTalkKey);
            if (wantsToTalk && !_isRecording) StartRecording();
            else if (!wantsToTalk && _isRecording) StopRecording();
        }

        public void StartRecording()
        {
            if (_config == null || Microphone.devices.Length == 0) return;
            _device = Microphone.devices[0];
            _micClip = Microphone.Start(_device, true, 1, _config.SampleRate);
            _lastPos = 0;
            _isRecording = true;
        }

        public void StopRecording()
        {
            if (!string.IsNullOrEmpty(_device)) Microphone.End(_device);
            _isRecording = false;
            if (_ringBuffer != null) _ringBuffer.Clear();
            CurrentVolume = 0;
        }

        private void ProcessMicrophone()
        {
            int frameSize = _config.FrameSize;
            if (frameSize <= 0) return;

            int currentPos = Microphone.GetPosition(_device);
            if (currentPos == _lastPos) return;

            int diff = currentPos - _lastPos;
            if (diff < 0) diff += _config.SampleRate;

            if (!_micClip.GetData(_micBuffer, _lastPos)) return;
            _ringBuffer.Write(_micBuffer, diff);
            _lastPos = currentPos;

            int loops = 0;
            while (_ringBuffer.Count >= frameSize && loops < 3)
            {
                _ringBuffer.Read(_frameBufferFloat, frameSize);
                _nativeInput.CopyFrom(_frameBufferFloat);

                var job = new AudioProcessJob { Input = _nativeInput, OutputShorts = _nativeOutput, MaxVolume = _nativeVolume };
                job.Schedule().Complete();

                float maxVol = _nativeVolume[0];
                CurrentVolume = maxVol;

                if (maxVol > _config.SilenceThreshold)
                {
                    // RENT from Pool (Zero GC)
                    short[] pooledBuffer = _pcmPool.Rent();

                    // Copy Native -> Pooled Array
                    _nativeOutput.CopyTo(pooledBuffer);

                    // Enqueue
                    lock (_queueLock)
                    {
                        _encodeQueue.Enqueue(pooledBuffer);
                    }
                }
                loops++;
            }
        }

        [BurstCompile(CompileSynchronously = true)]
        struct AudioProcessJob : IJob
        {
            [ReadOnly] public NativeArray<float> Input;
            [WriteOnly] public NativeArray<short> OutputShorts;
            [WriteOnly] public NativeArray<float> MaxVolume;

            public void Execute()
            {
                float maxVal = 0;
                int length = Input.Length;
                for (int i = 0; i < length; i++)
                {
                    float sample = Input[i];
                    float absVal = math.abs(sample);
                    if (absVal > maxVal) maxVal = absVal;

                    if (sample > 1.0f) sample = 1.0f;
                    if (sample < -1.0f) sample = -1.0f;
                    OutputShorts[i] = (short)(sample * 32767.0f);
                }
                MaxVolume[0] = maxVal;
            }
        }
    }
}