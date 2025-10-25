using PurrLobby;
using PurrNet;
using Steamworks;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using QFSW.QC;
using UnityEngine;
using PurrNet.Steam;
using System.Data;
using ConnectionState = PurrNet.Transports.ConnectionState;

public class LobbyHandler
{
    public static LobbyManager lobbyManager;
    public static NetworkManager networkManager;

    public LobbyHandler(LobbyManager lobbyManager, NetworkManager networkManager)
    {
        LobbyHandler.lobbyManager = lobbyManager;
        LobbyHandler.networkManager = networkManager;
    }

    public async Task<AsyncResult> Init()
    {
        var errorMessage = "";

        try
        {
            var initState = SteamAPI.Init();
            if (!initState)
            {
                SteamAPI.InitEx(out var initException);
                throw new Exception($"[LobbyHandler] Steam Failed to init.\n" +
                                    $"{initException}");
            }
            SteamAPI.RunCallbacks();
            await lobbyManager.CurrentProvider.InitializeAsync();
        }
        catch (Exception e)
        {
            errorMessage = $"[LobbyHandler] Steam Failed to init, unknown Error.\n" +
                           $"{e.StackTrace}";
            Debug.LogError(errorMessage);
            return AsyncResult.Fail(errorMessage);
        }

        return AsyncResult.Success();
    }

    [Command]
    public static async Task<AsyncResult<Lobby>> CreateLobbyAsync(int targetMaxPlayer = 4, Dictionary<string, string> targetRoomProperties = default)
    {
        var errorMessage = "";
        Lobby createdLobby;

        targetRoomProperties ??= new Dictionary<string, string>();

        try
        {
            var steamTransport = networkManager.transport as SteamTransport;
            if (steamTransport == null)
            {
                errorMessage = $"[LobbyHandler] Failed to create lobby.\n" +
                               $"Cannot find SteamTransport, please check the networkManager.";
                Debug.LogError(errorMessage);
            }

            var steamAccountId = SteamUser.GetSteamID().ToString();
            targetRoomProperties["steamAccountId"] = steamAccountId;
            createdLobby = await lobbyManager.CurrentProvider.CreateLobbyAsync(targetMaxPlayer, targetRoomProperties);

            var startServerTask = await StartClientServerAsync(steamTransport, steamAccountId);
            if (startServerTask.IsFail)
            {
                Debug.LogError(startServerTask.Message);
                return AsyncResult<Lobby>.Fail(startServerTask.Message);
            }

        }
        catch (Exception e)
        {
            errorMessage = $"[LobbyHandler] Failed to create lobby.\n" +
                               $"{e.StackTrace}";
            Debug.LogError(errorMessage);
            return AsyncResult<Lobby>.Fail(errorMessage);
        }

        return AsyncResult<Lobby>.Success(createdLobby);
    }

    [Command]
    public static async Task<AsyncResult<Lobby>> JoinLobbyByIdAsync(string roomId)
    {
        var errorMessage = "";
        Lobby currentLobby;

        try
        {
            currentLobby = await lobbyManager.CurrentProvider.JoinLobbyAsync(roomId);
            var steamTransport = networkManager.transport as SteamTransport;
            if (steamTransport == null)
            {
                errorMessage = $"[LobbyHandler] Failed to join lobby.\n" +
                               $"Cannot find SteamTransport, please check the networkManager.";
                Debug.LogError(errorMessage);
            }

            if (!currentLobby.Properties.TryGetValue("steamAccountId", out var ownerSteamAccountId))
            {
                errorMessage = $"[LobbyHandler] Failed to join lobby.\n" +
                               $"Cannot find steamAccountId, please check the lobby properties.";
                Debug.LogError(errorMessage);
            }

            var startClientTask = await StartClientAsync(steamTransport, ownerSteamAccountId);
            if (startClientTask.IsFail)
            {
                Debug.LogError(startClientTask.Message);
                return AsyncResult<Lobby>.Fail(startClientTask.Message);
            }
        }
        catch (Exception e)
        {
            errorMessage = $"[LobbyHandler] Failed to join lobby.\n" +
                               $"{e.StackTrace}";
            Debug.LogError(errorMessage);
            return AsyncResult<Lobby>.Fail(errorMessage);
        }

        return AsyncResult<Lobby>.Success(currentLobby);
        
    }

