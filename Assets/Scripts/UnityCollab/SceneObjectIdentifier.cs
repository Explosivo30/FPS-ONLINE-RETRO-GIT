using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways] // Importante: Funciona mientras editas, no solo al dar Play
public class SceneObjectIdentifier : MonoBehaviour
{
    [Tooltip("El DNI único de este objeto para la red.")]
    public string UniqueID;

    private void Awake()
    {
        // Al despertar, intentamos asegurar que tenga ID
        GenerateID();
    }

    public void ValidateID()
    {
        if (string.IsNullOrEmpty(UniqueID))
        {
            GenerateID();
        }
    }

    // --- ESTA ES LA FUNCIÓN QUE FALTABA ---
    // La hacemos 'public' para que el AutoObjectTagger pueda llamarla
    public void GenerateID()
    {
        // IMPORTANTE: Solo generamos un ID nuevo si está vacío.
        // Si ya tiene uno, NO lo tocamos (para no romper la conexión con el otro PC).
        if (string.IsNullOrEmpty(UniqueID))
        {
            // Creamos un identificador único global (GUID)
            UniqueID = System.Guid.NewGuid().ToString();

#if UNITY_EDITOR
            // Si estamos en el editor, avisamos a Unity de que hemos modificado este objeto
            // para que te pida guardar la escena (el asterisco en el nombre de la escena).
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(this);
            }
#endif
        }
    }

    // Un botón extra para el menú del inspector (click derecho en el componente)
    // Úsalo solo si necesitas forzar un cambio de ID manual
    [ContextMenu("Forzar Nuevo ID")]
    public void ForceRegenerate()
    {
        UniqueID = System.Guid.NewGuid().ToString();
        Debug.Log($"Nuevo ID generado manualmente: {UniqueID}");
    }
}