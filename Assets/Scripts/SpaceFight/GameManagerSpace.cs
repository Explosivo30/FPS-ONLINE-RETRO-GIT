using System;
using UnityEngine;
using Unity.Netcode;

public class GameManagerSpace : NetworkBehaviour
{
    public static GameManagerSpace Instance;

    public NetworkList<ulong> playersList;

    private void Awake()
    {
        if (Instance != null) return;

        Instance = this;
        playersList = new NetworkList<ulong>();

    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            playersList.Clear(); // Only the server manages the list
        }

        // Subscribe to the OnListChanged event
        playersList.OnListChanged += OnPlayersListChanged;
    }

    private void Update()
    {
        print(playersList.Count);
        Debug.Log(playersList.Count);
    }

    private void OnDestroy()
    {
        // Unsubscribe to prevent memory leaks
        if (playersList != null)
        {
            playersList.OnListChanged -= OnPlayersListChanged;
        }
        
    }

    private void OnPlayersListChanged(NetworkListEvent<ulong> changeEvent)
    {
        
    }

    public void AddPlayer(SpaceShipController player)
    {
        if (!IsServer) return; // Only the server can modify the list
        if (playersList.Contains(player.NetworkObjectId)) return;

        playersList.Add(player.NetworkObjectId);
        
    }

    public void RemovePlayer(SpaceShipController player)
    {
        if (!IsServer) return;
        if (!playersList.Contains(player.NetworkObjectId)) return;

        playersList.Remove(player.NetworkObjectId);
       
    }
}
