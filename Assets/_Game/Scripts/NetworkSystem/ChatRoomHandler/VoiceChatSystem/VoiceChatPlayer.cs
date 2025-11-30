using Concentus;
using Concentus.Structs;
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
    private bool _isInitialized = false;

    void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
        _audioSource.loop = true;
        _audioSource.spatialBlend = 1.0f;
        _audioSource.dopplerLevel = 0f;
    }

    public void Initialize(VoiceConfig config)
    {
        _config = config;
        _decoder = (OpusDecoder)OpusCodecFactory.CreateDecoder(_config.SampleRate, _config.Channels);
        _decodeBuffer = new float[_config.FrameSize * _config.Channels];

        // Restart AudioSource with correct settings
        if (_audioSource.clip != null) Destroy(_audioSource.clip);
        
        _audioSource.clip = AudioClip.Create("VoiceStream", _config.SampleRate, 1, _config.SampleRate, true, OnPcmRead);
        _audioSource.Play();
        
        _isInitialized = true;
    }
    
    /// <summary>
    /// This replaces OnAudioFilterRead. 
    /// Unity calls this to "fetch" data from our virtual clip.
    /// This happens BEFORE spatialization, allowing 3D effects to work.
    /// </summary>
    /// <param name="data">The buffer to fill. Since we created a Mono clip, data.Length will match sample count.</param>
    private void OnPcmRead(float[] data)
    {
        for (int i = 0; i < data.Length; i++)
        {
            if (_audioQueue.TryDequeue(out float sample))
            {
                data[i] = sample;
            }
            else
            {
                data[i] = 0.0f; // Silence if buffer empty
            }
        }
    }

    public void StopPlayer()
    {
        _audioSource.volume = 0;
        _audioSource.Stop();
        _audioQueue = new ConcurrentQueue<float>();
        _audioSource.enabled = false;
    }

    public void StartPlayer()
    {
        _audioSource.enabled = true;
        _audioSource.volume = 1;
        //_audioSource.spatialBlend = 1.0f; // 3D Audio
        _audioSource.Play();
    }

    /// <summary>
    /// Called by PurrNet/Network when a packet arrives.
    /// </summary>
    public void OnVoiceDataReceived(byte[] data)
    {
        try
        {
            int decodedSamples = _decoder.Decode(data, 0, data.Length, _decodeBuffer, 0, _config.FrameSize, false);

            // If the incoming Opus stream is Stereo, we might need to mix it down to Mono
            // so it fits into our Mono AudioClip properly.
            if (_config.Channels == 2)
            {
                // Simple Stereo -> Mono mixdown (Average L+R)
                for (int i = 0; i < decodedSamples; i += 2)
                {
                    float left = _decodeBuffer[i];
                    float right = _decodeBuffer[i + 1];
                    _audioQueue.Enqueue((left + right) * 0.5f);
                }
            }
            else
            {
                // Incoming is already Mono, just push it
                for (int i = 0; i < decodedSamples; i++)
                {
                    _audioQueue.Enqueue(_decodeBuffer[i]);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[OpusPlayer] Decode Error: {e.Message}");
        }
    }

    private void Update()
    {
        if (!_isInitialized)
        {
            return;
        }

        // If we have > 0.5 seconds of audio buffered, we are lagging behind. 
        // Catch up by discarding old audio.
        if (_audioQueue.Count > _config.SampleRate * 0.5f)
        {
            float garbage;
            // Throw away half the buffer to catch up
            int removeCount = _audioQueue.Count / 2;
            for (int k = 0; k < removeCount; k++) _audioQueue.TryDequeue(out garbage);
        }
    }
}