    [Command]
    public static async Task<AsyncResult> LeaveLobbyAsync()
    {
        var errorMessage = "";
        var currentLobby = lobbyManager.CurrentLobby;
        if (!currentLobby.IsValid)
        {
            errorMessage = $"[LobbyHandler] Attempting to leave lobby, but none is joined.\n" +
                           $"Doing nothing.";
            Debug.LogWarning(errorMessage);
            return AsyncResult.Fail(errorMessage);
        }

        try
        {
            var steamTransport = networkManager.transport as SteamTransport;
            if (steamTransport == null)
            {
                errorMessage = $"[LobbyHandler] Failed to join lobby.\n" +
                               $"Cannot find SteamTransport, please check the networkManager.";
                Debug.LogError(errorMessage);
            }

            if (currentLobby.IsOwner)
            {
                await StopClientServerAsync(steamTransport);
            }
            else
            {
                await StopClientAsync(steamTransport);
            }

            await lobbyManager.CurrentProvider.LeaveLobbyAsync();
        }
        catch (Exception e)
        {
            errorMessage = $"[LobbyHandler] Failed to leave lobby.\n" +
                               $"{e.StackTrace}";
            Debug.LogError(errorMessage);
            return AsyncResult.Fail(errorMessage);
        }

        return AsyncResult.Success();
    }

    [Command]
    public static async Task<AsyncResult> SetIsReadyAsync(bool targetState)
    {
        var errorMessage = "";
        var currentLobby = lobbyManager.CurrentLobby;
        var targetUserId = string.Empty;

        if (!currentLobby.IsValid)
        {
            errorMessage = $"[LobbyHandler] Attempting to set '{targetUserId}' ready state to '{targetState}', but none is joined.\n" +
                           $"Doing nothing.";
            Debug.LogWarning(errorMessage);
            return AsyncResult.Fail(errorMessage);
        }

        var localUserId = lobbyManager.CurrentProvider.GetLocalUserIdAsync().Result;
        if (string.IsNullOrWhiteSpace(localUserId))
        {
            errorMessage = $"[LobbyHandler] Can't toggle ready state, local user ID is null or empty.\n" +
                        $"Doing nothing.";
            Debug.LogWarning(errorMessage);
            return AsyncResult.Fail(errorMessage);
        }

        var localLobbyUser = currentLobby.Members.Find(x => x.Id == localUserId);
        targetUserId = localLobbyUser.Id;

        var setReadyTask = await SetIsReadyAsync(targetUserId, targetState);
        return setReadyTask;
    }

    public static async Task<AsyncResult> SetIsReadyAsync(string targetUserId, bool targetState)
    {
        var errorMessage = "";

        try
        {
            await lobbyManager.CurrentProvider.SetIsReadyAsync(targetUserId, targetState);
        }
        catch (Exception e)
        {
            errorMessage = $"[LobbyHandler] Failed to set '{targetUserId}' ready state to '{targetState}'.\n" +
                               $"{e.StackTrace}";
            Debug.LogError(errorMessage);
            return AsyncResult.Fail(errorMessage);
        }

        return AsyncResult.Success();
    }

    [Command]
    public static async Task<AsyncResult> SetAllReadyAsync()
    {
        var errorMessage = "";
        var currentLobby = lobbyManager.CurrentLobby;
        if (!currentLobby.IsValid)
        {
            errorMessage = $"[LobbyHandler] Failed to SetAllReady.\n" +
                           $"None is joined.";
            Debug.LogWarning(errorMessage);
            return AsyncResult.Fail(errorMessage);
        }

        if (!currentLobby.IsOwner)
        {
            errorMessage = $"[LobbyHandler] Failed to SetAllReady.\n" +
                           $"User is not the owner.";
            Debug.LogWarning(errorMessage);
            return AsyncResult.Fail(errorMessage);
        }

        try
        {
            await lobbyManager.CurrentProvider.SetAllReadyAsync();
        }
        catch (Exception e)
        {
            errorMessage = $"[LobbyHandler] Failed to SetAllReady.\n" +
                           $"{e.StackTrace}";
            Debug.LogError(errorMessage);
            return AsyncResult.Fail(errorMessage);
        }

        return AsyncResult.Success();
    }

    [Command]
    public static async Task<AsyncResult> SetLobbyStartedAsync()
    {
        var errorMessage = "";
        var currentLobby = lobbyManager.CurrentLobby;
        if (!currentLobby.IsValid)
        {
            errorMessage = $"[LobbyHandler] Attempting to started game for joined lobby, but none is joined.\n" +
                           $"Doing nothing.";
            Debug.LogWarning(errorMessage);
            return AsyncResult.Fail(errorMessage);
        }

        try
        {
            await lobbyManager.CurrentProvider.SetLobbyStartedAsync();
        }
        catch (Exception e)
        {
            errorMessage = $"[LobbyHandler] Failed to start game for joined lobby.\n" +
                               $"{e.StackTrace}";
            Debug.LogError(errorMessage);
            return AsyncResult.Fail(errorMessage);
        }

        return AsyncResult.Success();
    }

