using System;
using UnityEngine;
using Sirenix.OdinInspector;
using Concentus.Enums;

[CreateAssetMenu(fileName = "GlobalVoiceConfig", menuName = "Voice Chat/Global Config")]
public class VoiceConfig : SerializedScriptableObject
{
    [Title("Quality Settings")]
    [EnumToggleButtons]
    public OpusApplication ApplicationType = OpusApplication.OPUS_APPLICATION_VOIP;

    [SuffixLabel("Hz", Overlay = true)]
    public int SampleRate = 48000;

    [Range(1, 2)]
    public int Channels = 1;

    [BoxGroup("Compression")]
    [PropertyRange(6000, 96000)]
    [SuffixLabel("bps", Overlay = true)]
    public int Bitrate = 32000;

    [BoxGroup("Compression")]
    [EnumToggleButtons]
    public FrameDuration Duration = FrameDuration.Time20ms;

    [BoxGroup("Compression")]
    [ToggleLeft]
    public bool UseInbandFEC = true;

    [BoxGroup("VAD")]
    [Range(0f, 0.1f)]
    public float SilenceThreshold = 0.01f;

    public int FrameSize;
    public int FrameSizeOverride { get; set; }

    private void OnEnable()
    {
        FrameSize = GetFrameSize;
    }

    public int GetFrameSize
    {
        get
        {
            if (FrameSizeOverride > 0)
            {
                return FrameSizeOverride;
            }

            return SampleRate * (int)Duration / 1000;
        }
    }

    public enum FrameDuration { Time10ms = 10, Time20ms = 20, Time40ms = 40, Time60ms = 60 }
}