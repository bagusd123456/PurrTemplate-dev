using PurrLobby;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LobbySelectionPanel : View
{
    [SerializeField] private LobbyEntry lobbyEntryPrefab;
    [SerializeField] private GameObject lobbyEntryContainer;
    [SerializeField] private Button leaveMenuButton;

    private List<LobbyEntry> lobbyEntryList = new();
    private int searchIntervalMinutes = 5;
    private CancellationTokenSource _cancellationTokenSource;

    private void OnEnable()
    {
        leaveMenuButton.onClick.AddListener(HandleLeaveMenuButton);
        _cancellationTokenSource = new CancellationTokenSource();

        // Start the lobby search loop
        _ = HandleSearchLobbyLoop(_cancellationTokenSource.Token);
    }

    private void OnDisable()
    {
        leaveMenuButton.onClick.RemoveListener(HandleLeaveMenuButton);

        // Cancel the token and dispose of the source when the object is disabled
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private void HandleLeaveMenuButton()
    {
        _cancellationTokenSource?.Cancel();
        LobbyMenuView.Instance.ShowView<MainMenuPanel>();
    }

    private async Task HandleSearchLobbyLoop(CancellationToken cancellationToken)
    {
        while (gameObject.activeSelf && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Asynchronously search for lobbies
                var searchResult = await LobbyHandler.lobbyManager.CurrentProvider.SearchLobbiesAsync(new());

                // If the task was cancelled, we should not process the result
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // Initialize the lobby list with the results
                InitLobbyList(searchResult);
            }
            catch (Exception ex)
            {
                // Log any errors that occur during the search
                // This prevents the loop from crashing on a single failed attempt
                Debug.LogError($"Error searching for lobbies: {ex.Message}");
            }

            try
            {
                // Wait for the specified interval before the next search
                // This is a more efficient and readable approach than using a Stopwatch
                await Task.Delay(TimeSpan.FromMinutes(searchIntervalMinutes), cancellationToken);
            }
            catch (TaskCanceledException)
            {
                // This exception is expected when the token is cancelled, so we can break the loop
                break;
            }
        }
    }

    private void InitLobbyList(List<Lobby> targetList)
    {
        foreach (var lobby in lobbyEntryList)
        {
            lobby.gameObject.SetActive(false);
        }

        for (int i = 0; i < targetList.Count; i++)
        {
            Lobby item = targetList[i];
            var lobbyEntry = lobbyEntryList.Count > i ? lobbyEntryList[i] : Instantiate(lobbyEntryPrefab, lobbyEntryContainer.transform);

            lobbyEntry.Init(item);
            lobbyEntry.OnJoinButtonClicked -= HandleJoinButtonClicked;
            lobbyEntry.OnJoinButtonClicked += HandleJoinButtonClicked;
        }
    }

    private async void HandleJoinButtonClicked(Lobby targetLobby)
    {
        var loadingPanel = LobbyMenuView.Instance.ShowView<LobbyLoadingPanel>() as LobbyLoadingPanel;
        loadingPanel.Set("Joining Lobby...");
        var createLobbyTask = await LobbyHandler.JoinLobbyByIdAsync(targetLobby.LobbyId);
        if (createLobbyTask.IsSuccess)
        {
            var lobby = createLobbyTask.Result;
            if (lobby.Properties.TryGetValue("isStarted", out var startedState))
            {
                if (startedState.Equals("true"))
                {
                    SceneManager.LoadScene("Demo");
                    return;
                }
            }
        }
        LobbyMenuView.Instance.ShowView<LobbyViewPanel>();
    }
}
