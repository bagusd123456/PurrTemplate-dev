using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrLobby;
using PurrLobby.Providers;
using PurrNet;
using PurrNet.Steam;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LobbyMenuView : MonoBehaviour
{
    [SerializeField] private LobbyManager lobbyManager;
    private SteamLobbyProvider _lobbyProvider;
    private UnityLobbyProvider _unityLobbyProvider;
    [SerializeField] private LobbyButtonContainer lobbyButtonPrefab;
    [SerializeField] private Transform lobbyButtonParent;

    private List<LobbyButtonContainer> _lobbyButtons = new();

    private void OnEnable()
    {
        //lobbyManager.OnRoomUpdated.AddListener(HandleRefreshView);
    }

    private void OnDisable()
    {
        //lobbyManager.OnRoomUpdated.RemoveListener(HandleRefreshView);
    }

    private async void Awake()
    {
        while (_lobbyButtons.Count < 5)
        {
            var button = Instantiate(lobbyButtonPrefab, lobbyButtonParent);
            button.gameObject.SetActive(false);
            _lobbyButtons.Add(button);
        }

        await SetLobbyList();
    }

    [ContextMenu("Set Lobby List")]
    private async Task SetLobbyList()
    {
        Debug.Log("Loading Room");
        var lobbyList = await lobbyManager.CurrentProvider.SearchLobbiesAsync(64);

        for (int i = 0; i < lobbyList.Count; i++)
        {
            LobbyButtonContainer button;
            if (i < _lobbyButtons.Count)
                button = _lobbyButtons[i];
            else
            {
                var newButton = Instantiate(lobbyButtonPrefab, lobbyButtonParent);
                _lobbyButtons.Add(newButton);
                button = newButton;
            }
            button.gameObject.SetActive(true);
            button.Initialize(lobbyList[i], lobbyManager);
        }
        Debug.Log("Loading Room Finished");
    }

    private void ClearLobbyList()
    {
        foreach (var button in _lobbyButtons)
        {
            button.gameObject.SetActive(false);
        }
    }

    [ContextMenu("Create Lobby")]
    private async void CreateLobby()
    {
        try
        {
            Debug.Log("Creating Lobby");
            var ownerSteamId = "";
            if (NetworkManager.main.transport is SteamTransport steamTransport)
            {
                ownerSteamId = Steamworks.SteamUser.GetSteamID().ToString();
                steamTransport.peerToPeer = true;
                steamTransport.dedicatedServer = false;
                steamTransport.address = ownerSteamId;
            }

            var lobbyProperties = new Dictionary<string, string>()
            {
                { "ownerSteamId", ownerSteamId }
            };

            var currentLobby = await lobbyManager.CurrentProvider.CreateLobbyAsync(32, lobbyProperties);
            
            NetworkManager.main.StartHost();
            NetworkManager.main.sceneModule.LoadSceneAsync("Demo");

            Debug.Log("Creating Lobby Finished.");
        }
        catch (Exception e)
        {
            Debug.LogError(e);
            throw;
        }
    }

    private void HandleRefreshView(Lobby arg0)
    {
        throw new NotImplementedException();
    }
}