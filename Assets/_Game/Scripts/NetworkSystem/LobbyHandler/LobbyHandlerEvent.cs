using NyxMachina.Shared.EventFramework.Core.Payloads;
using PurrLobby;

namespace NyxMachina.Multiplayer
{
    public struct OnJoinLobby : IPayload
    {
        public Lobby CurrentLobby { get; private set; }
        public OnJoinLobby(Lobby targetLobby) => CurrentLobby = targetLobby;
    }

    public struct OnLeftLobby : IPayload
    {
        public Lobby LastJoinedLobby { get; private set; }
        public OnLeftLobby(Lobby lastJoinedLobby) => LastJoinedLobby = lastJoinedLobby;
    }

    public struct OnPlayerJoinLobby : IPayload
    {
        public ILobbyDataModel OnDataModel;
        public OnPlayerJoinLobby(ILobbyDataModel player) => OnDataModel = player;
    }

    public struct OnPlayerLeftLobby : IPayload
    {
        public ILobbyDataModel OnDataModel;
        public OnPlayerLeftLobby(ILobbyDataModel player) => OnDataModel = player;
    }
}