using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class CollabNetworkManager : MonoBehaviour
{
    public static CollabNetworkManager Instance;

    public string databaseURL = ""; // Tu URL de Firebase
    public string sessionID = "default_session";
    public bool IsConnected { get; private set; } = false;

    private float syncInterval = 0.2f;

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
        Debug.Log($"<color=cyan>[Firebase] Conectado a {sessionID}.</color>");

        // 1. IMPORTANTE: Subir cambios offline antes de nada (Recuperado)
        if (SceneSyncManager.Instance != null)
        {
            SceneSyncManager.Instance.UploadUnsyncedChanges();
        }

        // 2. Descargar estado del servidor
        StartCoroutine(DownloadData());
    }

    // --- ESTA ES LA FUNCIÓN QUE FALTABA ---
    public void Shutdown()
    {
        IsConnected = false;
        Debug.Log("[Firebase] Desconectado.");
    }

    public void BroadcastData(string json)
    {
        if (!IsConnected) return;
        StartCoroutine(PostData(json));
    }

    // --- CORRUTINAS DE RED ---

    IEnumerator PostData(string json)
    {
        // Usamos POST para añadir a la lista de eventos en Firebase
        string url = $"{databaseURL}/{sessionID}.json";
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
                Debug.LogError($"Error subiendo: {request.error}");
        }
    }

    // --- BUCLE DE DESCARGA (RECEPCIÓN) ---

    private double lastUpdate = 0;
    private void NetworkLoop()
    {
        if (!IsConnected) return;

        if (EditorApplication.timeSinceStartup - lastUpdate > syncInterval)
        {
            lastUpdate = EditorApplication.timeSinceStartup;
            StartCoroutine(DownloadData());
        }
    }

    IEnumerator DownloadData()
    {
        // Descargamos los últimos 20 eventos para mantener sincronía
        string url = $"{databaseURL}/{sessionID}.json?orderBy=\"$key\"&limitToLast=20";

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                ProcessServerData(request.downloadHandler.text);
            }
        }
    }

    // --- PROCESADOR DE DATOS INTELIGENTE ---
    private void ProcessServerData(string json)
    {
        if (string.IsNullOrEmpty(json) || json == "null") return;
        if (SceneSyncManager.Instance == null) return;

        // Limpiamos un poco el JSON para iterar mejor
        var entries = json.Split(new string[] { "{\"type\"" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in entries)
        {
            string cleanEntry = "{\"type\"" + entry;
            if (cleanEntry.EndsWith("}")) cleanEntry = cleanEntry.Substring(0, cleanEntry.LastIndexOf("}"));

            try
            {
                // Extraemos datos básicos
                string id = ExtractString(cleanEntry, "\"objectID\":");
                string type = ExtractString(cleanEntry, "\"type\":");

                // Extraemos INFO DE CREACIÓN (Prefab/Mesh)
                string prefabInfo = ExtractString(cleanEntry, "\"prefabGUID\":");
                string matInfo = ExtractString(cleanEntry, "\"material\":");

                if (string.IsNullOrEmpty(id)) continue;

                // Extraemos valores transform
                Vector3? pos = ExtractVector3(cleanEntry, "\"value\":");

                // Enviamos TODO al SceneSyncManager
                SceneSyncManager.Instance.ApplyFullState(
                    id,
                    (type == "transform") ? pos : null,
                    (type == "rotation") ? pos : null,
                    (type == "scale") ? pos : null,
                    matInfo,
                    prefabInfo // <--- Vital para crear objetos nuevos
                );
            }
            catch (Exception)
            {
                // Ignoramos errores de parseo puntuales
            }
        }
    }

    // --- PARSERS MANUALES ---

    private string ExtractString(string json, string key)
    {
        int startIdx = json.IndexOf(key);
        if (startIdx == -1) return null;

        startIdx += key.Length;

        int valueStart = json.IndexOf("\"", startIdx) + 1;
        int valueEnd = json.IndexOf("\"", valueStart);

        if (valueStart == 0 || valueEnd == -1) return null;

        return json.Substring(valueStart, valueEnd - valueStart);
    }

    private Vector3? ExtractVector3(string json, string key)
    {
        int startIdx = json.IndexOf(key);
        if (startIdx == -1) return null;

        int braceStart = json.IndexOf("{", startIdx);
        int braceEnd = json.IndexOf("}", braceStart);

        if (braceStart == -1 || braceEnd == -1) return null;

        string vectorJson = json.Substring(braceStart, braceEnd - braceStart + 1);
        vectorJson = vectorJson.Replace("\\", "");

        try
        {
            return JsonUtility.FromJson<SerializableVector3>(vectorJson).ToVector3();
        }
        catch
        {
            return null;
        }
    }

    // Helpers Editor Async
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
            else if (routine.Current is WaitForSeconds)
            {
                await System.Threading.Tasks.Task.Delay(500);
            }
            else
            {
                await System.Threading.Tasks.Task.Delay(10);
            }
        }
    }
}