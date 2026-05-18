using UnityEditor;
using UnityEngine;

namespace EZCollabTool
{
    public class EZCollabWindow : EditorWindow
    {
        string peerName = "";
        string hostIp = "";
        int port = 9876;
        bool isConnecting = false;

        [MenuItem("Window/EZCollabTool")]
        static void Open()
        {
            GetWindow<EZCollabWindow>("EZCollab");
        }

        void OnEnable()
        {
            peerName = System.Environment.UserName;
            EditorApplication.update += Repaint;
        }

        void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        void OnGUI()
        {
            if (EZCollabState.inSession)
                DrawSessionView();
            else
                DrawConnectView();
        }

        void DrawConnectView()
        {
            GUILayout.Space(8);
            EditorGUILayout.LabelField("EZCollabTool", EditorStyles.boldLabel);
            GUILayout.Space(4);

            peerName = EditorGUILayout.TextField("Tu nombre", peerName);
            port = EditorGUILayout.IntField("Puerto", port);

            GUILayout.Space(8);
            EditorGUILayout.LabelField("Host", EditorStyles.boldLabel);

            if (!isConnecting && GUILayout.Button("Start session (host)"))
            {
                if (string.IsNullOrWhiteSpace(peerName))
                {
                    EditorUtility.DisplayDialog("EZCollab", "Introduce tu nombre antes de empezar.", "Ok");
                    return;
                }
                EZCollabSession.StartHost(peerName, port);
            }

            GUILayout.Space(8);
            EditorGUILayout.LabelField("Cliente", EditorStyles.boldLabel);

            hostIp = EditorGUILayout.TextField("IP del host", hostIp);

            if (!isConnecting && GUILayout.Button("Join session"))
            {
                if (string.IsNullOrWhiteSpace(peerName) || string.IsNullOrWhiteSpace(hostIp))
                {
                    EditorUtility.DisplayDialog("EZCollab", "Rellena tu nombre y la IP del host.", "Ok");
                    return;
                }
                ConnectAsClient();
            }

            if (isConnecting)
                EditorGUILayout.HelpBox("Conectando...", UnityEditor.MessageType.Info);
        }

        async void ConnectAsClient()
        {
            isConnecting = true;
            Repaint();

            bool ok = await EZCollabSession.JoinAsClient(peerName, hostIp, port);

            isConnecting = false;

            if (!ok)
                EditorUtility.DisplayDialog("EZCollab", $"No se pudo conectar a {hostIp}:{port}", "Ok");

            Repaint();
        }

        void DrawSessionView()
        {
            GUILayout.Space(8);

            string role = EZCollabState.isHost ? "HOST" : "cliente";
            EditorGUILayout.LabelField($"Sesión activa — {role}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Tú: {EZCollabState.localPeerName}");

            if (EZCollabState.isHost)
            {
                int count = EZCollabSession.server?.clientCount ?? 0;
                EditorGUILayout.LabelField($"Clientes conectados: {count}");
            }

            GUILayout.Space(6);
            EditorGUILayout.LabelField("Peers en sesión", EditorStyles.boldLabel);

            if (EZCollabState.connectedPeers.Count == 0)
            {
                EditorGUILayout.LabelField("Nadie más conectado aún", EditorStyles.miniLabel);
            }
            else
            {
                foreach (var kv in EZCollabState.connectedPeers)
                    EditorGUILayout.LabelField($"  {kv.Value}");
            }

            GUILayout.Space(6);
            EditorGUILayout.LabelField("Locks activos", EditorStyles.boldLabel);

            bool anyLocks = false;
            foreach (var kv in EZCollabState.lockedObjects)
            {
                if (!EZCollabState.guidToObject.TryGetValue(kv.Key, out var go) || go == null) continue;
                string ownerName = EZCollabState.GetLockOwnerName(kv.Key) ?? kv.Value;
                string marker = kv.Value == EZCollabState.localPeerId ? " (tú)" : "";
                EditorGUILayout.LabelField($"  {go.name} → {ownerName}{marker}", EditorStyles.miniLabel);
                anyLocks = true;
            }

            if (!anyLocks)
                EditorGUILayout.LabelField("Ninguno", EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Stop session"))
            {
                if (EditorUtility.DisplayDialog("EZCollab", "¿Terminar la sesión?", "Sí", "Cancelar"))
                    EZCollabSession.Stop();
            }

            GUILayout.Space(4);
        }
    }
}
