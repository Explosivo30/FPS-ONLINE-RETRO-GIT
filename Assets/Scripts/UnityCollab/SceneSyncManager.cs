using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class SceneSyncManager : MonoBehaviour
{
    public static SceneSyncManager Instance;
    private bool isApplyingNetworkChange = false;
    private Dictionary<string, Vector3> lastPositions = new Dictionary<string, Vector3>();

    void OnEnable()
    {
        Instance = this;
#if UNITY_EDITOR
        EditorApplication.update += MonitorSelectedObjects;
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= MonitorSelectedObjects;
#endif
    }

    // --- ENVIAR ---
    private void MonitorSelectedObjects()
    {
        if (isApplyingNetworkChange) return;
        if (CollabNetworkManager.Instance == null || !CollabNetworkManager.Instance.IsConnected) return;

#if UNITY_EDITOR
        foreach (GameObject go in Selection.gameObjects)
        {
            var idComp = go.GetComponent<SceneObjectIdentifier>();
            if (idComp == null) continue;

            string id = idComp.UniqueID;
            Vector3 currentPos = go.transform.position;

            if (!lastPositions.ContainsKey(id))
            {
                lastPositions[id] = currentPos;
                continue;
            }

            if (Vector3.Distance(lastPositions[id], currentPos) > 0.01f)
            {
                // Ha habido movimiento -> ENVIAR
                SceneChangeData data = new SceneChangeData();
                data.type = "transform";
                data.objectID = id;
                data.value = JsonUtility.ToJson(new SerializableVector3(currentPos));
                data.timestamp = System.DateTime.Now.Ticks;

                string json = JsonUtility.ToJson(data);
                CollabNetworkManager.Instance.BroadcastData(json);

                lastPositions[id] = currentPos;
            }
        }
#endif
    }

    // --- RECIBIR ---
    public void ApplyChange(SceneChangeData data)
    {
        if (data == null) return;

        // No podemos usar FindObjectsOfType todo el rato, es lento.
        // Pero para prototipo sirve.
        SceneObjectIdentifier target = FindObjectById(data.objectID);

        if (target != null)
        {
            // Verificamos si realmente hay cambio para evitar parpadeo
            if (data.type == "transform")
            {
                SerializableVector3 posData = JsonUtility.FromJson<SerializableVector3>(data.value);
                Vector3 newPos = posData.ToVector3();

                if (Vector3.Distance(target.transform.position, newPos) > 0.05f)
                {
                    isApplyingNetworkChange = true;
                    target.transform.position = newPos;

                    // Actualizamos nuestro registro local para no re-enviarlo
                    if (lastPositions.ContainsKey(data.objectID))
                        lastPositions[data.objectID] = newPos;
                    else
                        lastPositions.Add(data.objectID, newPos);

                    isApplyingNetworkChange = false;

                    // Forzar repintado en editor
#if UNITY_EDITOR
                    if (!Application.isPlaying) EditorUtility.SetDirty(target.transform);
#endif
                }
            }
        }
    }

    private SceneObjectIdentifier FindObjectById(string id)
    {
        // Optimizacion: Podrías cachear esto, pero FindObjectsOfType es seguro
        foreach (var obj in FindObjectsOfType<SceneObjectIdentifier>())
        {
            if (obj.UniqueID == id) return obj;
        }
        return null;
    }
}