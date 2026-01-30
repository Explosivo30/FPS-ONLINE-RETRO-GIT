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

[ExecuteAlways] // Esto permite que funcione SIN Play Mode
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
        // Nos enganchamos al bucle del editor para funcionar sin Play
#if UNITY_EDITOR
        EditorApplication.update += UpdateNetworkLoop;
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= UpdateNetworkLoop;
#endif
        _ = Shutdown();
    }

    // --- 1. INICIALIZACIÓN ---
    public async Task InitializeServices()
    {
        if (UnityServices.State == ServicesInitializationState.Initialized) return;

        InitializationOptions options = new InitializationOptions();
        // Perfil aleatorio para evitar conflictos de "Usuario duplicado"
        string randomProfile = "Dev_" + UnityEngine.Random.Range(1000, 999999);
        options.SetProfile(randomProfile);

        try
        {
            await UnityServices.InitializeAsync(options);
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            playerId = AuthenticationService.Instance.PlayerId;
            Debug.Log($"<color=cyan>[MODO EDITOR] Conectado como: {randomProfile}</color>");
        }
        catch (Exception e)
        {
            Debug.LogError($"[Collab] Error Init: {e.Message}");
        }
    }

    // --- 2. CREAR SESIÓN ---
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
            return CurrentLobbyCode;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error creando: {e.Message}");
            await Shutdown();
            return null;
        }
    }

    // --- 3. UNIRSE A SESIÓN ---
    public async Task<bool> JoinSession(string lobbyCode)
    {
        await Shutdown();
        await InitializeServices();

        try
        {
            Lobby lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(lobbyCode);
            currentLobbyId = lobby.Id;
            CurrentLobbyCode = lobby.LobbyCode;

            string relayJoinCode = lobby.Data["RelayJoinCode"].Value;
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayJoinCode);

            relayServerData = new RelayServerData(joinAllocation, "dtls");
            BindTransport(relayServerData, false);

            IsConnected = true;
            Debug.Log("<color=green>¡CONECTADO EN MODO EDITOR!</color>");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error uniéndose: {e.Message}");
            await Shutdown();
            return false;
        }
    }

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
            if (driver.Bind(NetworkEndPoint.AnyIpv4) != 0) Debug.LogError("Bind falló");
            else driver.Listen();
        }
        else
        {
            driver.Bind(NetworkEndPoint.AnyIpv4);
            driver.Connect(relayData.Endpoint);
        }
    }

    // --- 4. BUCLE DE RED (IMPORTANTE PARA EDIT MODE) ---
    private void UpdateNetworkLoop()
    {
        if (!IsConnected || !driver.IsCreated) return;

        // [TRUCO CLAVE] 
        // Si estamos en el editor y NO estamos dando Play, obligamos a Unity 
        // a actualizarse constantemente para procesar la red.
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
                        // Procesamos el mensaje aunque estemos en Edit Mode
                        SceneSyncManager.Instance.ApplyChange(data: JsonUtility.FromJson<SceneChangeData>(json));
                    }
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
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