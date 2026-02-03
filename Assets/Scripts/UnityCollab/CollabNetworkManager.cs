using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;


#if UNITY_EDITOR
using UnityEditor;
#endif

// Clase auxiliar para deserializar el diccionario que retorna Firebase con POST
// Firebase retorna: { "-LxAbCdEfG": { ...dato... }, "-LxAbCdEfH": { ...dato... } }
// Este wrapper permite parsear eso como un Dictionary<string, SceneChangeData>
[Serializable]
public class FirebaseEntryWrapper
{
    // Mapa de key autogenerada de Firebase -> dato
    // No podemos usar Dictionary directamente con JsonUtility, así que parseamos manualmente
    // pero de forma robusta usando JsonUtility para cada entry individual
}

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
                // Descomenta para ver el JSON crudo de Firebase:
                // Debug.Log($"[RAW RECV] {request.downloadHandler.text}");
                ProcessServerData(request.downloadHandler.text);
            }
        }
    }

    /// <summary>
    /// Firebase con POST retorna un objeto así:
    /// {
    ///   "-LxKey1": { "type":"create", "objectID":"abc", "value":"1,2,3|0,0,0|1,1,1", "prefabGUID":"PRIMITIVE:Cube", "timestamp":123 },
    ///   "-LxKey2": { "type":"transform", "objectID":"abc", "value":"{\"x\":5,\"y\":2,\"z\":3}", ... }
    /// }
    /// 
    /// El problema anterior: se hacía split por '{"type"' lo cual rompía el JSON
    /// y luego se recortaba el último '}' — resultando en campos perdidos.
    /// 
    /// Este nuevo método extrae cada entry individual robustamente usando
    /// indexOf para encontrar los braces balanceados, y luego usa JsonUtility
    /// para parsear cada SceneChangeData individual.
    /// </summary>
    private void ProcessServerData(string json)
    {
        if (string.IsNullOrEmpty(json) || json == "null") return;
        if (SceneSyncManager.Instance == null) return;

        // El JSON exterior es { "key1": {...}, "key2": {...} }
        // Necesitamos extraer cada valor {...} individualmente
        var entries = ExtractFirebaseEntries(json);

        foreach (var entryJson in entries)
        {
            try
            {
                // Parsear con JsonUtility — esto resuelve el problema de root
                SceneChangeData data = JsonUtility.FromJson<SceneChangeData>(entryJson);

                if (string.IsNullOrEmpty(data.objectID)) continue;

                // Debug log para ver qué se está parseando:
                //Debug.Log($"<color=orange>[PARSER] Tipo:{data.type} | ID:{data.objectID} | Value:'{data.value}' | Prefab:'{data.prefabGUID}'</color>");

                Vector3? pos = null;
                Vector3? rot = null;
                Vector3? scl = null;
                string mat = null;

                if (data.type == "create")
                {
                    // El value tiene formato "x,y,z|rx,ry,rz|sx,sy,sz"
                    if (!string.IsNullOrEmpty(data.value))
                    {
                        string[] parts = data.value.Split('|');
                        if (parts.Length >= 3)
                        {
                            pos = StringToVector3(parts[0]);
                            rot = StringToVector3(parts[1]);
                            scl = StringToVector3(parts[2]);
                            //Debug.Log($"<color=green>[PARSER OK] P:{pos} R:{rot} S:{scl}</color>");
                        }
                        else
                        {
                            //Debug.LogError($"[PARSER ERROR] El payload no tiene 3 partes separadas por '|': '{data.value}'");
                        }
                    }
                }
                else if (data.type == "transform" || data.type == "rotation" || data.type == "scale")
                {
                    // El value aquí es un JSON serializado de SerializableVector3: {"x":1,"y":2,"z":3}
                    Vector3? val = null;
                    if (!string.IsNullOrEmpty(data.value))
                    {
                        try
                        {
                            SerializableVector3 sv = JsonUtility.FromJson<SerializableVector3>(data.value);
                            val = sv.ToVector3();
                        }
                        catch { /* val stays null */ }
                    }

                    if (data.type == "transform") pos = val;
                    else if (data.type == "rotation") rot = val;
                    else if (data.type == "scale") scl = val;
                }
                else if (data.type == "material")
                {
                    mat = data.value;
                }

                SceneSyncManager.Instance.ApplyFullState(data.objectID, pos, rot, scl, mat, data.prefabGUID);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PARSER EXCEPTION] Error procesando entrada: {e.Message}\nJSON: {entryJson}");
            }
        }
    }

    /// <summary>
    /// Extrae cada valor JSON individual del objeto raíz de Firebase.
    /// Maneja los braces balanceados correctamente para no cortar datos.
    /// </summary>
    private List<string> ExtractFirebaseEntries(string json)
    {
        var results = new List<string>();
        // Buscar cada '{' que corresponde a un valor dentro del objeto raíz
        // El raíz es { "key": { ... }, "key": { ... } }
        // Así que buscamos el primer '{' (el raíz), y luego los '{' internos

        int depth = 0;
        int entryStart = -1;

        for (int i = 0; i < json.Length; i++)
        {
            char c = json[i];

            // Saltar strings para no contar braces dentro de valores string
            if (c == '"')
            {
                i++; // saltar la comilla de apertura
                while (i < json.Length && json[i] != '"')
                {
                    if (json[i] == '\\') i++; // saltar escape
                    i++;
                }
                continue;
            }

            if (c == '{')
            {
                depth++;
                if (depth == 2) entryStart = i; // depth 1 = raíz, depth 2 = cada entry
            }
            else if (c == '}')
            {
                if (depth == 2 && entryStart != -1)
                {
                    // Extraer el substring completo de este entry
                    string entry = json.Substring(entryStart, i - entryStart + 1);
                    results.Add(entry);
                    entryStart = -1;
                }
                depth--;
            }
        }

        return results;
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