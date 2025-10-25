using PurrLobby;
using PurrNet;
using UnityEngine;

public class NetworkInitiator : MonoBehaviour
{
    public static NetworkInitiator Instance { get; private set; }
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private LobbyManager lobbyManager;
    public LobbyHandler LobbyHandler { get; private set; }


    private async void Awake()
    {
        // If there is an instance, and it's not me, delete myself.

        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        LobbyHandler = new LobbyHandler(lobbyManager);
        await LobbyHandler.Init();
    }
}
