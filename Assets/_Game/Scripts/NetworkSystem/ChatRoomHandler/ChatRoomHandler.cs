using NyxMachina.Shared.EventFramework;
using NyxMachina.Shared.EventFramework.Core.Payloads;
using QFSW.QC;
using Steamworks;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class ChatRoomHandler
{
    // Enum to define the recording mode
    public enum VoiceMode
    {
        None = -1,
        PushToTalk,
        VoiceActivity
    }

    public struct TextChatDataReceived : IPayload
    {
        public TextChatData TextData;

        public TextChatDataReceived(TextChatData receivedData)
        {
            TextData = receivedData;
        }
    }

    public struct TextChatData
    {
        public string senderName;
        public string message;
    }

    // Callbacks
    private Callback<LobbyChatMsg_t> _lobbyChatMsg;

    private CSteamID _currentLobbyID;
    public VoiceMode CurrentVoiceMode { get; private set; } = VoiceMode.None;

    public Task VoiceTransmissionTask;
    public CancellationTokenSource TransmissionTaskCancellation;

    public List<IVoiceNetworkTransport> VoiceChatHandlerList = new();

    private readonly VoiceHandlerPool _voicePool;

    public ChatRoomHandler(GameObject voicePrefab)
    {
        TransmissionTaskCancellation = new CancellationTokenSource();

        _lobbyChatMsg = Callback<LobbyChatMsg_t>.Create(OnReceivedChatMessage);
        
        EVENT.Subscribe<LobbyHandler.OnLeftLobby>(HandleOnLeftLobby);
        EVENT.Subscribe<LobbyHandler.OnJoinLobby>(HandleOnJoinLobby);
        EVENT.Subscribe<LobbyHandler.OnPlayerLeftLobby>(HandleOnPlayerLeftLobby);
        EVENT.Subscribe<LobbyHandler.OnPlayerJoinLobby>(HandleOnPlayerJoinLobby);

        Application.wantsToQuit -= HandleApplicationQuit;
        Application.wantsToQuit += HandleApplicationQuit;
        
        SetVoiceMode(VoiceMode.VoiceActivity);
    }

    private void HandleOnPlayerJoinLobby(LobbyHandler.OnPlayerJoinLobby obj)
    {
        
    }

    private void HandleOnPlayerLeftLobby(LobbyHandler.OnPlayerLeftLobby obj)
    {
        
    }

    private bool HandleApplicationQuit()
    {
        Shutdown();
        return true;
    }

    private void HandleOnJoinLobby(LobbyHandler.OnJoinLobby obj)
    {
        // Convert LobbyId (string) to ulong, then to CSteamID
        if (ulong.TryParse(obj.CurrentLobby.LobbyId, out var ulongLobbyId))
        {
            var convertedLobbyId = new CSteamID(ulongLobbyId);
            _currentLobbyID = convertedLobbyId;
        }
        else
        {
            Debug.LogError($"[ChatRoomHandler] Failed to parse LobbyId: {obj.CurrentLobby.LobbyId}");
        }
    }

    private void HandleOnLeftLobby(LobbyHandler.OnLeftLobby obj)
    {
        foreach (var voiceNetworkTransport in VoiceChatHandlerList)
        {
            voiceNetworkTransport.Shutdown();
        }
    }

    // Sets the desired voice recording mode
    public void SetVoiceMode(VoiceMode mode)
    {
        if (CurrentVoiceMode == mode) return;

        CurrentVoiceMode = mode;

        // If switching to VAD, start recording and let Steam handle activation
        if (CurrentVoiceMode == VoiceMode.VoiceActivity)
        {
            //SteamUser.StartVoiceRecording();
            Debug.Log("[System] Voice Activity Detection enabled.");
        }
        // If switching to PTT, stop the continuous recording
        else
        {
            //SteamUser.StopVoiceRecording();
            Debug.Log("[System] Push-to-Talk enabled.");
        }
    }

    public void StartPushToTalk()
    {
        if (CurrentVoiceMode == VoiceMode.PushToTalk)
        {
            SteamUser.StartVoiceRecording();
        }
    }

    public void StopPushToTalk()
    {
        if (CurrentVoiceMode == VoiceMode.PushToTalk)
        {
            SteamUser.StopVoiceRecording();
        }
    }

    public void SendChatMessage(string text)
    {
        if (string.IsNullOrEmpty(text) || _currentLobbyID == CSteamID.Nil)
        {
            return;
        }

        byte[] message = System.Text.Encoding.UTF8.GetBytes(text);
        SteamMatchmaking.SendLobbyChatMsg(_currentLobbyID, message, message.Length);
    }

    private void OnReceivedChatMessage(LobbyChatMsg_t callback)
    {
        byte[] buffer = new byte[4096];
        int dataSize = SteamMatchmaking.GetLobbyChatEntry(_currentLobbyID, (int)callback.m_iChatID, out var steamIDUser, buffer, buffer.Length, out var chatEntryType);

        if (chatEntryType != EChatEntryType.k_EChatEntryTypeChatMsg)
        {
            return;
        }

        string message = System.Text.Encoding.UTF8.GetString(buffer, 0, dataSize);
        string senderName = SteamFriends.GetFriendPersonaName(steamIDUser);
        Debug.Log(senderName + ": " + message);

        var textData = new TextChatData()
        {
            senderName = senderName,
            message = message
        };
        EVENT.Publish(new TextChatDataReceived(textData));
    }

    public void Shutdown()
    {
        _lobbyChatMsg = null;
        EVENT.Unsubscribe<LobbyHandler.OnLeftLobby>(HandleOnLeftLobby);
        EVENT.Unsubscribe<LobbyHandler.OnJoinLobby>(HandleOnJoinLobby);
        EVENT.Unsubscribe<LobbyHandler.OnPlayerLeftLobby>(HandleOnPlayerLeftLobby);
        EVENT.Unsubscribe<LobbyHandler.OnPlayerJoinLobby>(HandleOnPlayerJoinLobby);
        TransmissionTaskCancellation.Cancel();
    }
}

public static class ChatRoomHandlerUtil
{
    [Command]
    public static void SendChatMessage(string message)
    {
        LobbyHandler.chatRoomHandler.SendChatMessage(message);
    }

    [Command]
    public static void SetVoiceMode(int state)
    {
        if (state == 0)
        {
            LobbyHandler.chatRoomHandler.SetVoiceMode(ChatRoomHandler.VoiceMode.PushToTalk);
        }
        else if (state == 1)
        {
            LobbyHandler.chatRoomHandler.SetVoiceMode(ChatRoomHandler.VoiceMode.VoiceActivity);
        }
    }

    [Command]
    public static void StartPushToTalk()
    {
        LobbyHandler.chatRoomHandler.StartPushToTalk();
    }

    [Command]
    public static void StopPushToTalk()
    {
        LobbyHandler.chatRoomHandler.StopPushToTalk();
    }
}