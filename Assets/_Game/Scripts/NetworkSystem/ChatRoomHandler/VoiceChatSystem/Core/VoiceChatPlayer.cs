using Concentus;
using Concentus.Structs;
using NyxMachina.Shared.EventFramework;
using System;
using System.Collections.Concurrent;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class VoiceChatPlayer : MonoBehaviour
{
    private VoiceConfig _config;
    private OpusDecoder _decoder;
    private AudioSource _audioSource;
    private ConcurrentQueue<float> _audioQueue = new ConcurrentQueue<float>();
    private float[] _decodeBuffer;
    private bool _isInitialized;

    private ulong clientId;
    private int _lastSequenceId = -1; // For FEC
    private float _lastSample;   // For Soft Decay

    struct VolumeFrame
    {
        public double TargetDspTime;
        public float MaxVolume;
    }
    
    private ConcurrentQueue<VolumeFrame> _syncQueue = new ConcurrentQueue<VolumeFrame>();

    private bool _wasSpeaking;
    private float _speakTimer;
    private const float SPEAK_THRESHOLD = 0.001f; 
    private const float SPEAK_COOLDOWN = 0.05f;   
    private double _bufferLatency; 

    void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        _audioSource.loop = true;
        _audioSource.spatialBlend = 1.0f;
        _audioSource.dopplerLevel = 0f;
    }

    public void Initialize(VoiceConfig config, ulong clientId)
    {
        if (config == null || config.SampleRate <= 0 || config.FrameSize <= 0)
        {
            Debug.LogError($"[VoiceChatPlayer] Invalid Configuration! Disabling.");
            this.enabled = false;
            return;
        }

        this.clientId = clientId;
        _config = config;
        
        try 
        {
            _decoder = (OpusDecoder)OpusCodecFactory.CreateDecoder(_config.SampleRate, _config.Channels);
        }
        catch(Exception e)
        {
            Debug.LogError($"[VoiceChatPlayer] Failed to create Opus Decoder: {e.Message}");
            return;
        }

        _decodeBuffer = new float[_config.FrameSize * _config.Channels];

        if (_audioSource.clip != null)
            Destroy(_audioSource.clip);
        
        while(_audioQueue.TryDequeue(out _)){};
        while(_syncQueue.TryDequeue(out _)){};
        _lastSequenceId = -1;
        _lastSample = 0f;

        _audioSource.clip = AudioClip.Create("VoiceStream", _config.SampleRate, 1, _config.SampleRate, true, OnPcmRead);
        _audioSource.Play();

        AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
        _bufferLatency = (double)bufferLength / _config.SampleRate;

        _isInitialized = true;
    }
    
    private void OnPcmRead(float[] data)
    {
        try
        {
            float framePeak = 0f;
            int len = data.Length;

            for (int i = 0; i < len; i++)
            {
                if (_audioQueue.TryDequeue(out float sample))
                {
                    data[i] = sample;
                    _lastSample = sample;

                    float abs = sample > 0 ? sample : -sample;
                    if (abs > framePeak) framePeak = abs;
                }
                else
                {
                    // Smoothly fade out the last sample to prevent "POP" sound
                    _lastSample *= 0.95f;
                    if (_lastSample < 0.0001f && _lastSample > -0.0001f) _lastSample = 0f;
                    data[i] = _lastSample;
                }
            }

            double expectedPlayTime = AudioSettings.dspTime + _bufferLatency;
            _syncQueue.Enqueue(new VolumeFrame()
            {
                TargetDspTime = expectedPlayTime, MaxVolume = framePeak
            });
        }
        catch
        {
            // Don't log error here to avoid Force close
        }
    }

    private void Update()
    {
        if (!_isInitialized) return;

        // Anti-Freeze / Lag Compensation
        int maxBufferSize = _config.SampleRate * 1; 
        if (_audioQueue.Count > maxBufferSize)
        {
            int targetSize = (int)(_config.SampleRate * 0.8f);
            int amountToRemove = _audioQueue.Count - targetSize;
            int safeRemoveCount = Mathf.Min(amountToRemove, 4000);
            for (int k = 0; k < safeRemoveCount; k++) 
                _audioQueue.TryDequeue(out _);
        }

        ProcessSyncQueue();
    }

    public void OnVoiceDataReceived(byte[] data, int sequenceId)
    {
        if (!_isInitialized) return;

        try
        {
            // Detect Gap: If we jumped more than 1 step (e.g. 1 -> 3)
            bool packetLost = (sequenceId > _lastSequenceId + 1) && (_lastSequenceId != -1);

            // If gap detected, recover the lost packet and fill in the gap using FEC
            if (packetLost)
            {
                int fecSamples = _decoder.Decode(data.AsSpan(), _decodeBuffer.AsSpan(), _config.FrameSize, true);
                PushToQueue(fecSamples);
            }

            // Decode Current Packet Normally
            int normalSamples = _decoder.Decode(data.AsSpan(), _decodeBuffer.AsSpan(), _config.FrameSize, false);
            PushToQueue(normalSamples);

            _lastSequenceId = sequenceId;
        }
        catch (Exception e)
        {
            Debug.LogError($"[VoiceChatPlayer] Opus decode Error: {e.Message}");
        }
    }

    private void PushToQueue(int samplesPerChannel)
    {
        if (_config.Channels == 2)
        {
            // Stereo: We have (samplesPerChannel * 2) total floats in the buffer
            for (int i = 0; i < samplesPerChannel * 2; i += 2)
            {
                // Mix Left + Right into one Mono float for the AudioSource
                float left = _decodeBuffer[i];
                float right = _decodeBuffer[i + 1];
                _audioQueue.Enqueue((left + right) * 0.5f);
            }
        }
        else
        {
            // Mono
            for (int i = 0; i < samplesPerChannel; i++)
                _audioQueue.Enqueue(_decodeBuffer[i]);
        }
    }

    private void ProcessSyncQueue()
    {
        double currentTime = AudioSettings.dspTime;
        float currentFrameVolume = 0f;
        bool hasNewData = false;
        VolumeFrame frame;
        int safetyBreaker = 0;

        while (_syncQueue.TryPeek(out frame))
        {
            if (safetyBreaker++ > 100)
                break;

            if (frame.TargetDspTime <= currentTime)
            {
                if (_syncQueue.TryDequeue(out frame)) 
                {
                    currentFrameVolume = Mathf.Max(currentFrameVolume, frame.MaxVolume);
                    hasNewData = true;
                }
            }
            else break;
        }

        if (hasNewData) HandleSpeakingEvent(currentFrameVolume);
    }

    private void HandleSpeakingEvent(float volume)
    {
        bool isTechnicallySpeaking = volume > SPEAK_THRESHOLD;
        if (isTechnicallySpeaking)
            _speakTimer = SPEAK_COOLDOWN;
        else
            _speakTimer -= Time.deltaTime;

        bool isVisuallySpeaking = _speakTimer > 0;
        if (isVisuallySpeaking != _wasSpeaking)
        {
            _wasSpeaking = isVisuallySpeaking;
            EVENT.Publish(new VoiceChatEvent.OnPlayerTalk(clientId, isVisuallySpeaking, volume));
        }
    }

    public void StopPlayer()
    {
        if (_audioSource)
        {
            _audioSource.Stop();
            _audioSource.enabled = false;
        }

        _audioQueue = new ConcurrentQueue<float>();
        _syncQueue = new ConcurrentQueue<VolumeFrame>();
        if (_wasSpeaking)
        {
            EVENT.Publish(new VoiceChatEvent.OnPlayerTalk(clientId, false, 0f)); _wasSpeaking = false;
        }
    }

    public void StartPlayer() 
    {
        if (_audioSource)
        {
            _audioSource.enabled = true; _audioSource.volume = 1; _audioSource.Play();
        }
    }

    public void SetVolume(float volume)
    {
        _audioSource.volume = volume;
    }

    public void SetMute(bool state)
    {
        _audioSource.mute = state;
    }
}