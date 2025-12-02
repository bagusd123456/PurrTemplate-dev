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

public class LobbySystem
{
    #region Static Fields & State

    public static LobbyManager lobbyManager;
    public static NetworkManager networkManager;
    public static ChatRoomHandler chatRoomHandler;
    
    // Tasks & State
    public static Task steamCallbackTask;
    public static PlayerID currentPlayerId;
    public static Dictionary<ulong, ILobbyDataModel> PlayerList = new();

    #endregion

    #region Initialization & Cleanup

    public LobbySystem(LobbyManager lobbyManager, NetworkManager networkManager)
    {
        LobbySystem.lobbyManager = lobbyManager;
        LobbySystem.networkManager = networkManager;

        // Register Lobby Data Listeners
        lobbyManager.OnRoomUpdated.RemoveListener(HandleRoomUpdated);
        lobbyManager.OnRoomUpdated.AddListener(HandleRoomUpdated);

        // Register Network Listeners (Remove old ones first to avoid duplicates)
        networkManager.onPlayerJoined -= HandleOnPlayerJoin;
        networkManager.onPlayerLeft -= HandleOnPlayerLeft;

        networkManager.onPlayerJoined += HandleOnPlayerJoin;
        networkManager.onPlayerLeft += HandleOnPlayerLeft;
    }

