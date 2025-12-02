using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;
using PurrLobby;
using PurrNet;
using Newtonsoft.Json;
using UnityEngine.Events;

// Mock to bypass Steamworks
public class MockLobbyProvider : ILobbyProvider
{
    public bool IsOwner { get; set; } = false;
    public string CurrentLobbyId { get; set; } = "12345";
    public Dictionary<string, string> DataStore = new();

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }
    public void Shutdown()
    {
        
    }

    public Task<List<FriendUser>> GetFriendsAsync(LobbyManager.FriendFilter filter)
    {
        return (Task<List<FriendUser>>)Task.CompletedTask;
    }

    public Task InviteFriendAsync(FriendUser user)
    {
        return Task.CompletedTask; 
    }

    public Task<List<LobbyUser>> GetLobbyMembersAsync()
    {
        return (Task<List<LobbyUser>>)Task.CompletedTask; 
    }

    public Task<string> GetLocalUserIdAsync() => Task.FromResult("76561198000000000"); // Fake SteamID

    // Mock Data Setting
    public Task SetLobbyDataAsync(string key, string value) 
    {
        DataStore[key] = value;
        return Task.CompletedTask; 
    }

    public Task<string> GetLobbyDataAsync(string key)
    {
        return (Task<string>)Task.CompletedTask;
    }

    public Task SetPlayerData(string key, string value) => Task.CompletedTask;
    
    // ... Implement other interface members as no-ops or basic returns ...
    public Task<Lobby> CreateLobbyAsync(int maxPlayers, Dictionary<string, string> properties) => Task.FromResult(new Lobby());
    public Task LeaveLobbyAsync(string lobbyId)
    {
        return Task.CompletedTask;
    }

    public Task<Lobby> JoinLobbyAsync(string lobbyId) => Task.FromResult(new Lobby());
    public Task LeaveLobbyAsync() => Task.CompletedTask;
    public Task<List<Lobby>> SearchLobbiesAsync(int max, Dictionary<string, string> filters) => Task.FromResult(new List<Lobby>());
    public Task SetIsReadyAsync(string userId, bool isReady) => Task.CompletedTask;
    public Task SetAllReadyAsync() => Task.CompletedTask;
    public Task SetLobbyStartedAsync() => Task.CompletedTask;
    public event UnityAction<string> OnLobbyJoinFailed;
    public event UnityAction OnLobbyLeft;
    public event UnityAction<Lobby> OnLobbyUpdated;
    public event UnityAction<List<LobbyUser>> OnLobbyPlayerListUpdated;
    public event UnityAction<List<FriendUser>> OnFriendListPulled;
    public event UnityAction<string> OnError;
}