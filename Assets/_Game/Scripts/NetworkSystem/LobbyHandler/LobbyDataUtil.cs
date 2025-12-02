using PurrNet;
using UnityEngine;

public static class LobbyDataUtil
{
    public static bool IsLocal(this PlayerID player)
    {
        // Ensure NetworkManager exists to avoid errors during shutdown
        if (NetworkManager.main == null) return false;

        return player == NetworkManager.main.localPlayer;
    }

    public static PlayerID GetCurrentPlayerID()
    {
        var cachedPlayerId = LobbySystem.currentPlayerId;
        var networkManagerPlayerId = NetworkManager.main.localPlayer;

        if (cachedPlayerId.Equals(default))
        {
            Debug.LogWarning($"CurrentPlayerId is not cached yet, returning the id from NetworkManager.");
            return networkManagerPlayerId;
        }

        if (networkManagerPlayerId.Equals(default))
        {
            Debug.LogWarning($"PlayerID in NetworkManager is not initialized yet, returning the id from cache.");
            return cachedPlayerId;
        }

        if (cachedPlayerId != networkManagerPlayerId)
        {
            Debug.LogWarning($"Conflicting Player ID Detected!\n" +
                             $"Returning cachedPlayerID");
            return cachedPlayerId;
        }

        return networkManagerPlayerId;
    }
}