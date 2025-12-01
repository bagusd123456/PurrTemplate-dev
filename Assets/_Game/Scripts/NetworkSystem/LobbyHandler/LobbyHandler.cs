using NyxMachina.Multiplayer;
using NyxMachina.Shared.EventFramework;
using PurrLobby;
using PurrNet;
using PurrNet.Steam;
using PurrNet.Transports;
using QFSW.QC;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using ConnectionState = PurrNet.Transports.ConnectionState;

public class LobbyHandler
{
    public static LobbyManager lobbyManager;
    public static NetworkManager networkManager;
    public static ChatRoomHandler chatRoomHandler;
    public static Task steamCallbackTask;

    public static PlayerID currentPlayerId;

    public static Dictionary<ulong, ILobbyDataModel> PlayerList = new();

    public LobbyHandler(LobbyManager lobbyManager, NetworkManager networkManager)
    {
        LobbyHandler.lobbyManager = lobbyManager;
        LobbyHandler.networkManager = networkManager;

        lobbyManager.OnRoomUpdated.RemoveListener(HandleRoomUpdated);
        lobbyManager.OnRoomUpdated.AddListener(HandleRoomUpdated);

        // Remove old listeners to prevent duplicates if re-initialized
        networkManager.onPlayerJoined -= HandleOnPlayerJoin;
        networkManager.onPlayerLeft -= HandleOnPlayerLeft;

        networkManager.onPlayerJoined += HandleOnPlayerJoin;
        networkManager.onPlayerLeft += HandleOnPlayerLeft;
    }

    public async Task<AsyncResult> Init()
    {
        var errorMessage = "";

        Application.quitting -= AppQuitting;
        Application.quitting += AppQuitting;

        try
        {
            var initState = SteamAPI.Init();
            if (!initState)
            {
                SteamAPI.InitEx(out var initException);
                throw new Exception($"[LobbyHandler] Steam Failed to init.\n" +
                                    $"{initException}");
            }
            steamCallbackTask = RunSteamCallback();
            await lobbyManager.CurrentProvider.InitializeAsync();

            chatRoomHandler = new ChatRoomHandler();
        }
        catch (Exception e)
        {
            errorMessage = $"[LobbyHandler] Init Failed.\n{e.Message}";
            Debug.LogError(errorMessage);
            return AsyncResult.Fail(errorMessage);
        }

        return AsyncResult.Success();
    }

    private void AppQuitting()
    { 
        Debug.Log("App quitting.");
        _ = LeaveLobbyAsync();
    }

    private async Task RunSteamCallback()
    {
        var runCallbacks = true;
        while (runCallbacks)
        {
            SteamAPI.RunCallbacks();
            await Task.Delay(16);
        }
    }

    #region Data Access Wrappers

    public static async Task SetLobbyData(string key, string value) => 
        await LobbyDataHandler.SetLobbyDataAsync(key, value);

    public static async Task SetLocalUserData(string key, string value) => 
        await LobbyDataHandler.SetPlayerData(key, value);

    public static string GetLobbyData(string key) => 
        LobbyDataHandler.GetLobbyData(key);
    
    #endregion

    #region Lobby Logic

    [Command]
    public static async Task<AsyncResult<Lobby>> CreateLobbyAsync(int targetMaxPlayer = 4, Dictionary<string, string> targetRoomProperties = default)
    {
        string steamAccountId = "";
        var errorMessage = "";
        Lobby createdLobby;

        targetRoomProperties ??= new Dictionary<string, string>();

        try
        {
            var networkTransport = networkManager.transport;
            if (networkTransport is SteamTransport steamTransport)
            {
                steamAccountId = SteamUser.GetSteamID().ToString();
                targetRoomProperties["steamAccountId"] = steamAccountId;
                steamTransport.address = steamAccountId;
            }

            createdLobby = await lobbyManager.CurrentProvider.CreateLobbyAsync(targetMaxPlayer, targetRoomProperties);

            var startServerTask = await StartClientServerAsync(networkTransport);
            if (startServerTask.IsFail)
            {
                Debug.LogError(startServerTask.Message);
                return AsyncResult<Lobby>.Fail(startServerTask.Message);
            }
        }
        catch (Exception e)
        {
            errorMessage = $"Failed to create lobby.\n" +
                           $"Unknown Error: {e.Message}";
            Debug.LogError(errorMessage);
            return AsyncResult<Lobby>.Fail(errorMessage);
        }

        EVENT.Publish(new OnJoinLobby(createdLobby));
        return AsyncResult<Lobby>.Success(createdLobby);
    }

