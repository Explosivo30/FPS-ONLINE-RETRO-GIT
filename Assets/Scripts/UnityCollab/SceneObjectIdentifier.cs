using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class SceneObjectIdentifier : MonoBehaviour
{
    [Tooltip("El DNI único de este objeto para la red.")]
    public string UniqueID;

    // --- NUEVO: ¿He cambiado mientras estaba offline? ---
    [HideInInspector]
    public bool hasUnsyncedChanges = false;

    private void Awake()
    {
        GenerateID();
    }

    public void ValidateID()
    {
        if (string.IsNullOrEmpty(UniqueID)) GenerateID();
    }

    public void GenerateID()
    {
        if (string.IsNullOrEmpty(UniqueID))
        {
            UniqueID = System.Guid.NewGuid().ToString();
#if UNITY_EDITOR
            if (!Application.isPlaying) EditorUtility.SetDirty(this);
#endif
        }
    }
}