#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class CollabToolWindow : EditorWindow
{
    string joinCode = "";
    string status = "Desconectado";
    Vector2 scrollPos;

    [MenuItem("Collab Tool/Panel de Control")]
    public static void ShowWindow()
    {
        GetWindow<CollabToolWindow>("Collab Tool");
    }

    void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        GUILayout.Label("Herramienta de Colaboración (Modo Editor)", EditorStyles.boldLabel);
        GUILayout.Space(10);

        // Estado visual
        if (status.Contains("HOST")) GUI.backgroundColor = Color.cyan;
        else if (status.Contains("CLIENTE")) GUI.backgroundColor = Color.green;
        else if (status.Contains("Error")) GUI.backgroundColor = Color.red;
        else GUI.backgroundColor = Color.white;

        GUILayout.Label($"Estado: {status}", EditorStyles.helpBox);
        GUI.backgroundColor = Color.white;

        GUILayout.Space(10);

        if (CollabNetworkManager.Instance == null)
        {
            EditorGUILayout.HelpBox("Sistema no detectado en la escena.", MessageType.Warning);
            if (GUILayout.Button("Inicializar Sistema", GUILayout.Height(30)))
            {
                GameObject go = new GameObject("_NetworkSystem");
                go.AddComponent<CollabNetworkManager>();
                go.AddComponent<SceneSyncManager>();
                Debug.Log("Sistema creado.");
            }
        }
        else
        {
            if (!CollabNetworkManager.Instance.IsConnected)
            {
                // SECCIÓN HOST
                GUILayout.Label("Opción A: Ser el Host (Crear)", EditorStyles.boldLabel);
                if (GUILayout.Button("Crear Sesión", GUILayout.Height(30)))
                {
                    CreateSession();
                }

                GUILayout.Space(15);

                // SECCIÓN CLIENTE
                GUILayout.Label("Opción B: Unirse (Cliente)", EditorStyles.boldLabel);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Código:", GUILayout.Width(50));
                joinCode = EditorGUILayout.TextField(joinCode, GUILayout.Height(20));
                GUILayout.EndHorizontal();

                if (GUILayout.Button("Unirse a Sesión", GUILayout.Height(30)))
                {
                    JoinSession();
                }
            }
            else
            {
                GUILayout.Space(10);
                if (GUILayout.Button("Desconectar", GUILayout.Height(30)))
                {
                    CollabNetworkManager.Instance.Shutdown();
                    status = "Desconectado";
                }

                GUILayout.Space(5);
                EditorGUILayout.HelpBox("¡Conectado! Mueve objetos en la escena para sincronizar.", MessageType.Info);

                if (!string.IsNullOrEmpty(CollabNetworkManager.Instance.CurrentLobbyCode))
                {
                    EditorGUILayout.TextField("Código de Sala:", CollabNetworkManager.Instance.CurrentLobbyCode);
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }

    // Funciones Async Wrapper para los botones
    async void CreateSession()
    {
        if (CollabNetworkManager.Instance == null) return;
        status = "Iniciando servicios...";

        string code = await CollabNetworkManager.Instance.CreateSession();

        if (!string.IsNullOrEmpty(code))
        {
            status = "CONECTADO (HOST)";
            GUIUtility.systemCopyBuffer = code;
            Debug.Log($"<color=green>Sala creada. Código {code} copiado.</color>");
        }
        else
        {
            status = "Error al crear (ver consola)";
        }
    }

    async void JoinSession()
    {
        if (CollabNetworkManager.Instance == null || string.IsNullOrEmpty(joinCode)) return;
        status = "Conectando...";

        bool success = await CollabNetworkManager.Instance.JoinSession(joinCode);

        if (success)
        {
            status = "CONECTADO (CLIENTE)";
        }
        else
        {
            status = "Error al unirse (ver consola)";
        }
    }
}
#endif