    [Command]
    public static async Task<AsyncResult<Lobby>> JoinLobbyByIdAsync(string roomId)
    {
        var errorMessage = "";
        Lobby currentLobby;

        try
        {
            // Join Steam Lobby (Platform Layer)
            Debug.Log("[LobbyHandler] Joining Steam Lobby...");
            currentLobby = await lobbyManager.CurrentProvider.JoinLobbyAsync(roomId);

            if (!currentLobby.IsValid)
                return AsyncResult<Lobby>.Fail("Steam Lobby is invalid.");

            if (currentLobby.Properties.TryGetValue("isPrivate", out string value) && value == "true")
            {
                await LeaveLobbyAsync();
                return AsyncResult<Lobby>.Fail("This lobby is private.");
            }

            if (!currentLobby.Properties.TryGetValue("steamAccountId", out var ownerSteamAccountId))
            {
                await LeaveLobbyAsync();
                errorMessage = "Cannot find steamAccountId in lobby properties.";
                Debug.LogError(errorMessage);
                return AsyncResult<Lobby>.Fail(errorMessage);
            }

            // Connect Transport (Physical Layer)
            Debug.Log("[LobbyHandler] Connecting to Host via Transport...");
            var networkTransport = networkManager.transport;
            var startClientTask = await StartClientAsync(networkTransport, ownerSteamAccountId);

            if (startClientTask.IsFail)
            {
                await LeaveLobbyAsync();
                return AsyncResult<Lobby>.Fail(startClientTask.Message);
            }

            // Handshake (Logic Layer via RPC)
            Debug.Log("[LobbyHandler] Connection established. Validating with Host...");

            string mySteamId = SteamUser.GetSteamID().ToString();
            AsyncResult result;

            try
            {
                // Awaitable RPC: Pauses here until Server replies
                result = await LobbyNetworkHandler.ValidateHandshakeAsync_RPC(mySteamId);
            }
            catch (Exception e)
            {
                // Handle RPC Timeout or Transport drop during await
                await LeaveLobbyAsync();
                return AsyncResult<Lobby>.Fail($"Failed to join Lobby.\n" +
                                               $"Handshake Failed, Unknown error: {e.Message}");
            }

            // Process Result
            if (!result.IsSuccess)
            {
                Debug.LogWarning($"[LobbyHandler] Host rejected connection: {result.Message}");
                await LeaveLobbyAsync();
                return AsyncResult<Lobby>.Fail($"Connection Rejected: {result.Message}");
            }

            Debug.Log("[LobbyHandler] Join Successful!");
        }
        catch (Exception e)
        {
            errorMessage = $"[LobbyHandler] Exception during Join: {e.Message}" +
                           $"\n{e.StackTrace}";
            Debug.LogError(errorMessage);
            await LeaveLobbyAsync();
            return AsyncResult<Lobby>.Fail(errorMessage);
        }

        EVENT.Publish(new OnJoinLobby(currentLobby));
        return AsyncResult<Lobby>.Success(currentLobby);
    }

    [Command]
    public static async Task<AsyncResult> LeaveLobbyAsync()
    {
        var errorMessage = "";
        var currentLobby = lobbyManager.CurrentLobby;

        try
        {
            // Stop Networking
            if (networkManager.isServer)
            {
                await StopClientServerAsync(networkManager.transport);
            }
            else
            {
                await StopClientAsync(networkManager.transport);
            }

            // Stop Steam Lobby
            if (currentLobby.IsValid)
            {
                await lobbyManager.CurrentProvider.LeaveLobbyAsync();
                EVENT.Publish(new OnLeftLobby(currentLobby));
            }

            if (ulong.TryParse(currentLobby.LobbyId, out var convertedLobbyId))
            {
                CSteamID lobbyIdStruct = new CSteamID(convertedLobbyId);
                SteamMatchmaking.LeaveLobby(lobbyIdStruct);
            }

            PlayerList.Clear();
        }
        catch (Exception e)
        {
            errorMessage = $"Failed to leave lobby.\n{e.Message}";
            Debug.LogError($"[LobbyHandler] {errorMessage}");
            return AsyncResult.Fail(errorMessage);
        }
        return AsyncResult.Success();
    }

