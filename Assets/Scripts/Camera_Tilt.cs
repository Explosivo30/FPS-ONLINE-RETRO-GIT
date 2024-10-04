using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class Camera_Tilt : MonoBehaviour
{
    public void DoTilt(float zTilt)
    {
        transform.DOLocalRotate(new Vector3(0, 0, zTilt), 0.25f);
    }

}
