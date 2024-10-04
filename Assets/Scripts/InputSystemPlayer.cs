using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputSystemPlayer : MonoBehaviour, Player.IPlayerControlsActions
{
    public event Action jumpEvent;

    public event Action slideEvent;

    public event Action shootEvent;

    
    public Vector2 movementValue { get; private set; }
    public Vector2 lookValue { get; private set; }

    private Player controls;

    private void Awake()
    {
        controls = new Player();

        controls.PlayerControls.SetCallbacks(this);

        controls.PlayerControls.Enable();    
    }

    private void OnDestroy()
    {
        controls.PlayerControls.Disable();
    }




    public void OnMovement(InputAction.CallbackContext context)
    {
        movementValue = context.ReadValue<Vector2>();
    }

    public void OnChangeWeapon1(InputAction.CallbackContext context)
    {
        throw new System.NotImplementedException();
    }

    public void OnChangeWeapon2(InputAction.CallbackContext context)
    {
        throw new System.NotImplementedException();
    }

   

    public void OnShoot(InputAction.CallbackContext context)
    {
        if (context.started) { shootEvent?.Invoke(); }
    }

    public void OnJump(InputAction.CallbackContext context)
    {
        if (!context.performed){ return; }
        jumpEvent?.Invoke();
    }

    public void OnSlide(InputAction.CallbackContext context)
    {
        //if (context.phase.IsInProgress()) { isSliding = true; }

        //if(context.ReadValue<bool>()) { slideEvent?.Invoke(); }
        if (context.performed || context.canceled) { slideEvent?.Invoke();  }
    }

    public void OnLook(InputAction.CallbackContext context)
    {
        lookValue = context.ReadValue<Vector2>();
    }

}
