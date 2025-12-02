using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NyxMachina.Shared.EventFramework;
using PurrLobby;
using PurrNet;
using PurrNet.Transports;
using UnityEngine;

namespace NyxMachina.Multiplayer
{
    public class LobbyNetworkHandler
    {
        #region Handshake & Connection Logic

        /// <summary>
        /// Called by the Client to validate their presence with the Server.
        /// Performs Steam validation, adds user to Server RAM, and syncs lists.
        /// </summary>
        [ServerRpc(requireOwnership: false)]
        public static async Task<AsyncResult> ValidateHandshakeAsync_RPC(string steamId, ulong overrideClientId = 0, RPCInfo info = default)
        {
            // Server Integrity Check
            if (LobbySystem.lobbyManager == null || !LobbySystem.lobbyManager.CurrentLobby.IsValid)
            {
                return AsyncResult.Fail("Server is not in a valid Steam Lobby.");
            }

            // Steam Membership Validation (with Timeout)
            bool isMember = await IsUserInSteamLobbyAsync(steamId);
            if (!isMember)
            {
                Debug.LogWarning($"[Server] Handshake Rejected. SteamID {steamId} not found in lobby.");
                return AsyncResult.Fail("You are not a member of this Steam Lobby (Validation Timed Out).");
            }

            ulong clientId = overrideClientId == 0 ? info.sender.id.value : overrideClientId;

            // Register User on Server
            var steamUser = LobbyDataFactory.CreateSteamUser(clientId, steamId);
            if (steamUser != null)
            {
                LobbySystem.PlayerList[clientId] = steamUser;
                await LobbyDataHandler.SetPlayerData("IsReady", false);
                EVENT.Publish(new OnPlayerJoinLobby(steamUser));
            }
            else
            {
                return AsyncResult.Fail("Failed to create user data on Server.");
            }

            // Sync: Send Existing Players -> New Client
            var (existingClientIds, existingSteamIds) = GetExistingPlayersData(excludeClientId: clientId);
            
            // Find the specific connection to target
            PlayerID? newPlayerTarget = NetworkManager.main.players.FirstOrDefault(p => p.id.value == clientId);

            if (newPlayerTarget.HasValue)
            {
                SyncPlayers_TargetRPC(newPlayerTarget.Value, existingClientIds, existingSteamIds);
            }

            // Sync: Send New Client -> Existing Clients
            SyncPlayers_ObserverRPC(new ulong[] { clientId }, new string[] { steamId });

            // Finalize
            LobbySystem.currentPlayerId = NetworkManager.main.localPlayer;
            Debug.Log($"[Server] Handshake Approved for {steamUser.Username}");
            
            return AsyncResult.Success();
        }

        #endregion

        #region Player Synchronization (Add/Remove)

        private static TaskCompletionSource<bool> _pullListTcs;

        /// <summary>
        /// Public Awaitable API: Asks the server for the list and waits until it arrives.
        /// </summary>
        public static async Task<AsyncResult> RequestFullPlayerListAsync(int timeoutMs = 5000)
        {
            if (NetworkManager.main.clientState != ConnectionState.Connected)
                return AsyncResult.Fail("Not connected to server.");

            // Create the Wait Handle
            _pullListTcs = new TaskCompletionSource<bool>();

            // Send the Request
            RequestFullPlayerListServerRPC();

            // Wait for the Response (or Timeout)
            var timeoutTask = Task.Delay(timeoutMs);
            var completedTask = await Task.WhenAny(_pullListTcs.Task, timeoutTask);

            // Handle Result
            if (completedTask == timeoutTask)
            {
                _pullListTcs = null; // Cleanup
                return AsyncResult.Fail("Request timed out. Server did not respond.");
            }

            return AsyncResult.Success();
        }

        [ServerRpc(requireOwnership: false)]
        static void RequestFullPlayerListServerRPC(RPCInfo info = default)
        {
            ulong senderId = info.sender.id.value;
            Debug.Log($"[Server] Client {senderId} requested full player list sync.");

            // Gather Data (All existing players excluding the requester)
            var (clientIds, steamIds) = GetExistingPlayersData(excludeClientId: senderId);

            // Send Data (Reuse existing logic)
            // The transport guarantees order, so this data arrives BEFORE the response signal below.
            SyncPlayers_TargetRPC(info.sender, clientIds, steamIds);

            // Send Completion Signal
            RequestFullPlayerListResponseTargetRPC(info.sender);
        }

        [TargetRpc]
        static void RequestFullPlayerListResponseTargetRPC(PlayerID target)
        {
            // Unlock the waiting task on the Client
            if (_pullListTcs != null && !_pullListTcs.Task.IsCompleted)
            {
                _pullListTcs.TrySetResult(true);
            }
        }

        /// <summary>
        /// Sends a list of EXISTING players to a SPECIFIC new client.
        /// </summary>
        [TargetRpc]
        public static void SyncPlayers_TargetRPC(PlayerID target, ulong[] clientIds, string[] steamIds)
        {
            Internal_UpsertPlayers(clientIds, steamIds);
        }

