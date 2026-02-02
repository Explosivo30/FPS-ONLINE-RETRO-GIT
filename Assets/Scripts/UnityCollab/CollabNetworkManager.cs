using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Text.RegularExpressions; // <--- IMPORTANTE: Necesario para leer el nuevo JSON

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class CollabNetworkManager : MonoBehaviour
{
    public static CollabNetworkManager Instance;

    public string databaseURL = "";
    public string sessionID = "default_session";
    public bool IsConnected { get; private set; } = false;

    private double lastUpdate = 0;
    private float syncInterval = 0.5f; // Comprobamos cada 0.5s

    void OnEnable()
    {
        Instance = this;
#if UNITY_EDITOR
        EditorApplication.update += NetworkLoop;
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= NetworkLoop;
#endif
        IsConnected = false;
    }

    public void ConnectToFirebase(string url, string session)
    {
        if (url.EndsWith("/")) url = url.Substring(0, url.Length - 1);
        databaseURL = url;
        sessionID = session;
        IsConnected = true;
        Debug.Log($"<color=cyan>[Firebase] Conectado.</color>");

        // 1. Subir cambios offline antes de nada
        if (SceneSyncManager.Instance != null)
        {
            SceneSyncManager.Instance.UploadUnsyncedChanges();
        }

        // 2. Descargar el estado del mundo
        DownloadChanges();
    }

    public void Shutdown()
    {
        IsConnected = false;
        Debug.Log("[Firebase] Desconectado.");
    }

    private void NetworkLoop()
    {
        if (!IsConnected || string.IsNullOrEmpty(databaseURL)) return;

        double time = EditorApplication.timeSinceStartup;
        if (time - lastUpdate > syncInterval)
        {
            lastUpdate = time;
            DownloadChanges();
        }
    }

    // --- ENVIAR DATOS (Ahora usa sub-carpetas para no borrar datos previos) ---
    public void BroadcastData(string json)
    {
        if (!IsConnected) return;

        SceneChangeData data = JsonUtility.FromJson<SceneChangeData>(json);

        string subPath = "";
        string bodyToSend = "";

        // Clasificamos para enviar a la carpeta correcta en Firebase
        if (data.type == "transform")
        {
            subPath = "pos";
            bodyToSend = data.value;
        }
        else if (data.type == "rotation")
        {
            subPath = "rot";
            bodyToSend = data.value;
        }
        else if (data.type == "scale")
        {
            subPath = "scl";
            bodyToSend = data.value;
        }
        else if (data.type == "material")
        {
            subPath = "mat";
            bodyToSend = "\"" + data.value + "\""; // Strings necesitan comillas
        }

        // El Prefab ID siempre se intenta actualizar por si es nuevo
        if (!string.IsNullOrEmpty(data.prefabGUID))
        {
            string prefabEndpoint = $"{databaseURL}/{sessionID}/objects/{data.objectID}/prefab.json";
            StartCoroutine(PutRequest(prefabEndpoint, "\"" + data.prefabGUID + "\""));
        }

        // Enviamos el dato principal
        if (subPath != "")
        {
            string endpoint = $"{databaseURL}/{sessionID}/objects/{data.objectID}/{subPath}.json";
            StartCoroutine(PutRequest(endpoint, bodyToSend));
        }
    }

    IEnumerator PutRequest(string url, string bodyJson)
    {
        using (UnityWebRequest request = UnityWebRequest.Put(url, bodyJson))
        {
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();
        }
    }

    // --- RECIBIR DATOS ---
    void DownloadChanges()
    {
        string endpoint = $"{databaseURL}/{sessionID}/objects.json";
        StartCoroutine(GetRequest(endpoint));
    }

    IEnumerator GetRequest(string url)
    {
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string json = request.downloadHandler.text;
                if (json != "null" && !string.IsNullOrEmpty(json))
                {
                    ProcessFullSceneJSON(json);
                }
            }
        }
    }

    // --- PARSER INTELIGENTE (Sustituye al ApplyChange antiguo) ---
    void ProcessFullSceneJSON(string json)
    {
        if (SceneSyncManager.Instance == null) return;
        if (json.Length < 5) return;

        // Usamos Regex para buscar patrones: "ID_OBJETO": { ... DATOS ... }
        // Esto lee el diccionario de Firebase sin necesitar librerías externas
        var matches = Regex.Matches(json, "\"([a-zA-Z0-9_-]+)\":\\s*(\\{.*?\\}(?=\\s*,\\s*\"|\\s*\\}$))", RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            string objID = match.Groups[1].Value;   // El ID (ej: "Guid-1234")
            string content = match.Groups[2].Value; // El contenido (ej: {"pos":..., "rot":...})

            // Extraemos los trocitos de datos de dentro
            Vector3? pos = ExtractVector3(content, "\"pos\":");
            Vector3? rot = ExtractVector3(content, "\"rot\":");
            Vector3? scl = ExtractVector3(content, "\"scl\":");
            string mat = ExtractString(content, "\"mat\":");
            string prefab = ExtractString(content, "\"prefab\":");

            // --- AQUÍ ESTÁ LA MAGIA: Llamamos a la nueva función ---
            SceneSyncManager.Instance.ApplyFullState(objID, pos, rot, scl, mat, prefab);
        }
    }

    // --- Ayudantes para leer el texto JSON a mano ---
    Vector3? ExtractVector3(string json, string key)
    {
        int index = json.IndexOf(key);
        if (index == -1) return null;

        int start = json.IndexOf('{', index);
        int end = json.IndexOf('}', start);
        if (start == -1 || end == -1) return null;

        string vJson = json.Substring(start, end - start + 1);
        try
        {
            return JsonUtility.FromJson<SerializableVector3>(vJson).ToVector3();
        }
        catch { return null; }
    }

    string ExtractString(string json, string key)
    {
        int index = json.IndexOf(key);
        if (index == -1) return null;

        int startQuote = json.IndexOf('"', index + key.Length);
        if (startQuote == -1) return null;
        int endQuote = json.IndexOf('"', startQuote + 1);
        if (endQuote == -1) return null;

        return json.Substring(startQuote + 1, endQuote - startQuote - 1);
    }

    // Helper corrutinas editor
    public new void StartCoroutine(IEnumerator routine)
    {
#if UNITY_EDITOR
        EditorParallelRun(routine);
#endif
    }

    private async void EditorParallelRun(IEnumerator routine)
    {
        while (routine.MoveNext())
        {
            if (routine.Current is UnityWebRequestAsyncOperation op)
            {
                while (!op.isDone) await System.Threading.Tasks.Task.Delay(10);
            }
            else
            {
                await System.Threading.Tasks.Task.Delay(10);
            }
        }
    }
}