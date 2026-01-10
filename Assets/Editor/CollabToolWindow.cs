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

        GUILayout.Label("Herramienta de Colaboración", EditorStyles.boldLabel);
        GUILayout.Space(10);

        // Estado visual
        GUI.backgroundColor = status.Contains("Código") || status == "Conectado" ? Color.green : Color.white;
        GUILayout.Label($"Estado: {status}", EditorStyles.helpBox);
        GUI.backgroundColor = Color.white;

        GUILayout.Space(10);

        // --- SECCIÓN 1: INICIALIZACIÓN ---
        // Comprobamos si existe el Manager en la escena (ya no importa si es Play o Edit)
        if (CollabNetworkManager.Instance == null)
        {
            EditorGUILayout.HelpBox("No se detecta el sistema en la escena.", MessageType.Warning);
            if (GUILayout.Button("Inicializar Sistema", GUILayout.Height(30)))
            {
                // 1. Creamos UN solo objeto para todo el sistema
                GameObject go = new GameObject("_NetworkSystem");

                // 2. Le ponemos los DOS componentes necesarios
                go.AddComponent<CollabNetworkManager>(); // El Cartero
                go.AddComponent<SceneSyncManager>();     // El Cerebro

                // 3. (Opcional) Evita que se borre si cambias de escena, útil para managers
                // DontDestroyOnLoad(go); 

                // 4. Guardamos para que no desaparezca al cerrar Unity
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);

                Debug.Log("<color=green>Sistema inicializado: _NetworkSystem creado con ambos scripts.</color>");
            }
        }
        else
        {
            // --- SECCIÓN 2: CONTROLES DE SESIÓN ---
            GUILayout.Label("Eres el Host (Líder)", EditorStyles.label);
            if (GUILayout.Button("Crear Nueva Sesión", GUILayout.Height(40)))
            {
                CreateSession();
            }

            GUILayout.Space(20);
            GUILayout.Label("_____________________________", EditorStyles.centeredGreyMiniLabel);
            GUILayout.Space(20);

            GUILayout.Label("Eres un Cliente (Colaborador)", EditorStyles.label);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Código:", GUILayout.Width(50));
            joinCode = EditorGUILayout.TextField(joinCode, GUILayout.Height(20));
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Unirse a Sesión", GUILayout.Height(30)))
            {
                JoinSession();
            }

            GUILayout.Space(10);
            if (GUILayout.Button("Desconectar / Resetear", GUILayout.Height(20)))
            {
                CollabNetworkManager.Instance.Shutdown();
                status = "Desconectado";
            }
        }

        EditorGUILayout.EndScrollView();
    }

    async void CreateSession()
    {
        if (CollabNetworkManager.Instance == null) return;

        status = "Creando sala...";
        string code = await CollabNetworkManager.Instance.CreateSession();

        if (!string.IsNullOrEmpty(code))
        {
            status = $"Hospedando. Código: {code}";
            GUIUtility.systemCopyBuffer = code;
            Debug.Log($"<color=green>Sala creada. Código {code} copiado al portapapeles.</color>");
        }
        else
        {
            status = "Error al crear (mira la consola)";
        }
    }

    async void JoinSession()
    {
        if (CollabNetworkManager.Instance == null || string.IsNullOrEmpty(joinCode)) return;

        status = "Uniéndose...";
        bool success = await CollabNetworkManager.Instance.JoinSession(joinCode);

        if (success) status = "Conectado";
        else status = "Error al unirse";
    }
}
#endif