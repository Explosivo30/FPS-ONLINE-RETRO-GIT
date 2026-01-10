#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public class AutoObjectTagger
{
    // El constructor estático se llama en cuanto Unity carga
    static AutoObjectTagger()
    {
        // Nos suscribimos al evento de creación de objetos
        ObjectFactory.componentWasAdded += OnComponentAdded;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
    }

    // Método A: Detecta cuando añades componentes (como al crear un Cubo 3D)
    private static void OnComponentAdded(Component component)
    {
        // Si el objeto tiene un MeshFilter (es decir, es un objeto 3D visible)
        if (component is MeshFilter)
        {
            EnsureIdentifier(component.gameObject);
        }
    }

    // Método B: Detecta cambios en la jerarquía (por si arrastras prefabs)
    private static void OnHierarchyChanged()
    {
        // Solo revisamos lo que tengas seleccionado para no saturar
        foreach (GameObject go in Selection.gameObjects)
        {
            // Filtros: Que no sea el Manager, ni la Cámara, ni Luces
            if (ShouldTag(go))
            {
                EnsureIdentifier(go);
            }
        }
    }

    // La función que pone la etiqueta
    private static void EnsureIdentifier(GameObject go)
    {
        // Si ya lo tiene, no hacemos nada
        if (go.GetComponent<SceneObjectIdentifier>() != null) return;

        // ¡PUM! Le ponemos el script
        go.AddComponent<SceneObjectIdentifier>();

        // (Opcional) Le asignamos un ID inmediatamente si el script no lo hizo al Awake
        var idScript = go.GetComponent<SceneObjectIdentifier>();
        if (string.IsNullOrEmpty(idScript.UniqueID))
        {
            idScript.GenerateID();
        }

        Debug.Log($"[AutoTagger] Se ha etiquetado automáticamente a: {go.name}");
    }

    // Filtro de seguridad: ¿A qué cosas NO queremos ponerle script?
    private static bool ShouldTag(GameObject go)
    {
        // 1. Ignorar objetos de sistema (Managers)
        if (go.name.StartsWith("_")) return false;

        // 2. Ignorar Cámaras y Luces
        if (go.GetComponent<Camera>() != null) return false;
        if (go.GetComponent<Light>() != null) return false;

        // 3. SOLO queremos cosas que se vean (con MeshRenderer)
        if (go.GetComponent<MeshRenderer>() == null && go.GetComponent<SkinnedMeshRenderer>() == null) return false;

        return true;
    }

    // --- HERRAMIENTA MANUAL (Menú superior) ---
    // Si tienes objetos viejos, dale a este botón en el menú para arreglarlos todos de golpe
    [MenuItem("Collab Tool/Etiquetar Todos los Objetos de la Escena")]
    public static void TagEverything()
    {
        // Busca todos los objetos que tengan un MeshRenderer (cosas 3D)
        MeshRenderer[] allObjects = GameObject.FindObjectsOfType<MeshRenderer>();
        int count = 0;

        foreach (var rend in allObjects)
        {
            if (ShouldTag(rend.gameObject) && rend.gameObject.GetComponent<SceneObjectIdentifier>() == null)
            {
                rend.gameObject.AddComponent<SceneObjectIdentifier>();
                count++;
            }
        }
        Debug.Log($"<color=green>¡Hecho! Se han etiquetado {count} objetos antiguos.</color>");
    }
}
#endif