using NyxMachina.Shared.EventFramework;
using TMPro;
using UnityEngine;

public class VoiceReceiverInterfaceListener : MonoBehaviour
{
    private ulong _ownerId;

    [SerializeField] private GameObject speakerIconContainer;
    [SerializeField] private TMP_Text ownerNameText;

    public bool isSpeaking;

    public void Init(ulong ownerId, string ownerName)
    {
        _ownerId = ownerId;
        ownerNameText.text = ownerName;
    }

    private void OnEnable()
    {
        EVENT.Subscribe<VoiceChatEvent.OnPlayerTalk>(HandleOnPlayerTalk);
    }

    private void OnDisable()
    {
        EVENT.Unsubscribe<VoiceChatEvent.OnPlayerTalk>(HandleOnPlayerTalk);
    }

    private void HandleOnPlayerTalk(VoiceChatEvent.OnPlayerTalk evt)
    {
        if (evt.ClientId != _ownerId) return;

        speakerIconContainer.gameObject.SetActive(evt.IsSpeaking);
        isSpeaking = evt.IsSpeaking;
    }
}
