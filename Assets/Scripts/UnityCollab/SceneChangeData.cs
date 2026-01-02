using System;
using UnityEngine;

[Serializable]
public class SceneChangeData
{
    public string type;         // "transform", "add", "delete"
    public string objectID;
    public string parentID;     // Para jerarquias
    public string prefabGUID;   // Para instanciar prefabs
    public string value;        // JSON del valor (posición, etc)
    public long timestamp;
}

[Serializable]
public struct SerializableVector3
{
    public float x, y, z;
    public SerializableVector3(Vector3 v) { x = v.x; y = v.y; z = v.z; }
    public Vector3 ToVector3() { return new Vector3(x, y, z); }
}