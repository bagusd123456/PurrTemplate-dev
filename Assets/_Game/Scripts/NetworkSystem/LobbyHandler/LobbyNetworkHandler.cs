using System.Collections.Generic;
using System.Linq;
using NyxMachina.Shared.EventFramework;
using PurrLobby;
using PurrNet;
using System.Threading.Tasks;
using UnityEngine;

namespace NyxMachina.Multiplayer
{
    public class LobbyNetworkHandler
    {
        /// <summary>
        /// Called by the Client to validate their presence with the Server.
        /// Returns a Task that completes when the Server responds.
        /// </summary>
        [ServerRpc(requireOwnership: false)]
        public static async Task<AsyncResult> ValidateHandshakeAsync_RPC(string steamId, ulong overrideClientId = 0, RPCInfo info = default)
        {
            // Server Validation: Is the server actually in a lobby?
            // We don't retry this; if the server isn't in a lobby, something is critically wrong.
            if (LobbyHandler.lobbyManager == null || !LobbyHandler.lobbyManager.CurrentLobby.IsValid)
            {
                return AsyncResult.Fail("Server is not in a valid Steam Lobby.");
            }

            #region Retry Logic

            float timeoutDuration = 5.0f;
            float startTime = Time.realtimeSinceStartup;
            bool userFound = false;
            LobbyUser lobbyUser = default;

            // Loop until timeout
            while (Time.realtimeSinceStartup - startTime < timeoutDuration)
            {
                // Try to find the user
                lobbyUser = LobbyHandler.lobbyManager.CurrentLobby.Members.Find(x => x.Id == steamId);

                // Check if the user is valid (SteamID found)
                if (!string.IsNullOrEmpty(lobbyUser.Id))
                {
                    userFound = true;
                    break; // Exit the loop immediately
                }

                // Not found yet? Wait 250ms before checking again.
                await Task.Delay(250);
            }

            #endregion

            // Final Check: Did we find them after the 5 seconds?
            if (!userFound)
            {
                Debug.LogWarning($"[Server] Handshake Rejected. SteamID {steamId} not found in lobby after {timeoutDuration}s.");
                return AsyncResult.Fail("You are not a member of this Steam Lobby (Validation Timed Out).");
            }

            ulong clientId = overrideClientId == 0 ? info.sender.id.value : overrideClientId;

            // Create the Concrete Class with Steamworks Integration
            var steamUser = LobbyHandler.CreateSteamUser(clientId, steamId);
            if (steamUser != null)
            {
                // Add to the Dictionary
                LobbyHandler.PlayerList[clientId] = steamUser;
                var setPlayerTask = await LobbyDataHandler.SetPlayerData("IsReady", false);

                if (!setPlayerTask.IsSuccess)
                {
                    Debug.LogWarning($"Failed while trying to upload data.\n" +
                                     $"Unknown Error: {setPlayerTask.Message}");
                }
                
                // Publish Event locally on Server so UI updates
                EVENT.Publish(new OnPlayerJoinLobby(steamUser));
            }

            List<ulong> existingClientIds = new List<ulong>();
            List<string> existingSteamIds = new List<string>();

            foreach((ulong keyClientId, var data) in LobbyHandler.PlayerList)
            {
                if(keyClientId == clientId) continue; // Don't send the new player to themselves yet
                if(data is SteamLobbyUser u) 
                {
                    existingClientIds.Add(u.ClientId);
                    existingSteamIds.Add(u.SteamID.ToString());
                }
            }

            foreach (var playerId in NetworkManager.main.players)
            {
                if (existingClientIds.Exists(x => x == playerId.id.value))
                {
                    SyncExistingPlayers_RPC(playerId, existingClientIds.ToArray(), existingSteamIds.ToArray());
                }
            }

            // For client, it should be safe getting playerID from NetworkManager
            LobbyHandler.currentPlayerId = NetworkManager.main.localPlayer;
            Debug.Log($"[Server] Handshake Approved for {steamUser?.Username} (PurrID: {clientId}).");

            return AsyncResult.Success();
        }

