using System;
using JetBrains.Annotations;
using NyxMachina.Shared.EventFramework;
using NyxMachina.Shared.EventFramework.Core.Payloads;
using PurrNet;
using QFSW.QC;
using Steamworks;
using System.Collections;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class ChatRoomHandler
{
    // Enum to define the recording mode
    public enum VoiceMode
    {
        None = -1,
        PushToTalk,
        VoiceActivity
    }

    public struct TextChatDataReceived : IPayload
    {
        public TextChatData TextData;

        public TextChatDataReceived(TextChatData receivedData)
        {
            TextData = receivedData;
        }
    }

    public struct TextChatData
    {
        public string senderName;
        public string message;
    }

    [Serializable]
    public struct AudioData
    {
        public float[] audioSamples;
        public int channels;
        public int frequency;

        public AudioData(AudioClip clip)
        {
            // Get the raw audio data as a float array
            audioSamples = new float[clip.samples * clip.channels];
            clip.GetData(audioSamples, 0);

            // Get the necessary metadata
            channels = clip.channels;
            frequency = clip.frequency;
        }

        public AudioClip ToAudioClip()
        {
            // Create an empty AudioClip
            AudioClip clip = AudioClip.Create("received_voice", audioSamples.Length / channels, channels, frequency, false);

            // Load the sample data into the new clip
            clip.SetData(audioSamples, 0);

            return clip;
        }
    }

    [Serializable]
    public struct VoiceChatDataReceived : IPayload
    {
        public AudioData VoiceAudio;
        public string SenderPlayerId;
        public long SentTimestampTicks; 
        public VoiceChatDataReceived(AudioData voiceAudio, string senderId)
        {
            VoiceAudio = voiceAudio;
            SenderPlayerId = senderId;

            SentTimestampTicks = DateTime.Now.Ticks;
        }
    }

    public struct ProcessedVoiceData
    {
        public float[] AudioData;
        public uint SampleRate;
        public int Channels;
        public int Samples;
    }

    // Callbacks
    private Callback<LobbyChatMsg_t> _lobbyChatMsg;

    private CSteamID _currentLobbyID;
    public VoiceMode CurrentVoiceMode { get; private set; } = VoiceMode.None;

    public Task VoiceTransmissionTask;
    public CancellationTokenSource TransmissionTaskCancellation;

    // Thread-safe queues for communication between threads
    private readonly ConcurrentQueue<byte[]> rawVoiceDataQueue = new ConcurrentQueue<byte[]>();
    private readonly ConcurrentQueue<ProcessedVoiceData> processedVoiceDataQueue = new ConcurrentQueue<ProcessedVoiceData>();

    public ChatRoomHandler()
    {
        TransmissionTaskCancellation = new CancellationTokenSource();

        _lobbyChatMsg = Callback<LobbyChatMsg_t>.Create(OnReceivedChatMessage);
        EVENT.Subscribe<LobbyHandler.OnLobbyJoined>(HandleOnLobbyJoined);
        VoiceTransmissionTask = Task.Run(()=> VoiceProcessingLoop(TransmissionTaskCancellation.Token));

        Application.wantsToQuit += HandleApplicationQuit;
        MainThreadDispatcher.Init();
        MainThreadDispatcher.Instance.StartCoroutineOnMainThread(VoiceProcessingCoroutine());

        SetVoiceMode(VoiceMode.VoiceActivity);
    }

    private bool HandleApplicationQuit()
    {
        Shutdown();
        return true;
    }

    private void HandleOnLobbyJoined(LobbyHandler.OnLobbyJoined obj)
    {
        // Convert LobbyId (string) to ulong, then to CSteamID
        if (ulong.TryParse(obj.CurrentLobby.LobbyId, out var ulongLobbyId))
        {
            var convertedLobbyId = new CSteamID(ulongLobbyId);
            _currentLobbyID = convertedLobbyId;
        }
        else
        {
            Debug.LogError($"[ChatRoomHandler] Failed to parse LobbyId: {obj.CurrentLobby.LobbyId}");
        }
    }

    /// <summary>
    /// Main thread coroutine: Produces raw data and Finalizes processed data.
    /// </summary>
    public IEnumerator VoiceProcessingCoroutine()
    {
        Debug.Log("Voice processing coroutine started on main thread.");
        while (TransmissionTaskCancellation is { IsCancellationRequested: false })
        {
            // Get data bytes to produce ProcessedVoiceData
            if (SteamUser.GetAvailableVoice(out uint compressedSize) == EVoiceResult.k_EVoiceResultOK && compressedSize > 0)
            {
                byte[] compressedBuffer = new byte[compressedSize];
                if (SteamUser.GetVoice(true, compressedBuffer, compressedSize, out uint bytesWritten) == EVoiceResult.k_EVoiceResultOK && bytesWritten > 0)
                {
                    rawVoiceDataQueue.Enqueue(compressedBuffer);
                }
            }

            // Queue raw Data to be processed
            if (processedVoiceDataQueue.TryDequeue(out ProcessedVoiceData data))
            {
                //Create the AudioClip here.
                AudioClip clip = AudioClip.Create("Voice", data.Samples, data.Channels, (int)data.SampleRate, false);
                clip.SetData(data.AudioData, 0);

                // Publish the event with the valid AudioClip
                //EVENT.Publish(new VoiceChatDataReceived(new AudioData(clip), SteamUser.GetSteamID().ToString()));
            }

            yield return null; // Wait for the next frame
        }
        Debug.Log("Voice processing coroutine finished.");
    }

    
    private async Task VoiceProcessingLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (rawVoiceDataQueue.TryDequeue(out byte[] compressedVoice))
            {
                var result = ProcessVoiceDataInBackground(compressedVoice);
                if (result.HasValue)
                {
                    processedVoiceDataQueue.Enqueue(result.Value);
                }
            }
            else
            {
                await Task.Delay(10, token); // Prevent busy-waiting
            }
        }
    }

    // Sets the desired voice recording mode
    public void SetVoiceMode(VoiceMode mode)
    {
        if (CurrentVoiceMode == mode) return;

        CurrentVoiceMode = mode;

        // If switching to VAD, start recording and let Steam handle activation
        if (CurrentVoiceMode == VoiceMode.VoiceActivity)
        {
            //SteamUser.StartVoiceRecording();
            Debug.Log("[System] Voice Activity Detection enabled.");
        }
        // If switching to PTT, stop the continuous recording
        else
        {
            //SteamUser.StopVoiceRecording();
            Debug.Log("[System] Push-to-Talk enabled.");
        }
    }

    public void StartPushToTalk()
    {
        if (CurrentVoiceMode == VoiceMode.PushToTalk)
        {
            SteamUser.StartVoiceRecording();
        }
    }

    public void StopPushToTalk()
    {
        if (CurrentVoiceMode == VoiceMode.PushToTalk)
        {
            SteamUser.StopVoiceRecording();
        }
    }

    public void SendChatMessage(string text)
    {
        if (string.IsNullOrEmpty(text) || _currentLobbyID == CSteamID.Nil)
        {
            return;
        }

        byte[] message = System.Text.Encoding.UTF8.GetBytes(text);
        SteamMatchmaking.SendLobbyChatMsg(_currentLobbyID, message, message.Length);
    }

    private void OnReceivedChatMessage(LobbyChatMsg_t callback)
    {
        byte[] buffer = new byte[4096];
        int dataSize = SteamMatchmaking.GetLobbyChatEntry(_currentLobbyID, (int)callback.m_iChatID, out var steamIDUser, buffer, buffer.Length, out var chatEntryType);

        if (chatEntryType != EChatEntryType.k_EChatEntryTypeChatMsg)
        {
            return;
        }

        string message = System.Text.Encoding.UTF8.GetString(buffer, 0, dataSize);
        string senderName = SteamFriends.GetFriendPersonaName(steamIDUser);
        Debug.Log(senderName + ": " + message);

        var textData = new TextChatData()
        {
            senderName = senderName,
            message = message
        };
        EVENT.Publish(new TextChatDataReceived(textData));
    }

    [CanBeNull]
    private ProcessedVoiceData? ProcessVoiceDataInBackground(byte[] voiceByteArray)
    {
        const uint optimalSampleRate = 48000;
        byte[] decompressedBuffer = new byte[optimalSampleRate * 4]; // Buffer for 2 seconds of audio

        if (SteamUser.DecompressVoice(voiceByteArray, (uint)voiceByteArray.Length, decompressedBuffer,
                (uint)decompressedBuffer.Length, out var bytesWritten, optimalSampleRate) != EVoiceResult.k_EVoiceResultOK ||
            bytesWritten <= 0)
        {
            return null;
        }

        int samples = (int)bytesWritten / 2; // 2 bytes per sample for 16-bit audio
        float[] audioData = new float[samples];

        for (int i = 0; i < samples; i++)
        {
            // Convert 16-bit PCM to float
            short pcm = (short)(decompressedBuffer[i * 2] | decompressedBuffer[i * 2 + 1] << 8);
            audioData[i] = pcm / 32768.0f;
        }

        // Return a struct with all the info needed to create the AudioClip on the main thread.
        return new ProcessedVoiceData
        {
            AudioData = audioData,
            SampleRate = optimalSampleRate,
            Channels = 1,
            Samples = samples
        };
    }

    public void Shutdown()
    {
        _lobbyChatMsg = null;
        EVENT.Unsubscribe<LobbyHandler.OnLobbyJoined>(HandleOnLobbyJoined);
        TransmissionTaskCancellation.Cancel();
    }
}

public static class ChatRoomHandlerUtil
{
    [Command]
    public static void SendChatMessage(string message)
    {
        LobbyHandler.chatRoomHandler.SendChatMessage(message);
    }

    [Command]
    public static void SetVoiceMode(int state)
    {
        if (state == 0)
        {
            LobbyHandler.chatRoomHandler.SetVoiceMode(ChatRoomHandler.VoiceMode.PushToTalk);
        }
        else if (state == 1)
        {
            LobbyHandler.chatRoomHandler.SetVoiceMode(ChatRoomHandler.VoiceMode.VoiceActivity);
        }
    }

    [Command]
    public static void StartPushToTalk()
    {
        LobbyHandler.chatRoomHandler.StartPushToTalk();
    }

    [Command]
    public static void StopPushToTalk()
    {
        LobbyHandler.chatRoomHandler.StopPushToTalk();
    }
}