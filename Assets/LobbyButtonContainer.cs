using PurrLobby;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyButtonContainer : MonoBehaviour
{
    [SerializeField] private Lobby currentLobby;
    [SerializeField] private TMP_Text lobbyNameText;
    [SerializeField] private TMP_Text lobbyIdText;
    [SerializeField] private TMP_Text lobbyCodeText;
    [SerializeField] private TMP_Text lobbyMemberCountText;

    [SerializeField] private Button joinButton;
    private LobbyManager lobbyManager;

    public void Initialize(Lobby targetLobby, LobbyManager targetLobbyManager)
    {
        currentLobby = targetLobby;
        lobbyManager = targetLobbyManager;
        SetView(targetLobby);
    }

    private void OnEnable()
    {
        joinButton.onClick.AddListener(OnJoinButtonClicked);
    }

    private void OnDisable()
    {
        joinButton.onClick.RemoveListener(OnJoinButtonClicked);
    }

    private void SetView(Lobby targetLobby)
    {
        lobbyNameText.text = targetLobby.Name;
        lobbyIdText.text = targetLobby.LobbyId;
        lobbyCodeText.text = targetLobby.LobbyCode;
        lobbyMemberCountText.text = $"{targetLobby.Members.Count}/{targetLobby.MaxPlayers}";
    }

    private void OnJoinButtonClicked()
    {
        lobbyManager.JoinLobby(currentLobby.LobbyId);
    }
}