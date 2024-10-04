using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public class ZombieMove : NetworkBehaviour
{
    public Transform player; // Reference to the player's transform
    public NavMeshAgent agent; // Reference to the NavMeshAgent component
    public float followRange = 15f; // Range within which the enemy follows the player


    public override void OnNetworkSpawn()
    {
        // Get the NavMeshAgent component
        agent = GetComponent<NavMeshAgent>();

        // Optionally find the player by tag
        if (player == null)
        {
            player = GameObject.FindWithTag("Player").transform;
        }

    }

    private void Update()
    {
        if (!IsServer) return; // Only run the movement logic on the server

        // If the player is not null, set the agent's destination to the player's position
        if (player != null)
        {
            float distanceToPlayer = Vector3.Distance(transform.position, player.position);

            if (distanceToPlayer >= followRange)
            {
                agent.SetDestination(player.position);
                UpdateClientPositionServerRpc(agent.destination); // Update the clients with the new position
            }
            else
            {
                agent.ResetPath(); // Stop the enemy if the player is out of range
            }
        }
    }

    [ServerRpc]
    private void UpdateClientPositionServerRpc(Vector3 newPosition, ServerRpcParams rpcParams = default)
    {
        
        // Update the client's NavMeshAgent position
        UpdateClientPositionClientRpc(newPosition);
    }

    [ClientRpc]
    private void UpdateClientPositionClientRpc(Vector3 newPosition, ClientRpcParams rpcParams = default)
    {
        if (IsServer) return; // Do not update on the server
        agent.SetDestination(newPosition);
    }
}
