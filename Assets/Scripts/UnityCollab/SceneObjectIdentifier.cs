using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SceneObjectIdentifier : MonoBehaviour
{
    [HideInInspector]
    public string UniqueID;

    // Generar ID si no existe (útil para prefabs)
    public void ValidateID()
    {
        if (string.IsNullOrEmpty(UniqueID))
        {
            UniqueID = System.Guid.NewGuid().ToString();
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}
