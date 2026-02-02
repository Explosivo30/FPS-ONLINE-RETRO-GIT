using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class SceneObjectIdentifier : MonoBehaviour
{
    public string UniqueID;

    [HideInInspector]
    public bool hasUnsyncedChanges = false;

    // Bool para que el script sepa que la ID viene de Firebase (Red) y no debe cambiarla
    private bool _lockedByNetwork = false;

    private void Awake()
    {
        ValidateID();
    }

#if UNITY_EDITOR
    // Este evento salta cuando cambias algo en el inspector O cuando DUPLICAS el objeto
    private void OnValidate()
    {
        // Usamos delayCall para esperar a que Unity termine de hacer la copia
        EditorApplication.delayCall += ValidateID;
    }
#endif


    public void SetNetworkID(string id)
    {
        _lockedByNetwork = true; // Bloqueamos validaciones automáticas
        UniqueID = id;
        hasUnsyncedChanges = false; // Esto no cuenta como un cambio local

        // Esperamos un poco para desbloquear, por si Unity hace cosas raras al instanciar
#if UNITY_EDITOR
        EditorApplication.delayCall += () => { _lockedByNetwork = false; };
#endif
    }

    public void ValidateID()
    {
        // Seguridad: si el objeto se está borrando, no hacemos nada
        if (this == null) return;

        // 1. Evitar cambiar IDs a los archivos Prefab del proyecto (solo instancias en escena)
        if (IsPrefabAsset()) return;

        bool generateNew = false;

        // 2. ¿No tengo DNI? -> Generar
        if (string.IsNullOrEmpty(UniqueID))
        {
            generateNew = true;
        }
        // 3. ¿Tengo DNI, pero ya lo tiene otro objeto en la escena? -> Generar (Soy un clon)
        else if (IsDuplicate(UniqueID))
        {
            generateNew = true;
        }

        if (generateNew)
        {
            string oldID = UniqueID;
            GenerateID();

            // Solo avisamos si ha cambiado (no si era nuevo)
            if (!string.IsNullOrEmpty(oldID))
                Debug.Log($"[Collab] Clon detectado. ID cambiado: {oldID} -> {UniqueID}");

            // MAGIA: Avisamos al Manager para que suba este nuevo objeto YA
#if UNITY_EDITOR
            if (SceneSyncManager.Instance != null)
                SceneSyncManager.Instance.UploadNewObject(this.gameObject);
#endif
        }
    }

    private bool IsDuplicate(string idToCheck)
    {
        // Buscamos TODOS los identificadores en la escena
        // (Es fuerza bruta, pero seguro al 100% contra duplicados)
        var allIdentifiers = FindObjectsOfType<SceneObjectIdentifier>();

        foreach (var other in allIdentifiers)
        {
            // Si encuentro otro objeto QUE NO SOY YO y tiene MI MISMO ID...
            if (other != this && other.UniqueID == idToCheck)
            {
                return true; // ¡Hay un duplicado!
            }
        }
        return false;
    }

    public void GenerateID()
    {
        UniqueID = System.Guid.NewGuid().ToString();
        hasUnsyncedChanges = true;
#if UNITY_EDITOR
        if (!Application.isPlaying) EditorUtility.SetDirty(this);
#endif
    }

    private bool IsPrefabAsset()
    {
#if UNITY_EDITOR
        // Si la escena es nula o no es válida, es un archivo en la carpeta Project, no en la escena
        return this.gameObject.scene.rootCount == 0 || this.gameObject.scene.name == null;
#else
        return false;
#endif
    }
}