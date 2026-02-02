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

        // Forzamos una descarga inmediata al conectar
        StartCoroutine(DownloadData());
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
        // Descargamos todo el historial de la sesión
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

    // --- AQUÍ ESTÁ EL ARREGLO IMPORTANTE ---
    private void ProcessServerData(string json)
    {
        if (string.IsNullOrEmpty(json) || json == "null") return;
        if (SceneSyncManager.Instance == null) return;

        // Firebase devuelve un diccionario de IDs aleatorios {"-Nxyz": {...}, "-Nabc": {...}}
        // Vamos a recorrer el JSON "a lo bruto" buscando objetos

        // 1. Limpiamos un poco el JSON para iterar mejor
        var entries = json.Split(new string[] { "{\"type\"" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in entries)
        {
            // Reconstruimos el fragmento de JSON (le quitamos la llave de cierre del objeto padre si hace falta)
            string cleanEntry = "{\"type\"" + entry;
            if (cleanEntry.EndsWith("}")) cleanEntry = cleanEntry.Substring(0, cleanEntry.LastIndexOf("}"));

            try
            {
                // Extraemos datos básicos
                string id = ExtractString(cleanEntry, "\"objectID\":");
                string type = ExtractString(cleanEntry, "\"type\":");

                // === AQUÍ LA CLAVE: LEER EL PREFAB GUID/MESH ===
                string prefabInfo = ExtractString(cleanEntry, "\"prefabGUID\":");
                string matInfo = ExtractString(cleanEntry, "\"material\":"); // Por si acaso viaja aquí

                if (string.IsNullOrEmpty(id)) continue;

                // Extraemos valores transform
                Vector3? pos = ExtractVector3(cleanEntry, "\"value\":");
                // A veces el valor viene directo o dentro de un objeto, intentamos parsear lo que haya

                // Enviamos TODO al SceneSyncManager
                // Él decidirá: "Si el objeto 'id' no existe, uso 'prefabInfo' para crearlo"
                SceneSyncManager.Instance.ApplyFullState(
                    id,
                    (type == "transform") ? pos : null,
                    (type == "rotation") ? pos : null, // Reutilizamos pos porque el json es igual {x,y,z}
                    (type == "scale") ? pos : null,
                    matInfo,
                    prefabInfo // <--- ESTE DATO ES VITAL PARA CREAR OBJETOS NUEVOS
                );
            }
            catch (Exception e)
            {
                // Ignoramos errores de parseo puntuales
            }
        }
    }

    // --- PARSERS MANUALES (Más rápidos y seguros que JsonUtility para fragmentos sucios) ---

    private string ExtractString(string json, string key)
    {
        // Busca: "key":"VALOR"
        int startIdx = json.IndexOf(key);
        if (startIdx == -1) return null;

        startIdx += key.Length;

        // Saltamos espacios y comillas
        int valueStart = json.IndexOf("\"", startIdx) + 1;
        int valueEnd = json.IndexOf("\"", valueStart);

        if (valueStart == 0 || valueEnd == -1) return null;

        return json.Substring(valueStart, valueEnd - valueStart);
    }

    private Vector3? ExtractVector3(string json, string key)
    {
        // Busca el objeto JSON {x:1, y:2, z:3} después de la clave
        int startIdx = json.IndexOf(key);
        if (startIdx == -1) return null;

        // Buscamos el inicio del objeto valor '{'
        int braceStart = json.IndexOf("{", startIdx);
        int braceEnd = json.IndexOf("}", braceStart);

        if (braceStart == -1 || braceEnd == -1) return null;

        string vectorJson = json.Substring(braceStart, braceEnd - braceStart + 1);

        // Un poco sucio pero efectivo: limpiamos las comillas extra que mete Unity al serializar strings dentro de strings
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
            else if (routine.Current is WaitForSeconds ws)
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