    public static async Task<AsyncResult<List<Lobby>>> SearchLobbyAsync(int maxRoomsToFind = 10, Dictionary<string, string> filters = null)
    {
        var errorMessage = "";
        filters ??= new Dictionary<string, string>();

        try
        {
            var result = await lobbyManager.CurrentProvider.SearchLobbiesAsync(maxRoomsToFind, filters);
            return AsyncResult<List<Lobby>>.Success(result);
        }
        catch (Exception e)
        {
            errorMessage = $"[LobbyHandler] Failed to start game for joined lobby.\n" +
                           $"{e.StackTrace}";
            Debug.LogWarning(errorMessage);
            return AsyncResult<List<Lobby>>.Fail(errorMessage);
        }
    }


    #region Server Execution Handler

    private static async Task<AsyncResult> StartClientAsync(SteamTransport steamTransport, string ownerSteamAccountId)
    {
        var tcs = new TaskCompletionSource<AsyncResult>();
        networkManager.onClientConnectionState += ListenConnectionState;

        steamTransport.address = ownerSteamAccountId;
        networkManager.StartClient();
        var result = await tcs.Task;
        networkManager.onClientConnectionState -= ListenConnectionState;
        return result;

        void ListenConnectionState(ConnectionState state)
        {
            if (state is ConnectionState.Disconnected)
            {
                var errorMessage = $"[LobbyHandler] Cannot connect to Host.\n" +
                                $"Unknown error.";
                tcs.TrySetResult(AsyncResult.Fail(errorMessage));
                return;
            }
            else if (state is ConnectionState.Connected)
            {
                tcs.TrySetResult(AsyncResult.Success());
            }
        }
    }

    private static async Task<AsyncResult> StopClientAsync(SteamTransport steamTransport)
    {
        var tcs = new TaskCompletionSource<AsyncResult>();
        networkManager.onClientConnectionState += ListenConnectionState;

        steamTransport.address = "";
        networkManager.StopClient();
        var result = await tcs.Task;
        networkManager.onClientConnectionState -= ListenConnectionState;
        return result;

        void ListenConnectionState(ConnectionState state)
        {
            if (state is ConnectionState.Disconnected)
            {
                tcs.TrySetResult(AsyncResult.Success());
                return;
            }
        }
    }

    private static async Task<AsyncResult> StartClientServerAsync(SteamTransport steamTransport, string steamAccountId)
    {
        var tcs = new TaskCompletionSource<AsyncResult>();
        networkManager.onServerConnectionState += ListenConnectionState;

        steamTransport.address = steamAccountId;
        networkManager.StartServer();
        var startServerResult = await tcs.Task;
        networkManager.onServerConnectionState -= ListenConnectionState;
        if (startServerResult.IsFail)
        {
            return startServerResult;
        }
        
        var startClientResult = await StartClientAsync(steamTransport, steamAccountId);
        return startClientResult;

        void ListenConnectionState(ConnectionState state)
        {
            if (state is ConnectionState.Disconnected)
            {
                var errorMessage = $"[LobbyHandler] Cannot start as host.\n" +
                                $"Unknown error.";
                tcs.TrySetResult(AsyncResult.Fail(errorMessage));
                return;
            }
            else if (state is ConnectionState.Connected)
            {
                tcs.TrySetResult(AsyncResult.Success());
            }
        }
    }

    private static async Task<AsyncResult> StopClientServerAsync(SteamTransport steamTransport)
    {
        var tcs = new TaskCompletionSource<AsyncResult>();
        networkManager.onServerConnectionState += ListenConnectionState;

        steamTransport.address = "";
        networkManager.StopServer();
        var stopServerResult = await tcs.Task;
        networkManager.onServerConnectionState -= ListenConnectionState;
        if (stopServerResult.IsFail)
        {
            return stopServerResult;
        }

        var stopClientResult = await StopClientAsync(steamTransport);
        return stopClientResult;

        void ListenConnectionState(ConnectionState state)
        {
            if (state is ConnectionState.Disconnected)
            {
                tcs.TrySetResult(AsyncResult.Success());
                return;
            }
        }
    }

    #endregion
}
