using Steamworks;
using System.Collections.Generic;
using UnityEngine;

namespace NyxMachina.Multiplayer
{
    public static class LobbyDataFactory
    {
        public static SteamLobbyUser CreateSteamUser(ulong clientId, string steamIdString)
        {
            // Validate steamId if it's a correct ulong format
            if (!ulong.TryParse(steamIdString, out ulong steamIdLong)) 
                return null;

            var hostSteamId = LobbySystem.lobbyManager.CurrentLobby.Properties.GetValueOrDefault("steamAccountId");
            bool isHost = hostSteamId == steamIdString;

            CSteamID cSteamId = new CSteamID(steamIdLong);
            var newUser = new SteamLobbyUser(clientId, cSteamId, isHost);
            
            var savedData = LobbyDataHandler.GetPlayerDataBySteamId(steamIdString);
            if (savedData == null)
                return newUser;

            // Recover Data from Global Lobby Store if available
            Debug.Log($"[LobbyHandler] Recovered data for {newUser.Username}");
            newUser.IsReady = savedData.IsReady;
            newUser.UpdateInternalData("IsReady", savedData.IsReady.ToString());

            foreach (var kvp in savedData.Extra)
            {
                newUser.UpdateInternalData(kvp.Key, kvp.Value.ToString());
            }

            return newUser;
        }
    }
}