using System;
using UnityEngine;
using Concentus.Structs;
using Concentus.Enums;
using System.Collections.Generic;
using Concentus;

public class VoiceChatRecorder : MonoBehaviour
{
    // Settings (Injected from Bridge)
    public KeyCode pushToTalkKey = KeyCode.V;
    public bool usePushToTalk = true;

    // Public property for the Bridge to visualize
    public float CurrentVolume { get; private set; }

    private VoiceConfig _config;
    private OpusEncoder _encoder;
    
    // Buffers
    private byte[] _encodedBuffer;
    private float[] _micBuffer;
    private float[] _frameBuffer;
    private AudioRingBuffer _ringBuffer;

    private AudioClip _micClip;
    private string _device;
    private int _lastPos;
    private bool _isRecording;
    private IVoiceNetworkTransport _transport;

    public void Initialize(IVoiceNetworkTransport transport, VoiceConfig config)
    {
        _transport = transport;
        _config = config;

        // Initialize Opus Encoder (VoIP optimized)
        _encoder = (OpusEncoder)OpusCodecFactory.CreateEncoder(_config.SampleRate, _config.Channels, OpusApplication.OPUS_APPLICATION_VOIP);
        _encoder.Bitrate = _config.Bitrate; // 32kbps is Discord quality. Up to 64000 for music.
        _encoder.UseInbandFEC = _config.UseInbandFEC;
        _encoder.SignalType = OpusSignal.OPUS_SIGNAL_VOICE;

        // Resize Buffers
        _frameBuffer = new float[_config.FrameSize * _config.Channels];
        _encodedBuffer = new byte[1275]; 
        _micBuffer = new float[_config.SampleRate * _config.Channels]; // 1 second mic capture buffer

        // Initialize Ring Buffer (Capacity = 1 second of audio to be safe)
        // 48000 samples is plenty to handle Unity's mic update variability
        _ringBuffer = new AudioRingBuffer(_config.SampleRate * _config.Channels); 
        
        // Restart Mic if running
        if (_isRecording)
        {
            StopRecording();
            StartRecording();
        }
    }

    void Update()
    {
        HandleInput();

        if (_isRecording)
        {
            ProcessMicrophone();
        }
    }

    void HandleInput()
    {
        bool wantsToTalk = usePushToTalk ? Input.GetKey(pushToTalkKey) : true;

        if (wantsToTalk && !_isRecording) StartRecording();
        else if (!wantsToTalk && _isRecording) StopRecording();
    }

    void StartRecording()
    {
        if (_config == null || Microphone.devices.Length == 0) return;
        
        _device = Microphone.devices[0];
        _micClip = Microphone.Start(_device, true, 1, _config.SampleRate);
        _lastPos = 0;
        _isRecording = true;
    }

    void StopRecording()
    {
        Microphone.End(_device);
        _isRecording = false;
        _ringBuffer.Clear();
        CurrentVolume = 0;
    }

    void ProcessMicrophone()
    {
        int currentPos = Microphone.GetPosition(_device);
        if (currentPos == _lastPos) return;

        // 1. Calculate samples available
        int diff = currentPos - _lastPos;
        if (diff < 0) diff += _config.SampleRate;

        // 2. Read from Unity Mic Clip into temp buffer
        if (!_micClip.GetData(_micBuffer, _lastPos)) return;
        
        // 3. OPTIMIZED: Write directly to RingBuffer (Zero GC, Fast Copy)
        // We only write 'diff' amount from the _micBuffer
        _ringBuffer.Write(_micBuffer, diff);

        _lastPos = currentPos;

        // 4. Process while enough data exists in RingBuffer for a full Opus Frame
        while (_ringBuffer.Count >= _config.FrameSize)
        {
            // READ from RingBuffer into the FrameBuffer
            _ringBuffer.Read(_frameBuffer, _config.FrameSize);

            // Calculate Volume (VAD)
            float maxVol = 0;
            // A simple loop here is fine as it's small (480-960 iterations) and registers are fast
            for (int i = 0; i < _config.FrameSize; i++)
            {
                float val = Mathf.Abs(_frameBuffer[i]);
                if (val > maxVol) maxVol = val;
            }

            CurrentVolume = maxVol; 

            if (maxVol > _config.SilenceThreshold)
            {
                EncodeAndSend(_frameBuffer);
            }
        }
    }

    void EncodeAndSend(float[] pcmData)
    {
        try
        {
            // Encode floats to bytes
            int encodedLength = _encoder.Encode(pcmData, 0, _config.FrameSize, _encodedBuffer, 0, _encodedBuffer.Length);

            if (encodedLength > 0)
            {
                // Create exact array to send (PurrNet handles the copy)
                byte[] packet = new byte[encodedLength];
                System.Array.Copy(_encodedBuffer, packet, encodedLength);

                _transport.SendVoiceData(packet);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Opus Encode Error: {e.Message}");
        }
    }
}

public class AudioRingBuffer
{
    private readonly float[] _buffer;
    private readonly int _capacity;
    private int _writeHead;
    private int _readHead;
    
    // Public tracker for how many samples are currently buffered
    public int Count { get; private set; }

    public AudioRingBuffer(int capacity)
    {
        _capacity = capacity;
        _buffer = new float[capacity];
        _writeHead = 0;
        _readHead = 0;
        Count = 0;
    }

    /// <summary>
    /// Writes a chunk of data into the ring buffer using fast Array.Copy
    /// </summary>
    public void Write(float[] input, int length)
    {
        if (length > _capacity - Count)
        {
            // Optional: Handle overflow (e.g., clear buffer or log warning)
            // For voice, we usually just accept the glitch or ensure buffer is huge.
            // Resetting is safest to prevent desync.
            Clear(); 
        }

        // Calculate how much space is left at the end of the array before wrapping
        int spaceAtEnd = _capacity - _writeHead;

        if (length <= spaceAtEnd)
        {
            // Case A: No wrapping needed
            Array.Copy(input, 0, _buffer, _writeHead, length);
            _writeHead += length;
        }
        else
        {
            // Case B: We need to wrap around to the start
            // 1. Copy until the end
            Array.Copy(input, 0, _buffer, _writeHead, spaceAtEnd);
            
            // 2. Copy the rest to the beginning
            int remaining = length - spaceAtEnd;
            Array.Copy(input, spaceAtEnd, _buffer, 0, remaining);
            
            _writeHead = remaining;
        }

        if (_writeHead >= _capacity) _writeHead = 0;
        Count += length;
    }

    /// <summary>
    /// Reads a chunk of data from the ring buffer into the output array
    /// </summary>
    public void Read(float[] output, int length)
    {
        if (length > Count) throw new Exception("Buffer Underflow");

        int spaceAtEnd = _capacity - _readHead;

        if (length <= spaceAtEnd)
        {
            // Case A: No wrapping needed
            Array.Copy(_buffer, _readHead, output, 0, length);
            _readHead += length;
        }
        else
        {
            // Case B: Wrapped around
            // 1. Read until end
            Array.Copy(_buffer, _readHead, output, 0, spaceAtEnd);

            // 2. Read from start
            int remaining = length - spaceAtEnd;
            Array.Copy(_buffer, 0, output, spaceAtEnd, remaining);

            _readHead = remaining;
        }

        if (_readHead >= _capacity) _readHead = 0;
        Count -= length;
    }

    public void Clear()
    {
        _writeHead = 0;
        _readHead = 0;
        Count = 0;
    }
}