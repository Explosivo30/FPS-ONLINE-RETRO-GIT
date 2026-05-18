using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Newtonsoft.Json;

namespace EZCollabTool
{
    [System.Serializable]
    public class SceneSnapshot
    {
        public List<SerializedObject> objects = new List<SerializedObject>();
    }

    [System.Serializable]
    public class SerializedObject
    {
        public string guid;
        public string parentGuid;
        public string name;
        public bool active;
        public ObjectSourceType sourceType;
        public string prefabPath;
        public PrimitiveType primitiveType;
        public SerializedTransform localTransform;
    }

    public static class EZSceneSerializer
    {
        public static SceneSnapshot CaptureScene()
        {
            var snapshot = new SceneSnapshot();
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

            foreach (var root in roots)
                SerializeHierarchy(root, null, snapshot.objects);

            return snapshot;
        }

        static void SerializeHierarchy(GameObject go, string parentGuid, List<SerializedObject> list)
        {
            string guid = EZCollabState.GetOrAssignGuid(go);

            var serialized = new SerializedObject
            {
                guid = guid,
                parentGuid = parentGuid,
                name = go.name,
                active = go.activeSelf,
                localTransform = SerializedTransform.FromTransform(go.transform)
            };

            string prefabPath = GetPrefabPath(go);
            if (prefabPath != null)
            {
                serialized.sourceType = ObjectSourceType.Prefab;
                serialized.prefabPath = prefabPath;
            }
            else
            {
                serialized.sourceType = ObjectSourceType.Primitive;
                serialized.primitiveType = GuessPrimitiveType(go);
            }

            list.Add(serialized);

            for (int i = 0; i < go.transform.childCount; i++)
                SerializeHierarchy(go.transform.GetChild(i).gameObject, guid, list);
        }

        public static void ApplySnapshot(SceneSnapshot snapshot)
        {
            var existingGuids = new HashSet<string>(EZCollabState.guidToObject.Keys);

            // Crear los que no existen todavía, en orden (padres antes que hijos)
            var byGuid = new Dictionary<string, SerializedObject>();
            foreach (var obj in snapshot.objects)
                byGuid[obj.guid] = obj;

            foreach (var obj in snapshot.objects)
            {
                if (!existingGuids.Contains(obj.guid))
                    CreateFromSerialized(obj, byGuid);
            }

            // Aplicar transforms a todos
            foreach (var obj in snapshot.objects)
            {
                if (EZCollabState.guidToObject.TryGetValue(obj.guid, out GameObject go) && go != null)
                {
                    go.transform.localPosition = obj.localTransform.GetPosition();
                    go.transform.localRotation = obj.localTransform.GetRotation();
                    go.transform.localScale = obj.localTransform.GetScale();
                    go.SetActive(obj.active);
                }
            }
        }

        public static GameObject CreateFromPayload(CreateObjectPayload payload)
        {
            GameObject go = null;

            if (payload.sourceType == ObjectSourceType.Prefab)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(payload.prefabPath);
                if (prefab != null)
                    go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            }

            if (go == null)
                go = GameObject.CreatePrimitive(payload.primitiveType);

            go.name = payload.objectName;
            go.transform.localPosition = payload.localTransform.GetPosition();
            go.transform.localRotation = payload.localTransform.GetRotation();
            go.transform.localScale = payload.localTransform.GetScale();

            if (!string.IsNullOrEmpty(payload.parentGuid) &&
                EZCollabState.guidToObject.TryGetValue(payload.parentGuid, out GameObject parent) &&
                parent != null)
            {
                go.transform.SetParent(parent.transform, false);
            }

            EZCollabState.RegisterGuid(go, payload.guid);
            return go;
        }

        static void CreateFromSerialized(SerializedObject data, Dictionary<string, SerializedObject> byGuid)
        {
            // Asegurar que el padre existe antes de crear el hijo
            if (!string.IsNullOrEmpty(data.parentGuid) && !EZCollabState.guidToObject.ContainsKey(data.parentGuid))
            {
                if (byGuid.TryGetValue(data.parentGuid, out var parentData))
                    CreateFromSerialized(parentData, byGuid);
            }

            var payload = new CreateObjectPayload
            {
                guid = data.guid,
                parentGuid = data.parentGuid,
                objectName = data.name,
                sourceType = data.sourceType,
                prefabPath = data.prefabPath,
                primitiveType = data.primitiveType,
                localTransform = data.localTransform
            };

            CreateFromPayload(payload);
        }

        static string GetPrefabPath(GameObject go)
        {
            var prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (prefabAsset == null) return null;
            return AssetDatabase.GetAssetPath(prefabAsset);
        }

        static PrimitiveType GuessPrimitiveType(GameObject go)
        {
            var filter = go.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return PrimitiveType.Cube;

            string meshName = filter.sharedMesh.name.ToLower();
            if (meshName.Contains("sphere")) return PrimitiveType.Sphere;
            if (meshName.Contains("cylinder")) return PrimitiveType.Cylinder;
            if (meshName.Contains("capsule")) return PrimitiveType.Capsule;
            if (meshName.Contains("plane")) return PrimitiveType.Plane;
            if (meshName.Contains("quad")) return PrimitiveType.Quad;
            return PrimitiveType.Cube;
        }
    }
}
