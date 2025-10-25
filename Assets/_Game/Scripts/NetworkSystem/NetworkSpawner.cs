using System;
using System.Collections;
using System.Collections.Generic;
using PurrNet;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkSpawner : NetworkBehaviour
{
    private GameObject currentPlayerCharacter;
    [SerializeField] private GameObject carPrefab;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene arg0, LoadSceneMode arg1)
    {
        if (arg0.name != "Demo")
        {
            return;
        }

        if (currentPlayerCharacter != null)
        {
            return;
        }

        if (isServer)
        {
            SpawnPlayerCharacter();
        }
        else
        {
            SpawnPlayerCharacterRpc();
        }
    }

    [ServerRpc]
    private void SpawnPlayerCharacter()
    {
        var spawnedPrefab = Instantiate(carPrefab);
        if (spawnedPrefab.TryGetComponent<NetworkIdentity>(out var networkIdentity))
        {
            networkIdentity.GiveOwnership(localPlayer);
            currentPlayerCharacter = spawnedPrefab;
        }
    }

    [Client]
    private void SpawnPlayerCharacterRpc()
    {
        SpawnPlayerCharacter();
    }
}
