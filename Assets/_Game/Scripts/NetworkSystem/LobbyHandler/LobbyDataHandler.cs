using Newtonsoft.Json;
using NyxMachina.Multiplayer;
using System;
using System.Threading.Tasks;
using UnityEngine;

public static class LobbyDataHandler
{
    #region Global Lobby Data

    public static string GetLobbyData(string key)
    {
        if (LobbyHandler.lobbyManager.CurrentLobby.IsValid &&
            LobbyHandler.lobbyManager.CurrentLobby.Properties.TryGetValue(key, out var value))
        {
            return value;
        }
        return "";
    }

    public static async Task<AsyncResult> SetLobbyDataAsync(string key, string value)
    {
        // Security Check (Client side, mostly for UI feedback)
        if (!LobbyHandler.lobbyManager.CurrentLobby.IsOwner)
            return AsyncResult.Fail("Only Host can set Global Lobby Data.");

        try
        {
            // Update Steam (Source of Truth)
            await LobbyHandler.lobbyManager.CurrentProvider.SetLobbyDataAsync(key, value);

            // Update Network (Realtime)
            LobbyNetworkHandler.NotifyLobbyDataChanged_RPC(key, value);
            
            return AsyncResult.Success();
        }
        catch (Exception e)
        {
            return AsyncResult.Fail(e.Message);
        }
    }

    #endregion

    #region Player Data

    /// <summary>
    /// Reads the JSON blob from Lobby Global Data for a specific Steam User.
    /// </summary>
    public static SteamLobbyUser GetPlayerDataBySteamId(string steamId)
    {
        string key = $"User_{steamId}";

        if (LobbyHandler.lobbyManager.CurrentLobby.IsValid &&
            LobbyHandler.lobbyManager.CurrentLobby.Properties.TryGetValue(key, out string json))
        {
            try
            {
                return JsonConvert.DeserializeObject<SteamLobbyUser>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbyData] JSON Parse Error for {steamId}: {e.Message}");
            }
        }
        return null; 
    }

    /// <summary>
    /// The MAIN entry point for a client to change their data.
    /// </summary>
    public static async Task<AsyncResult> SetPlayerData(string key, object value)
    {
        var localId = PlayerIDExtensions.GetCurrentPlayerID().id.value;
        string strValue = value.ToString();

        // Update Local RAM (Optimistic / Instant UI)
        if (!LobbyHandler.PlayerList.TryGetValue(localId, out var user))
        {
            return AsyncResult.Fail("Local user not found.");
        }

        user.UpdateInternalData(key, strValue);

        // Send Request to Host
        // Host will: Update their RAM, Broadcast to others, and Save to Steam JSON
        await LobbyNetworkHandler.RequestPlayerDataUpdate_RPC(key, strValue);

        return AsyncResult.Success();
    }

    /// <summary>
    /// HOST ONLY: Serializes a user's current state to JSON and saves to Steam.
    /// </summary>
    public static async Task SavePlayerJsonToLobby(ulong clientId, string steamId)
    {
        if (!LobbyHandler.PlayerList.TryGetValue(clientId, out var user))
            return;

        string json = JsonConvert.SerializeObject(user);
        string lobbyKey = $"User_{steamId}";

        // TODO: Save Client Data to Host Local Save Data instead, for faster iteration
        // Save to Steam
        await LobbyHandler.lobbyManager.CurrentProvider.SetLobbyDataAsync(lobbyKey, json);
        
        Debug.Log($"[Host] Saved persistence for {user.Username}");
    }

    #endregion
}