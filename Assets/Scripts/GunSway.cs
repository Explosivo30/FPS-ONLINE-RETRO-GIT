using System;
using UnityEngine;

public class WeaponSway : MonoBehaviour
{

    [Header("Sway Settings")]
    [SerializeField] private float smooth;
    [SerializeField] private float multiplier;
    [SerializeField] float maxClamp = 50f;
    [SerializeField] float minClamp = -50f;
    



    InputSystemPlayer player;
    private void OnEnable()
    {
        player = GetComponentInParent<InputSystemPlayer>();
    }

    private void Update()
    {
        // get mouse input
        float mouseX = player.lookValue.x * multiplier;
        float mouseY = player.lookValue.y * multiplier;

        mouseX = Mathf.Clamp(mouseX, minClamp, maxClamp);
        mouseY = Mathf.Clamp(mouseY, minClamp, maxClamp);

        // calculate target rotation
        Quaternion rotationX = Quaternion.AngleAxis(-mouseY, Vector3.right);
        Quaternion rotationY = Quaternion.AngleAxis(mouseX, Vector3.up);
        
        Quaternion targetRotation = rotationX * rotationY;

        

        // rotate 
        transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRotation, smooth * Time.deltaTime);
    }
}