    public async Task<AsyncResult> Init()
    {
        Application.quitting -= AppQuitting;
        Application.quitting += AppQuitting;

        try
        {
            var initState = SteamAPI.Init();
            if (!initState)
            {
                SteamAPI.InitEx(out var initException);
                throw new Exception($"[LobbyHandler] Steam Failed to init.\n{initException}");
            }
            
            steamCallbackTask = RunSteamCallback();
            await lobbyManager.CurrentProvider.InitializeAsync();

            chatRoomHandler = new ChatRoomHandler();
        }
        catch (Exception e)
        {
            var unknownErrorMsg = $"[LobbyHandler] Init Failed.\n{e.Message}";
            Debug.LogError(unknownErrorMsg);
            return AsyncResult.Fail(unknownErrorMsg);
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

    #endregion

    #region Lobby Lifecycle (Create, Join, Leave, Search)

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
            errorMessage = $"Failed to create lobby.\nUnknown Error: {e.Message}";
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
            // 1. Join Steam Lobby (Platform Layer)
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

            // 2. Connect Transport (Physical Layer)
            Debug.Log("[LobbyHandler] Connecting to Host via Transport...");
            var networkTransport = networkManager.transport;
            var startClientTask = await StartClientAsync(networkTransport, ownerSteamAccountId);

            if (startClientTask.IsFail)
            {
                await LeaveLobbyAsync();
                return AsyncResult<Lobby>.Fail(startClientTask.Message);
            }

            // 3. Handshake (Logic Layer via RPC)
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
                await LeaveLobbyAsync();
                return AsyncResult<Lobby>.Fail($"Failed to join Lobby.\nHandshake Failed, Unknown error: {e.Message}");
            }

            if (!result.IsSuccess)
            {
                Debug.LogWarning($"[LobbyHandler] Host rejected connection: {result.Message}");
                await LeaveLobbyAsync();
                return AsyncResult<Lobby>.Fail($"Connection Rejected: {result.Message}");
            }

            // 4. Finalize Local State
            ulong myClientId = networkManager.localPlayer.id.value;
            if (!PlayerList.ContainsKey(myClientId))
            {
                var myUser = LobbyDataFactory.CreateSteamUser(myClientId, mySteamId);
                if (myUser != null)
                {
                    PlayerList.Add(myClientId, myUser);
                    Debug.Log($"[LobbyHandler] Added local player {myUser.Username} to PlayerList.");
                    EVENT.Publish(new OnPlayerJoinLobby(myUser));
                }
                else
                {
                    Debug.LogError("[LobbyHandler] Failed to create local SteamLobbyUser.");
                }
            }

            Debug.Log("[LobbyHandler] Join Successful!");
        }
        catch (Exception e)
        {
            errorMessage = $"[LobbyHandler] Exception during Join: {e.Message}\n{e.StackTrace}";
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

    #region Data Management & Sync

    public static async Task SetLobbyData(string key, string value) => 
        await LobbyDataHandler.SetLobbyDataAsync(key, value);

    public static async Task SetLocalUserData(string key, string value) => 
        await LobbyDataHandler.SetPlayerData(key, value);

    public static string GetLobbyData(string key) => 
        LobbyDataHandler.GetLobbyData(key);

    private void HandleRoomUpdated(Lobby currentLobby)
    {
        // Refresh data when Steam says something changed
        foreach ((ulong key, var value) in PlayerList)
        {
            if (value is SteamLobbyUser steamUser)
            {
                // Deserialize DataModel
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

    #region Gameplay State

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

    #endregion

    #region Network Event Callbacks

    private async void HandleOnPlayerLeft(PlayerID player, bool asServer)
    {
        // Remove from local dictionary
        if (PlayerList.ContainsKey(player.id.value))
        {
            PlayerList.Remove(player.id.value);
        }

        // If WE are the client and WE lost connection unexpectedly
        if (!asServer)
        {
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
        // Only track joining player
        if (isReconnect)
        {
            string reconnectedString = "<color=blue>reconnected</color>";
            Debug.Log($"[LobbyHandler] Player with id '{player.id.value}' has {reconnectedString}.");
            return;
        }

        string connectedString = "<color=green>connected</color>";
        Debug.Log($"[LobbyHandler] Player with id '{player.id}' has {connectedString}.");

        // Host Logic: Validate connection via RPC Handshake
        if (asServer)
        {
            Debug.Log("[LobbyHandler] Transport started. Validating Host with Server...");

            string currentSteamId = LobbyHandlerUtil.GetCurrentSteamId().ToString();
            currentPlayerId = player;
            
            var handshakeResult = await LobbyNetworkHandler.ValidateHandshakeAsync_RPC(currentSteamId, currentPlayerId.id.value);

            if (!handshakeResult.IsSuccess)
            {
                Debug.LogError($"[LobbyHandler] Host Registration Failed: {handshakeResult.Message}");
                return;
            }

            Debug.Log("[LobbyHandler] Host registered successfully.");
        }
    }

    #endregion

    #region Transport Management (Internal)

    private static async Task<AsyncResult> StartClientServerAsync(GenericTransport networkTransport)
    {
        var tcs = new TaskCompletionSource<AsyncResult>();
        var cts = new CancellationTokenSource(5000);

        cts.Token.Register(() =>
        {
            string hostStartTimeOut = $"Failed to start host.\n" +
                                      $"Request time out, taking too long.";
            tcs.TrySetResult(AsyncResult.Fail(hostStartTimeOut));
        });

        try
        {
            string address = "";
            if (networkTransport is SteamTransport steamTransport)
                address = steamTransport.address;

            if (string.IsNullOrWhiteSpace(address))
            {
                string nullAddressMsg = $"Failed to start host.\n" +
                                        $"Address value is NULL: '{address}'.";
                return AsyncResult.Fail(nullAddressMsg);
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
            string unknownErrorMsg = "Failed to start host.\n" +
                                     $"Unknown error: {e}";
            return AsyncResult.Fail(unknownErrorMsg);
        }
        finally
        {
            networkManager.onServerConnectionState -= OnServerStateChange;
            cts.Dispose();
        }

        void OnServerStateChange(ConnectionState state)
        {
            if (state == ConnectionState.Connected) tcs.TrySetResult(AsyncResult.Success());
            else if (state == ConnectionState.Disconnected) tcs.TrySetResult(AsyncResult.Fail("Server failed to start."));
        }
    }

    private static async Task<AsyncResult> StartClientAsync(GenericTransport networkTransport, string targetAddress)
    {
        var tcs = new TaskCompletionSource<AsyncResult>();
        var cts = new CancellationTokenSource(5000);

        cts.Token.Register(() =>
        {
            string timeOutMsg = "Failed to connect to host.\n" +
                                "Connection timed out.";
            tcs.TrySetResult(AsyncResult.Fail(timeOutMsg));
        });

        try
        {
            if (networkTransport is SteamTransport steamTransport)
                steamTransport.address = targetAddress;

            networkManager.onClientConnectionState += OnConnectionStateChange;
            networkManager.StartClient();

            return await tcs.Task;
        }
        finally
        {
            networkManager.onClientConnectionState -= OnConnectionStateChange;
            cts.Dispose();
        }

        void OnConnectionStateChange(ConnectionState state)
        {
            if (state == ConnectionState.Connected) tcs.TrySetResult(AsyncResult.Success());
            else if (state == ConnectionState.Disconnected)
            {
                string disconnectedMsg = "Failed to connect to host.\n" +
                                         "Disconnected before connection could be established.";
                tcs.TrySetResult(AsyncResult.Fail(disconnectedMsg));
            }
        }
    }

    private static async Task<AsyncResult> StopClientAsync(GenericTransport networkTransport)
    {
        if (!networkManager.isHost && networkTransport is SteamTransport steamTransport)
            steamTransport.address = "";

        networkManager.StopClient();
        return AsyncResult.Success();
    }

    private static async Task<AsyncResult> StopClientServerAsync(GenericTransport networkTransport)
    {
        await StopClientAsync(networkTransport); // Stop Client side first
        networkManager.StopServer(); // Stop Server side

        if (networkTransport is SteamTransport steamTransport)
            steamTransport.address = "";

        return AsyncResult.Success();
    }

    #endregion
}