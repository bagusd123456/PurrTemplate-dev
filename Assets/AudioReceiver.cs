using NyxMachina.Shared.EventFramework;
using PurrNet;
using Steamworks;
using UnityEngine;

public class AudioReceiver : NetworkBehaviour
{
    public static SyncEvent<ChatRoomHandler.VoiceChatDataReceived> VoiceChatReceivedSyncEvent = new();

    public AudioSource audioSource;
    public float volumeScale = 0.1f;

    public bool debugHearSelf;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        EVENT.Subscribe<ChatRoomHandler.VoiceChatDataReceived>(HandleLocalVoiceReceived);
        VoiceChatReceivedSyncEvent.AddListener(PlaySound);
    }

    private void OnDisable()
    {
        EVENT.Unsubscribe<ChatRoomHandler.VoiceChatDataReceived>(HandleLocalVoiceReceived);
        VoiceChatReceivedSyncEvent.RemoveListener(PlaySound);
    }

    private void HandleLocalVoiceReceived(ChatRoomHandler.VoiceChatDataReceived evt)
    {
        VoiceChatReceivedSyncEvent?.Invoke(evt);
    }

    private void PlaySound(ChatRoomHandler.VoiceChatDataReceived voiceChatData)
    {
        if (!debugHearSelf)
        {
            if (SteamUser.GetSteamID().ToString().Equals(voiceChatData.SenderPlayerId))
            {
                return;
            }
        }

        audioSource.PlayOneShot(voiceChatData.VoiceAudio, volumeScale);
    }
}
