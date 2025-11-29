using PurrNet;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages the online game state, specifically handling the spawning and despawning of player prefabs
/// as they transition into and out of the main gameplay scene. This class operates as a server-authoritative singleton,
/// meaning all core logic is executed only on the server to maintain a consistent game state for all clients.
/// It is designed to be persistent across scene loads.
/// </summary>
public class OnlineGameExecutor : NetworkBehaviour
{
    [Header("Scene and Prefab Configuration")]
    /// <summary>
    /// The name of the scene that is considered the primary gameplay area.
    /// Player prefabs will be spawned when a player loads this scene.
    /// The [PurrScene] attribute provides a dropdown in the Unity Inspector for easy scene selection.
    /// </summary>
    [PurrScene] public string gameplayScene;

    /// <summary>
    /// The network-aware prefab to be instantiated for each player.
    /// This prefab must have a NetworkIdentity component.
    /// </summary>
    [SerializeField] private NetworkIdentity carPrefab;

    /// <summary>
    /// A server-side dictionary that tracks the spawned character GameObject for each connected player.
    /// Key: The PlayerID of the player.
    /// Value: The spawned GameObject representing the player in the game world.
    /// </summary>
    private Dictionary<PlayerID, GameObject> spawnedCarList = new();

    /// <summary>
    /// Gets the singleton instance of the OnlineGameExecutor.
    /// This provides a global access point for other scripts to interact with this manager.
    /// </summary>
    public static OnlineGameExecutor Instance { get; private set; }

    /// <summary>
    /// Called when the script instance is being loaded.
    /// Initializes the singleton pattern, ensures the object persists across scene loads,
    /// and subscribes to essential network events for scene transitions.
    /// </summary>
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[OnlineGameExecutor] Duplicate instance detected. Destroying the new one.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("[OnlineGameExecutor] Singleton instance initialized.");

