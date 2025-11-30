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
        public LobbyUser UserData;
        public OnPlayerJoinLobby(LobbyUser user) => UserData = user;
    }

    public struct OnPlayerLeftLobby : IPayload
    {
        public LobbyUser UserData;
        public OnPlayerLeftLobby(LobbyUser user) => UserData = user;
    }
}