using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class EnemyManager : NetworkBehaviour
{
    public static EnemyManager Instance;

    public GameObject enemyPrefab; // Assign the enemy prefab in the inspector
    public Transform[] spawnPoints; // Assign the spawn points in the inspector
    public int initialEnemyCount = 5; // Number of enemies to spawn initially

    private void Awake()
    {
        // Ensure only one instance of the EnemyManager exists
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            // Spawn initial enemies
            SpawnInitialEnemies();
        }
    }

    
    private void SpawnInitialEnemies()
    {
        for (int i = 0; i < initialEnemyCount; i++)
        {
            Transform spawnPoint = spawnPoints[i % spawnPoints.Length];
            GameObject enemy = Instantiate(enemyPrefab, spawnPoint.position, spawnPoint.rotation);
            enemy.GetComponent<NetworkObject>().Spawn();
        }
    }

    [ServerRpc]
    public void SpawnEnemiesServerRpc()
    {
        foreach (Transform spawnPoint in spawnPoints)
        {
            GameObject enemy = Instantiate(enemyPrefab, spawnPoint.position, spawnPoint.rotation);
            enemy.GetComponent<NetworkObject>().Spawn();
        }
    }
}
