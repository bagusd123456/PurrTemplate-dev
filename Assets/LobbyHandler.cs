using System;
using System.Collections;
using System.Collections.Generic;
using PurrLobby;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LobbyHandler : MonoBehaviour
{
    public LobbyManager lobbyManager;

    private void OnEnable()
    {
        lobbyManager.OnRoomJoined.AddListener(HandleJoinLobby);
    }

    private void HandleJoinLobby(Lobby arg0)
    {
        SceneManager.LoadScene("Demo");
    }
}
