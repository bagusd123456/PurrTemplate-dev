using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using PurrLobby;
using PurrNet;
using QFSW.QC;
using Steamworks;
using UnityEngine;

namespace NyxMachina.Multiplayer
{
    public static class LobbyHostCommand
    {
        private static LobbyManager LobbyManager => LobbySystem.lobbyManager;
        private static NetworkManager NetworkManager => LobbySystem.networkManager;

        [Command]
        public static async Task<AsyncResult> SetAllReadyAsync()
        {
            if (!LobbyManager.CurrentLobby.IsValid)
                return AsyncResult.Fail("Lobby is not valid");

            if (!LobbyManager.CurrentLobby.IsOwner)
                return AsyncResult.Fail("Only the Host can force all player as ready.");

            try
            {
                await LobbyManager.CurrentProvider.SetAllReadyAsync();
            }
            catch (Exception e)
            {
                return AsyncResult.Fail(e.Message);
            }
            return AsyncResult.Success();
        }

        [Command]
        public static async Task<AsyncResult> SetLobbyStartedAsync()
        {
            if (!LobbyManager.CurrentLobby.IsOwner)
                return AsyncResult.Fail("Only the Host can start lobby.");

            try
            {
                await LobbyManager.CurrentProvider.SetLobbyStartedAsync();
            }
            catch (Exception e)
            {
                return AsyncResult.Fail($"Failed to start Lobby.\nUnknown error: {e}");
            }
            return AsyncResult.Success();
        }

        [Command]
        public static async Task<AsyncResult> KickPlayerAsync(ulong targetClientId)
        {
            if (!LobbyManager.CurrentLobby.IsOwner)
                return AsyncResult.Fail("Only the Host can kick players.");

            if (targetClientId == NetworkManager.localPlayer.id)
                return AsyncResult.Fail("You cannot kick yourself.");

            if (!LobbySystem.PlayerList.TryGetValue(targetClientId, out var userData))
                return AsyncResult.Fail("Player not found.");

            try
            {
                Debug.Log($"[LobbyHandler] Kicking player {userData.Username}...");
                PlayerID? targetPlayer = null;

                foreach (var player in NetworkManager.players)
                {
                    if (player.id.value != targetClientId) continue;
                    targetPlayer = player;
                    break;
                }

                if (!targetPlayer.HasValue)
                    return AsyncResult.Fail($" [LobbyHandler] Failed to kick.\n" +
                                            $"Cannot found player with id '{targetClientId}'.");

                LobbyNetworkHandler.NotifyKicked_RPC(targetPlayer.Value);
                return AsyncResult.Success();
            }
            catch (Exception e)
            {
                return AsyncResult.Fail($"[LobbyHandler] Failed to kick.\n" +
                                        $"Unknown error: {e.Message}");
            }
        }

        [Command]
        public static async Task<AsyncResult> SetLobbyLockedStateAsync(bool isLocked)
        {
            if (!LobbyManager.CurrentLobby.IsOwner)
                return AsyncResult.Fail("[LobbyHandler] Only the Host can lock the lobby.");

            ulong lobbyId = ulong.Parse(LobbyManager.CurrentLobby.LobbyId);
            bool success = SteamMatchmaking.SetLobbyJoinable(new CSteamID(lobbyId), isLocked);

            return success ? AsyncResult.Success() : AsyncResult.Fail("[LobbyHandler] Failed to lock Lobby (Steam API returned false).");
        }

        [Command]
        public static async Task<AsyncResult> SetLobbyPrivateStateAsync(bool isPrivate, string password)
        {
            if (!LobbyManager.CurrentLobby.IsOwner)
                return AsyncResult.Fail("[LobbyHandler] Only the Host can private the lobby.");

            try
            {
                return await LobbyDataHandler.SetLobbyDataAsync("isPrivate", isPrivate.ToString().ToLower());
            }
            catch (Exception e)
            {
                return AsyncResult.Fail(e.Message);
            }
        }

        [Command]
        public static async Task<AsyncResult> PromoteToHostAsync(ulong targetClientId)
        {
            return AsyncResult.Fail("[LobbyHandler] Feature still in development!");
            /* 
            // Logic for future implementation
            ulong lobbyId = ulong.Parse(lobbyManager.CurrentLobby.LobbyId);
            SteamMatchmaking.SetLobbyOwner(new CSteamID(lobbyId), steamUser.SteamID);
            */
        }
    }
}
