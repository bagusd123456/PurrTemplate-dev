using PurrNet;
using Steamworks;
using UnityEngine;

public class AudioReceiver : NetworkBehaviour
{
    public AudioSource audioSource;
    public float volumeScale = 0.1f;

    public bool debugHearSelf;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        ChatRoomHandler.VoiceChatReceivedSyncEvent.AddListener(PlaySound);
    }

    private void OnDisable()
    {
        ChatRoomHandler.VoiceChatReceivedSyncEvent.RemoveListener(PlaySound);
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
