using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Threading.Tasks;

namespace EZCollabTool
{
    [InitializeOnLoad]
    public static class EZCollabHooks
    {
        static double lastTransformSend = 0;
        const double transformSendInterval = 0.05; // 20hz

        static readonly HashSet<string> pendingLockRequests = new HashSet<string>();

        static EZCollabHooks()
        {
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            Undo.postprocessModifications += OnModifications;
            EditorApplication.update += OnUpdate;
        }

        static void OnUpdate()
        {
            if (!EZCollabState.inSession) return;

            ProcessIncomingMessages();
        }

        static void OnSelectionChanged()
        {
            if (!EZCollabState.inSession) return;

            // Liberar locks de objetos que ya no están seleccionados
            var toRelease = new List<string>();
            foreach (var guid in EZCollabState.myLocks)
            {
                bool stillSelected = false;
                foreach (var go in Selection.gameObjects)
                {
                    if (EZCollabState.TryGetGuid(go, out string g) && g == guid)
                    {
                        stillSelected = true;
                        break;
                    }
                }
                if (!stillSelected) toRelease.Add(guid);
            }

            foreach (var guid in toRelease)
                ReleaseLock(guid);

            // Pedir lock para los recién seleccionados
            foreach (var go in Selection.gameObjects)
            {
                if (!EZCollabState.TryGetGuid(go, out string guid)) continue;
                if (EZCollabState.IsLockedByMe(guid)) continue;
                if (EZCollabState.IsLockedByOther(guid)) continue;
                if (pendingLockRequests.Contains(guid)) continue;

                RequestLock(guid);
            }
        }

        static void OnHierarchyChanged()
        {
            if (!EZCollabState.inSession) return;

            // Detectar objetos nuevos (no tienen guid todavía)
            var roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var root in roots)
                CheckForNewObjects(root);
        }

        static void CheckForNewObjects(GameObject go)
        {
            if (!EZCollabState.objectToGuid.ContainsKey(go))
            {
                // Objeto nuevo — asignar guid y hacer broadcast CREATE
                string guid = EZCollabState.GetOrAssignGuid(go);
                SendCreateObject(go, guid);
            }

            for (int i = 0; i < go.transform.childCount; i++)
                CheckForNewObjects(go.transform.GetChild(i).gameObject);
        }

        static UndoPropertyModification[] OnModifications(UndoPropertyModification[] mods)
        {
            if (!EZCollabState.inSession) return mods;

            double now = EditorApplication.timeSinceStartup;
            if (now - lastTransformSend < transformSendInterval) return mods;

            var dirtyObjects = new HashSet<GameObject>();

            foreach (var mod in mods)
            {
                if (mod.currentValue.target is GameObject go)
                    dirtyObjects.Add(go);
                else if (mod.currentValue.target is Component comp)
                    dirtyObjects.Add(comp.gameObject);
            }

            foreach (var go in dirtyObjects)
            {
                if (!EZCollabState.TryGetGuid(go, out string guid)) continue;
                if (!EZCollabState.IsLockedByMe(guid)) continue;

                SendTransformDelta(go, guid);
            }

            lastTransformSend = now;
            return mods;
        }

        static void SendTransformDelta(GameObject go, string guid)
        {
            var payload = TransformPayload.FromTransform(guid, go.transform);
            var msg = EZMessage.Create(MessageType.TransformDelta, EZCollabState.localPeerId, payload);

            if (EZCollabState.isHost)
                EZCollabSession.server?.incomingMessages.Enqueue(msg);
            else
                _ = EZCollabSession.client?.Send(msg);
        }

        static void SendCreateObject(GameObject go, string guid)
        {
            string prefabPath = null;
            var prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (prefabAsset != null)
                prefabPath = AssetDatabase.GetAssetPath(prefabAsset);

            var payload = new CreateObjectPayload
            {
                guid = guid,
                parentGuid = go.transform.parent != null && EZCollabState.TryGetGuid(go.transform.parent.gameObject, out string parentGuid) ? parentGuid : null,
                objectName = go.name,
                sourceType = prefabPath != null ? ObjectSourceType.Prefab : ObjectSourceType.Primitive,
                prefabPath = prefabPath,
                primitiveType = PrimitiveType.Cube,
                localTransform = SerializedTransform.FromTransform(go.transform)
            };

            var msg = EZMessage.Create(MessageType.CreateObject, EZCollabState.localPeerId, payload);

            if (EZCollabState.isHost)
                EZCollabSession.server?.incomingMessages.Enqueue(msg);
            else
                _ = EZCollabSession.client?.Send(msg);
        }