    #endregion

    #region Gameplay State Management

    [Command]
    public static async Task<AsyncResult> SetIsReadyAsync(bool targetState)
    {
        var localUserId = await lobbyManager.CurrentProvider.GetLocalUserIdAsync();
        if (string.IsNullOrWhiteSpace(localUserId))
            return AsyncResult.Fail("Local User ID is null.");

        await SetIsReadyAsync(localUserId, targetState);

        return await LobbyDataHandler.SetPlayerData("IsReady", targetState);
    }

    public static async Task<AsyncResult> SetIsReadyAsync(string targetUserId, bool targetState)
    {
        try
        {
            await lobbyManager.CurrentProvider.SetIsReadyAsync(targetUserId, targetState);
        }
        catch (Exception e)
        {
            return AsyncResult.Fail(e.Message);
        }
        return AsyncResult.Success();
    }

    [Command]
    public static async Task<AsyncResult> SetAllReadyAsync()
    {
        if (!lobbyManager.CurrentLobby.IsValid || !lobbyManager.CurrentLobby.IsOwner)
            return AsyncResult.Fail("Invalid lobby or not owner.");

        try
        {
            await lobbyManager.CurrentProvider.SetAllReadyAsync();
        }
        catch (Exception e)
        {
            return AsyncResult.Fail(e.Message);
        }
        return AsyncResult.Success();
    }

    [Command]
    public static async Task<AsyncResult> SetLobbyStartedAsync()
    {
        try
        {
            await lobbyManager.CurrentProvider.SetLobbyStartedAsync();
        }
        catch (Exception e)
        {
            return AsyncResult.Fail("Failed to start Lobby.\n" +
                                    $"Unknown error: {e}");
        }
        
        return AsyncResult.Success();
    }

    public static async Task SavePlayerJsonToLobby(ulong clientId, string steamId)
    {
        // Gather all current data for this player from the Server's RAM
        if (!PlayerList.TryGetValue(clientId, out var user) || user is not SteamLobbyUser steamUser)
            return;

        var steamIdStruct = new CSteamID(clientId);
        // Build the data
        SteamLobbyUser model = new SteamLobbyUser(clientId, steamIdStruct, user.IsHost);

        // Copy everything else to Extra
        foreach((string key, var value) in steamUser.Extra)
        {
            if (key != "IsReady" && key != "AvatarId")
                model.Extra[key] = value.ToString();
        }

        // Serialize to JSON
        string json = JsonConvert.SerializeObject(model);
        string lobbyKey = $"User_{steamId}";

        // Write to Steam Global Lobby Data
        await lobbyManager.CurrentProvider.SetLobbyDataAsync(lobbyKey, json);
        
        Debug.Log($"[Host] Saved JSON for {steamUser.Username}: {json}");
    }

    private void HandleRoomUpdated(Lobby currentLobby)
    {
        // Refresh data when Steam says something changed
        foreach ((ulong key, var value) in PlayerList)
        {
            if (value is SteamLobbyUser steamUser)
            {
                // Deserialize DTO
                var serializedData = LobbyDataHandler.GetPlayerDataBySteamId(steamUser.SteamID.ToString());
                if (serializedData == null) continue;

                // Apply JSON data to local object
                if (serializedData.IsReady != steamUser.IsReady) 
                    steamUser.UpdateInternalData("IsReady", serializedData.IsReady.ToString());

                foreach (var extra in serializedData.Extra)
                {
                    steamUser.UpdateInternalData(extra.Key, extra.Value.ToString());
                }
            }
        }
    }

    #endregion

    #region Admin (Host Only)

