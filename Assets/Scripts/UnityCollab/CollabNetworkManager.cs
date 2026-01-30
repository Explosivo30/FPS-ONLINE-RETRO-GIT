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
        _ = Shutdown();
    }

    // --- 1. INICIALIZACIÓN (VERSIÓN ARREGLADA) ---
    public async Task InitializeServices()
    {
        if (UnityServices.State == ServicesInitializationState.Initialized) return;

        InitializationOptions options = new InitializationOptions();

        // --- CAMBIO CLAVE AQUÍ ---
        // Antes usabas el nombre de la carpeta. ESO ESTABA MAL.
        // Ahora usamos un número aleatorio. Esto arregla el "Player already in lobby".
        string randomProfile = "User_" + UnityEngine.Random.Range(1000, 999999);
        options.SetProfile(randomProfile);
        // -------------------------

        try
        {
            await UnityServices.InitializeAsync(options);

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            playerId = AuthenticationService.Instance.PlayerId;
            Debug.Log($"<color=cyan>[Collab] Conectado como: {randomProfile} (ID Único)</color>");
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
        await Shutdown(); // Limpieza preventiva para evitar error rojo
        await InitializeServices();

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
            Debug.Log("<color=green>¡CONECTADO CON ÉXITO!</color>");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error uniéndose: {e.Message}");
            await Shutdown();
            return false;
        }
    }

    // --- 4. RED Y TRANSPORTE ---
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

    private void UpdateNetworkLoop()
    {
        if (!IsConnected || !driver.IsCreated) return;

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
                    Debug.Log("Alguien se desconectó.");
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
        // Limpiamos agresivamente para evitar el error rojo de "Pending Events"
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