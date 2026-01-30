#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public class CollabToolWindow : EditorWindow
{
    // Persistencia para que no tengas que escribir la URL cada vez
    string firebaseURL
    {
        get { return EditorPrefs.GetString("Collab_FirebaseURL", ""); }
        set { EditorPrefs.SetString("Collab_FirebaseURL", value); }
    }

    string sessionName = "Sesion1";
    string status = "Desconectado";

    [MenuItem("Collab Tool/Panel Firebase")]
    public static void ShowWindow()
    {
        GetWindow<CollabToolWindow>("Collab Firebase");
    }

    void OnGUI()
    {
        GUILayout.Label("Conexión vía Firebase (Internet)", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUILayout.HelpBox("Pega aquí la URL de tu Firebase Realtime Database.\nDebe empezar por https:// y terminar en .firebasedatabase.app/", MessageType.Info);

        firebaseURL = EditorGUILayout.TextField("URL Base de Datos:", firebaseURL);
        sessionName = EditorGUILayout.TextField("Nombre Sala:", sessionName);

        GUILayout.Space(10);

        if (CollabNetworkManager.Instance == null)
        {
            if (GUILayout.Button("Inicializar Sistema"))
            {
                GameObject go = new GameObject("_NetworkSystem");
                go.AddComponent<CollabNetworkManager>();
                go.AddComponent<SceneSyncManager>();
            }
        }
        else
        {
            if (!CollabNetworkManager.Instance.IsConnected)
            {
                GUI.backgroundColor = Color.green;
                if (GUILayout.Button("CONECTAR AHORA", GUILayout.Height(40)))
                {
                    if (string.IsNullOrEmpty(firebaseURL))
                        EditorUtility.DisplayDialog("Error", "Falta la URL", "OK");
                    else
                    {
                        CollabNetworkManager.Instance.ConnectToFirebase(firebaseURL, sessionName);
                        status = "Conectado";
                    }
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("Desconectar", GUILayout.Height(30)))
                {
                    CollabNetworkManager.Instance.Shutdown();
                    status = "Desconectado";
                }
                GUI.backgroundColor = Color.white;

                GUILayout.Space(10);
                GUILayout.Label("¡Sincronización Activa!", EditorStyles.largeLabel);
                GUILayout.Label("Mueve objetos en la escena y verás.", EditorStyles.miniLabel);
            }
        }
    }
}
#endif