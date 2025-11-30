using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Concentus;
using Concentus.Enums;
using Concentus.Structs;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

public class VoiceChatRecorder : MonoBehaviour
{
    [Header("Settings")]
    public KeyCode pushToTalkKey = KeyCode.V;
    public bool usePushToTalk = true;
    [Range(0, 10)] public int OpusComplexity = 2;

    [Header("Network Optimization")]
    // 5 frames * 20ms = 100ms added latency (acceptable for VoIP)
    // Reduces network calls by 5x
    [Range(1, 10)] public int BatchSize = 2; 

    public float CurrentVolume { get; private set; }

    private VoiceConfig _config;
    
    // Buffers
    private float[] _micBuffer;       
    private float[] _frameBufferFloat;
    private AudioRingBuffer _ringBuffer;

    // Native
    private NativeArray<float> _nativeInput;
    private NativeArray<short> _nativeOutput;
    private NativeArray<float> _nativeVolume; 

    // Threading
    private ConcurrentQueue<short[]> _encodeQueue;
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

        // Allocations
        int frameSize = _config.FrameSize * _config.Channels;
        int micBufferSize = _config.SampleRate * _config.Channels;

        _frameBufferFloat = new float[frameSize];
        _micBuffer = new float[micBufferSize]; 
        _ringBuffer = new AudioRingBuffer(micBufferSize); 

        if (_nativeInput.IsCreated) _nativeInput.Dispose();
        _nativeInput = new NativeArray<float>(frameSize, Allocator.Persistent);

        if (_nativeOutput.IsCreated) _nativeOutput.Dispose();
        _nativeOutput = new NativeArray<short>(frameSize, Allocator.Persistent);
        
        if (_nativeVolume.IsCreated) _nativeVolume.Dispose();
        _nativeVolume = new NativeArray<float>(1, Allocator.Persistent);

        _encodeQueue = new ConcurrentQueue<short[]>();
        _cancellationTokenSource = new CancellationTokenSource();
        _encodeTask = Task.Run(() => EncoderWorker(_cancellationTokenSource.Token));

        if (_isRecording)
        {
            StopRecording();
            StartRecording();
        }
    }

    // --- BATCHING WORKER ---
    private void EncoderWorker(CancellationToken token)
    {
        var encoder = (OpusEncoder)OpusCodecFactory.CreateEncoder(_config.SampleRate, _config.Channels, OpusApplication.OPUS_APPLICATION_VOIP);
        encoder.Complexity = OpusComplexity; 
        encoder.Bitrate = _config.Bitrate;
        encoder.UseInbandFEC = _config.UseInbandFEC;

        int frameSize = _config.FrameSize;
        byte[] tempEncodeBuffer = new byte[1275]; 

        // Batch Buffer: [Length(2)][Data...][Length(2)][Data...]
        // 4096 is plenty for ~10 opus packets
        byte[] batchBuffer = new byte[4096]; 
        int batchOffset = 0;
        int framesInBatch = 0;

        try
        {
            while (!token.IsCancellationRequested)
            {
                bool hasData = _encodeQueue.TryDequeue(out short[] pcmData);

                if (hasData)
                {
                    try
                    {
                        // Encode
                        int len = encoder.Encode(pcmData, 0, frameSize, tempEncodeBuffer, 0, tempEncodeBuffer.Length);

                        if (len > 0)
                        {
                            // Append to Batch Buffer
                            // Write Length (ushort) - 2 bytes
                            batchBuffer[batchOffset] = (byte)(len & 0xFF);
                            batchBuffer[batchOffset + 1] = (byte)((len >> 8) & 0xFF);
                            batchOffset += 2;

                            // Write Data
                            Buffer.BlockCopy(tempEncodeBuffer, 0, batchBuffer, batchOffset, len);
                            batchOffset += len;
                            framesInBatch++;

                            // Send if Batch is Full
                            if (framesInBatch >= BatchSize)
                            {
                                FlushBatch(ref batchBuffer, ref batchOffset, ref framesInBatch);
                            }
                        }
                    }
                    catch (Exception ex) { Debug.LogError($"[VoiceWorker] {ex.Message}"); }
                }
                else
                {
                    // No data pending?
                    // Check if we have a partial batch waiting to go
                    if (framesInBatch > 0)
                    {
                        FlushBatch(ref batchBuffer, ref batchOffset, ref framesInBatch);
                    }
                    
                    Thread.Sleep(5); // Sleep to save CPU
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void FlushBatch(ref byte[] buffer, ref int offset, ref int count)
    {
        if (count == 0) return;

        // Create exact packet for network
        byte[] packet = new byte[offset];
        Buffer.BlockCopy(buffer, 0, packet, 0, offset);

        // Reset Batch
        offset = 0;
        count = 0;

        // Send to Main Thread
        int seq = Interlocked.Increment(ref _packetSequence);
        _transport.SendVoiceData(new ArraySegment<byte>(packet), seq);
    }

    // --- MAIN THREAD UPDATES ---
    private void Update()
    {
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
                short[] copy = new short[frameSize];
                _nativeOutput.CopyTo(copy);
                _encodeQueue.Enqueue(copy);
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
            int len = Input.Length;
            for (int i = 0; i < len; i++)
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