    [Command]
    public static async Task<AsyncResult> KickPlayerAsync(ulong targetClientId)
    {
        // Validation
        if (!lobbyManager.CurrentLobby.IsOwner)
            return AsyncResult.Fail("Only the Host can kick players.");

        if (targetClientId == networkManager.localPlayer.id)
            return AsyncResult.Fail("You cannot kick yourself.");

        if (!PlayerList.TryGetValue(targetClientId, out var userData))
            return AsyncResult.Fail("Player not found.");

        try
        {
            Debug.Log($"[LobbyHandler] Kicking player {userData.Username}...");
            PlayerID? targetPlayer = null;

            foreach (var player in networkManager.players)
            {
                if (player.id.value != targetClientId) continue;
                targetPlayer = player;
                break;
            }

            if (!targetPlayer.HasValue)
                return AsyncResult.Fail($"Player with id '{targetClientId}' not found.");

            LobbyNetworkHandler.NotifyKicked_RPC(targetPlayer.Value);
            return AsyncResult.Success();

        }
        catch (Exception e)
        {
            return AsyncResult.Fail($"Kick failed: {e.Message}");
        }
    }

    public static async Task<AsyncResult> SetLobbyLockedStateAsync(bool isLocked)
    {
        if (!lobbyManager.CurrentLobby.IsOwner)
            return AsyncResult.Fail("Only the Host can lock the lobby.");

        // If this is true, lobby IsValid will return false

        // Steamworks: false if player can join; otherwise true.
        ulong lobbyId = ulong.Parse(lobbyManager.CurrentLobby.LobbyId);
        bool success = SteamMatchmaking.SetLobbyJoinable(new CSteamID(lobbyId), isLocked);

        return success ? AsyncResult.Success() : AsyncResult.Fail("Failed to lock Lobby.\n" +
                                                                  "Steam API return false");
    }

    [Command]
    public static async Task<AsyncResult> SetLobbyPrivateStateAsync(bool isPrivate, string password)
    {
        if (!lobbyManager.CurrentLobby.IsOwner)
            return AsyncResult.Fail("Only the Host can private the lobby.");

        try
        {
            // Also set a data property so we can check it easily later
            return await LobbyDataHandler.SetLobbyDataAsync("isPrivate", isPrivate.ToString().ToLower());
        }
        catch (Exception e)
        {
            return AsyncResult.Fail(e.Message);
        }
    }

    public static async Task<AsyncResult<List<Lobby>>> SearchLobbyAsync(int maxRoomsToFind = 10, Dictionary<string, string> filters = null)
    {
        filters ??= new Dictionary<string, string>();
        try
        {
            var result = await lobbyManager.CurrentProvider.SearchLobbiesAsync(maxRoomsToFind, filters);
            return AsyncResult<List<Lobby>>.Success(result);
        }
        catch (Exception e)
        {
            return AsyncResult<List<Lobby>>.Fail(e.Message);
        }
    }

    #endregion

    #region Network Events & Sync

    private async void HandleOnPlayerLeft(PlayerID player, bool asServer)
    {
        // Remove from local dictionary
        if (PlayerList.ContainsKey(player.id.value))
        {
            var user = PlayerList[player.id.value];
            PlayerList.Remove(player.id.value);
        }

        // If WE are the client and WE lost connection unexpectedly
        if (!asServer)
        {
            // Check if the player leaving is NOT us (meaning we didn't initiate a quit)
            // AND the network state is disconnected (meaning the cable was pulled or host crashed)
            if (player.IsLocal() &&
                (networkManager.clientState == ConnectionState.Disconnected ||
                 networkManager.clientState == ConnectionState.Disconnecting))
            {
                Debug.LogWarning("[LobbyHandler] Lost connection to server. Leaving Steam Lobby.");
                await LeaveLobbyAsync();
            }
        }
    }

    private async void HandleOnPlayerJoin(PlayerID player, bool isReconnect, bool asServer)
    {
        // Note: Logic validation is now done in LobbyNetworkController via RPC
        // This callback is mostly for logging or raw transport events.
        if (isReconnect)
            return;

        Debug.Log($"[LobbyHandler] Player with id '{player.id}' connected via Transport.");

        if (asServer)
        {
            string errorMessage = "";
            Debug.Log("[LobbyHandler] Transport started. Validating Host with Server...");

            // Call the Handshake RPC. 
            // Since we are the Host, this executes immediately on our local server,
            // creating the SteamLobbyUser and adding it to PlayerList.
            string currentSteamId = LobbyHandlerUtil.GetCurrentSteamId().ToString();
            // Cache PlayerID
            currentPlayerId = player;
            var handshakeResult = await LobbyNetworkHandler.ValidateHandshakeAsync_RPC(currentSteamId, currentPlayerId.id.value);

            if (!handshakeResult.IsSuccess)
            {
                Debug.LogError($"[LobbyHandler] Host Registration Failed: {handshakeResult.Message}");
                return;
            }

            //await LobbyDataHandler.SetLocalUserProperty("IsReady", false);

            Debug.Log("[LobbyHandler] Host registered successfully.");
        }
    }

