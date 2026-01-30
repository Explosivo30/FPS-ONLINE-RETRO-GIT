#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public class AutoObjectTagger
{
    static AutoObjectTagger()
    {
        ObjectFactory.componentWasAdded += OnComponentAdded;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
    }

    private static void OnComponentAdded(Component component)
    {
        // Detectar si añades malla O físicas
        if (component is MeshFilter || component is CharacterController || component is Rigidbody)
        {
            EnsureIdentifier(component.gameObject);
        }
    }

    private static void OnHierarchyChanged()
    {
        foreach (GameObject go in Selection.gameObjects)
        {
            if (ShouldTag(go)) EnsureIdentifier(go);
        }
    }

    private static void EnsureIdentifier(GameObject go)
    {
        if (go.GetComponent<SceneObjectIdentifier>() != null) return;

        go.AddComponent<SceneObjectIdentifier>();
        var idScript = go.GetComponent<SceneObjectIdentifier>();

        // Generamos ID si no lo tiene
        if (idScript != null) idScript.GenerateID();

        Debug.Log($"[AutoTagger] Etiquetado: {go.name}");
    }

    // --- FILTRO MEJORADO ---
    private static bool ShouldTag(GameObject go)
    {
        if (go.name.StartsWith("_")) return false;
        if (go.GetComponent<Camera>() != null) return false;
        if (go.GetComponent<Light>() != null) return false;

        // 1. AHORA SÍ detecta al Player (Controller o Rigidbody)
        if (go.GetComponent<CharacterController>() != null) return true;
        if (go.GetComponent<Rigidbody>() != null) return true;

        // 2. También detecta objetos visibles normales
        if (go.GetComponent<MeshRenderer>() != null) return true;

        return false;
    }

    [MenuItem("Collab Tool/Etiquetar Todos los Objetos de la Escena")]
    public static void TagEverything()
    {
        // Buscamos Mallas Y Físicas
        var meshObjects = GameObject.FindObjectsOfType<MeshRenderer>();
        var physicsObjects = GameObject.FindObjectsOfType<CharacterController>();
        var rigidObjects = GameObject.FindObjectsOfType<Rigidbody>();

        int count = 0;

        void ProcessList<T>(T[] list) where T : Component
        {
            foreach (var item in list)
            {
                if (ShouldTag(item.gameObject) && item.gameObject.GetComponent<SceneObjectIdentifier>() == null)
                {
                    item.gameObject.AddComponent<SceneObjectIdentifier>().GenerateID();
                    count++;
                }
            }
        }

        ProcessList(meshObjects);
        ProcessList(physicsObjects);
        ProcessList(rigidObjects);

        Debug.Log($"<color=green>¡Hecho! Se han etiquetado {count} objetos (incluyendo Players).</color>");
    }
}
#endif