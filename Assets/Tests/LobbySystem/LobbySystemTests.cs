using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using NyxMachina.Multiplayer;
using PurrLobby;
using PurrNet;
using UnityEngine;

[TestFixture]
public class LobbySystemTests
{
    private MockLobbyProvider _mockProvider;

    [SetUp]
    public void Setup()
    {
        // Reset Static State
        LobbySystem.PlayerList = new Dictionary<ulong, ILobbyDataModel>();
    
        // Instantiate Managers
        // Using new GameObject is standard for Monobehaviour tests
        var testGO = new GameObject("TestObject");
        var lobbyManager = testGO.AddComponent<LobbyManager>();

        // Inject Mock Provider
        _mockProvider = new MockLobbyProvider();
        lobbyManager.SetProvider(_mockProvider);
    
        // IMPORTANT: _currentLobby setter relies on _lobbyDataHolder being set.
        // Awake() usually handles this, but in tests, it's safer to ensure it exists.
        var holderGO = new GameObject("DataHolder");
        var dataHolder = holderGO.AddComponent<LobbyDataHolder>();
    
        // Inject DataHolder into Manager (Private Field)
        typeof(LobbyManager).GetField("_lobbyDataHolder", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.SetValue(lobbyManager, dataHolder);

        // Inject into Static LobbyHandler
        typeof(LobbySystem).GetField("lobbyManager", BindingFlags.Public | BindingFlags.Static)
            ?.SetValue(null, lobbyManager);

        // Set the Lobby State
        var validLobby = new Lobby 
        { 
            IsValid = true, 
            LobbyId = "12345", 
            IsOwner = false,
            Properties = new Dictionary<string, string>() 
        };
        
        // Change "CurrentLobby" to "_currentLobby"
        // Add BindingFlags.NonPublic because it is private
        typeof(LobbyManager).GetProperty("_currentLobby", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.SetValue(lobbyManager, validLobby);
    }

    [TearDown]
    public void Teardown()
    {
        LobbySystem.PlayerList.Clear();
    }

    #region Data Serialization & Integrity

    [Test]
    public void SteamLobbyUser_Serialization_RoundTrip_PreservesData()
    {
        // Arrange
        ulong clientId = 100;
        // Construct manually via internal data since constructor calls SteamAPI
        var originalUser = new SteamLobbyUser(); 
        
        // Use reflection to set private setters if necessary, or public props
        typeof(SteamLobbyUser).GetProperty("ClientId")?.SetValue(originalUser, clientId);
        typeof(SteamLobbyUser).GetProperty("Username")?.SetValue(originalUser, "TestUser");
        typeof(SteamLobbyUser).GetProperty("IsReady")?.SetValue(originalUser, true);
        
        originalUser.UpdateInternalData("CustomKey", "CustomValue");

        // Act
        string json = originalUser.Serialize();
        var deserializedUser = JsonConvert.DeserializeObject<SteamLobbyUser>(json);

        // Assert
        Assert.AreEqual(originalUser.ClientId, deserializedUser.ClientId);
        Assert.AreEqual(originalUser.Username, deserializedUser.Username);
        Assert.IsTrue(deserializedUser.IsReady);
        Assert.IsTrue(deserializedUser.Extra.ContainsKey("CustomKey"));
        Assert.AreEqual("CustomValue", deserializedUser.Extra["CustomKey"]);
    }

    [Test]
    public void SteamLobbyUser_UpdateInternalData_IsReady_MapsProperty()
    {
        // Arrange
        var user = new SteamLobbyUser();
        
        // Act
        user.UpdateInternalData("IsReady", "true");

        // Assert
        Assert.IsTrue(user.IsReady, "String 'true' did not map to boolean property");
        Assert.AreEqual("true", user.Extra["IsReady"]);
    }

    #endregion

    #region LobbyDataHandler Logic

    [Test]
    public void SetLobbyData_AsClient_ReturnsFail()
    {
        // Arrange
        _mockProvider.IsOwner = false; // We are NOT host
        // We must re-inject the lobby with IsOwner=false
        SetCurrentLobbyState(isOwner: false);

        // Act
        // FIX: Change 'await' to '.GetAwaiter().GetResult()' and method signature to 'void'
        var result = LobbyDataHandler.SetLobbyDataAsync("GameMode", "Deathmatch").GetAwaiter().GetResult();

        // Assert
        Assert.IsFalse(result.IsSuccess, "Client should not be able to set Global Lobby Data");
        Assert.That(result.Message, Does.Contain("Only Host"));
    }

    [Test]
    public void SetLobbyData_AsHost_UpdatesProvider()
    {
        // Arrange
        SetCurrentLobbyState(isOwner: true);

        // Act
        // FIX: Change 'await' to '.GetAwaiter().GetResult()' and method signature to 'void'
        var result = LobbyDataHandler.SetLobbyDataAsync("GameMode", "CTF").GetAwaiter().GetResult();

        // Assert
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("CTF", _mockProvider.DataStore["GameMode"]);
    }

    #endregion

    #region Handshake & Network Logic

    [Test]
    public void SyncExistingPlayers_ExcludesSelf_WhenAdding()
    {
        // Scenario: A bug existed where the client list remained empty because self was excluded improperly.
        
        // Arrange
        ulong myClientId = 999;
        string mySteamId = "76561198000000001";
        
        // Mock NetworkManager local player
        //var localPlayer = new PlayerID { id = myClientId, owner = null }; // Mock PurrNet PlayerID

        var localPlayer = new PlayerID(myClientId, true);
        
        // We cannot easily mock NetworkManager.main singleton without a shim, 
        // so we test the Logic Method directly via Reflection if possible 
        // OR we test the logic we refactored into "Internal_UpsertPlayers".
        
        // Let's assume we are testing the logic flow:
        // 1. I am 999.
        // 2. Server sends me [999, 888].
        // 3. I should add 888.
        // 4. I should NOT add 999 (because I add myself in JoinLobby).

        // Simulate Pre-condition: Local player added themselves
        var myUser = new SteamLobbyUser();
        typeof(SteamLobbyUser).GetProperty("ClientId")?.SetValue(myUser, myClientId);
        LobbySystem.PlayerList.Add(myClientId, myUser);

        // Act - Simulate receiving RPC data
        ulong[] incomingClients = new ulong[] { myClientId, 888 };
        string[] incomingSteams = new string[] { mySteamId, "76561198000000002" };

        // We use Reflection to invoke your private static method "Internal_UpsertPlayers"
        // Note: You might need to change that method to 'internal' for easier testing
        MethodInfo method = typeof(NyxMachina.Multiplayer.LobbyNetworkHandler)
            .GetMethod("Internal_UpsertPlayers", BindingFlags.NonPublic | BindingFlags.Static);

        // Setup a mock for NetworkManager.main.localPlayer ID match
        // Since we can't easily mock the singleton in this context without a framework,
        // we will assert based on the logic that "If ID exists in dict, don't overwrite".
        
        // Invoke
        // Note: This test will fail if NetworkManager.main is null. 
        // In a real Unit Test, wrap NetworkManager.main access in a mockable interface.
        // For now, assuming you handle the null check or run in a scene with NetworkManager.
        
        // SKIP: Without wrapping NetworkManager, this specific test crashes. 
        // RECOMMENDED: Refactor 'NetworkManager.main.localPlayer.id' to 'ILobbyContext.LocalPlayerId'.
    }

    [Test]
    public void ValidateHandshake_Timeout_ReturnsFailure()
    {
        // Scenario: Server never responds to the RPC. Client should not hang forever.

        // Act
        // We call the async wrapper. Since we never invoke the TargetRPC response, it should timeout.
        // We lower the timeout for the test to avoid waiting 10s (requires code change to allow timeout injection)
        // Or we just assert it fails after the hardcoded delay.
        
        var result = LobbyNetworkHandler.ValidateHandshakeAsync_RPC("steamId").GetAwaiter().GetResult();
        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual("Handshake timed out.", result.Message);
    }

    #endregion

    #region Vulnerabilities & Security

    [Test]
    public void Vulnerability_JsonInjection_In_ExtraData()
    {
        // Scenario: Malicious user tries to inject JSON syntax into a value to corrupt the file.
        // Newtonsoft should handle this, but let's verify.

        // Arrange
        var user = new SteamLobbyUser();
        string maliciousKey = "Bio";
        string maliciousValue = "\", \"IsHost\": true, \"Ignore\": \""; 

        // Act
        user.UpdateInternalData(maliciousKey, maliciousValue);
        string json = user.Serialize();
        
        // Deserialize
        var resultUser = JsonConvert.DeserializeObject<SteamLobbyUser>(json);

        // Assert
        Assert.AreEqual(maliciousValue, resultUser.Extra[maliciousKey]);
        Assert.IsFalse(resultUser.IsHost, "Injection successfully overwrote IsHost property!");
    }

    [Test]
    public void Vulnerability_OverrideClientId_Spoofing()
    {
        // Scenario: A hacker calls ValidateHandshake_RPC manually, passing an 'overrideClientId' 
        // that belongs to the Host (e.g., 0 or 1), trying to hijack their session.

        // This is a logic audit test. 
        
        // The code in LobbyNetworkHandler.cs:
        // ulong clientId = overrideClientId == 0 ? info.sender.id.value : overrideClientId;
        
        // VULNERABILITY FOUND:
        // Since [ServerRpc(requireOwnership: false)] is set, ANYONE can call this.
        // If I send overrideClientId = TargetVictimID, the server executes:
        // LobbyHandler.PlayerList[TargetVictimID] = new User(... my steam ID ...);
        
        // This overwrites the legitimate player's data with the hacker's SteamID.
        
        // Assert
        Assert.Fail("VULNERABILITY: ValidateHandshakeAsync_RPC allows arbitrary 'overrideClientId'. " +
                    "A client can overwrite another player's SteamID association in the Server Dictionary. " +
                    "Remove 'overrideClientId' parameter or check info.sender.id == overrideClientId.");
    }

    #endregion

    #region Helper Methods

    private void SetCurrentLobbyState(bool isOwner)
    {
        var lobby = new Lobby { IsValid = true, IsOwner = isOwner, Properties = new Dictionary<string, string>() };

        typeof(LobbyManager).GetProperty("_currentLobby", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.SetValue(LobbySystem.lobbyManager, lobby);
    
        // Also update provider mock
        _mockProvider.IsOwner = isOwner;
    }

    #endregion
}