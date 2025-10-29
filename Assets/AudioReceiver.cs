using System;
using Newtonsoft.Json;
using NyxMachina.Shared.EventFramework;
using PurrNet;
using Steamworks;
using System.Text;
using PurrNet.Transports;
using UnityEngine;
using CompressionLevel = PurrNet.CompressionLevel;

public class AudioReceiver : NetworkBehaviour
{
    public AudioSource audioSource;
    public float volumeScale = 0.1f;

    public bool debugHearSelf;
    public bool debugPrintDelay;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        EVENT.Subscribe<ChatRoomHandler.VoiceChatDataReceived>(HandleLocalVoiceReceived);
    }

    private void OnDisable()
    {
        EVENT.Unsubscribe<ChatRoomHandler.VoiceChatDataReceived>(HandleLocalVoiceReceived);
    }

    private void HandleLocalVoiceReceived(ChatRoomHandler.VoiceChatDataReceived evt)
    {
        var jsonData = JsonConvert.SerializeObject(evt);
        var byteArrayData = Encoding.UTF8.GetBytes(jsonData);
        PlaySoundToObservers(byteArrayData);
    }

    private void PlaySound(ChatRoomHandler.VoiceChatDataReceived voiceChatData)
    {
        if (debugPrintDelay)
        {
            long nowTicks = DateTime.Now.Ticks;
            TimeSpan totalDelay = TimeSpan.FromTicks(nowTicks - voiceChatData.SentTimestampTicks);
            Debug.Log($"[VoiceChat] Playing audio from {voiceChatData.SenderPlayerId}. Total delay: {totalDelay.TotalMilliseconds:F1} ms");
        }

        audioSource.PlayOneShot(voiceChatData.VoiceAudio.ToAudioClip(), volumeScale);
    }

    [ObserversRpc(Channel.Unreliable, compressionLevel:CompressionLevel.Best, requireServer: false)]
    private void PlaySoundToObservers(byte[] voiceChatDataByte)
    {
        var jsonData = Encoding.UTF8.GetString(voiceChatDataByte);
        var convertedData = JsonConvert.DeserializeObject<ChatRoomHandler.VoiceChatDataReceived>(jsonData);

        if (!debugHearSelf)
        {
            if (SteamUser.GetSteamID().ToString().Equals(convertedData.SenderPlayerId))
            {
                return;
            }
        }

        if (debugPrintDelay)
        {
            var receivedTicks = DateTime.Now.Ticks;
            // Calculate the delay from when the packet was sent to when it was received and deserialized.
            TimeSpan networkDelay = TimeSpan.FromTicks(receivedTicks - convertedData.SentTimestampTicks);
            Debug.Log($"[VoiceChat] Received audio packet. Network+Serialization delay: {networkDelay.TotalMilliseconds:F1} ms");
        }
        
        PlaySound(convertedData);
    }
}
