using System;
using Concentus;
using Concentus.Enums;
using Concentus.Structs;
using UnityEngine;

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

    private int _packetSequence = 0;

    public void Initialize(IVoiceNetworkTransport transport, VoiceConfig config)
    {
        _transport = transport;
        _config = config;

        // Initialize Opus Encoder (VoIP optimized)
        _encoder = (OpusEncoder)OpusCodecFactory.CreateEncoder(_config.SampleRate, _config.Channels, OpusApplication.OPUS_APPLICATION_VOIP);
        _encoder.Bitrate = _config.Bitrate; // 32kbps is Discord quality. Up to 64kbps for music.
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

    private void Update()
    {
        HandleInput();

        if (_isRecording)
        {
            ProcessMicrophone();
        }
    }

    private void HandleInput()
    {
        bool wantsToTalk = !usePushToTalk || Input.GetKey(pushToTalkKey);

        if (wantsToTalk && !_isRecording)
            StartRecording();
        else if (!wantsToTalk && _isRecording)
            StopRecording();
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
        Microphone.End(_device);
        _isRecording = false;
        _ringBuffer.Clear();
        CurrentVolume = 0;
    }

    private void ProcessMicrophone()
    {
        int currentPos = Microphone.GetPosition(_device);
        if (currentPos == _lastPos) 
            return;

        // If FrameSize is invalid, we would loop forever here.
        if (_config.FrameSize <= 0) 
            return;

        // Calculate samples available
        int diff = currentPos - _lastPos;
        if (diff < 0) diff += _config.SampleRate;

        // Read from Unity Mic Clip into temp buffer
        if (!_micClip.GetData(_micBuffer, _lastPos)) 
            return;
        
        // Write directly to RingBuffer (Zero GC, Fast Copy)
        // We only write 'diff' amount from the _micBuffer
        _ringBuffer.Write(_micBuffer, diff);

        _lastPos = currentPos;

        // Safety Counter: Never let this loop run more than 50 times in one frame
        int safetyLoopCount = 0; 

        // Process while enough data exists in RingBuffer for a full Opus Frame
        while (_ringBuffer.Count >= _config.FrameSize)
        {
            if (safetyLoopCount++ > 50) 
            {
                Debug.LogWarning("[VoiceChatRecorder] Recorder Buffer Overflow: Forced break to prevent freeze.");
                _ringBuffer.Clear(); // Dump buffer to reset
                break;
            }

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

            safetyLoopCount++;
        }
    }

    private void EncodeAndSend(float[] pcmData)
    {
        try
        {
            // Pass Audio Input, Frame size, Output Buffer, Max Buffer Capacity
            int encodedLength = _encoder.Encode(pcmData.AsSpan(), _config.FrameSize, _encodedBuffer.AsSpan(), _encodedBuffer.Length);

            if (encodedLength > 0)
            {
                byte[] packet = new byte[encodedLength];
                System.Array.Copy(_encodedBuffer, packet, encodedLength);

                // Increment sequence (wrap around at int.MaxValue to be safe)
                _packetSequence++; 

                // Send both Data AND Sequence ID
                _transport.SendVoiceData(packet, _packetSequence);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VoiceChatRecorder] Opus Encode Error: {e.Message}");
        }
    }
}