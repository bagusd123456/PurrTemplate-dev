using PurrNet;
using PurrNet.Modules;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

public class OnlineGameExecutor : NetworkBehaviour
{
    [PurrScene] public string gameplayScene;
    [SerializeField] private NetworkIdentity carPrefab;

    private Dictionary<PlayerID, GameObject> spawnedCarList = new();

    public static OnlineGameExecutor Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        //NetworkManager.main.onPlayerLoadedScene += HandlePlayerSceneLoaded;
        //NetworkManager.main.onPlayerUnloadedScene += HandlePlayerSceneUnloaded;
        NetworkManager.main.onPlayerJoinedScene += HandlePlayerSceneLoaded;
        NetworkManager.main.onPlayerLeftScene += HandlePlayerSceneUnloaded;
    }

    private void HandlePlayerSceneUnloaded(PlayerID player, SceneID scene, bool asServer)
    {
        var gameplaySceneData = SceneManager.GetSceneByName(gameplayScene);
        if (!gameplaySceneData.IsValid() && !gameplaySceneData.isLoaded)
        {
            return;
        }

        if (!LobbyHandler.networkManager.sceneModule.TryGetSceneID(gameplaySceneData, out var gameplaySceneId))
        {
            return;
        }

        if (gameplaySceneId != scene)
        {
            return;
        }

        DespawnPlayer(player);
    }

    private void HandlePlayerSceneLoaded(PlayerID player, SceneID scene, bool asServer)
    {
        var gameplaySceneData = SceneManager.GetSceneByName(gameplayScene);
        if (!gameplaySceneData.IsValid() && !gameplaySceneData.isLoaded)
        {
            return;
        }

        if (!LobbyHandler.networkManager.sceneModule.TryGetSceneID(gameplaySceneData, out var gameplaySceneId))
        {
            return;
        }

        if (gameplaySceneId != scene)
        {
            return;
        }

        SpawnPlayer(player);
    }

    private void SpawnPlayer(PlayerID player)
    {
        if (!isServer)
        {
            return;
        }

        if (spawnedCarList.TryGetValue(player, out var playerCharacter))
        {
            return;
        }

        var spawnedCar = Instantiate(carPrefab.gameObject);
        spawnedCarList[player] = spawnedCar;
        NetworkManager.main.Spawn(spawnedCar);
        carPrefab.GiveOwnership(player);
    }

    private void DespawnPlayer(PlayerID player)
    {
        if (!isServer)
        {
            return;
        }

        if (spawnedCarList.TryGetValue(player, out var playerObject)) 
        { 
            spawnedCarList.Remove(player);
            Destroy(playerObject);
        }
    }

    public async Task<AsyncResult> ChangeScene()
    {
        PurrSceneSettings settings = new()
        {
            isPublic = true,
            mode = LoadSceneMode.Single
        };
        var loadSceneTask = LobbyHandler.networkManager.sceneModule.LoadSceneAsync(gameplayScene, settings);
        while (!loadSceneTask.isDone)
        {
            await Task.Yield();
        }

        return AsyncResult.Success();
    }

    [ServerRpc(requireOwnership: false)]
    public void RequestSceneChange(RPCInfo info = default)
    {
        var scene = SceneManager.GetSceneByName(gameplayScene);
        if (scene.isLoaded)
            return;

        if (LobbyHandler.networkManager.sceneModule.TryGetSceneID(scene, out var sceneId))
        {
            LobbyHandler.networkManager.scenePlayersModule.AddPlayerToScene(info.sender, sceneId);
        }
    }
}
