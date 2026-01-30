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

    // Variables de sesión
    private string currentLobbyId;
    private string playerId;

    // Variables de Red
    private NetworkDriver driver;
    private NativeList<NetworkConnection> connections;
    private RelayServerData relayServerData;

    void OnEnable()
    {
        Instance = this;
        // IMPORTANTE: Nos enganchamos al update del editor directamente
#if UNITY_EDITOR
        EditorApplication.update -= NetworkTick; // Limpieza preventiva
        EditorApplication.update += NetworkTick;
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= NetworkTick;
#endif
        _ = Shutdown();
    }

    // --- 1. INICIALIZACIÓN ---
    public async Task<bool> InitializeServices()
    {
        if (UnityServices.State == ServicesInitializationState.Initialized) return true;

        InitializationOptions options = new InitializationOptions();
        // ID Aleatorio para evitar conflictos de "Usuario duplicado"
        string randomProfile = "EditorUser_" + UnityEngine.Random.Range(1000, 999999);
        options.SetProfile(randomProfile);

        try
        {
            await UnityServices.InitializeAsync(options);

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            playerId = AuthenticationService.Instance.PlayerId;
            Debug.Log($"<color=cyan>[Collab] Servicios Listos. Perfil: {randomProfile}</color>");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Collab] Error Init: {e.Message}");
            return false;
        }
    }

    // --- 2. CREAR SESIÓN (HOST) ---
    public async Task<string> CreateSession()
    {
        if (!await InitializeServices()) return null;

        try
        {
            // 1. Crear Relay
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MAX_PLAYERS);
            string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

            // 2. Configurar Transporte
            relayServerData = new RelayServerData(allocation, "dtls");
            BindTransport(relayServerData, true);

            // 3. Crear Lobby
            CreateLobbyOptions options = new CreateLobbyOptions();
            options.Data = new Dictionary<string, DataObject> {
                { "RelayJoinCode", new DataObject(DataObject.VisibilityOptions.Member, joinCode) }
            };

            Lobby lobby = await LobbyService.Instance.CreateLobbyAsync(LOBBY_NAME, MAX_PLAYERS, options);
            currentLobbyId = lobby.Id;
            CurrentLobbyCode = lobby.LobbyCode;

            IsConnected = true;
            Debug.Log($"<color=green>[HOST] Sala creada: {CurrentLobbyCode}</color>");

            // TRUCO: Forzamos un repintado inmediato para evitar congelación
#if UNITY_EDITOR
            EditorApplication.QueuePlayerLoopUpdate();
#endif
            return CurrentLobbyCode;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error creando sesión: {e.Message}");
            await Shutdown();
            return null;
        }
    }

    // --- 3. UNIRSE A SESIÓN (CLIENTE) ---
    public async Task<bool> JoinSession(string lobbyCode)
    {
        await Shutdown(); // Limpieza preventiva
        if (!await InitializeServices()) return false;

        try
        {
            Debug.Log($"Buscando sala {lobbyCode}...");
            Lobby lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode);

            currentLobbyId = lobby.Id;
            CurrentLobbyCode = lobby.LobbyCode;

            string relayJoinCode = lobby.Data["RelayJoinCode"].Value;

            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayJoinCode);
            relayServerData = new RelayServerData(joinAllocation, "dtls");
            BindTransport(relayServerData, false);

            IsConnected = true;
            Debug.Log("<color=green>[CLIENTE] ¡Conectado!</color>");

#if UNITY_EDITOR
            EditorApplication.QueuePlayerLoopUpdate();
#endif
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error uniéndose: {e.Message}");
            await Shutdown();
            return false;
        }
    }

    // --- 4. LÓGICA DE RED (EL CORAZÓN) ---
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
            if (driver.Bind(NetworkEndPoint.AnyIpv4) != 0) Debug.LogError("Host Bind Falló");
            else driver.Listen();
        }
        else
        {
            driver.Bind(NetworkEndPoint.AnyIpv4);
            driver.Connect(relayData.Endpoint);
        }
    }

    // Esta función se ejecuta CADA FRAME DEL EDITOR
    private void NetworkTick()
    {
        // Si no estamos conectados o el driver no existe, no hacemos nada
        if (!IsConnected || !driver.IsCreated) return;

        // IMPORTANTE: Obligamos a Unity a redibujar y procesar eventos
        // Esto evita que la conexión muera por inactividad
#if UNITY_EDITOR
        if (!EditorApplication.isPlaying)
        {
            EditorApplication.QueuePlayerLoopUpdate();
        }
#endif

        driver.ScheduleUpdate().Complete();
        CleanConnections();
        AcceptNewConnections();
        ProcessMessages();
    }

    private void ProcessMessages()
    {
        DataStreamReader stream;
        for (int i = 0; i < connections.Length; i++)
        {
            if (!connections[i].IsCreated) continue;

            NetworkEvent.Type cmd;
            while ((cmd = driver.PopEventForConnection(connections[i], out stream)) != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Data)
                {
                    var rawString = stream.ReadFixedString4096();
                    string json = rawString.ToString();

                    if (SceneSyncManager.Instance != null)
                    {
                        var data = JsonUtility.FromJson<SceneChangeData>(json);
                        SceneSyncManager.Instance.ApplyChange(data);
                    }
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    Debug.Log("Desconexión detectada.");
                    connections[i] = default(NetworkConnection);
                }
            }
        }
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

    private void CleanConnections()
    {
        for (int i = 0; i < connections.Length; i++)
        {
            if (!connections[i].IsCreated)
            {
                connections.RemoveAtSwapBack(i); --i;
            }
        }
    }

    private void AcceptNewConnections()
    {
        NetworkConnection c;
        while ((c = driver.Accept()) != default(NetworkConnection))
        {
            connections.Add(c);
        }
    }

    public async Task Shutdown()
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