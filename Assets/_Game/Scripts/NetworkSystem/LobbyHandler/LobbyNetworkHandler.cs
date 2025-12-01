using System.Collections.Generic;
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

            // Check ownership status from the PurrLobby data
            // (Assuming the Lobby Owner is the Host)
            bool isOwner = LobbyHandler.lobbyManager.CurrentLobby.IsOwner;

            ulong clientId = overrideClientId == 0 ? info.sender.id.value : overrideClientId;

            // Create the Concrete Class with Steamworks Integration
            var steamUser = LobbyHandler.CreateSteamUser(clientId, steamId, isOwner);
            if (steamUser != null)
            {
                // Add to the Dictionary
                LobbyHandler.PlayerList[clientId] = steamUser;
                
                // Publish Event locally on Server so UI updates
                EVENT.Publish(new OnPlayerJoinLobby(steamUser));
            }

            List<ulong> existingIds = new List<ulong>();
            List<string> existingSteamIds = new List<string>();

            foreach(var kvp in LobbyHandler.PlayerList)
            {
                if(kvp.Key == clientId) continue; // Don't send self
                if(kvp.Value is SteamLobbyUser u) 
                {
                    existingIds.Add(u.ClientId);
                    existingSteamIds.Add(u.SteamID.ToString());
                }
            }
            if(existingIds.Count > 0)
                NotifyLobbyDataChanged_RPC("PlayerList", "");

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
            // This is for instant UI updates, so we don't have to wait for the Steam Callback
            Debug.Log($"[Lobby] Setting updated: {key} = {value}");
            
            // EVENT.Publish(new OnLobbySettingChanged(key, value));
        }

        [ServerRpc(requireOwnership: false)]
        public static async Task<AsyncResult> UpdatePlayerData_RPC(string key, string value, RPCInfo info = default)
        {
            // Server updates its own list
            if (LobbyHandler.PlayerList.TryGetValue(info.sender.id, out var user))
            {
                // Cast to concrete class to access internal dictionary
                if (user is SteamLobbyUser steamUser)
                {
                    steamUser.UserDataDictionary[key] = value;
                }
            }

            // Server tells all other clients to update
            NotifyPlayerDataChanged_RPC(info.sender.id, key, value);
            return AsyncResult.Success();
        }

        [ObserversRpc]
        public static void NotifyPlayerDataChanged_RPC(ulong targetClientId, string key, string value)
        {
            if (LobbyHandler.PlayerList.TryGetValue(targetClientId, out var user))
            {
                if (user is SteamLobbyUser steamUser)
                {
                    steamUser.UserDataDictionary[key] = value;
                    // Trigger an event so UI redraws
                    // EVENT.Publish(new OnPlayerDataUpdated(targetClientId, key));
                    Debug.Log($"Player '{targetClientId}' Data '{key}' to '{value}' Updated");
                }
            }
        }

        [ServerRpc(requireOwnership: false)]
        public static async Task SyncPlayerProperty_RPC(string key, string value, RPCInfo info = default)
        {
            // Server updates its own list
            UpdateLocalList(info.sender.id, key, value);

            // Server forwards to all other clients
            SyncPlayerProperty_ObserverRPC(info.sender.id, key, value);
        
            await Task.CompletedTask;
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
                    steamUser.UserDataDictionary[key] = value;

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
    }
}
