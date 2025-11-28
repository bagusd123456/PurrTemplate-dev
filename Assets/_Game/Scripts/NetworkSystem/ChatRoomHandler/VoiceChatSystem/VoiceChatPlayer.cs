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
    }

    public void Initialize(VoiceConfig config)
    {
        _config = config;
        _decoder = (OpusDecoder)OpusCodecFactory.CreateDecoder(_config.SampleRate, _config.Channels);
        _decodeBuffer = new float[_config.FrameSize * _config.Channels];

        // Restart AudioSource with correct settings
        if (_audioSource.clip != null) Destroy(_audioSource.clip);
        
        _audioSource.clip = AudioClip.Create("Processor", _config.SampleRate, _config.Channels, _config.SampleRate, false);
        _audioSource.Play();
        
        _isInitialized = true;
    }

    public void StopPlayer()
    {
        _audioSource.volume = 0;
        _audioSource.Stop();
        _audioSource.enabled = false;
    }

    public void StartPlayer()
    {
        _audioSource.enabled = true;
        _audioSource.volume = 1;
        _audioSource.spatialBlend = 1.0f; // 3D Audio
        _audioSource.Play();
    }

    /// <summary>
    /// Called by PurrNet/Network when a packet arrives.
    /// </summary>
    public void OnVoiceDataReceived(byte[] data)
    {
        try
        {
            // 1. Decode the Opus bytes into PCM Floats
            int decodedSamples = _decoder.Decode(data, 0, data.Length, _decodeBuffer, 0, _config.FrameSize, false);

            // 2. Push the floats into the queue
            for (int i = 0; i < decodedSamples; i++)
            {
                _audioQueue.Enqueue(_decodeBuffer[i]);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[OpusPlayer] Decode Error: {e.Message}");
        }
    }

    /// <summary>
    /// Unity calls this ~50-100 times per second on a separate AUDIO THREAD.
    /// We must fill the 'data' array with sound, or 0 for silence.
    /// </summary>
    private void OnAudioFilterRead(float[] data, int channels)
    {
        // 'data' contains all samples for the next audio frame (e.g. 1024 samples)
        // 'channels' is usually 2 (Stereo) even if our voice is Mono.

        for (int i = 0; i < data.Length; i += channels)
        {
            // Try to get the next voice sample
            if (_audioQueue.TryDequeue(out float sample))
            {
                // Write the sample to Left and Right channels
                data[i] = sample;
                if (channels == 2) data[i + 1] = sample;
            }
            else
            {
                // Prevents looping/ghosting sound.
                data[i] = 0.0f;
                if (channels == 2) data[i + 1] = 0.0f;
            }
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