        // Subscribe to network events. This event-driven approach is key to reacting
        // to players loading and unloading scenes dynamically.
        NetworkManager.main.onPlayerLoadedScene += HandlePlayerSceneLoaded;
        NetworkManager.main.onPlayerUnloadedScene += HandlePlayerSceneUnloaded;
    }

    /// <summary>
    /// Called when the MonoBehaviour will be destroyed.
    /// Unsubscribes from network events to prevent memory leaks and unexpected errors
    /// if the NetworkManager is destroyed before this object.
    /// </summary>
    private void OnDestroy()
    {
        if (NetworkManager.main != null)
        {
            NetworkManager.main.onPlayerLoadedScene -= HandlePlayerSceneLoaded;
            NetworkManager.main.onPlayerUnloadedScene -= HandlePlayerSceneUnloaded;
        }
    }

    /// <summary>
    /// Server-side callback triggered when a player finishes unloading a scene.
    /// This method filters events to act only when the player has left the main gameplay scene.
    /// </summary>
    /// <param name="player">The ID of the player who unloaded the scene.</param>
    /// <param name="lastScene">The ID of the scene the player left.</param>
    /// <param name="asServer">Indicates if this callback is running on the server.</param>
    private void HandlePlayerSceneUnloaded(PlayerID player, SceneID lastScene, bool asServer)
    {
        if (!isServer) return;

        Debug.Log($"[HandlePlayerSceneUnloaded] Player '{player.id}' left scene '{lastScene.id}'.");
        HandleDespawnGameplayPlayer(player, lastScene);
    }

    /// <summary>
    /// This is the core event handler for synchronizing player characters, including for late-joiners.
    /// It's called on the server whenever ANY player (new or existing) finishes loading ANY scene.
    /// This ensures that when a player enters the gameplay scene, their character is spawned
    /// and they see all other players who are already in that scene.
    /// </summary>
    /// <param name="player">The ID of the player who loaded the scene.</param>
    /// <param name="scene">The ID of the scene the player finished loading.</param>
    /// <param name="asServer">Indicates if this callback is running on the server.</param>
    private void HandlePlayerSceneLoaded(PlayerID player, SceneID scene, bool asServer)
    {
        if (!isServer) return;

        Debug.Log($"[HandlePlayerSceneLoaded] Player '{player.id}' finished loading scene '{scene.id}'.");
        HandleSpawnGameplayPlayer(player, scene);
    }

    /// <summary>
    /// Spawns a character prefab for a given player on the network if one doesn't already exist.
    /// This method is server-only.
    /// </summary>
    /// <param name="player">The PlayerID for whom to spawn the character.</param>
    /// <remarks>
    /// This method checks for pre-existing or stale entries in the `spawnedCarList` to prevent duplicates.
    /// After instantiation, it uses `NetworkManager.main.Spawn()` to make the object visible to all clients
    /// and then `GiveOwnership()` to allow the target client to control their character.
    /// </remarks>
    private void SpawnPlayer(PlayerID player)
    {
        return;
        if (!isServer) return;

        // Check for existing entries, including "stale" entries where the GameObject was destroyed but the key remains.
        if (spawnedCarList.TryGetValue(player, out var playerCharacter) && playerCharacter != null)
        {
            Debug.Log($"[SpawnPlayer] Character for player '{player.id}' already exists. Skipping spawn.");
            return;
        }

        Debug.Log($"[SpawnPlayer] Spawning a new character for player '{player.id}'.");

        // Instantiate the prefab at a random position.
        var position = new Vector3(Random.Range(-10, 10), 1, Random.Range(-10, 10));
        var spawnedCar = Instantiate(carPrefab.gameObject, position, Quaternion.identity);
        spawnedCarList[player] = spawnedCar; // Track the new car.

        // Spawn the object across the network so all clients are aware of it.
        NetworkManager.main.Spawn(spawnedCar);
        Debug.Log($"[SpawnPlayer] Network-spawned character for player '{player.id}'.");

        // Assign ownership of the spawned object to the corresponding player.
        if (spawnedCar.TryGetComponent<NetworkIdentity>(out var networkIdentity))
        {
            networkIdentity.GiveOwnership(player);
            Debug.Log($"[SpawnPlayer] Assigned ownership of character to player '{player.id}'.");
        }
    }

    /// <summary>
    /// Despawns a player's character prefab from the network.
    /// This method is server-only.
    /// </summary>
    /// <param name="player">The PlayerID whose character should be despawned.</param>
    public void DespawnPlayer(PlayerID player)
    {
        if (!isServer) return;

        if (spawnedCarList.TryGetValue(player, out var playerObject))
        {
            Debug.Log($"[DespawnPlayer] Found character for player '{player.id}'. Preparing to despawn.");
            spawnedCarList.Remove(player);

            if (playerObject != null)
            {
                Destroy(playerObject);
                Debug.Log($"[DespawnPlayer] Successfully despawned character for player '{player.id}' via NetworkManager.");
            }
            else
            {
                Debug.LogWarning($"[DespawnPlayer] Character for player '{player.id}' was already null before despawning.");
            }
        }
        else
        {
            Debug.LogWarning($"[DespawnPlayer] Could not find a spawned character for player '{player.id}' to despawn.");
        }
    }

    /// <summary>
    /// Logic to determine if a player's character should be despawned based on the scene they left.
    /// </summary>
    /// <param name="player">The player who left the scene.</param>
    /// <param name="lastScene">The scene that the player unloaded.</param>
    private void HandleDespawnGameplayPlayer(PlayerID player, SceneID lastScene)
    {
        var gameSceneData = SceneManager.GetSceneByName(gameplayScene);
        if (!gameSceneData.IsValid())
        {
            Debug.LogError("Invalid Scene name configured in 'gameplayScene'.");
            return;
        }

        // Try to get the network SceneID for our configured gameplay scene name.
        if (!LobbyHandler.networkManager.sceneModule.TryGetSceneID(gameSceneData, out var gameplaySceneId))
        {
            return;
        }

        // If the scene the player left is not the main gameplay scene, do nothing.
        if (gameplaySceneId != lastScene)
        {
            return;
        }

        Debug.Log($"Player '{player.id}' has left the gameplay scene. Despawning their character.");
        DespawnPlayer(player);
    }

    /// <summary>
    /// Logic to determine if a player's character should be spawned based on the scene they loaded.
    /// This also handles spawning existing players for the new joiner.
    /// </summary>
    /// <param name="player">The player who loaded the scene.</param>
    /// <param name="scene">The scene that was loaded by the player.</param>
    private void HandleSpawnGameplayPlayer(PlayerID player, SceneID scene)
    {
        // Ensure the gameplay scene is valid and loaded on the server.
        var gameplaySceneData = SceneManager.GetSceneByName(gameplayScene);
        if (!gameplaySceneData.IsValid() || !gameplaySceneData.isLoaded)
        {
            Debug.LogWarning($"[HandlePlayerSceneLoaded] Gameplay scene '{gameplayScene}' is not valid or loaded on server. Aborting spawn check.");
            return;
        }

        // Get the network SceneID for the gameplay scene.
        if (!LobbyHandler.networkManager.sceneModule.TryGetSceneID(gameplaySceneData, out var gameplaySceneId))
        {
            Debug.LogError($"[HandlePlayerSceneLoaded] Failed to get SceneID for gameplay scene '{gameplayScene}'.");
            return;
        }

        // Get the list of scenes the player is currently in.
        if (!LobbyHandler.networkManager.TryGetPlayerScenes(player, out var currentPlayerScenes))
        {
            Debug.LogError($"[HandlePlayerSceneLoaded] Could not get player '{player.id}' current scene list.");
            return;
        }

        // If the player is not in the gameplay scene, do nothing.
        if (!currentPlayerScenes.Any(s => s.id == gameplaySceneId.id))
        {
            return;
        }

        // At this point, a player has entered the gameplay scene.
        // We must ensure that EVERY player in that scene has a character spawned.
        // This handles both the new player and ensures existing players are visible to them.
        foreach (var playerToSpawn in LobbyHandler.networkManager.players)
        {
            // Verify each player is actually in the gameplay scene before spawning them.
            if (LobbyHandler.networkManager.TryGetPlayerScenes(playerToSpawn, out var scenesOfPlayer) &&
                scenesOfPlayer.Any(s => s.id == gameplaySceneId.id))
            {
                SpawnPlayer(playerToSpawn);
            }
        }
    }

    /// <summary>
    /// Initiates a scene change from the server. This is the entry point for server-driven scene transitions.
    /// </summary>
    /// <returns>A task that completes when the operation is finished.</returns>
    /// <remarks>
    /// This method calls an ObserversRpc, which in turn tells all clients to request a scene change.
    /// This pattern ensures the server remains in control of the scene transition process.
    /// </remarks>
    public async Task<AsyncResult> ServerChangeScene()
    {
        Debug.Log("[ServerChangeScene] Server is initiating a scene change.");
        NotifyObserverSceneChange();
        return AsyncResult.Success();
    }

    /// <summary>
    /// A Remote Procedure Call (RPC) sent from a client to the server to request a scene change.
    /// </summary>
    /// <param name="info">RPC metadata, including the sender's PlayerID.</param>
    /// <returns>A task that resolves to true upon successful processing.</returns>
    [ServerRpc(requireOwnership: false, asyncTimeoutInSec: 30f)]
    private async Task<bool> ClientRequestSceneChange(RPCInfo info = default)
    {
        Debug.Log($"[ClientRequestSceneChange] Server received scene change request from client '{info.sender.id}'.");
        await ServerSceneChangeAsync(info.sender);
        return true;
    }

    /// <summary>
    /// The core server-side logic for changing a scene asynchronously for a specific player.
    /// </summary>
    /// <param name="playerId">The ID of the player for whom the scene change is being processed.</param>
    /// <returns>A task that resolves to true if the scene change was successful.</returns>
    private async Task<bool> ServerSceneChangeAsync(PlayerID playerId)
    {
        LoadSceneParameters loadSceneParam = new()
        {
            loadSceneMode = LoadSceneMode.Single, // Replaces all current scenes with this one.
        };

        // Asynchronously load the scene on the server.
        var loadSceneTask = LobbyHandler.networkManager.sceneModule.LoadSceneAsync(gameplayScene, loadSceneParam);
        while (!loadSceneTask.isDone)
        {
            await Task.Yield(); // Wait for the scene to finish loading.
        }

        var scene = SceneManager.GetSceneByName(gameplayScene);
        if (!scene.IsValid())
        {
            Debug.LogError($"[ServerSceneChangeAsync] The loaded scene '{gameplayScene}' is invalid!");
            return false;
        }

        // Once the scene is loaded, add the player to the scene's tracking list.
        if (LobbyHandler.networkManager.sceneModule.TryGetSceneID(scene, out var sceneId))
        {
            LobbyHandler.networkManager.scenePlayersModule.AddPlayerToScene(playerId, sceneId);
            Debug.Log($"Assigned player '{playerId.id}' to the new scene with ID '{sceneId.id}'.");
        }

        Debug.Log($"Server has finished loading scene '{gameplayScene}'.");
        return true;
    }

    /// <summary>
    /// An RPC sent from the server to all connected clients (observers).
    /// This method instructs each client to call the `ClientRequestSceneChange` RPC,
    /// effectively telling the server they are ready to change scenes.
    /// </summary>
    [ObserversRpc]
    private void NotifyObserverSceneChange()
    {
        Debug.Log("[NotifyObserverSceneChange] Client has been notified by the server to change scenes.");
        // The client, upon receiving this, will send a request back to the server.
        _ = ClientRequestSceneChange();
    }
}