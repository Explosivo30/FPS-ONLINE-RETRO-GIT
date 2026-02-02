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
        // Detectar si añades malla O físicas (Cubos, Cápsulas, Players...)
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
        // 1. Si ya tiene ID, nos vamos (para no repetir envíos)
        if (go.GetComponent<SceneObjectIdentifier>() != null) return;

        // 2. Si no tiene, se lo ponemos
        var idScript = go.AddComponent<SceneObjectIdentifier>();
        idScript.GenerateID();

        Debug.Log($"[AutoTagger] Nuevo objeto etiquetado: {go.name}");

        // 3. MAGIA: Si el sistema de red está listo, subimos este objeto AL INSTANTE
        if (SceneSyncManager.Instance != null &&
            CollabNetworkManager.Instance != null &&
            CollabNetworkManager.Instance.IsConnected)
        {
            // Esperamos un frame para asegurar que Unity ha terminado de crear el objeto
            EditorApplication.delayCall += () =>
            {
                if (go != null) SceneSyncManager.Instance.UploadNewObject(go);
            };
        }
    }

    // FILTRO
    private static bool ShouldTag(GameObject go)
    {
        if (go.name.StartsWith("_")) return false; // Ignorar cosas internas
        if (go.GetComponent<Camera>() != null) return false;
        if (go.GetComponent<Light>() != null) return false;

        // Detectar Players, Cubos, Prefabs visuales...
        if (go.GetComponent<CharacterController>() != null) return true;
        if (go.GetComponent<Rigidbody>() != null) return true;
        if (go.GetComponent<MeshRenderer>() != null) return true;

        return false;
    }

    [MenuItem("Collab Tool/Etiquetar Todos los Objetos de la Escena")]
    public static void TagEverything()
    {
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

        Debug.Log($"<color=green>¡Hecho! Se han etiquetado {count} objetos.</color>");
    }
}
#endif