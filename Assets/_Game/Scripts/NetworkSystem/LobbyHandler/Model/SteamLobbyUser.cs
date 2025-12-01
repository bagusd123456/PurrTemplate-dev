using Steamworks;
using UnityEngine;
using System.Collections.Generic;

public class SteamLobbyUser : UserLobbyData
{
    public ulong ClientId { get; internal set; }

    public string Username { get; internal set; }
    public Texture2D UserAvatar { get; internal set; }
    public bool IsReady { get; internal set; }
    public bool IsHost { get; internal set; }
    public Dictionary<string, object> UserDataDictionary { get; internal set; }

    public CSteamID SteamID { get; private set; }

    public SteamLobbyUser(ulong purrNetClientId, CSteamID steamId, bool isHost)
    {
        ClientId = purrNetClientId;
        SteamID = steamId;
        IsHost = isHost;
        IsReady = false;
        UserDataDictionary = new Dictionary<string, object>();

        // Get Username
        Username = SteamFriends.GetFriendPersonaName(steamId);

        // Get Avatar
        UserAvatar = GetSteamImageAsTexture2D(steamId);
    }

    public static Texture2D GetSteamImageAsTexture2D(CSteamID steamId)
    {
        // Get the Image ID for the Large Avatar
        int imageId = SteamFriends.GetLargeFriendAvatar(steamId);

        // If -1, the avatar isn't loaded yet (or invalid). 
        // In a real scenario, you might want to listen to PersonaStateChange_t callback to retry.
        if (imageId == -1) 
            return null; 

        if (SteamUtils.GetImageSize(imageId, out uint width, out uint height))
        {
            // Allocate buffer (RGBA * 4 bytes)
            byte[] imageBuffer = new byte[width * height * 4];

            if (SteamUtils.GetImageRGBA(imageId, imageBuffer, (int)(width * height * 4)))
            {
                // Steam provides images "upside down" compared to Unity. We must flip it.
                // We can do this efficiently by processing the buffer before creating the texture.
                FlipImageBufferVertical(imageBuffer, (int)width, (int)height);

                // Create Texture
                Texture2D texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false);
                texture.LoadRawTextureData(imageBuffer);
                texture.Apply();
                return texture;
            }
        }
        
        return null;
    }

    private static void FlipImageBufferVertical(byte[] buffer, int width, int height)
    {
        // Width * 4 bytes per pixel
        int rowSpan = width * 4; 
        byte[] tempRow = new byte[rowSpan];

        for (int y = 0; y < height / 2; y++)
        {
            int topRowIndex = y * rowSpan;
            int bottomRowIndex = (height - y - 1) * rowSpan;

            // Copy top to temp
            System.Array.Copy(buffer, topRowIndex, tempRow, 0, rowSpan);
            // Copy bottom to top
            System.Array.Copy(buffer, bottomRowIndex, buffer, topRowIndex, rowSpan);
            // Copy temp to bottom
            System.Array.Copy(tempRow, 0, buffer, bottomRowIndex, rowSpan);
        }
    }
}