        /// <summary>
        /// Sends a list of NEW players (usually just an array of 1) to EVERYONE.
        /// </summary>
        [ObserversRpc]
        public static void SyncPlayers_ObserverRPC(ulong[] clientIds, string[] steamIds)
        {
            Internal_UpsertPlayers(clientIds, steamIds);
        }

        /// <summary>
        /// Shared logic: Takes arrays of IDs and ensures they exist in the local PlayerList.
        /// This handles 1 player (new join) or 50 players (bulk sync) exactly the same way.
        /// </summary>
        private static void Internal_UpsertPlayers(ulong[] clientIdArray, string[] steamIdArray)
        {
            for (int i = 0; i < clientIdArray.Length; i++)
            {
                ulong clientId = clientIdArray[i];
                string steamId = steamIdArray[i];

                // Skip if it's me (Local Player) - I already added myself during Join
                if (clientId == NetworkManager.main.localPlayer.id.value) continue;

                // If user already exists, skip
                if (LobbySystem.PlayerList.ContainsKey(clientId)) continue;

                // Create and Add
                var user = LobbyDataFactory.CreateSteamUser(clientId, steamId);
                if (user != null)
                {
                    LobbySystem.PlayerList[clientId] = user;
                    Debug.Log($"[Lobby] Synced User: {user.Username} (ID: {clientId})");
                    EVENT.Publish(new OnPlayerJoinLobby(user));
                }
            }
        }

        #endregion

        #region Data Synchronization (Lobby & Player Data)

        [ObserversRpc]
        public static void NotifyLobbyDataChanged_RPC(string key, string value)
        {
            // Update PurrLobby wrapper locally so GetLobbyData works instantly for clients
            if (LobbySystem.lobbyManager.CurrentLobby.IsValid)
            {
                LobbySystem.lobbyManager.CurrentLobby.Properties[key] = value;
            }
            Debug.Log($"[Lobby] Global Update: {key} = {value}");
        }

        [ObserversRpc]
        public static void NotifyPlayerDataChanged_RPC(ulong targetClientId, string key, string value)
        {
            // Skip self (Optimistic update already happened locally)
            if (NetworkManager.main.localPlayer.id == targetClientId) return;

            SyncLocalPlayerList(targetClientId, key, value);
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

            // Persistence (Host Only) - Save to Steam JSON
            if (LobbySystem.lobbyManager.CurrentLobby.IsOwner)
            {
                if (LobbySystem.PlayerList.TryGetValue(senderId, out var user) && user is SteamLobbyUser steamUser)
                {
                    await LobbyDataHandler.SavePlayerJsonToLobby(senderId, steamUser.SteamID.ToString());
                }
            }
        }

        /// <summary>
        /// Updates the local dictionary and maps special keys (like IsReady) to properties.
        /// </summary>
        internal static void SyncLocalPlayerList(ulong clientId, string key, string value)
        {
            if (LobbySystem.PlayerList.TryGetValue(clientId, out var user) && user is SteamLobbyUser steamUser)
            {
                // Update the Dictionary
                steamUser.UpdateInternalData(key, value);

                // Handle Property Mapping
                switch (key)
                {
                    case "IsReady":
                        if (bool.TryParse(value, out bool readyState))
                            steamUser.IsReady = readyState;
                        break;
                }

                Debug.Log($"[Lobby] User {steamUser.Username} updated {key} -> {value}");
            }
            else
            {
                Debug.LogWarning($"[Lobby] Received update for unknown client {clientId}");
            }
        }

        #endregion

        #region Moderation & Admin

        [TargetRpc]
        public static void NotifyKicked_RPC(PlayerID target, RPCInfo info = default)
        {
            Debug.LogWarning("[Lobby] You have been kicked from the lobby.");
        
            // Leave Steam Lobby and Transport
            _ = LobbySystem.LeaveLobbyAsync();
            
            // TODO: UI Feedback via Event
            // EVENT.Publish(new OnPlayerKicked());
        }

        #endregion

        #region Internal Helper Methods

        /// <summary>
        /// Loops for a few seconds checking if Steam API reports the user as a member.
        /// Handles the race condition where Transport connects before Steam callbacks fire.
        /// </summary>
        private static async Task<bool> IsUserInSteamLobbyAsync(string steamId)
        {
            float timeoutDuration = 5.0f;
            float startTime = Time.realtimeSinceStartup;

            while (Time.realtimeSinceStartup - startTime < timeoutDuration)
            {
                var member = LobbySystem.lobbyManager.CurrentLobby.Members.Find(x => x.Id == steamId);
                if (!string.IsNullOrEmpty(member.Id))
                {
                    return true;
                }
                await Task.Delay(250);
            }
            return false;
        }

        /// <summary>
        /// Compiles lists of all current players excluding the specific ID (usually the new joiner).
        /// </summary>
        private static (ulong[] clientIds, string[] steamIds) GetExistingPlayersData(ulong excludeClientId)
        {
            List<ulong> clientIds = new List<ulong>();
            List<string> steamIds = new List<string>();

            foreach ((ulong keyClientId, var data) in LobbySystem.PlayerList)
            {
                if (keyClientId == excludeClientId) continue;

                clientIds.Add(data.ClientId);
                steamIds.Add(data.UniqueUserId);
            }

            return (clientIds.ToArray(), steamIds.ToArray());
        }

        #endregion
    }
}