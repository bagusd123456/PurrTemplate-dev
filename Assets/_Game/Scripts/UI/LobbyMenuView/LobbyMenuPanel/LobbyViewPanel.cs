using PurrLobby;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class LobbyViewPanel : View
{
    [SerializeField] private CodeButton lobbyCode;
    [SerializeField] private Button setReadyButton;
    [SerializeField] private Button leaveLobbyButton;
    [SerializeField] private FriendEntry friendStatusPrefab;
    [SerializeField] private GameObject friendListContainer;
    [SerializeField] private MemberEntry lobbyMemberPrefab;
    [SerializeField] private GameObject lobbyMemberContainer;

    private List<FriendEntry> friendEntryList = new();
    private List<MemberEntry> lobbyEntryList = new();

    private bool IsReady;
    private bool IsStarting;

    private float pullFriendsIntervalMinutes = 3.0f;

    private CancellationTokenSource _cancellationTokenSource;

    private void Start()
    {
        setReadyButton.onClick.AddListener(HandleSetReady);
        leaveLobbyButton.onClick.AddListener(HandleLeaveLobby);
    }

    private void OnEnable()
    {
        if (LobbyHandler.lobbyManager.CurrentLobby.IsValid)
        {
            HandleRoomUpdated(LobbyHandler.lobbyManager.CurrentLobby);
        }
        LobbyHandler.lobbyManager.OnRoomUpdated.AddListener(HandleRoomUpdated);
        LobbyHandler.lobbyManager.OnFriendListPulled.AddListener(HandleFriendListPulled);

        // It's best practice to create a new CancellationTokenSource in OnEnable.
        _cancellationTokenSource = new CancellationTokenSource();
        // Start the asynchronous loop and pass it the token
        _ = PullFriendsLoop(_cancellationTokenSource.Token);
    }

    private void OnDisable()
    {
        LobbyHandler.lobbyManager.OnRoomUpdated.RemoveListener(HandleRoomUpdated);
        LobbyHandler.lobbyManager.OnFriendListPulled.RemoveListener(HandleFriendListPulled);

        // Cancel the token to stop the loop and dispose of the source.
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private async Task PullFriendsLoop(CancellationToken cancellationToken)
    {
        // The loop continues as long as the object is active and cancellation hasn't been requested
        while (gameObject.activeSelf && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Asynchronously get the list of friends
                var friendsList = await LobbyHandler.lobbyManager.CurrentProvider.GetFriendsAsync(new());

                // Check if cancellation was requested while waiting
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                // Log errors to prevent the loop from crashing
                Debug.LogError($"Error pulling friends list: {ex.Message}");
            }

            try
            {
                // Wait for the specified interval in a non-blocking way
                await Task.Delay(TimeSpan.FromMinutes(pullFriendsIntervalMinutes), cancellationToken);
            }
            catch (TaskCanceledException)
            {
                // This is expected when OnDisable is called, so we break out of the loop
                break;
            }
        }
    }

    private void HandleFriendListPulled(List<FriendUser> friendList)
    {
        foreach (var friendEntry in friendEntryList)
        {
            friendEntry.gameObject.SetActive(false);
        }

        for (int i = 0; i < friendList.Count; i++)
        {
            var user = friendList[i];
            var friendEntry = friendEntryList.Count > i ? friendEntryList[i] : null;
            if (friendEntry == null)
            {
                friendEntry = Instantiate(friendStatusPrefab, friendListContainer.transform);
                friendEntryList.Add(friendEntry);
            }

            friendEntry.Init(user, LobbyHandler.lobbyManager);
            friendEntry.gameObject.SetActive(true);
        }
    }

    private void HandleRoomUpdated(Lobby currentLobby)
    {
        lobbyCode.Init(currentLobby.LobbyId);
        foreach (var memberEntry in lobbyEntryList)
        {
            memberEntry.gameObject.SetActive(false);
        }

        for (int i = 0; i < currentLobby.Members.Count; i++)
        {
            var user = currentLobby.Members[i];
            var memberEntry = lobbyEntryList.Count > i ? lobbyEntryList[i] : null;
            if (memberEntry == null)
            {
                memberEntry = Instantiate(lobbyMemberPrefab, lobbyMemberContainer.transform);
                lobbyEntryList.Add(memberEntry);
            }

            memberEntry.Init(user);
            memberEntry.gameObject.SetActive(true);
        }

        var isMemberReady = currentLobby.Members.All(x => x.IsReady);
        if (isMemberReady)
        {
            HandlePlayGame();
        }
    }

    private async void HandlePlayGame()
    {
        if (IsStarting) 
        {
            Debug.Log($"Play Game already triggered.");
            return;
        }

        Debug.Log("Starting Game...");
        var loadingPanel = LobbyMenuView.Instance.ShowView<LobbyLoadingPanel>() as LobbyLoadingPanel;
        loadingPanel.Set($"Starting Game...");

        if (LobbyHandler.networkManager.isServer)
        {
            await LobbyHandler.SetLobbyStartedAsync();
            await OnlineGameExecutor.Instance.ServerChangeScene();
        }
        LobbyMenuView.Instance.HideView<LobbyLoadingPanel>();
    }

    private async void HandleSetReady()
    {
        var loadingPanel = LobbyMenuView.Instance.ShowView<LobbyLoadingPanel>() as LobbyLoadingPanel;
        IsReady = !IsReady;
        loadingPanel.Set($"Setting ready to '{IsReady}'...");
        var setReadyTask = await LobbyHandler.SetIsReadyAsync(IsReady);
        LobbyMenuView.Instance.ShowView<LobbyViewPanel>();
    }

    private async void HandleLeaveLobby()
    {
        var loadingPanel = LobbyMenuView.Instance.ShowView<LobbyLoadingPanel>() as LobbyLoadingPanel;
        loadingPanel.Set($"Leaving Lobby...");
        var leavelobbyTask = await LobbyHandler.LeaveLobbyAsync();

        LobbyMenuView.Instance.ShowView<MainMenuPanel>();
    }
}
