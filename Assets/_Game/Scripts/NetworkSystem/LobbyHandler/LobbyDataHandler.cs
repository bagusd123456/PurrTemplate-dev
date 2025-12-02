using Newtonsoft.Json;
using NyxMachina.Multiplayer;
using System;
using System.Threading.Tasks;
using PurrNet;
using UnityEngine;

public static class LobbyDataHandler
{
    #region Global Lobby Data

    public static string GetLobbyData(string key)
    {
        if (LobbySystem.lobbyManager.CurrentLobby.IsValid &&
            LobbySystem.lobbyManager.CurrentLobby.Properties.TryGetValue(key, out var value))
        {
            return value;
        }
        return "";
    }

    /// <summary>
    /// Set Key 
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public static async Task<AsyncResult> SetLobbyDataAsync(string key, string value)
    {
        // Security Check (Client side, mostly for UI feedback)
        if (!LobbySystem.lobbyManager.CurrentLobby.IsOwner)
        {
            Debug.LogError("Only Host can set Global Lobby Data.");
            return AsyncResult.Fail("Only Host can set Global Lobby Data.");
        }

        try
        {
            // Update LobbyData (Source of Truth)
            await LobbySystem.lobbyManager.CurrentProvider.SetLobbyDataAsync(key, value);
            await GetPlayerDataBySteamIdAsync(key);

            // Notify all observer that LobbyData has changed
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

        if (LobbySystem.lobbyManager.CurrentLobby.IsValid &&
            LobbySystem.lobbyManager.CurrentLobby.Properties.TryGetValue(key, out string json))
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
    /// Reads the JSON blob from Lobby Global Data for a specific Steam User.
    /// </summary>
    public static async Task<SteamLobbyUser> GetPlayerDataBySteamIdAsync(string steamId)
    {
        SteamLobbyUser result;

        string key = $"User_{steamId}";
        string json = await LobbySystem.lobbyManager.CurrentProvider.GetLobbyDataAsync(key);

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            result = JsonConvert.DeserializeObject<SteamLobbyUser>(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LobbyData] JSON Parse Error for {steamId}: {e.Message}");
            return null;
        }

        return result; 
    }

    /// <summary>
    /// The MAIN entry point for a client to change their data.
    /// </summary>
    public static async Task<AsyncResult> SetPlayerData(string key, object value)
    {
        var localId = LobbyDataUtil.GetCurrentPlayerID().id.value;
        string strValue = value.ToString();

        // Update Local RAM (Optimistic / Instant UI)
        if (!LobbySystem.PlayerList.TryGetValue(localId, out var user))
        {
            return AsyncResult.Fail("Local user not found.");
        }

        user.UpdateInternalData(key, strValue);

        // Send Request to Host
        // Host will: Update their RAM, Broadcast to others, and Save to Steam JSON
        await RequestPlayerDataUpdate_RPC(key, strValue);

        return AsyncResult.Success();
    }

    /// <summary>
    /// HOST ONLY: Serializes a user's current state to JSON and saves to Steam.
    /// </summary>
    public static async Task SavePlayerJsonToLobby(ulong clientId, string steamId)
    {
        if (!LobbySystem.PlayerList.TryGetValue(clientId, out var user))
            return;

        string json = JsonConvert.SerializeObject(user);
        string lobbyKey = $"User_{steamId}";

        // TODO: Save Client Data to Host Local Save Data instead, for faster iteration
        // Save to Steam
        await LobbySystem.lobbyManager.CurrentProvider.SetLobbyDataAsync(lobbyKey, json);
        
        Debug.Log($"[Host] Saved persistence for {user.Username}");
    }

    /// <summary>
    /// Client asks Host: "Please save my data to the Steam Lobby so late joiners can see it."
    /// </summary>
    [ServerRpc(requireOwnership: false)]
    public static async Task RequestPlayerDataUpdate_RPC(string key, string value, RPCInfo info = default)
    {
        ulong senderId = info.sender.id;

        // Sync Server local data
        LobbyNetworkHandler.SyncLocalPlayerList(senderId, key, value);

        // Broadcast to others
        LobbyNetworkHandler.NotifyPlayerDataChanged_RPC(senderId, key, value);

        // Persistence (Host Only)
        if (LobbySystem.lobbyManager.CurrentLobby.IsOwner)
        {
            if (LobbySystem.PlayerList.TryGetValue(senderId, out var user) && user is SteamLobbyUser steamUser)
            {
                await SavePlayerJsonToLobby(senderId, steamUser.SteamID.ToString());
            }
        }
    }

    #endregion
}