using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class CollabNetworkManager : MonoBehaviour
{
    public static CollabNetworkManager Instance;

    // Tu URL de Firebase (se pondrá desde la ventana)
    public string databaseURL = "";
    public string sessionID = "default_session";
    public bool IsConnected { get; private set; } = false;

    // Control de tiempo para no saturar internet
    private double lastUpdate = 0;
    private float syncInterval = 0.2f; // Sincroniza 5 veces por segundo (suficiente para editor)

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
        // Limpiamos la URL por si tiene barras al final
        if (url.EndsWith("/")) url = url.Substring(0, url.Length - 1);

        databaseURL = url;
        sessionID = session;
        IsConnected = true;

        Debug.Log($"<color=cyan>[Firebase] Conectado a sala: {session}</color>");

        // Al conectar, limpiamos la base de datos vieja para empezar limpio (opcional)
        // CleanDatabase(); 
    }

    public void Shutdown()
    {
        IsConnected = false;
        Debug.Log("[Firebase] Desconectado.");
    }

    // --- BUCLE PRINCIPAL (Funciona en EDIT MODE) ---
    private void NetworkLoop()
    {
        if (!IsConnected || string.IsNullOrEmpty(databaseURL)) return;

        // Comprobamos si toca actualizar (polling)
        double time = EditorApplication.timeSinceStartup;
        if (time - lastUpdate > syncInterval)
        {
            lastUpdate = time;
            // Descargamos cambios
            DownloadChanges();
        }
    }

    // --- ENVIAR DATOS (SUBIDA) ---
    public void BroadcastData(string json)
    {
        if (!IsConnected) return;

        // Parseamos para sacar el ID del objeto y guardarlo individualmente
        // Esto evita sobrescribir todo el JSON gigante
        SceneChangeData data = JsonUtility.FromJson<SceneChangeData>(json);

        string endpoint = $"{databaseURL}/{sessionID}/objects/{data.objectID}.json";

        // Usamos PUT para actualizar ese objeto específico
        StartCoroutine(PutRequest(endpoint, json));
    }

    IEnumerator PutRequest(string url, string bodyJson)
    {
        using (UnityWebRequest request = UnityWebRequest.Put(url, bodyJson))
        {
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // Debug.LogError($"Error subida: {request.error}");
                // Silenciamos errores comunes para no saturar
            }
        }
    }

    // --- RECIBIR DATOS (BAJADA) ---
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
                    ProcessFirebaseJSON(json);
                }
            }
        }
    }

    // Firebase devuelve un JSON raro: {"id1":{data}, "id2":{data}}. Hay que limpiarlo.
    void ProcessFirebaseJSON(string json)
    {
        // Truco rápido: En lugar de usar librerías JSON externas complejas,
        // vamos a envolver esto para que Unity pueda leerlo, o iterar manualmente.

        // Como Unity JsonUtility es muy básico y no maneja diccionarios,
        // vamos a hacer un parseo muy simple basado en corchetes.
        // NOTA: Para producción usaríamos Newtonsoft.Json, pero aquí queremos cero dependencias.

        // Si el SceneSyncManager existe, le pasamos los datos
        if (SceneSyncManager.Instance != null)
        {
            // Este es un hack sucio pero funcional para evitar instalar paquetes:
            // Dividimos el JSON por objetos.
            // Firebase devuelve: {"ObjID_1": {...contenido...}, "ObjID_2": {...}}

            // 1. Quitamos corchete inicial y final
            if (json.Length < 5) return;
            string clean = json.Substring(1, json.Length - 2);

            // 2. Buscamos patrones de objetos
            // Esto requeriría un parser real. Para simplificar al máximo y que funcione YA:
            // Vamos a confiar en que el SceneSyncManager ignora su propio ID si no ha cambiado.

            // Solución robusta sin plugins:
            // Simplemente pasamos el JSON crudo al SyncManager y dejamos que él intente extraer info
            // O mejor: Modificamos el SyncManager para que no necesite un parser complejo.

            // *PARCHE TEMPORAL*: Solo funcionará bien si movemos de 1 en 1.
            // Para arreglar esto bien necesitamos un parser.
            // Pero espera... ¡Podemos usar "SimpleJSON" o similar!
            // O mejor aún, parseamos manualmente lo básico.

            // Vamos a intentar extraer objetos individuales buscando las llaves
            int depth = 0;
            string currentObj = "";
            bool insideObject = false;

            for (int i = 0; i < clean.Length; i++)
            {
                char c = clean[i];
                if (c == '{') { depth++; insideObject = true; }
                if (c == '}') { depth--; }

                if (insideObject) currentObj += c;

                if (depth == 0 && insideObject)
                {
                    // Fin de un objeto json
                    try
                    {
                        SceneChangeData data = JsonUtility.FromJson<SceneChangeData>(currentObj);
                        SceneSyncManager.Instance.ApplyChange(data);
                    }
                    catch { }

                    currentObj = "";
                    insideObject = false;
                    // Saltamos comas y comillas hasta el siguiente {
                }
            }
        }
    }

    // Helper para lanzar corrutinas en modo editor
    public new void StartCoroutine(IEnumerator routine)
    {
#if UNITY_EDITOR
        // Usamos una implementación dummy muy simple para ejecutar el IEnumerator paso a paso
        EditorParallelRun(routine);
#endif
    }

    // Ejecutor de "Corrutinas" para Editor sin Play Mode
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