using System;
using Newtonsoft.Json;
using Steamworks;
using UnityEngine;

public static class LobbyHandlerUtil
{
    public static ILobbyDataModel GetPlayerDataByClientId(ulong clientId)
    {
        ILobbyDataModel result = null;
        foreach (var playerData in LobbySystem.PlayerList.Values)
        {
            if (playerData.ClientId == clientId)
            {
                result = playerData;
            }
        }

        if (result == null)
        {
            Debug.LogWarning($"Cannot found LobbyUserData with clientId '{clientId}'.\n" +
                             $"Returning null!");
            return null;
        }
        
        return result;
    }

    public static CSteamID GetCurrentSteamId()
    {
        CSteamID steamId = SteamUser.GetSteamID();
        return steamId;
    }
}