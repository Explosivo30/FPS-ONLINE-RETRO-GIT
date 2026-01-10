using UnityEngine;
using System.Collections.Generic;

// 1. IMPORTANTE: Envolvemos el using de UnityEditor
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class SceneSyncManager : MonoBehaviour
{
    public static SceneSyncManager Instance;

    private bool isApplyingNetworkChange = false;

    private Dictionary<string, Vector3> lastPositions = new Dictionary<string, Vector3>();
    private Dictionary<string, Quaternion> lastRotations = new Dictionary<string, Quaternion>();

    void OnEnable()
    {
        Instance = this;
        // 2. Solo nos suscribimos al update del Editor si estamos en el Editor
#if UNITY_EDITOR
        EditorApplication.update += MonitorSelectedObjects;
#endif
    }

    void OnDisable()
    {
        // 3. Lo mismo para desuscribirnos
#if UNITY_EDITOR
        EditorApplication.update -= MonitorSelectedObjects;
#endif
    }

    // --- 1. ENVIAR (Detectar movimiento local) ---
    // 4. Toda esta función usa Selection, así que la ocultamos del juego final
#if UNITY_EDITOR
    private void MonitorSelectedObjects()
    {
        if (isApplyingNetworkChange) return;

        // Verificamos si el Manager existe antes de intentar usarlo
        if (CollabNetworkManager.Instance == null || !CollabNetworkManager.Instance.IsConnected) return;

        // AQUÍ estaba el error: Selection no existe fuera del editor
        foreach (GameObject go in Selection.gameObjects)
        {
            var idComp = go.GetComponent<SceneObjectIdentifier>();
            if (idComp == null) continue;

            bool hasMoved = false;

            if (!lastPositions.ContainsKey(idComp.UniqueID) || Vector3.Distance(lastPositions[idComp.UniqueID], go.transform.position) > 0.001f)
            {
                lastPositions[idComp.UniqueID] = go.transform.position;
                hasMoved = true;
            }

            if (!lastRotations.ContainsKey(idComp.UniqueID) || Quaternion.Angle(lastRotations[idComp.UniqueID], go.transform.rotation) > 0.1f)
            {
                lastRotations[idComp.UniqueID] = go.transform.rotation;
                hasMoved = true;
            }

            if (hasMoved)
            {
                SendTransformUpdate(go, idComp.UniqueID);
            }
        }
    }
#endif

    private void SendTransformUpdate(GameObject obj, string id)
    {
        // SceneChangeData debe ser accesible, asegúrate de tener esa clase definida
        SceneChangeData data = new SceneChangeData
        {
            type = "transform",
            objectID = id,
            value = JsonUtility.ToJson(new SerializableVector3(obj.transform.position)),
            timestamp = System.DateTime.Now.Ticks
        };

        string json = JsonUtility.ToJson(data);

        if (CollabNetworkManager.Instance != null)
        {
            CollabNetworkManager.Instance.BroadcastData(json);
        }
    }

    // --- 2. RECIBIR (Aplicar movimiento remoto) ---
    public void ApplyChange(SceneChangeData data)
    {
        MainThreadDispatcher.Execute(() =>
        {
            SceneObjectIdentifier target = FindObjectById(data.objectID);

            if (target != null)
            {
                isApplyingNetworkChange = true;

                if (data.type == "transform")
                {
                    SerializableVector3 posData = JsonUtility.FromJson<SerializableVector3>(data.value);
                    target.transform.position = posData.ToVector3();

                    if (lastPositions.ContainsKey(data.objectID))
                        lastPositions[data.objectID] = target.transform.position;
                }

                isApplyingNetworkChange = false;
            }
        });
    }

    private SceneObjectIdentifier FindObjectById(string id)
    {
        foreach (var obj in FindObjectsOfType<SceneObjectIdentifier>())
        {
            if (obj.UniqueID == id) return obj;
        }
        return null;
    }
}

public static class MainThreadDispatcher
{
    public static void Execute(System.Action action)
    {
#if UNITY_EDITOR
        // En editor usamos esto
        EditorApplication.delayCall += () => action();
#else
        // Si quisieras que funcione en el juego compilado (runtime),
        // aquí necesitarías otra lógica, pero para tu herramienta esto basta:
        Debug.LogWarning("Esta herramienta solo funciona en el Editor por ahora.");
#endif
    }
}