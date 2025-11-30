using PurrLobby;
using PurrNet;
using System.Threading.Tasks;
using UnityEngine;

namespace NyxMachina.Multiplayer
{
    public class LobbyNetworkHandler : NetworkBehaviour
    {
        public static LobbyNetworkHandler Instance { get; private set; }

        protected override void OnSpawned()
        {
            base.OnSpawned();
            Instance = this;
        }

        protected override void OnDespawned()
        {
            base.OnDespawned();
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Called by the Client to validate their presence with the Server.
        /// Returns a Task that completes when the Server responds.
        /// </summary>
        [ServerRpc(requireOwnership: false)]
        public async Task<AsyncResult> ValidateHandshakeAsync_RPC(string steamId, RPCInfo info = default)
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

            // Success Logic: Map PurrNet PlayerID to Steam User Data
            LobbyHandler.PlayerList[info.sender.id.value] = lobbyUser;

            Debug.Log($"[Server] Handshake Approved for {lobbyUser.DisplayName} (PurrID: {info.sender.id}).");

            // Return Success to the waiting Client
            return AsyncResult.Success();
        }
    }
}
