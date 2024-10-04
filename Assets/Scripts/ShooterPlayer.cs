using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Cinemachine;

public class ShooterPlayer : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] CinemachineVirtualCamera virtualCamera;

    [Header("Settings")]
    [SerializeField] int ownerPriority = 15;
    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            virtualCamera.Priority = ownerPriority;
        }
    }

   
}
