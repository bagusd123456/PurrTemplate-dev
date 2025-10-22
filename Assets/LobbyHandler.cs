using System;
using System.Collections;
using System.Collections.Generic;
using PurrLobby;
using PurrNet;
using PurrNet.Logging;
using PurrNet.Modules;
using PurrNet.Steam;
using Steamworks;
using UnityEngine;
using UnityEngine.SceneManagement;
using SteamClient = Steamworks.SteamClient;

public class LobbyHandler : MonoBehaviour
{
    public LobbyManager lobbyManager;
    public SteamTransport transport;
    public NetworkManager networkManager;

    public GameObject _playerPrefab;

    public static LobbyHandler Instance { get; private set; }
    private void Awake()
    {
        // If there is an instance, and it's not me, delete myself.

        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(this.gameObject);
            networkManager.onPlayerLoadedScene += OnPlayerLoadedScene;
        }
    }

    private void OnEnable()
    {
        lobbyManager.OnRoomJoined.AddListener(HandleJoinLobby);
    }

    private void HandleJoinLobby(Lobby arg0)
    {
        if (arg0.Properties.TryGetValue("ownerSteamId", out var value))
        {
            if (NetworkManager.main.transport is SteamTransport steamTransport)
            {
                steamTransport.peerToPeer = true;
                steamTransport.dedicatedServer = false;
                steamTransport.address = value;
            
                NetworkManager.main.StartClient();
                //NetworkManager.main.sceneModule.LoadSceneAsync("Demo");
                SceneManager.LoadScene("Demo");
                // Log
                Debug.Log($"Joining Lobby with steamID: {value}");
                lobbyManager.OnRoomJoined.RemoveListener(HandleJoinLobby);
            }
        }
    }

    private void OnPlayerLoadedScene(PlayerID player, SceneID scene, bool asServer)
    {
        var main = NetworkManager.main;

        if (!main || !main.TryGetModule(out ScenesModule scenes, true))
            return;

        var unityScene = gameObject.scene;

        if (!scenes.TryGetSceneID(unityScene, out var sceneID))
            return;

        if (sceneID != scene)
            return;

        if (!asServer)
            return;

        bool isDestroyOnDisconnectEnabled = main.networkRules.ShouldDespawnOnOwnerDisconnect();
        if (!isDestroyOnDisconnectEnabled && main.TryGetModule(out GlobalOwnershipModule ownership, true) &&
            ownership.PlayerOwnsSomething(player))
            return;

        if (unityScene.name != "Demo")
        {
            return;
        }

        _playerPrefab.transform.GetPositionAndRotation(out var position, out var rotation);
        var newPlayer = UnityProxy.Instantiate(_playerPrefab, position, rotation, unityScene);

        if (newPlayer.TryGetComponent(out NetworkIdentity identity))
            identity.GiveOwnership(player);
    }
}