    #endregion

    #region Transport Execution Handler

    private static async Task<AsyncResult> StartClientAsync(GenericTransport networkTransport, string targetAddress)
    {
        var tcs = new TaskCompletionSource<AsyncResult>();
        // 5 Second Timeout
        var cts = new CancellationTokenSource(5000);

        void OnConnectionStateChange(ConnectionState state)
        {
            if (state == ConnectionState.Connected)
            {
                tcs.TrySetResult(AsyncResult.Success());
            }
            else if (state == ConnectionState.Disconnected)
            {
                // Only fail if we haven't succeeded yet
                tcs.TrySetResult(AsyncResult.Fail("Disconnected before connection could be established."));
            }
        }

        // Register Timeout
        cts.Token.Register(() => tcs.TrySetResult(AsyncResult.Fail("Connection timed out.")));

        try
        {
            if (networkTransport is SteamTransport steamTransport)
            {
                steamTransport.address = targetAddress;
            }

            networkManager.onClientConnectionState += OnConnectionStateChange;
            networkManager.StartClient();

            return await tcs.Task;
        }
        finally
        {
            networkManager.onClientConnectionState -= OnConnectionStateChange;
            cts.Dispose();
        }
    }

    private static async Task<AsyncResult> StopClientAsync(GenericTransport networkTransport)
    {
        if (networkTransport is SteamTransport steamTransport) steamTransport.address = "";
        networkManager.StopClient();
        return AsyncResult.Success();
    }

    private static async Task<AsyncResult> StartClientServerAsync(GenericTransport networkTransport)
    {
        var tcs = new TaskCompletionSource<AsyncResult>();
        var cts = new CancellationTokenSource(5000);

        void OnServerStateChange(ConnectionState state)
        {
            if (state == ConnectionState.Connected) tcs.TrySetResult(AsyncResult.Success());
            else if (state == ConnectionState.Disconnected) tcs.TrySetResult(AsyncResult.Fail("Server failed to start."));
        }

        cts.Token.Register(() => tcs.TrySetResult(AsyncResult.Fail("Server start timed out.")));

        try
        {
            string address = "";

            if (networkTransport is SteamTransport steamTransport)
            {
                address = steamTransport.address;
            }

            if (string.IsNullOrWhiteSpace(address))
            {
                return AsyncResult.Fail("Server failed to start.\n" +
                                        "Address is null!");
            }

            networkManager.onServerConnectionState += OnServerStateChange;
            networkManager.StartServer();

            var serverResult = await tcs.Task;
            if (serverResult.IsFail) return serverResult;

            // Start Client (Host)
            return await StartClientAsync(networkTransport, address);
        }
        catch (Exception e)
        {
            return AsyncResult.Fail($"Server failed to start.\n" +
                                    $"Unknown error: {e}");
        }
        finally
        {
            networkManager.onServerConnectionState -= OnServerStateChange;
            cts.Dispose();
        }
    }

    private static async Task<AsyncResult> StopClientServerAsync(GenericTransport networkTransport)
    {
        await StopClientAsync(networkTransport); // Stop Client side first
        networkManager.StopServer(); // Stop Server side

        return AsyncResult.Success();
    }

    #endregion

