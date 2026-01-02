#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class AutoIDAssigner
{
    static AutoIDAssigner()
    {
        // Se ejecuta cada vez que cambia la jerarquía
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
    }

    private static void OnHierarchyChanged()
    {
        if (Application.isPlaying) return;

        // Busca objetos sin ID y se lo asigna
        foreach (GameObject go in Object.FindObjectsOfType<GameObject>())
        {
            // Solo procesar si está en una escena válida
            if (!go.scene.IsValid()) continue;

            var idComponent = go.GetComponent<SceneObjectIdentifier>();
            if (idComponent == null)
            {
                // Opcional: Solo añadirlo si el usuario quiere sincronizar este objeto
                // Para simplificar, aquí lo añadimos a todo, pero en prod podrías filtrar.
                // go.AddComponent<SceneObjectIdentifier>().ValidateID(); 
            }
            else
            {
                idComponent.ValidateID();
            }
        }
    }
}
#endif