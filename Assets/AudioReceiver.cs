using NyxMachina.Shared.EventFramework;
using UnityEngine;

public class AudioReceiver : MonoBehaviour
{
    public AudioSource audioSource;
    public float volumeScale = 0.1f;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        EVENT.Subscribe<ChatRoomHandler.VoiceChatDataReceived>(HandleVoiceChatReceived);
    }

    private void OnDisable()
    {
        EVENT.Unsubscribe<ChatRoomHandler.VoiceChatDataReceived>(HandleVoiceChatReceived);
    }

    private void HandleVoiceChatReceived(ChatRoomHandler.VoiceChatDataReceived obj)
    {
        audioSource.PlayOneShot(obj.VoiceAudio, volumeScale);
    }
}