    public static SteamLobbyUser CreateSteamUser(ulong clientId, string steamIdString)
    {
        if (ulong.TryParse(steamIdString, out ulong steamIdLong))
        {
            var hostSteamId = LobbyHandler.lobbyManager.CurrentLobby.Properties.GetValueOrDefault("steamAccountId");
            bool isHost = hostSteamId == steamIdString;

            CSteamID cSteamId = new CSteamID(steamIdLong);
            var newUser = new SteamLobbyUser(clientId, cSteamId, isHost);

            var savedData = LobbyDataHandler.GetPlayerDataBySteamId(steamIdString);
            if (savedData != null)
            {
                Debug.Log($"[LobbyHandler] Recovered data for {newUser.Username}");
                
                newUser.IsReady = savedData.IsReady;
                newUser.UpdateInternalData("IsReady", savedData.IsReady.ToString());

                foreach (var kvp in savedData.Extra)
                {
                    newUser.UpdateInternalData(kvp.Key, kvp.Value.ToString());
                }
            }

            return newUser;
        }
        return null;
    }

    #region WIP Feature

    [Command]
    public static async Task<AsyncResult> PromoteToHostAsync(ulong targetClientId)
    {
        return AsyncResult.Fail("Feature still in development!");

        if (!lobbyManager.CurrentLobby.IsOwner)
            return AsyncResult.Fail("Only the Host can promote players.");

        if (!PlayerList.TryGetValue(targetClientId, out var userData))
            return AsyncResult.Fail("Player not found.");

        if (userData is not SteamLobbyUser steamUser)
            return AsyncResult.Fail("Invalid User Data.");

        try
        {
            // Steamworks: Set new owner
            // Note: This only changes who controls the Steam Lobby (Settings, Invites).
            // It DOES NOT change who acts as the PurrNet Server. 
            // True Host Migration is very complex; this is "Lobby Leadership Transfer".
            ulong lobbyId = ulong.Parse(lobbyManager.CurrentLobby.LobbyId);
            SteamMatchmaking.SetLobbyOwner(new CSteamID(lobbyId), steamUser.SteamID);

            Debug.Log($"[LobbyHandler] Ownership transferred to {steamUser.Username}");
            return AsyncResult.Success();
        }
        catch (Exception e)
        {
            return AsyncResult.Fail($"Promote failed: {e.Message}");
        }
    }

    #endregion
}

public static class LobbyHandlerUtil
{
    public static ILobbyDataModel GetPlayerDataByClientId(ulong clientId)
    {
        if (!LobbyHandler.PlayerList.TryGetValue(clientId, out var result))
        {
            Debug.LogWarning($"Cannot found LobbyUserData with clientId '{clientId}'.");
            return null;
        }

        return result;
    }

    public static ILobbyDataModel GetPlayerDataBySteamId(CSteamID steamId)
    {
        // Construct the key we expect to find in Lobby Properties
        string key = $"User_{steamId}";

        var currentLobby = LobbyHandler.lobbyManager.CurrentLobby;

        // Check global lobby data
        if (currentLobby.IsValid && currentLobby.Properties.TryGetValue(key, out string json))
        {
            try
            {
                SteamLobbyUser convertedData = JsonConvert.DeserializeObject<SteamLobbyUser>(json);

                return convertedData;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbyData] Failed to parse JSON for {key}.\n" +
                                 $"Unkown Error: {e}");
            }
        }
        return null; // Return null
    }

    public static CSteamID GetCurrentSteamId()
    {
        CSteamID steamId = SteamUser.GetSteamID();
        return steamId;
    }
}

public static class PlayerIDExtensions
{
    public static bool IsLocal(this PlayerID player)
    {
        // Ensure NetworkManager exists to avoid errors during shutdown
        if (NetworkManager.main == null) return false;

        return player == NetworkManager.main.localPlayer;
    }

    public static PlayerID GetCurrentPlayerID()
    {
        var cachedPlayerId = LobbyHandler.currentPlayerId;
        var networkManagerPlayerId = NetworkManager.main.localPlayer;

        if (cachedPlayerId.Equals(default))
        {
            Debug.LogWarning($"CurrentPlayerId is not cached yet, returning the id from NetworkManager.");
            return networkManagerPlayerId;
        }

        if (networkManagerPlayerId.Equals(default))
        {
            Debug.LogWarning($"PlayerID in NetworkManager is not initialized yet, returning the id from cache.");
            return cachedPlayerId;
        }

        if (cachedPlayerId != networkManagerPlayerId)
        {
            Debug.LogWarning($"Conflicting Player ID Detected!\n" +
                             $"Returning cachedPlayerID");
            return cachedPlayerId;
        }

        return networkManagerPlayerId;
    }
}