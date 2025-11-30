using PurrNet;
using PurrNet.Packing;
using PurrNet.Prediction;
using PurrNet.Transports;
using Sirenix.OdinInspector;
using UnityEngine;

public interface IVoiceNetworkTransport
{
    void SendVoiceData(byte[] compressedData);
}

[RequireComponent(typeof(VoiceChatRecorder))]
[RequireComponent(typeof(VoiceChatPlayer))]
public class PurrNetOpusBridge : NetworkIdentity, IVoiceNetworkTransport
{
    [Title("Central Voice Configuration", "Settings apply to Recorder & Player", TitleAlignments.Centered)]
    [InlineEditor(Expanded = false)]
    [Required]
    [AssetsOnly]
    public VoiceConfig config;

    [Title("Runtime Debug")]
    [ShowInInspector, ReadOnly, ProgressBar(0, 0.1f)] 
    public float CurrentMicVolume => _voiceRecorder ? _voiceRecorder.CurrentVolume : 0f;

    private VoiceChatRecorder _voiceRecorder;
    private VoiceChatPlayer _voicePlayer;

    private bool _canHearSelf;

    private void Awake()
    {
        _voicePlayer = GetComponent<VoiceChatPlayer>();
        _voiceRecorder = GetComponent<VoiceChatRecorder>();
    }

    protected override void OnSpawned()
    {
        _voicePlayer.Initialize(config);

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

    /// <summary>
    /// Send current player voice data to all observer
    /// </summary>
    /// <param name="compressedData"></param>
    public void SendVoiceData(byte[] compressedData)
    {
        if (NetworkManager.main.clientToServerConn != null)
        {
            SendOpusRpc(compressedData);
        }
    }

    /// <summary>
    /// Received Broadcasted RPC
    /// </summary>
    /// <param name="data"></param>
    /// <param name="info"></param>
    [ObserversRpc(Channel.Unreliable, requireServer: false, runLocally: false)]
    private void SendOpusRpc(byte[] data, RPCInfo info = default)
    {
        if (_voicePlayer != null)
        {
            _voicePlayer.OnVoiceDataReceived(data);
        }
    }

    // Allow updating settings at runtime for testing
    [Button("Apply Config Runtime", ButtonSizes.Medium)]
    private void ReapplySettings()
    {
        if(_voiceRecorder) _voiceRecorder.Initialize(this, config);
        if(_voicePlayer) _voicePlayer.Initialize(config);
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