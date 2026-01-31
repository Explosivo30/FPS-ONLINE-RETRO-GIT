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

    // Memorias para saber qué ha cambiado
    private Dictionary<string, Vector3> lastPositions = new Dictionary<string, Vector3>();
    private Dictionary<string, Quaternion> lastRotations = new Dictionary<string, Quaternion>();
    private Dictionary<string, Vector3> lastScales = new Dictionary<string, Vector3>(); // NUEVO
    private Dictionary<string, string> lastMaterials = new Dictionary<string, string>();

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

    // --- 1. ENVIAR (Vigilante) ---
    private void MonitorSelectedObjects()
    {
        if (isApplyingNetworkChange) return;
        if (CollabNetworkManager.Instance == null || !CollabNetworkManager.Instance.IsConnected) return;

#if UNITY_EDITOR
        foreach (GameObject go in Selection.gameObjects)
        {
            var idComp = go.GetComponent<SceneObjectIdentifier>();
            if (idComp == null) continue; // Si no tiene DNI, pasamos

            string id = idComp.UniqueID;

            // --- NUEVO: ¿Es la primera vez que tocamos este objeto? ---
            // Si movemos algo, enviamos un "KeepAlive" o "Create" implícito
            // Para simplificar: Si detectamos cambio, enviamos datos.
            // Si el otro lado no tiene el objeto, le enviaremos info de CREACIÓN.

            // A. POSICIÓN
            Vector3 currentPos = go.transform.position;
            if (!lastPositions.ContainsKey(id)) lastPositions[id] = currentPos;

            if (Vector3.Distance(lastPositions[id], currentPos) > 0.01f)
            {
                // Al movernos, enviamos también qué tipo de objeto es, por si el otro no lo tiene
                SendData("transform", id, JsonUtility.ToJson(new SerializableVector3(currentPos)), go);
                lastPositions[id] = currentPos;
            }

            // B. ROTACIÓN
            Quaternion currentRot = go.transform.rotation;
            if (!lastRotations.ContainsKey(id)) lastRotations[id] = currentRot;

            if (Quaternion.Angle(lastRotations[id], currentRot) > 0.5f)
            {
                SendData("rotation", id, JsonUtility.ToJson(new SerializableVector3(currentRot.eulerAngles)), go);
                lastRotations[id] = currentRot;
            }

            // C. ESCALA (NUEVO)
            Vector3 currentScale = go.transform.localScale;
            if (!lastScales.ContainsKey(id)) lastScales[id] = currentScale;

            if (Vector3.Distance(lastScales[id], currentScale) > 0.01f)
            {
                SendData("scale", id, JsonUtility.ToJson(new SerializableVector3(currentScale)), go);
                lastScales[id] = currentScale;
            }

            // D. MATERIAL
            Renderer rend = go.GetComponent<Renderer>();
            if (rend != null && rend.sharedMaterial != null)
            {
                string matName = rend.sharedMaterial.name;
                if (!lastMaterials.ContainsKey(id)) lastMaterials[id] = matName;
                if (lastMaterials[id] != matName)
                {
                    SendData("material", id, matName, go);
                    lastMaterials[id] = matName;
                }
            }
        }
#endif
    }

    private void SendData(string type, string id, string val, GameObject go)
    {
        SceneChangeData data = new SceneChangeData();
        data.type = type;
        data.objectID = id;
        data.value = val;
        data.timestamp = System.DateTime.Now.Ticks;

        // --- INFO DE CREACIÓN (PREFABS) ---
#if UNITY_EDITOR
        // Adjuntamos siempre la ruta del prefab o info del objeto
        // Así si el receptor NO tiene el objeto, sabe cuál crear.

        PrefabAssetType prefabType = PrefabUtility.GetPrefabAssetType(go);
        if (prefabType == PrefabAssetType.Regular || prefabType == PrefabAssetType.Variant)
        {
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            data.prefabGUID = path; // Enviamos la ruta: "Assets/Enemigos/Robot.prefab"
        }
        else
        {
            // Si es un cubo primitivo creado en escena
            data.prefabGUID = "PRIMITIVE";
        }
#endif

        string json = JsonUtility.ToJson(data);
        CollabNetworkManager.Instance.BroadcastData(json);
    }

    // --- 2. RECIBIR (Aplicar cambios) ---
    public void ApplyChange(SceneChangeData data)
    {
        if (data == null) return;

        SceneObjectIdentifier target = FindObjectById(data.objectID);

        // --- MAGIA: SI NO EXISTE, LO CREAMOS ---
        if (target == null)
        {
            target = SpawnObject(data);
            if (target == null) return; // Si falló la creación, abortamos
        }

        isApplyingNetworkChange = true;

        // APLICAR CAMBIOS
        if (data.type == "transform")
        {
            SerializableVector3 v = JsonUtility.FromJson<SerializableVector3>(data.value);
            target.transform.position = v.ToVector3();
            lastPositions[data.objectID] = target.transform.position;
        }
        else if (data.type == "rotation")
        {
            SerializableVector3 v = JsonUtility.FromJson<SerializableVector3>(data.value);
            target.transform.rotation = Quaternion.Euler(v.ToVector3());
            lastRotations[data.objectID] = target.transform.rotation;
        }
        else if (data.type == "scale") // NUEVO
        {
            SerializableVector3 v = JsonUtility.FromJson<SerializableVector3>(data.value);
            target.transform.localScale = v.ToVector3();
            lastScales[data.objectID] = target.transform.localScale;
        }
        else if (data.type == "material")
        {
            ApplyMaterial(target.gameObject, data.value);
        }

        MarkDirty(target);
        isApplyingNetworkChange = false;
    }

    // --- SISTEMA DE SPAWN (NUEVO) ---
    private SceneObjectIdentifier SpawnObject(SceneChangeData data)
    {
#if UNITY_EDITOR
        GameObject newObj = null;

        if (data.prefabGUID == "PRIMITIVE")
        {
            // Si no sabemos qué es, creamos un Cubo por defecto
            newObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            newObj.name = "Cubo_Red_" + data.objectID.Substring(0, 4);
        }
        else if (!string.IsNullOrEmpty(data.prefabGUID))
        {
            // Intentamos cargar el prefab desde la ruta
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(data.prefabGUID);
            if (prefab != null)
            {
                newObj = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            }
            else
            {
                Debug.LogWarning($"[Collab] Falta el prefab: {data.prefabGUID}. Creando cubo temporal.");
                newObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                newObj.name = "MISSING_PREFAB";
            }
        }

        if (newObj != null)
        {
            // IMPORTANTE: Le asignamos el ID que viene de la red
            var idComp = newObj.GetComponent<SceneObjectIdentifier>();
            if (idComp == null) idComp = newObj.AddComponent<SceneObjectIdentifier>();

            idComp.UniqueID = data.objectID; // Forzamos el ID del otro usuario
            return idComp;
        }
#endif
        return null;
    }

    private void ApplyMaterial(GameObject go, string matName)
    {
#if UNITY_EDITOR
        Renderer rend = go.GetComponent<Renderer>();
        if (rend == null) return;

        string[] guids = AssetDatabase.FindAssets(matName + " t:Material");
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            Material foundMat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (foundMat != null) rend.sharedMaterial = foundMat;
        }
#endif
    }

    private void MarkDirty(Component target)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) EditorUtility.SetDirty(target);
#endif
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