using System;
using System.Text;
using UnityEngine;
using Newtonsoft.Json;

namespace EZCollabTool
{
    public enum MessageType
    {
        SceneSnapshot,
        CreateObject,
        DestroyObject,
        TransformDelta,
        ComponentDelta,
        LockRequest,
        LockGranted,
        LockDenied,
        LockRelease,
        ObjectLocked,
        ObjectUnlocked,
        PeerJoined,
        PeerLeft,
        Heartbeat,
        Info
    }

    [Serializable]
    public class EZMessage
    {
        public MessageType type;
        public string senderId;
        public long timestamp;
        public string payload;

        public static EZMessage Create(MessageType type, string senderId, object payload)
        {
            return new EZMessage
            {
                type = type,
                senderId = senderId,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                payload = JsonConvert.SerializeObject(payload)
            };
        }

        public T GetPayload<T>()
        {
            return JsonConvert.DeserializeObject<T>(payload);
        }

        public byte[] ToBytes()
        {
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this));
        }

        public static EZMessage FromBytes(byte[] bytes)
        {
            return JsonConvert.DeserializeObject<EZMessage>(Encoding.UTF8.GetString(bytes));
        }
    }

    [Serializable]
    public class TransformPayload
    {
        public string guid;
        public float[] position;
        public float[] rotation;
        public float[] scale;

        public static TransformPayload FromTransform(string guid, Transform t)
        {
            return new TransformPayload
            {
                guid = guid,
                position = new[] { t.position.x, t.position.y, t.position.z },
                rotation = new[] { t.rotation.x, t.rotation.y, t.rotation.z, t.rotation.w },
                scale = new[] { t.localScale.x, t.localScale.y, t.localScale.z }
            };
        }

        public Vector3 GetPosition() => new Vector3(position[0], position[1], position[2]);
        public Quaternion GetRotation() => new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]);
        public Vector3 GetScale() => new Vector3(scale[0], scale[1], scale[2]);
    }

    [Serializable]
    public class LockPayload
    {
        public string guid;
        public string peerId;
        public string peerName;
    }

    [Serializable]
    public class PeerPayload
    {
        public string peerId;
        public string peerName;
    }

    [Serializable]
    public class CreateObjectPayload
    {
        public string guid;
        public string parentGuid;
        public string objectName;
        public ObjectSourceType sourceType;
        public string prefabPath;
        public PrimitiveType primitiveType;
        public SerializedTransform localTransform;
    }

    public enum ObjectSourceType
    {
        Primitive,
        Prefab
    }

    [Serializable]
    public class SerializedTransform
    {
        public float[] position;
        public float[] rotation;
        public float[] scale;

        public static SerializedTransform FromTransform(Transform t)
        {
            return new SerializedTransform
            {
                position = new[] { t.localPosition.x, t.localPosition.y, t.localPosition.z },
                rotation = new[] { t.localRotation.x, t.localRotation.y, t.localRotation.z, t.localRotation.w },
                scale = new[] { t.localScale.x, t.localScale.y, t.localScale.z }
            };
        }

        public Vector3 GetPosition() => new Vector3(position[0], position[1], position[2]);
        public Quaternion GetRotation() => new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]);
        public Vector3 GetScale() => new Vector3(scale[0], scale[1], scale[2]);
    }

    [Serializable]
    public class DestroyObjectPayload
    {
        public string guid;
    }
}
