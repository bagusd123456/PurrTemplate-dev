using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuPanel : View
{
    [SerializeField] private Button createLobbyButton;
    [SerializeField] private Button lobbySelectionButton;
    [SerializeField] private Button joinLobbyByCodeButton;
    [SerializeField] private TMP_InputField lobbyIdField;

    private void Start()
    {
        createLobbyButton.onClick.AddListener(HandleCreateLobby);
        lobbySelectionButton.onClick.AddListener(HandleLobbySelection);
        joinLobbyByCodeButton.onClick.AddListener(HandleJoinLobbyByCode);
    }

    private async void HandleJoinLobbyByCode()
    {
        var loadingPanel = LobbyMenuView.Instance.ShowView<LobbyLoadingPanel>() as LobbyLoadingPanel;
        loadingPanel.Set("Joining Lobby...");
        var joinLobbyByIdTask = await LobbySystem.JoinLobbyByIdAsync(lobbyIdField.text);

        if (joinLobbyByIdTask.IsFailOrCanceled)
        {
            Debug.LogError($"Found error while trying to join lobby by code.\n" +
                $"{joinLobbyByIdTask.Message}");
            LobbyMenuView.Instance.ShowView<MainMenuPanel>();
            return;
        }

        LobbyMenuView.Instance.ShowView<LobbyViewPanel>();
    }

    private void HandleLobbySelection()
    {
        LobbyMenuView.Instance.ShowView<LobbySelectionPanel>();
    }

    private async void HandleCreateLobby()
    {
        var loadingPanel = LobbyMenuView.Instance.ShowView<LobbyLoadingPanel>() as LobbyLoadingPanel;
        loadingPanel.Set("Creating Lobby...");
        var createLobbyTask = await LobbySystem.CreateLobbyAsync();

        if (createLobbyTask.IsFailOrCanceled)
        {
            Debug.LogError($"Found error while trying to create lobby.\n" +
                $"{createLobbyTask.Message}");
            LobbyMenuView.Instance.ShowView<MainMenuPanel>();
            return;
        }

        LobbyMenuView.Instance.ShowView<LobbyViewPanel>();
    }
}
