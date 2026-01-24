using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Collections;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class CollabNetworkManager : MonoBehaviour
{
    public static CollabNetworkManager Instance;

    private const string LOBBY_NAME = "CollabSession";
    private const int MAX_PLAYERS = 4;

    public bool IsConnected { get; private set; } = false;
    public string CurrentLobbyCode { get; private set; }
    private string currentLobbyId;
    private string playerId;

    private NetworkDriver driver;
    private NativeList<NetworkConnection> connections;
    private RelayServerData relayServerData;

    void OnEnable()
    {
        Instance = this;
#if UNITY_EDITOR
        EditorApplication.update += UpdateNetworkLoop;
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= UpdateNetworkLoop;
#endif
        Shutdown();
    }

    // --- 1. Inicialización ---
    public async Task InitializeServices()
    {
        if (UnityServices.State == ServicesInitializationState.Initialized) return;

        InitializationOptions options = new InitializationOptions();

        // TRUCO FINAL: Usamos un número aleatorio grande. 
        // Esto asegura que aunque uses la misma cuenta y el mismo PC, 
        // Unity te vea como un usuario distinto cada vez.
        string randomProfile = "User_" + UnityEngine.Random.Range(0, 999999).ToString();
        options.SetProfile(randomProfile);

        try
        {
            await UnityServices.InitializeAsync(options);

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            playerId = AuthenticationService.Instance.PlayerId;
            Debug.Log($"<color=cyan>[Collab] Autenticado. Perfil: {randomProfile} | ID: {playerId}</color>");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Collab] Error Init: {e.Message}");
        }
    }

    // --- 2. Crear Sesión (Host) ---
    public async Task<string> CreateSession()
    {
        await InitializeServices();

        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MAX_PLAYERS);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            relayServerData = new RelayServerData(allocation, "dtls");
            BindTransport(relayServerData, true);

            CreateLobbyOptions options = new CreateLobbyOptions();
            options.Data = new Dictionary<string, DataObject> {
                { "RelayJoinCode", new DataObject(DataObject.VisibilityOptions.Member, joinCode) }
            };

            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(LOBBY_NAME, MAX_PLAYERS, options);
            currentLobbyId = lobby.Id;
            CurrentLobbyCode = lobby.LobbyCode;

            IsConnected = true;

#if UNITY_EDITOR
            EditorApplication.QueuePlayerLoopUpdate();
#endif
            return CurrentLobbyCode;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error creando sesión: {e.Message}");
            Shutdown();
            return null;
        }
    }

    // --- 3. Unirse a Sesión (Cliente) ---
    public async Task<bool> JoinSession(string lobbyCode)
    {
        // PASO 1: Asegurarnos de que no estamos conectados de antes
        Shutdown();

        await InitializeServices();

        try
        {
            // PASO 2: Verificar si ya estamos en ese Lobby (por si acaso)
            if (!string.IsNullOrEmpty(currentLobbyId))
            {
                try { await LobbyService.Instance.RemovePlayerAsync(currentLobbyId, playerId); }
                catch { /* Ignoramos si falla al salir, es solo limpieza */ }
                currentLobbyId = null;
            }

            Debug.Log($"Intentando unirse a: {lobbyCode}...");
            Lobby lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode);

            currentLobbyId = lobby.Id;
            CurrentLobbyCode = lobby.LobbyCode;

            string relayJoinCode = lobby.Data["RelayJoinCode"].Value;

            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayJoinCode);
            relayServerData = new RelayServerData(joinAllocation, "dtls");
            BindTransport(relayServerData, false);

            IsConnected = true;
            Debug.Log("<color=green>CLIENTE: Conexión establecida con Relay.</color>");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error uniéndose: {e.Message}");
            // Si falla, reseteamos todo para que el usuario pueda volver a darle al botón sin errores
            Shutdown();
            return false;
        }
    }

    // --- 4. Lógica de Red ---
    private void BindTransport(RelayServerData relayData, bool isHost)
    {
        if (driver.IsCreated) driver.Dispose();
        if (connections.IsCreated) connections.Dispose();

        var settings = new NetworkSettings();
        settings.WithRelayParameters(ref relayData);

        driver = NetworkDriver.Create(settings);
        connections = new NativeList<NetworkConnection>(MAX_PLAYERS, Allocator.Persistent);

        if (isHost)
        {
            if (driver.Bind(NetworkEndPoint.AnyIpv4) != 0)
                Debug.LogError("Host Bind Falló");
            else
                driver.Listen();
        }
        else
        {
            driver.Bind(NetworkEndPoint.AnyIpv4);
            driver.Connect(relayData.Endpoint);
        }
    }

    private void UpdateNetworkLoop()
    {
        if (!IsConnected || !driver.IsCreated) return;

        driver.ScheduleUpdate().Complete();

        for (int i = 0; i < connections.Length; i++)
        {
            if (!connections[i].IsCreated)
            {
                connections.RemoveAtSwapBack(i);
                --i;
            }
        }

        NetworkConnection c;
        while ((c = driver.Accept()) != default(NetworkConnection))
        {
            connections.Add(c);
        }

        // --- PROCESAMIENTO DE MENSAJES (AQUÍ ESTABA EL ERROR) ---
        DataStreamReader stream;
        for (int i = 0; i < connections.Length; i++)
        {
            if (!connections[i].IsCreated) continue;

            NetworkEvent.Type cmd;
            while ((cmd = driver.PopEventForConnection(connections[i], out stream)) != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Data)
                {
                    // >>> CORRECCIÓN: Leemos directamente el valor devuelto <<<
                    var rawString = stream.ReadFixedString4096();
                    string json = rawString.ToString();

                    Debug.Log($"Mensaje recibido: {json}");

                    if (SceneSyncManager.Instance != null)
                    {
                        var data = JsonUtility.FromJson<SceneChangeData>(json);
                        SceneSyncManager.Instance.ApplyChange(data);
                    }

                   
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    Debug.Log("Usuario desconectado.");
                    connections[i] = default(NetworkConnection);
                }
            }
        }
    }

    void Update()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && IsConnected)
        {
            EditorApplication.QueuePlayerLoopUpdate();
        }
#endif
    }

    public void BroadcastData(string json)
    {
        if (!IsConnected || !driver.IsCreated) return;

        var fixedString = new FixedString4096Bytes(json);
        for (int i = 0; i < connections.Length; i++)
        {
            if (connections[i].IsCreated)
            {
                driver.BeginSend(connections[i], out var writer);
                writer.WriteFixedString4096(fixedString);
                driver.EndSend(writer);
            }
        }
    }

    public async void Shutdown()
    {
        IsConnected = false;
        if (driver.IsCreated) driver.Dispose();
        if (connections.IsCreated) connections.Dispose();

        if (!string.IsNullOrEmpty(currentLobbyId) && playerId != null)
        {
            try { await LobbyService.Instance.RemovePlayerAsync(currentLobbyId, playerId); }
            catch { }
        }
        currentLobbyId = null;
    }
}