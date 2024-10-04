using Cinemachine;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Knife.RealBlood.SimpleController
{
    /// <summary>
    /// Player Hands behaviour
    /// </summary>
    public class Hands : NetworkBehaviour
    {
        public Weapon[] Weapons;
        public Camera Cam;
        public CinemachineVirtualCamera virtualCamera;
        public KeyCode[] Keys;

        // Use a NetworkVariable to track the index of the active weapon
        private NetworkVariable<int> activeWeaponIndex = new NetworkVariable<int>(0);
        private float startFov;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                activeWeaponIndex.Value = 0; // Set the first weapon active by default
            }

            // Register for changes to the active weapon index
            activeWeaponIndex.OnValueChanged += OnActiveWeaponIndexChanged;
            if (IsOwner)
            {
                virtualCamera.m_Lens.FieldOfView = Weapons[activeWeaponIndex.Value].CurrentFov;
            }
            startFov = 75f;

            // Ensure initial weapon state is synchronized

            StartCoroutine(ExecuteAfterTime());
        }

        public override void OnNetworkDespawn()
        {
            activeWeaponIndex.OnValueChanged -= OnActiveWeaponIndexChanged;
        }

        private void Start()
        {
            if (!IsOwner) return;
            
            // Initialize all weapons to be inactive except the first one
            for (int i = 0; i < Weapons.Length; i++)
            {
                Weapons[i].gameObject.SetActive(i == activeWeaponIndex.Value);
            }

        }

        private void Update()
        {
            if (!IsOwner) return;

            // Check for weapon switch input
            for (int i = 0; i < Keys.Length; i++)
            {
                if (Input.GetKeyDown(Keys[i]))
                {
                    // Request the server to toggle weapon state
                    ToggleWeaponServerRpc(i);
                    break;
                }
            }

            // Update the camera's field of view based on the active weapon
            foreach (Weapon weapon in Weapons)
            {
                if (weapon.gameObject.activeSelf)
                {
                    virtualCamera.m_Lens.FieldOfView = weapon.CurrentFov;
                    return;
                }
            }
            virtualCamera.m_Lens.FieldOfView = startFov;
        }

        [ServerRpc]
        private void ToggleWeaponServerRpc(int index)
        {
            // Update the active weapon index on the server
            activeWeaponIndex.Value = index;

            // Notify all clients to update their weapon states
            UpdateWeaponStateClientRpc(index);
        }

        [ClientRpc]
        private void UpdateWeaponStateClientRpc(int index)
        {
            // Update the weapon state on all clients
            UpdateWeaponState(index);
        }

        private void OnActiveWeaponIndexChanged(int oldIndex, int newIndex)
        {
            // Update weapon state when the active weapon index changes
            UpdateWeaponState(newIndex);
        }

        private void UpdateWeaponState(int index)
        {
            // Activate the selected weapon and deactivate all others
            for (int i = 0; i < Weapons.Length; i++)
            {
                Weapons[i].gameObject.SetActive(i == index);
            }
            if (IsOwner)
            {
                virtualCamera.m_Lens.FieldOfView = Weapons[index].CurrentFov;
            }
        }

        IEnumerator ExecuteAfterTime()
        {
            yield return new WaitForSeconds(0.5f);

            UpdateWeaponState(activeWeaponIndex.Value);
            
            yield break;
        }

    }
}