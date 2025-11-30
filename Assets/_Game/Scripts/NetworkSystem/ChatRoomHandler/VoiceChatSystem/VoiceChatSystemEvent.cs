using NyxMachina.Shared.EventFramework.Core.Payloads;


public class VoiceChatEvent
{
    public struct OnPlayerTalk : IPayload
    {
        public ulong ClientId;
        public bool IsSpeaking;
        public float CurrentAmplitude;

        public OnPlayerTalk(ulong clientId, bool isSpeak, float currentAmplitude)
        {
            ClientId = clientId;
            IsSpeaking = isSpeak;
            CurrentAmplitude = currentAmplitude;
        }
    }
}
