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

    public string databaseURL = "";
    public string sessionID = "default_session";
    public bool IsConnected { get; private set; } = false;

    private float syncInterval = 0.5f;

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

        if (SceneSyncManager.Instance != null) SceneSyncManager.Instance.UploadUnsyncedChanges();
        StartCoroutine(DownloadData());
    }

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

    IEnumerator PostData(string json)
    {
        string url = $"{databaseURL}/{sessionID}.json";
        using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
        {
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) Debug.LogError($"Error subiendo: {request.error}");
        }
    }

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
        string url = $"{databaseURL}/{sessionID}.json?orderBy=\"$key\"&limitToLast=20";
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                // --- DEBUG LOG CRÍTICO ---
                // Descomenta esto si quieres ver TODO lo que llega de Firebase (puede ser mucho texto)
                // Debug.Log($"[RAW RECV] {request.downloadHandler.text}");
                // -------------------------
                ProcessServerData(request.downloadHandler.text);
            }
        }
    }

    private void ProcessServerData(string json)
    {
        if (string.IsNullOrEmpty(json) || json == "null") return;
        if (SceneSyncManager.Instance == null) return;

        var entries = json.Split(new string[] { "{\"type\"" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in entries)
        {
            string cleanEntry = "{\"type\"" + entry;
            if (cleanEntry.EndsWith("}")) cleanEntry = cleanEntry.Substring(0, cleanEntry.LastIndexOf("}"));

            try
            {
                string id = ExtractString(cleanEntry, "\"objectID\":");
                string type = ExtractString(cleanEntry, "\"type\":");
                string prefabInfo = ExtractString(cleanEntry, "\"prefabGUID\":");
                string matInfo = ExtractString(cleanEntry, "\"material\":");

                if (string.IsNullOrEmpty(id)) continue;

                Vector3? pos = null;
                Vector3? rot = null;
                Vector3? scl = null;

                if (type == "create")
                {
                    string rawPayload = ExtractString(cleanEntry, "\"value\":");

                    // --- DEBUG LOG CRÍTICO ---
                    Debug.Log($"<color=orange>[PARSER] Procesando CREATE para ID:{id}. Payload raw: '{rawPayload}'</color>");
                    // -------------------------

                    if (!string.IsNullOrEmpty(rawPayload))
                    {
                        string[] parts = rawPayload.Split('|');
                        if (parts.Length >= 3)
                        {
                            pos = StringToVector3(parts[0]);
                            rot = StringToVector3(parts[1]);
                            scl = StringToVector3(parts[2]);
                            // --- DEBUG LOG CRÍTICO ---
                            Debug.Log($"<color=orange>[PARSER OK] P:{pos} R:{rot} S:{scl}</color>");
                            // -------------------------
                        }
                        else
                        {
                            Debug.LogError($"[PARSER ERROR] El payload no tiene 3 partes separadas por '|': {rawPayload}");
                        }
                    }
                }
                else
                {
                    Vector3? val = ExtractVector3(cleanEntry, "\"value\":");
                    if (type == "transform") pos = val;
                    if (type == "rotation") rot = val;
                    if (type == "scale") scl = val;
                }

                SceneSyncManager.Instance.ApplyFullState(id, pos, rot, scl, matInfo, prefabInfo);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PARSER EXCEPTION] Error procesando entrada: {e.Message}");
            }
        }
    }

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
        try { return JsonUtility.FromJson<SerializableVector3>(vectorJson).ToVector3(); }
        catch { return null; }
    }

    private Vector3? StringToVector3(string s)
    {
        try
        {
            string[] nums = s.Split(',');
            if (nums.Length < 3) return null;
            return new Vector3(
                float.Parse(nums[0], System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(nums[1], System.Globalization.CultureInfo.InvariantCulture),
                float.Parse(nums[2], System.Globalization.CultureInfo.InvariantCulture)
            );
        }
        catch { return null; }
    }

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
            if (routine.Current is UnityWebRequestAsyncOperation op) { while (!op.isDone) await System.Threading.Tasks.Task.Delay(10); }
            else if (routine.Current is WaitForSeconds) { await System.Threading.Tasks.Task.Delay(500); }
            else { await System.Threading.Tasks.Task.Delay(10); }
        }
    }
}