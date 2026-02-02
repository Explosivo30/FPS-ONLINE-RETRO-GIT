using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class SceneObjectIdentifier : MonoBehaviour
{
    public string UniqueID;

    // Guardamos "quién era yo" para detectar si me han clonado
    [SerializeField, HideInInspector] private int originalInstanceID = 0;

    [HideInInspector]
    public bool hasUnsyncedChanges = false;

    private void Awake()
    {
        ValidateID();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Esto ocurre justo después de duplicar o pegar
        ValidateID();
    }
#endif

    public void ValidateID()
    {
        int currentInstanceID = GetInstanceID();

        // CASO 1: No tengo ID (soy nuevo) -> Generar
        if (string.IsNullOrEmpty(UniqueID))
        {
            GenerateID();
        }
        // CASO 2: Tengo ID, pero mi InstanceID ha cambiado (SOY UN CLON) -> Regenerar
        else if (originalInstanceID != 0 && originalInstanceID != currentInstanceID)
        {
            string oldID = UniqueID;
            GenerateID();
            Debug.Log($"[Collab] Detectado duplicado. ID cambiado de {oldID} a {UniqueID}");

            // Importante: Marcar como "nuevo" para que el AutoTagger lo suba
            // Esperamos un frame para que el Tagger lo pille
#if UNITY_EDITOR
            EditorApplication.delayCall += () => {
                if (SceneSyncManager.Instance != null) SceneSyncManager.Instance.UploadNewObject(this.gameObject);
            };
#endif
        }
    }

    public void GenerateID()
    {
        UniqueID = System.Guid.NewGuid().ToString();
        originalInstanceID = GetInstanceID();
        hasUnsyncedChanges = true; // Al nacer, tengo cambios pendientes
#if UNITY_EDITOR
        if (!Application.isPlaying) EditorUtility.SetDirty(this);
#endif
    }
}