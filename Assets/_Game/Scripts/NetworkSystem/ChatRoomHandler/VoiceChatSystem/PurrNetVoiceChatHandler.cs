using System;
using PurrNet;
using PurrNet.Transports;
using Sirenix.OdinInspector;
using UnityEngine;

public interface IVoiceNetworkTransport
{
    void Init();
    void SendVoiceData(ArraySegment<byte> data, int sequence);
    void MuteInput(bool state);
    void SetOutputVolume(float volume);
    void MuteOutput(bool state);
    void Shutdown();
}

[RequireComponent(typeof(VoiceChatRecorder))]
[RequireComponent(typeof(VoiceChatPlayer))]
public class PurrNetVoiceChatHandler : NetworkIdentity, IVoiceNetworkTransport
{
    [Title("Central Voice Configuration", "Settings apply to Recorder & Player", TitleAlignments.Centered)]
    [InlineEditor(Expanded = false)]
    [Required]
    [AssetsOnly]
    public VoiceConfig config;

    [Title("Runtime Debug")]
    [ShowInInspector, ReadOnly, ProgressBar(0, 1f)] 
    public float CurrentMicVolume => _voiceRecorder ? _voiceRecorder.CurrentVolume : 0f;

    private VoiceChatRecorder _voiceRecorder;
    private VoiceChatPlayer _voicePlayer;

    private bool _canHearSelf;

    public ulong ClientId { get; private set; }

    private void Awake()
    {
        _voicePlayer = GetComponent<VoiceChatPlayer>();
        _voiceRecorder = GetComponent<VoiceChatRecorder>();
    }

    protected override void OnSpawned()
    {
        ulong clientId = 0;

        if (isOwner && owner.HasValue)
        {
            clientId = owner.Value.id.value;
            GiveOwnership(owner.Value);
        }

        ClientId = clientId;
        Init();
    }

    protected override void OnDespawned()
    {
        Shutdown();
    }

    public void Init()
    {
        _voicePlayer.Initialize(config, ClientId);
        if (isOwner)
        {
            // Local Player: Initialize Recorder
            _voiceRecorder.Initialize(this, config);
            _voiceRecorder.enabled = true;

            _voicePlayer.StopPlayer();
        }
        else
        {
            // Remote Player: Disable Recorder
            _voiceRecorder.enabled = false;
            _voicePlayer.StartPlayer();
        }
    }

    public void MuteInput(bool state)
    {
        if (!isOwner)
            return;

        if (state)
        {
            _voiceRecorder.StopRecording();
        }
        else
        {
            _voiceRecorder.StartRecording();
        }
    }

    public void SetOutputVolume(float volume)
    {
        _voicePlayer.SetVolume(volume);
    }

    public void MuteOutput(bool state)
    {
        _voicePlayer.SetMute(state);
    }

    public void Shutdown()
    {
        _voicePlayer.StopPlayer();
        _voiceRecorder.enabled = false;
    }

    /// <summary>
    /// Send current player voice data to all observer
    /// </summary>
    /// <param name="compressedData"></param>
    public void SendVoiceData(ArraySegment<byte> compressedData, int sequenceId)
    {
        if (NetworkManager.main.clientToServerConn != null)
        {
            byte[] data = compressedData.ToArray();
            SendVoiceDataRpc(data, sequenceId);
        }
    }

    /// <summary>
    /// Received Broadcasted RPC
    /// </summary>
    /// <param name="data"></param>
    /// <param name="info"></param>
    [ObserversRpc(Channel.Unreliable, requireServer: false, runLocally: false)]
    private void SendVoiceDataRpc(byte[] data, int sequenceId, RPCInfo info = default)
    {
        if (_voicePlayer != null)
        {
            // Pass the ID to the player
            _voicePlayer.OnVoiceDataReceived(data, sequenceId);
        }
    }

    // Allow updating settings at runtime for testing
    [Button("Apply Config Runtime", ButtonSizes.Medium)]
    private void ReapplySettings()
    {
        if(_voiceRecorder) _voiceRecorder.Initialize(this, config);
        if (_voicePlayer)
        {
            uint ownerId = 0;
            if (!owner.HasValue)
            {
                Debug.LogError("Owner not found, setting the ID to '0'");
            }
            else
            {
                ownerId = (uint)owner.Value.id.value;
            }

            _voicePlayer.Initialize(config, ownerId);
        } 
    }

    [ContextMenu("ToggleHearSelf")]
    public void ToggleHearSelf()
    {
        _canHearSelf = !_canHearSelf;

        if (_canHearSelf)
        {
            _voicePlayer.StartPlayer();
        }
        else
        {
            _voicePlayer.StopPlayer();
        }

        Debug.Log($"CanHearSelf set to: {_canHearSelf}");
    }
    
}