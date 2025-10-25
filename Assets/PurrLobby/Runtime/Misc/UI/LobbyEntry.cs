using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PurrLobby
{
    public class LobbyEntry : MonoBehaviour
    {
        public Action<Lobby> OnJoinButtonClicked;

        [SerializeField] private TMP_Text lobbyNameText;
        [SerializeField] private TMP_Text playersText;
        [SerializeField] private Button joinButton;

        private Lobby _room;

        public void Awake()
        {
            joinButton.onClick.AddListener(HandleJoinButtonClicked);
        }

        public void Init(Lobby room)
        {
            lobbyNameText.text = room.Name.Length > 0 ? room.Name : room.LobbyId;
            playersText.text = $"{room.Members.Count}/{room.MaxPlayers}";
            _room = room;
        }

        private void HandleJoinButtonClicked()
        {
            OnJoinButtonClicked?.Invoke(_room);
        }
    }
}
