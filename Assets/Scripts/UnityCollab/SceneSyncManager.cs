using UnityEngine;
using System.Collections.Generic;
using System.Globalization;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class SceneSyncManager : MonoBehaviour
{
    public static SceneSyncManager Instance;
    public bool isApplyingNetworkChange = false;

    private List<GameObject> pendingUploads = new List<GameObject>();

    // Memorias
    private Dictionary<string, Vector3> lastPositions = new Dictionary<string, Vector3>();
    private Dictionary<string, Quaternion> lastRotations = new Dictionary<string, Quaternion>();
    private Dictionary<string, Vector3> lastScales = new Dictionary<string, Vector3>();
    private Dictionary<string, string> lastMaterials = new Dictionary<string, string>();
    // Memoria de matrículas de Unity
    private Dictionary<string, int> knownInstanceIDs = new Dictionary<string, int>();

    void OnEnable()
    {
        Instance = this;
#if UNITY_EDITOR
        EditorApplication.update += MainLoop;
        Selection.selectionChanged += OnSelectionChanged;
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= MainLoop;
        Selection.selectionChanged -= OnSelectionChanged;
#endif
    }

    private void OnSelectionChanged()
    {
        if (isApplyingNetworkChange) return;

#if UNITY_EDITOR
        foreach (GameObject go in Selection.gameObjects)
        {
            var idComp = go.GetComponent<SceneObjectIdentifier>();
            if (idComp != null)
            {
                if (string.IsNullOrEmpty(idComp.UniqueID)) idComp.GenerateID();
                string currentID = idComp.UniqueID;
                int currentUnityID = go.GetInstanceID();

                // Detector de clones
                if (knownInstanceIDs.ContainsKey(currentID) && knownInstanceIDs[currentID] != currentUnityID)
                {
                    Debug.LogWarning($"[SceneSync] Clon detectado: {go.name}. Generando nueva ID.");
                    idComp.GenerateID();
                    UploadNewObject(go);
                }
                else if (!knownInstanceIDs.ContainsKey(currentID))
                {
                    // Debug.Log($"[SceneSync] Nuevo objeto seleccionado: {go.name}. Subiendo.");
                    UploadNewObject(go);
                }
                knownInstanceIDs[idComp.UniqueID] = currentUnityID;
            }
        }
#endif
    }

    private void MainLoop()
    {
        if (isApplyingNetworkChange) return;
        ProcessPendingUploads();
        MonitorSelectedObjects();
    }

    public void UploadNewObject(GameObject go)
    {
        if (!pendingUploads.Contains(go)) pendingUploads.Add(go);
    }

    private void ProcessPendingUploads()
    {
        if (pendingUploads.Count == 0) return;
        if (CollabNetworkManager.Instance == null || !CollabNetworkManager.Instance.IsConnected) return;

        GameObject[] processList = pendingUploads.ToArray();
        pendingUploads.Clear();

        foreach (GameObject go in processList)
        {
            if (go == null) continue;

            var idComp = go.GetComponent<SceneObjectIdentifier>();
            if (idComp == null) continue;

            if (string.IsNullOrEmpty(idComp.UniqueID)) idComp.GenerateID();
            string id = idComp.UniqueID;
            knownInstanceIDs[id] = go.GetInstanceID();

            // Preparar datos
            string p = V3ToString(go.transform.position);
            string r = V3ToString(go.transform.eulerAngles);
            string s = V3ToString(go.transform.localScale);
            string simplePayload = $"{p}|{r}|{s}";

            // --- DEBUG LOG CRÍTICO ---
            Debug.Log($"<color=yellow>[SEND] Enviando CREATE para {go.name} (ID:{id}). Payload: {simplePayload}</color>");
            // -------------------------

            SendData("create", id, simplePayload, go);

            Renderer rend = go.GetComponent<Renderer>();
            if (rend != null && rend.sharedMaterial != null)
            {
                SendData("material", id, rend.sharedMaterial.name, go);
            }

            lastPositions[id] = go.transform.position;
            lastRotations[id] = go.transform.rotation;
            lastScales[id] = go.transform.localScale;
        }
    }

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
            knownInstanceIDs[id] = go.GetInstanceID();
            bool changed = false;

            // POSICIÓN
            Vector3 currentPos = go.transform.position;
            if (!lastPositions.ContainsKey(id)) lastPositions[id] = currentPos;
            if (Vector3.Distance(lastPositions[id], currentPos) > 0.01f)
            {
                if (isConnected) SendData("transform", id, JsonUtility.ToJson(new SerializableVector3(currentPos)), go);
                lastPositions[id] = currentPos;
                changed = true;
            }

            // ROTACIÓN
            Quaternion currentRot = go.transform.rotation;
            if (!lastRotations.ContainsKey(id)) lastRotations[id] = currentRot;
            if (Quaternion.Angle(lastRotations[id], currentRot) > 0.5f)
            {
                if (isConnected) SendData("rotation", id, JsonUtility.ToJson(new SerializableVector3(currentRot.eulerAngles)), go);
                lastRotations[id] = currentRot;
                changed = true;
            }

            // ESCALA
            Vector3 currentScale = go.transform.localScale;
            if (!lastScales.ContainsKey(id)) lastScales[id] = currentScale;
            if (Vector3.Distance(lastScales[id], currentScale) > 0.01f)
            {
                if (isConnected) SendData("scale", id, JsonUtility.ToJson(new SerializableVector3(currentScale)), go);
                lastScales[id] = currentScale;
                changed = true;
            }

            // MATERIAL
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
        PrefabAssetType prefabType = PrefabUtility.GetPrefabAssetType(go);
        if (prefabType == PrefabAssetType.Regular || prefabType == PrefabAssetType.Variant)
        {
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            if (!string.IsNullOrEmpty(path)) data.prefabGUID = path;
            else data.prefabGUID = "PRIMITIVE:Cube";
        }
        else
        {
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

        if (target == null && !string.IsNullOrEmpty(prefabPath))
        {
            // --- DEBUG LOG CRÍTICO ---
            Debug.Log($"<color=green>[RECV] Creando objeto ID:{id} (Prefab:{prefabPath}) Pos:{pos} Rot:{rot} Scl:{scl}</color>");
            // -------------------------

            SceneChangeData temp = new SceneChangeData();
            temp.objectID = id;
            temp.prefabGUID = prefabPath;
            target = SpawnObject(temp, pos, rot, scl);
        }

        if (target != null)
        {
            knownInstanceIDs[id] = target.gameObject.GetInstanceID();
            isApplyingNetworkChange = true;
            Transform t = target.transform;

            if (pos.HasValue && Vector3.Distance(t.position, pos.Value) > 0.05f)
            { t.position = pos.Value; lastPositions[id] = t.position; }

            if (rot.HasValue)
            {
                Quaternion q = Quaternion.Euler(rot.Value);
                if (Quaternion.Angle(t.rotation, q) > 1f)
                { t.rotation = q; lastRotations[id] = t.rotation; }
            }

            if (scl.HasValue && Vector3.Distance(t.localScale, scl.Value) > 0.01f)
            { t.localScale = scl.Value; lastScales[id] = t.localScale; }

            if (!string.IsNullOrEmpty(mat))
            { ApplyMaterial(target.gameObject, mat); lastMaterials[id] = mat; }

            isApplyingNetworkChange = false;
        }
    }

    private SceneObjectIdentifier SpawnObject(SceneChangeData data, Vector3? initialPos, Vector3? initialRot, Vector3? initialScl)
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
        }
        else if (!string.IsNullOrEmpty(data.prefabGUID))
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(data.prefabGUID);
            if (prefab != null) newObj = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            else newObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        }

        if (newObj != null)
        {
            newObj.name = "NetObj_" + data.objectID.Substring(0, 4);

            if (initialPos.HasValue) newObj.transform.position = initialPos.Value;
            if (initialRot.HasValue) newObj.transform.eulerAngles = initialRot.Value;
            if (initialScl.HasValue) newObj.transform.localScale = initialScl.Value;

            var idComp = newObj.GetComponent<SceneObjectIdentifier>();
            if (idComp == null) idComp = newObj.AddComponent<SceneObjectIdentifier>();
            idComp.SetNetworkID(data.objectID);
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
    private SceneObjectIdentifier FindObjectById(string id) { foreach (var obj in FindObjectsOfType<SceneObjectIdentifier>()) if (obj.UniqueID == id) return obj; return null; }
    private string V3ToString(Vector3 v) { return $"{v.x.ToString(CultureInfo.InvariantCulture)},{v.y.ToString(CultureInfo.InvariantCulture)},{v.z.ToString(CultureInfo.InvariantCulture)}"; }
}