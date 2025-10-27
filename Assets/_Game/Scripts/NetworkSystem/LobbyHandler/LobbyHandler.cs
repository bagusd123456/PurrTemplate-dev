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
using NyxMachina.Shared.EventFramework;
using NyxMachina.Shared.EventFramework.Core.Payloads;
using UnityEngine;
using ConnectionState = PurrNet.Transports.ConnectionState;

public class LobbyHandler
{
    public struct OnLobbyJoined : IPayload
    {
        public Lobby CurrentLobby { get; private set; }

        public OnLobbyJoined (Lobby targetLobby)
        {
            var result = new OnLobbyJoined
            {
                CurrentLobby = targetLobby
            };

            this = result;
        }
    }

    public static LobbyManager lobbyManager;
    public static NetworkManager networkManager;
    public static ChatRoomHandler chatRoomHandler;
    public static Task steamCallbackTask;

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
            steamCallbackTask = RunSteamCallback();
            await lobbyManager.CurrentProvider.InitializeAsync();
            chatRoomHandler = new ChatRoomHandler();
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

    private async Task RunSteamCallback()
    {
        var runCallbacks = true;
        while (runCallbacks)
        {
            SteamAPI.RunCallbacks();
            await Task.Delay(16);
        }
    }

    [Command]
    public static async Task<AsyncResult<Lobby>> CreateLobbyAsync(int targetMaxPlayer = 4, Dictionary<string, string> targetRoomProperties = default)
    {
        var errorMessage = "";
        Lobby createdLobby;

        targetRoomProperties ??= new Dictionary<string, string>();

        try
        {
            var networkTransport = networkManager.transport;
            if (networkTransport is SteamTransport steamTransport)
            {
                var steamAccountId = SteamUser.GetSteamID().ToString();
                targetRoomProperties["steamAccountId"] = steamAccountId;
            }

            targetRoomProperties["isStarted"] = "false";
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
            errorMessage = $"[LobbyHandler] Failed to create lobby.\n" +
                               $"{e.StackTrace}";
            Debug.LogError(errorMessage);
            return AsyncResult<Lobby>.Fail(errorMessage);
        }

        EVENT.Publish(new OnLobbyJoined(createdLobby));
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
            var networkTransport = networkManager.transport;

            if (!currentLobby.Properties.TryGetValue("steamAccountId", out var ownerSteamAccountId))
            {
                errorMessage = $"[LobbyHandler] Failed to join lobby.\n" +
                               $"Cannot find steamAccountId, please check the lobby properties.";
                Debug.LogError(errorMessage);
            }

            var startClientTask = await StartClientAsync(networkTransport, ownerSteamAccountId);
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

        EVENT.Publish(new OnLobbyJoined(currentLobby));
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
        localLobbyUser.IsReady = targetState;

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
            currentLobby.Properties["isStarted"] = "true";
            await lobbyManager.CurrentProvider.SetLobbyDataAsync("isStarted", "true");
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

    private static async Task<AsyncResult> StartClientAsync(GenericTransport networkTransport, string targetAddress = "")
    {
        var cts = new CancellationTokenSource(5000);
        var tcs = new TaskCompletionSource<AsyncResult>();

        while (!cts.IsCancellationRequested)
        {
            if (networkTransport is SteamTransport steamTransport)
            {
                if (!string.IsNullOrWhiteSpace(targetAddress))
                {
                    steamTransport.address = targetAddress;
                }
            }

            networkManager.onClientConnectionState += ListenConnectionState;
            networkManager.StartClient();
            var result = await tcs.Task;
            networkManager.onClientConnectionState -= ListenConnectionState;
            return result;
        }

        return AsyncResult.Fail($"[LobbyHandler] StartClientAsync Request Time Out.");

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

    private static async Task<AsyncResult> StopClientAsync(GenericTransport networkTransport)
    {
        using var cts = new CancellationTokenSource(5000);
        var tcs = new TaskCompletionSource<AsyncResult>();

        while (!cts.IsCancellationRequested)
        {
            if (networkTransport is SteamTransport steamTransport)
            {
                steamTransport.address = "";
            }

            networkManager.onClientConnectionState += ListenConnectionState;
            networkManager.StopClient();
            var result = await tcs.Task;
            networkManager.onClientConnectionState -= ListenConnectionState;
            return result;
        }

        return AsyncResult.Fail($"[LobbyHandler] StopClientAsync Request Time Out.");

        void ListenConnectionState(ConnectionState state)
        {
            if (state is ConnectionState.Disconnected)
            {
                tcs.TrySetResult(AsyncResult.Success());
                return;
            }
        }
    }

    private static async Task<AsyncResult> StartClientServerAsync(GenericTransport networkTransport)
    {
        using var cts = new CancellationTokenSource(5000);
        var tcs = new TaskCompletionSource<AsyncResult>();

        while (!cts.IsCancellationRequested)
        {
            if (networkTransport is SteamTransport steamTransport)
            {
                var steamAccountId = SteamUser.GetSteamID().ToString();
                steamTransport.address = steamAccountId;
            }

            networkManager.onServerConnectionState += ListenConnectionState;
            networkManager.StartServer();
            var startServerResult = await tcs.Task;
            networkManager.onServerConnectionState -= ListenConnectionState;
            if (startServerResult.IsFail)
            {
                return startServerResult;
            }

            var startClientResult = await StartClientAsync(networkTransport);
            return startClientResult;
        }

        return AsyncResult.Fail($"[LobbyHandler] StartClientServerAsync Request Time Out.");

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

    private static async Task<AsyncResult> StopClientServerAsync(GenericTransport networkTransport)
    {
        var cts = new CancellationTokenSource(5000);
        var tcs = new TaskCompletionSource<AsyncResult>();

        while (!cts.IsCancellationRequested)
        {
            if (networkTransport is SteamTransport steamTransport)
            {
                steamTransport.address = "";
            }

            var stopClientResult = await StopClientAsync(networkTransport);
            if (stopClientResult.IsFail)
            {
                return stopClientResult;
            }
            networkManager.onServerConnectionState += ListenConnectionState;
            networkManager.StopServer();
            var stopServerResult = await tcs.Task;
            networkManager.onServerConnectionState -= ListenConnectionState;
            return stopServerResult;
        }

        return AsyncResult.Fail($"[LobbyHandler] StopClientServer Request Time Out.");

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
