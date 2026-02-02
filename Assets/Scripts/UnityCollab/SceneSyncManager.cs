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

    private Dictionary<string, Vector3> lastPositions = new Dictionary<string, Vector3>();
    private Dictionary<string, Quaternion> lastRotations = new Dictionary<string, Quaternion>();
    private Dictionary<string, Vector3> lastScales = new Dictionary<string, Vector3>();
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

    public void UploadNewObject(GameObject go)
    {
        if (CollabNetworkManager.Instance != null && CollabNetworkManager.Instance.IsConnected && !isApplyingNetworkChange)
        {
            var idComp = go.GetComponent<SceneObjectIdentifier>();
            if (idComp == null) return;
            // Aseguramos que el ID esté validado antes de subir
            idComp.ValidateID();

            string id = idComp.UniqueID;

            SendData("transform", id, JsonUtility.ToJson(new SerializableVector3(go.transform.position)), go);
            SendData("rotation", id, JsonUtility.ToJson(new SerializableVector3(go.transform.eulerAngles)), go);
            SendData("scale", id, JsonUtility.ToJson(new SerializableVector3(go.transform.localScale)), go);

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
        if (isApplyingNetworkChange) return;
        bool isConnected = (CollabNetworkManager.Instance != null && CollabNetworkManager.Instance.IsConnected);

#if UNITY_EDITOR
        foreach (GameObject go in Selection.gameObjects)
        {
            var idComp = go.GetComponent<SceneObjectIdentifier>();
            if (idComp == null) continue;

            // Validación extra por si acaso es un duplicado reciente
            idComp.ValidateID();

            string id = idComp.UniqueID;
            bool changed = false;

            Vector3 currentPos = go.transform.position;
            if (!lastPositions.ContainsKey(id)) lastPositions[id] = currentPos;
            if (Vector3.Distance(lastPositions[id], currentPos) > 0.01f)
            {
                if (isConnected) SendData("transform", id, JsonUtility.ToJson(new SerializableVector3(currentPos)), go);
                lastPositions[id] = currentPos;
                changed = true;
            }

            Quaternion currentRot = go.transform.rotation;
            if (!lastRotations.ContainsKey(id)) lastRotations[id] = currentRot;
            if (Quaternion.Angle(lastRotations[id], currentRot) > 0.5f)
            {
                if (isConnected) SendData("rotation", id, JsonUtility.ToJson(new SerializableVector3(currentRot.eulerAngles)), go);
                lastRotations[id] = currentRot;
                changed = true;
            }

            Vector3 currentScale = go.transform.localScale;
            if (!lastScales.ContainsKey(id)) lastScales[id] = currentScale;
            if (Vector3.Distance(lastScales[id], currentScale) > 0.01f)
            {
                if (isConnected) SendData("scale", id, JsonUtility.ToJson(new SerializableVector3(currentScale)), go);
                lastScales[id] = currentScale;
                changed = true;
            }

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
            data.prefabGUID = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
        }
        else
        {
            // DETECTAR TIPO DE PRIMITIVA
            string meshName = "Cube"; // Default
            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) meshName = mf.sharedMesh.name;

            // Unity suele llamar a las mallas "Cube Instance" o "Sphere", limpiamos el nombre
            if (meshName.Contains("Sphere")) meshName = "Sphere";
            else if (meshName.Contains("Capsule")) meshName = "Capsule";
            else if (meshName.Contains("Cylinder")) meshName = "Cylinder";
            else if (meshName.Contains("Plane")) meshName = "Plane";
            else meshName = "Cube"; // Fallback

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

        // LÓGICA DE PRIMITIVAS MEJORADA
        if (data.prefabGUID.StartsWith("PRIMITIVE"))
        {
            PrimitiveType type = PrimitiveType.Cube;
            string pType = data.prefabGUID.Replace("PRIMITIVE:", ""); // Quitamos el prefijo

            if (pType == "Sphere") type = PrimitiveType.Sphere;
            else if (pType == "Capsule") type = PrimitiveType.Capsule;
            else if (pType == "Cylinder") type = PrimitiveType.Cylinder;
            else if (pType == "Plane") type = PrimitiveType.Plane;

            newObj = GameObject.CreatePrimitive(type);
            newObj.name = pType + "_" + data.objectID.Substring(0, 4);
        }
        else if (!string.IsNullOrEmpty(data.prefabGUID))
        {
            // Lógica de Prefabs (Sigue funcionando igual)
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

            // ASIGNAMOS ID Y GUARDAMOS EL INSTANCE ID PARA QUE NO CREA QUE ES CLON
            idComp.UniqueID = data.objectID;
            SerializedObject so = new SerializedObject(idComp);
            so.FindProperty("originalInstanceID").intValue = newObj.GetInstanceID();
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