        [TargetRpc]
        public static void NotifyKicked_RPC(PlayerID target, RPCInfo info = default)
        {
            Debug.LogWarning("[Lobby] You have been kicked from the lobby.");
        
            // Leave Steam Lobby
            _ = LobbyHandler.LeaveLobbyAsync();
        
            // Show UI Feedback
            // UIManager.ShowPopup("Kicked", "The host has removed you from the lobby.");
            Debug.Log("The host has removed you from the lobby.");
        }

        [ObserversRpc]
        public static void NotifyLobbyDataChanged_RPC(string key, string value)
        {
            // Update PurrLobby wrapper locally so GetLobbyData works instantly for clients
            if (LobbyHandler.lobbyManager.CurrentLobby.IsValid)
            {
                LobbyHandler.lobbyManager.CurrentLobby.Properties[key] = value;
            }
            Debug.Log($"[Lobby] Global Update: {key} = {value}");
        }

        [ObserversRpc]
        public static void NotifyPlayerDataChanged_RPC(ulong targetClientId, string key, string value)
        {
            // Skip self (Optimistic update already happened)
            if (NetworkManager.main.localPlayer.id == targetClientId) return;

            SyncLocalPlayerList(targetClientId, key, value);
        }

        [ObserversRpc]
        public static void SyncPlayerProperty_ObserverRPC(ulong targetClientId, string key, string value)
        {
            // Avoid double-updating the person who sent it (they did optimistic update)
            if (NetworkManager.main.localPlayer.id == targetClientId) return;

            UpdateLocalList(targetClientId, key, value);
        }

        private static void UpdateLocalList(ulong clientId, string key, string value)
        {
            if (LobbyHandler.PlayerList.TryGetValue(clientId, out var user))
            {
                if (user is SteamLobbyUser steamUser)
                {
                    // Update the Dictionary
                    steamUser.Extra[key] = value;

                    // Handle Property Mapping (Keeping the Class Properties in sync with Dictionary)
                    switch (key)
                    {
                        case "IsReady":
                            steamUser.IsReady = bool.Parse(value);
                            break;
                        // case "IsHost": ...
                    }

                    Debug.Log($"[Lobby] Synced {user.Username}: {key} = {value}");
                    
                    // EVENT.Publish(new OnLobbyDataUpdated(clientId)); 
                }
            }
            else
            {
                Debug.LogWarning($"[Lobby] Received update for unknown client {clientId}");
            }
        }

        /// <summary>
        /// Client asks Host: "Please save my data to the Steam Lobby so late joiners can see it."
        /// </summary>
        [ServerRpc(requireOwnership: false)]
        public static async Task RequestPlayerDataUpdate_RPC(string key, string value, RPCInfo info = default)
        {
            ulong senderId = info.sender.id;

            // Update Server RAM
            SyncLocalPlayerList(senderId, key, value);

            // Broadcast to others
            NotifyPlayerDataChanged_RPC(senderId, key, value);

            // Persistence (Host Only)
            if (LobbyHandler.lobbyManager.CurrentLobby.IsOwner)
            {
                if (LobbyHandler.PlayerList.TryGetValue(senderId, out var user) && user is SteamLobbyUser steamUser)
                {
                    await LobbyDataHandler.SavePlayerJsonToLobby(senderId, steamUser.SteamID.ToString());
                }
            }
        }

        private static void SyncLocalPlayerList(ulong clientId, string key, string value)
        {
            if (LobbyHandler.PlayerList.TryGetValue(clientId, out var user) && user is SteamLobbyUser steamUser)
            {
                steamUser.UpdateInternalData(key, value);
                Debug.Log($"[Lobby] User {steamUser.Username} updated {key} -> {value}");
            }
        }

        [TargetRpc]
        public static void SyncExistingPlayers_RPC(PlayerID target, ulong[] clientIds, string[] steamIds, RPCInfo info = default)
        {
            for(int i=0; i<clientIds.Length; i++)
            {
                // Create user locally
                var user = LobbyHandler.CreateSteamUser(clientIds[i], steamIds[i]);
                if(user != null) LobbyHandler.PlayerList[clientIds[i]] = user;
            }
            // EVENT.Publish(new OnLobbyRefreshed());
        }
    }
}
