using NUnit.Framework;
using NyxMachina.Multiplayer;
using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using UnityEngine;
using UnityEngine.TestTools;

public class VoiceChatTests
{
    private GameObject _playerGo;
    private VoiceChatPlayer _player;
    private VoiceConfig _testConfig;

    private float FrameSize;

    [SetUp]
    public void Setup()
    {
        _playerGo = new GameObject("VoicePlayer_Test");
        _playerGo.AddComponent<AudioSource>();
        _player = _playerGo.AddComponent<VoiceChatPlayer>();

        _testConfig = (VoiceConfig)ScriptableObject.CreateInstance(typeof(VoiceConfig));
        _testConfig.SampleRate = 48000;
        _testConfig.Channels = 1;
        _testConfig.FrameSizeOverride = 960; // 20ms

        // Initialize with dummy ID
        _player.Initialize(_testConfig, 100);
    }

    [TearDown]
    public void Teardown()
    {
        if (_playerGo) Object.Destroy(_playerGo);
        if (_testConfig != null)
        {
            //Object.Destroy(_testConfig);
        }
    }

    [UnityTest]
    public IEnumerator Buffer_ShouldNotCutAudio_WhenPacketBurstArrives()
    {
        // --- ARRANGE ---
        // Get access to the private _audioQueue via Reflection
        var queueField = typeof(VoiceChatPlayer).GetField("_audioQueue", BindingFlags.NonPublic | BindingFlags.Instance);
        var audioQueue = (ConcurrentQueue<float>)queueField.GetValue(_player);

        // Calculate thresholds based on your code logic:
        // Threshold is SampleRate * 0.5f (24,000 samples for 48k)
        int threshold = (int)(_testConfig.SampleRate * 0.5f);
        
        // Simulate a "Network Burst" (Jitter)
        // We inject 0.6 seconds of audio (slightly over the 0.5s threshold)
        int burstSampleCount = (int)(_testConfig.SampleRate * 0.6f); 
        
        Debug.Log($"Injecting {burstSampleCount} samples. Threshold is {threshold}.");

        for (int i = 0; i < burstSampleCount; i++)
        {
            audioQueue.Enqueue(0.5f); // Add dummy audio data
        }

        Assert.AreEqual(burstSampleCount, audioQueue.Count, "Queue should be full before Update.");

        // --- ACT ---
        // Allow Unity to run one frame. The VoiceChatPlayer.Update() method will run.
        yield return null; 

        // --- ASSERT ---
        int finalCount = audioQueue.Count;
        
        // YOUR CURRENT LOGIC: If count > 0.5s, remove count / 2.
        // Expected behavior IF BUG EXISTS: Count drops dramatically (stutter/cut).
        // Expected behavior IF FIXED: Count should be roughly same (maybe minus one frame of playback).
        
        Debug.Log($"Buffer size after Update: {finalCount}");

        // If the buffer lost ~14,000 samples in one frame, that's a massive audio cut.
        if (finalCount < burstSampleCount - 48000 * 0.05f) // Allow small variance for playback
        {
             Assert.Fail($"STUTTER DETECTED: The player deleted {burstSampleCount - finalCount} samples instantly to catch up. This causes the cut.");
        }
        else
        {
            Assert.Pass("Buffer handled the burst gracefully.");
        }
    }
}