using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class SpaceShipController : NetworkBehaviour
{
    public float moveSpeed = 10.0f; // Forward/backward speed
    public float rotationSpeed = 100.0f; // Turning speed
    public float drag = 0.5f; // Drag to simulate space-like movement

    private CharacterController _controller;
    private Vector3 _velocity = Vector3.zero;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            NotifyServerPlayerConnectedServerRpc();
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }
        
        if(IsHost)
        {
            GameManagerSpace.Instance.AddPlayer(this);
            Debug.Log("Added Host");
        }
        
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            NotifyServerPlayerDisconnectedServerRpc();
        }

        if (IsHost)
        {
            GameManagerSpace.Instance.RemovePlayer(this);
        }
    }

    [ServerRpc]
    private void NotifyServerPlayerConnectedServerRpc(ServerRpcParams rpcParams = default)
    {
        GameManagerSpace.Instance.AddPlayer(this);
        
    }

    [ServerRpc]
    private void NotifyServerPlayerDisconnectedServerRpc(ServerRpcParams rpcParams = default)
    {
        GameManagerSpace.Instance.RemovePlayer(this);
    }

    private void Update()
    {
        if (!IsOwner) return;

        HandleMovement();
        ApplyDrag();

        // Move the ship using the CharacterController
        _controller.Move(_velocity * Time.deltaTime);
    }

    private void HandleMovement()
    {
        // Forward/backward movement
        float moveInput = Input.GetAxis("Vertical"); // W/S or Up/Down arrows
        Vector3 moveDirection = transform.forward * moveInput * moveSpeed;

        // Add movement input to velocity
        _velocity += moveDirection * Time.deltaTime;

        // Rotation (turning)
        float turnInput = Input.GetAxis("Horizontal"); // A/D or Left/Right arrows
        transform.Rotate(0, turnInput * rotationSpeed * Time.deltaTime, 0);
    }

    private void ApplyDrag()
    {
        // Apply drag to slow down the velocity over time
        _velocity *= Mathf.Clamp01(1 - drag * Time.deltaTime);

        // Optional: Ensure the velocity doesn't become too small
        if (_velocity.magnitude < 0.01f)
        {
            _velocity = Vector3.zero;
        }
    }
}
