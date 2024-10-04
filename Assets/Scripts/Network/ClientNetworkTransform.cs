using System.Collections;
using System.Collections.Generic;
using Unity.Netcode.Components;
using UnityEngine;

public class ClientNetworkTransform : NetworkTransform
{
    public GameObject cameraHolder;
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        CanCommitToTransform = IsOwner;
    }

    //OLD UPDATE WITH NT TICK DOUBLE ISSUES
    /*
    protected override void Update()
    {
        
        CanCommitToTransform = IsOwner;
        base.Update();
        if(NetworkManager != null)
        {
            if(NetworkManager.IsConnectedClient || NetworkManager.IsListening)
            {
                if(CanCommitToTransform)
                {
                    TryCommitTransformToServer(transform,NetworkManager.LocalTime.Time);
                }
            }
        }
    }
    */
    override protected void Update()
    {
        CanCommitToTransform = IsOwner;
        base.Update();
        if (!IsHost && NetworkManager != null && NetworkManager.IsConnectedClient && CanCommitToTransform)
        {
            TryCommitTransformToServer(transform, NetworkManager.LocalTime.Time);
        }
    }
    protected override bool OnIsServerAuthoritative()
    {
        return false;
    }
}
