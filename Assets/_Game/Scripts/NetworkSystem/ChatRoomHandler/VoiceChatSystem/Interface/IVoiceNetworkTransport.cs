using System;

public interface IVoiceNetworkTransport
{
    void Init();
    void SendVoiceData(ArraySegment<byte> data, int sequence);
    void MuteInput(bool state);
    void SetOutputVolume(float volume);
    void MuteOutput(bool state);
    void Shutdown();
}