using PurrLobby;
using PurrNet;
using Steamworks;
using System;
using UnityEngine;

public class NetworkInitiator : MonoBehaviour
{
    public static NetworkInitiator Instance { get; private set; }
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private LobbyManager lobbyManager;
    [SerializeField] private OnlineGameExecutor onlineGameExecutor;
    public LobbySystem LobbySystem { get; private set; }

    private async void Awake()
    {
        // If there is an instance, and it's not me, delete myself.

        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LobbySystem = new LobbySystem(lobbyManager, networkManager);
        await LobbySystem.Init();
        Instantiate(onlineGameExecutor);
    }

    [PurrButton()]
    private void ShutdownSteamAPI()
    {
        try
        {
            SteamAPI.Shutdown();
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to Shutdown SteamAPI.\n" +
                $"{e}");
        }
    }
}