        static void RequestLock(string guid)
        {
            pendingLockRequests.Add(guid);

            var payload = new LockPayload { guid = guid, peerId = EZCollabState.localPeerId, peerName = EZCollabState.localPeerName };
            var msg = EZMessage.Create(MessageType.LockRequest, EZCollabState.localPeerId, payload);

            if (EZCollabState.isHost)
            {
                // El host se concede el lock a sí mismo directamente
                EZCollabState.ApplyLock(guid, EZCollabState.localPeerId);
                pendingLockRequests.Remove(guid);

                var broadcast = EZMessage.Create(MessageType.ObjectLocked, EZCollabState.localPeerId, payload);
                EZCollabSession.server?.incomingMessages.Enqueue(broadcast);
            }
            else
            {
                _ = EZCollabSession.client?.Send(msg);
            }
        }

        static void ReleaseLock(string guid)
        {
            EZCollabState.ReleaseLock(guid);

            var payload = new LockPayload { guid = guid, peerId = EZCollabState.localPeerId };
            var msg = EZMessage.Create(MessageType.LockRelease, EZCollabState.localPeerId, payload);

            if (EZCollabState.isHost)
                EZCollabSession.server?.incomingMessages.Enqueue(msg);
            else
                _ = EZCollabSession.client?.Send(msg);
        }

        static void ProcessIncomingMessages()
        {
            var queue = EZCollabState.isHost
                ? EZCollabSession.server?.incomingMessages
                : EZCollabSession.client?.incomingMessages;

            if (queue == null) return;

            int processed = 0;
            while (queue.TryDequeue(out var msg) && processed < 32)
            {
                ApplyMessage(msg);
                processed++;
            }
        }

        static void ApplyMessage(EZMessage msg)
        {
            switch (msg.type)
            {
                case MessageType.SceneSnapshot:
                {
                    var snapshot = msg.GetPayload<SceneSnapshot>();
                    EZSceneSerializer.ApplySnapshot(snapshot);
                    break;
                }

                case MessageType.TransformDelta:
                {
                    var payload = msg.GetPayload<TransformPayload>();
                    if (!EZCollabState.guidToObject.TryGetValue(payload.guid, out var go) || go == null) break;

                    Undo.RecordObject(go.transform, "EZCollab transform");
                    go.transform.position = payload.GetPosition();
                    go.transform.rotation = payload.GetRotation();
                    go.transform.localScale = payload.GetScale();
                    break;
                }

                case MessageType.CreateObject:
                {
                    var payload = msg.GetPayload<CreateObjectPayload>();
                    if (EZCollabState.guidToObject.ContainsKey(payload.guid)) break;
                    EZSceneSerializer.CreateFromPayload(payload);
                    break;
                }

                case MessageType.DestroyObject:
                {
                    var payload = msg.GetPayload<DestroyObjectPayload>();
                    if (!EZCollabState.guidToObject.TryGetValue(payload.guid, out var go) || go == null) break;
                    EZCollabState.RemoveObject(go);
                    Undo.DestroyObjectImmediate(go);
                    break;
                }

                case MessageType.ObjectLocked:
                {
                    var payload = msg.GetPayload<LockPayload>();
                    EZCollabState.ApplyLock(payload.guid, payload.peerId);
                    pendingLockRequests.Remove(payload.guid);
                    SceneView.RepaintAll();
                    break;
                }

                case MessageType.ObjectUnlocked:
                {
                    var payload = msg.GetPayload<LockPayload>();
                    EZCollabState.ReleaseLock(payload.guid);
                    SceneView.RepaintAll();
                    break;
                }

                case MessageType.LockGranted:
                {
                    var payload = msg.GetPayload<LockPayload>();
                    EZCollabState.ApplyLock(payload.guid, EZCollabState.localPeerId);
                    pendingLockRequests.Remove(payload.guid);
                    break;
                }

                case MessageType.LockDenied:
                {
                    var payload = msg.GetPayload<LockPayload>();
                    pendingLockRequests.Remove(payload.guid);
                    // Deseleccionar el objeto si no conseguimos el lock
                    if (EZCollabState.guidToObject.TryGetValue(payload.guid, out var go) && go != null)
                    {
                        var newSelection = new List<GameObject>(Selection.gameObjects);
                        newSelection.Remove(go);
                        Selection.objects = newSelection.ToArray();
                    }
                    break;
                }

                case MessageType.PeerJoined:
                {
                    var payload = msg.GetPayload<PeerPayload>();
                    EZCollabState.connectedPeers[payload.peerId] = payload.peerName;
                    break;
                }

                case MessageType.PeerLeft:
                {
                    var payload = msg.GetPayload<PeerPayload>();
                    EZCollabState.connectedPeers.Remove(payload.peerId);
                    EZCollabState.ReleaseAllLocksForPeer(payload.peerId);
                    SceneView.RepaintAll();
                    break;
                }
            }
        }
    }
}
