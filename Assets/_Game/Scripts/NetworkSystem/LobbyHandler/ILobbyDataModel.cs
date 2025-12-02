using Newtonsoft.Json;
using System.Collections.Generic;
using UnityEngine;

public interface ILobbyDataModel
{
    [JsonProperty("ClientId")]
    public ulong ClientId { get; }
    [JsonProperty("UniqueUserId")]
    public string UniqueUserId { get; }

    [JsonProperty("Username")]
    public string Username { get; }
    public Texture2D UserAvatar { get; }
    [JsonProperty("IsReady")]
    public bool IsReady { get; }
    [JsonProperty("IsHost")]
    public bool IsHost { get; }

    [JsonProperty("Extra")]
    public Dictionary<string, object> Extra { get; }

    public string Serialize();
    void UpdateInternalData(string key, string value);
}