using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class SceneSyncManager : MonoBehaviour
{
    public static SceneSyncManager Instance;
    public bool isApplyingNetworkChange = false;

    // --- COLA DE ESPERA PARA OBJETOS NUEVOS ---
    private List<GameObject> pendingUploads = new List<GameObject>();
    private double lastUploadTime = 0;

    // Diccionarios para no repetir envíos
    private Dictionary<string, Vector3> lastPositions = new Dictionary<string, Vector3>();
    private Dictionary<string, Quaternion> lastRotations = new Dictionary<string, Quaternion>();
    private Dictionary<string, Vector3> lastScales = new Dictionary<string, Vector3>();
    private Dictionary<string, string> lastMaterials = new Dictionary<string, string>();

    void OnEnable()
    {
        Instance = this;
#if UNITY_EDITOR
        EditorApplication.update += MainLoop;
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= MainLoop;
#endif
    }

    // --- BUCLE PRINCIPAL ---
    private void MainLoop()
    {
        if (isApplyingNetworkChange) return;

        // 1. Procesar la cola de objetos nuevos (Evita errores al duplicar muy rápido)
        ProcessPendingUploads();

        // 2. Vigilar objetos seleccionados (moverse, rotar...)
        MonitorSelectedObjects();
    }

    // --- FUNCIÓN PÚBLICA: ALGUIEN QUIERE SUBIR UN OBJETO NUEVO ---
    public void UploadNewObject(GameObject go)
    {
        // No lo subimos YA. Lo metemos en la cola para el siguiente frame.
        if (!pendingUploads.Contains(go))
        {
            pendingUploads.Add(go);
        }
    }

    // --- PROCESADOR DE LA COLA (Se ejecuta en cada update) ---
    private void ProcessPendingUploads()
    {
        if (pendingUploads.Count == 0) return;
        if (CollabNetworkManager.Instance == null || !CollabNetworkManager.Instance.IsConnected) return;

        // Copiamos la lista para poder limpiarla mientras iteramos
        GameObject[] processList = pendingUploads.ToArray();
        pendingUploads.Clear();

        foreach (GameObject go in processList)
        {
            if (go == null) continue; // Por si lo borraste antes de subirlo

            var idComp = go.GetComponent<SceneObjectIdentifier>();
            if (idComp == null) continue;

            // Forzamos validación final
            idComp.ValidateID();
            string id = idComp.UniqueID;

            Debug.Log($"[Collab] Procesando subida de: {go.name} ({id})");

            // ENVIAMOS TODO
            SendData("transform", id, JsonUtility.ToJson(new SerializableVector3(go.transform.position)), go);
            SendData("rotation", id, JsonUtility.ToJson(new SerializableVector3(go.transform.eulerAngles)), go);
            SendData("scale", id, JsonUtility.ToJson(new SerializableVector3(go.transform.localScale)), go);

            Renderer rend = go.GetComponent<Renderer>();
            if (rend != null && rend.sharedMaterial != null)
            {
                SendData("material", id, rend.sharedMaterial.name, go);
            }

            // Registramos estado actual para no reenviarlo
            lastPositions[id] = go.transform.position;
            lastRotations[id] = go.transform.rotation;
            lastScales[id] = go.transform.localScale;
        }
    }

    // --- VIGILANTE (Solo mira lo seleccionado) ---
    private void MonitorSelectedObjects()
    {
        bool isConnected = (CollabNetworkManager.Instance != null && CollabNetworkManager.Instance.IsConnected);

#if UNITY_EDITOR
        if (Selection.gameObjects.Length == 0) return;

        foreach (GameObject go in Selection.gameObjects)
        {
            var idComp = go.GetComponent<SceneObjectIdentifier>();
            if (idComp == null) continue;

            string id = idComp.UniqueID;
            bool changed = false;

            // A. POSICIÓN
            Vector3 currentPos = go.transform.position;
            if (!lastPositions.ContainsKey(id)) lastPositions[id] = currentPos;
            if (Vector3.Distance(lastPositions[id], currentPos) > 0.01f)
            {
                if (isConnected) SendData("transform", id, JsonUtility.ToJson(new SerializableVector3(currentPos)), go);
                lastPositions[id] = currentPos;
                changed = true;
            }

            // B. ROTACIÓN
            Quaternion currentRot = go.transform.rotation;
            if (!lastRotations.ContainsKey(id)) lastRotations[id] = currentRot;
            if (Quaternion.Angle(lastRotations[id], currentRot) > 0.5f)
            {
                if (isConnected) SendData("rotation", id, JsonUtility.ToJson(new SerializableVector3(currentRot.eulerAngles)), go);
                lastRotations[id] = currentRot;
                changed = true;
            }

            // C. ESCALA
            Vector3 currentScale = go.transform.localScale;
            if (!lastScales.ContainsKey(id)) lastScales[id] = currentScale;
            if (Vector3.Distance(lastScales[id], currentScale) > 0.01f)
            {
                if (isConnected) SendData("scale", id, JsonUtility.ToJson(new SerializableVector3(currentScale)), go);
                lastScales[id] = currentScale;
                changed = true;
            }

            // D. MATERIAL
            Renderer rend = go.GetComponent<Renderer>();
            if (rend != null && rend.sharedMaterial != null)
            {
                string matName = rend.sharedMaterial.name;
                if (!lastMaterials.ContainsKey(id)) lastMaterials[id] = matName;
                if (lastMaterials[id] != matName)
                {
                    if (isConnected) SendData("material", id, matName, go);
                    lastMaterials[id] = matName;
                    changed = true;
                }
            }

            if (changed && !isConnected)
            {
                idComp.hasUnsyncedChanges = true;
                EditorUtility.SetDirty(idComp);
            }
        }
#endif
    }

    public void UploadUnsyncedChanges()
    {
#if UNITY_EDITOR
        SceneObjectIdentifier[] allObjects = FindObjectsOfType<SceneObjectIdentifier>();
        foreach (var obj in allObjects)
        {
            if (obj.hasUnsyncedChanges)
            {
                UploadNewObject(obj.gameObject);
                obj.hasUnsyncedChanges = false;
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

#if UNITY_EDITOR
        // DETECCIÓN DE PREFAB / PRIMITIVA
        PrefabAssetType prefabType = PrefabUtility.GetPrefabAssetType(go);

        // Es un Prefab válido Y no es un modelo importado (Model) sino un Prefab variante o regular
        if (prefabType == PrefabAssetType.Regular || prefabType == PrefabAssetType.Variant)
        {
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            // Fix: A veces Unity devuelve string vacio justo al duplicar
            if (!string.IsNullOrEmpty(path))
            {
                data.prefabGUID = path;
            }
            else
            {
                // Fallback si falla la detección de prefab
                data.prefabGUID = "PRIMITIVE:Cube";
            }
        }
        else
        {
            // LÓGICA DE PRIMITIVAS
            string meshName = "Cube";
            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) meshName = mf.sharedMesh.name;

            if (meshName.Contains("Sphere")) meshName = "Sphere";
            else if (meshName.Contains("Capsule")) meshName = "Capsule";
            else if (meshName.Contains("Cylinder")) meshName = "Cylinder";
            else if (meshName.Contains("Plane")) meshName = "Plane";
            else meshName = "Cube";

            data.prefabGUID = "PRIMITIVE:" + meshName;
        }
#endif
        string json = JsonUtility.ToJson(data);
        CollabNetworkManager.Instance.BroadcastData(json);
    }

    public void ApplyFullState(string id, Vector3? pos, Vector3? rot, Vector3? scl, string mat, string prefabPath)
    {
        SceneObjectIdentifier target = FindObjectById(id);
        if (target != null && target.hasUnsyncedChanges) return;

        // SI NO EXISTE -> CREARLO (SPAWN)
        if (target == null && !string.IsNullOrEmpty(prefabPath))
        {
            SceneChangeData temp = new SceneChangeData();
            temp.objectID = id;
            temp.prefabGUID = prefabPath;
            target = SpawnObject(temp);
        }

        if (target != null)
        {
            isApplyingNetworkChange = true;
            Transform t = target.transform;

            if (pos.HasValue && Vector3.Distance(t.position, pos.Value) > 0.05f) { t.position = pos.Value; lastPositions[id] = t.position; }
            if (rot.HasValue) { Quaternion q = Quaternion.Euler(rot.Value); if (Quaternion.Angle(t.rotation, q) > 1f) { t.rotation = q; lastRotations[id] = t.rotation; } }
            if (scl.HasValue && Vector3.Distance(t.localScale, scl.Value) > 0.01f) { t.localScale = scl.Value; lastScales[id] = t.localScale; }
            if (!string.IsNullOrEmpty(mat)) { ApplyMaterial(target.gameObject, mat); lastMaterials[id] = mat; }

            MarkDirty(target);
            isApplyingNetworkChange = false;
        }
    }

    private SceneObjectIdentifier SpawnObject(SceneChangeData data)
    {
#if UNITY_EDITOR
        GameObject newObj = null;

        if (data.prefabGUID.StartsWith("PRIMITIVE"))
        {
            PrimitiveType type = PrimitiveType.Cube;
            string pType = data.prefabGUID.Replace("PRIMITIVE:", "");

            if (pType == "Sphere") type = PrimitiveType.Sphere;
            else if (pType == "Capsule") type = PrimitiveType.Capsule;
            else if (pType == "Cylinder") type = PrimitiveType.Cylinder;
            else if (pType == "Plane") type = PrimitiveType.Plane;

            newObj = GameObject.CreatePrimitive(type);
            newObj.name = pType + "_" + data.objectID.Substring(0, 4);
        }
        else if (!string.IsNullOrEmpty(data.prefabGUID))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(data.prefabGUID);
            if (prefab != null) newObj = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            else
            {
                newObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                newObj.name = "MISSING: " + data.prefabGUID;
            }
        }

        if (newObj != null)
        {
            var idComp = newObj.GetComponent<SceneObjectIdentifier>();
            if (idComp == null) idComp = newObj.AddComponent<SceneObjectIdentifier>();

            idComp.UniqueID = data.objectID;
            // IMPORTANTE: Evitamos que el nuevo objeto clonado se crea un duplicado
            SerializedObject so = new SerializedObject(idComp);
            // Truco: Al crearlo de red, NO queremos que valide ID. Ya tiene ID.
            // Pero como es ExecuteAlways, validará. 
            // Así que necesitamos que ValidateID sepa que este objeto está "autorizado".
            // De momento, confiamos en el ApplyModifiedProperties.
            so.ApplyModifiedProperties();

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
        if (rend.sharedMaterial != null && rend.sharedMaterial.name == matName) return;
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
    private SceneObjectIdentifier FindObjectById(string id) { foreach (var obj in FindObjectsOfType<SceneObjectIdentifier>()) if (obj.UniqueID == id) return obj; return null; }
}