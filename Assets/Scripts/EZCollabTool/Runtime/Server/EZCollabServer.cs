using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EZCollabTool
{
    public class EZCollabServer
    {
        TcpListener tcpListener;
        CancellationTokenSource cts;

        readonly ConcurrentDictionary<string, ConnectedClient> clients = new ConcurrentDictionary<string, ConnectedClient>();
        readonly EZCollabLockManager lockManager = new EZCollabLockManager();

        // Cola de mensajes para procesar en el hilo principal de Unity
        public readonly ConcurrentQueue<EZMessage> incomingMessages = new ConcurrentQueue<EZMessage>();

        public int clientCount => clients.Count;

        class ConnectedClient
        {
            public string peerId;
            public string peerName;
            public TcpClient client;
            public NetworkStream stream;
        }

        public void Start(int port)
        {
            cts = new CancellationTokenSource();
            tcpListener = new TcpListener(IPAddress.Any, port);
            tcpListener.Start();

            Task.Run(() => AcceptLoop(cts.Token));
        }

        public void Stop()
        {
            cts?.Cancel();
            tcpListener?.Stop();

            foreach (var client in clients.Values)
            {
                try { client.client.Close(); } catch { }
            }

            clients.Clear();
        }

        async Task AcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var client = await tcpListener.AcceptTcpClientAsync();
                    client.NoDelay = true; // Para baja latencia
                    _ = Task.Run(() => HandleClient(client, token));
                }
                catch (ObjectDisposedException) { break; } // El listener se cerró
                catch (Exception e) when (!token.IsCancellationRequested)
                {
                    Debug.LogError($"[EZCollab] Accept error: {e.Message}");
                }
            }
        }

        async Task<int> ReadExact(NetworkStream stream, byte[] buffer, int count, CancellationToken token)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer, totalRead, count - totalRead, token);
                if (read == 0) return 0;
                totalRead += read;
            }
            return totalRead;
        }

        async Task HandleClient(TcpClient tcpClient, CancellationToken token)
        {
            string peerId = null;
            string peerName = null;
            var stream = tcpClient.GetStream();
            var lengthBuffer = new byte[4];
            var buffer = new byte[32768];

            try
            {
                // El primer mensaje que manda el cliente es PeerJoined con su info
                int read = await ReadExact(stream, lengthBuffer, 4, token);
                if (read == 0) return;
                
                int length = BitConverter.ToInt32(lengthBuffer, 0);
                if (length > 10 * 1024 * 1024) throw new Exception("Payload too large");
                if (buffer.Length < length) buffer = new byte[length];

                read = await ReadExact(stream, buffer, length, token);
                if (read == 0) return;

                var joinMsg = EZMessage.FromBytes(buffer[..length]);

                if (joinMsg.type != MessageType.PeerJoined)
                {
                    tcpClient.Close();
                    return;
                }

                var peerInfo = joinMsg.GetPayload<PeerPayload>();
                peerId = peerInfo.peerId;
                peerName = peerInfo.peerName;

                var client = new ConnectedClient { peerId = peerId, peerName = peerName, client = tcpClient, stream = stream };
                clients[peerId] = client;

                // Mandar snapshot de escena al nuevo cliente
                await SendSnapshot(client, token);

                // Notificar a todos que entró alguien
                var joinedMsg = EZMessage.Create(MessageType.PeerJoined, EZCollabState.localPeerId, peerInfo);
                await BroadcastExcept(joinedMsg, peerId, token);

                incomingMessages.Enqueue(joinMsg);

                // Bucle de recepción
                while (tcpClient.Connected && !token.IsCancellationRequested)
                {
                    read = await ReadExact(stream, lengthBuffer, 4, token);
                    if (read == 0) break; // Desconectado

                    length = BitConverter.ToInt32(lengthBuffer, 0);
                    if (length > 10 * 1024 * 1024) throw new Exception("Payload too large");
                    if (buffer.Length < length) buffer = new byte[length];

                    read = await ReadExact(stream, buffer, length, token);
                    if (read == 0) break;

                    var msg = EZMessage.FromBytes(buffer[..length]);
                    await ProcessMessage(msg, peerId, token);
                }
            }
            catch (Exception e) when (!token.IsCancellationRequested)
            {
                if (!(e is System.IO.IOException))
                    Debug.LogWarning($"[EZCollab] Client error ({peerId}): {e.Message}");
            }
            finally
            {
                try { tcpClient.Close(); } catch { }
                if (peerId != null)
                    await HandleDisconnect(peerId, peerName, token);
            }
        }

        async Task ProcessMessage(EZMessage msg, string fromPeerId, CancellationToken token)
        {
            switch (msg.type)
            {
                case MessageType.LockRequest:
                {
                    var payload = msg.GetPayload<LockPayload>();
                    bool granted = lockManager.TryAcquire(payload.guid, fromPeerId);

                    var response = EZMessage.Create(
                        granted ? MessageType.LockGranted : MessageType.LockDenied,
                        EZCollabState.localPeerId,
                        new LockPayload { guid = payload.guid, peerId = fromPeerId, peerName = payload.peerName }
                    );

                    await SendTo(fromPeerId, response, token);

                    if (granted)
                    {
                        var broadcast = EZMessage.Create(MessageType.ObjectLocked, EZCollabState.localPeerId,
                            new LockPayload { guid = payload.guid, peerId = fromPeerId, peerName = payload.peerName });
                        await BroadcastExcept(broadcast, fromPeerId, token);
                        incomingMessages.Enqueue(broadcast);
                    }
                    break;
                }

                case MessageType.LockRelease:
                {
                    var payload = msg.GetPayload<LockPayload>();
                    lockManager.Release(payload.guid, fromPeerId);

                    var unlocked = EZMessage.Create(MessageType.ObjectUnlocked, EZCollabState.localPeerId,
                        new LockPayload { guid = payload.guid, peerId = fromPeerId });
                    await BroadcastExcept(unlocked, fromPeerId, token);
                    incomingMessages.Enqueue(unlocked);
                    break;
                }

                case MessageType.TransformDelta:
                case MessageType.ComponentDelta:
                case MessageType.CreateObject:
                case MessageType.DestroyObject:
                {
                    // Rebroadcast a todos los demás y encolar para aplicar localmente en Unity
                    await BroadcastExcept(msg, fromPeerId, token);
                    incomingMessages.Enqueue(msg);
                    break;
                }

                case MessageType.Heartbeat:
                    break;
            }
        }

        async Task HandleDisconnect(string peerId, string peerName, CancellationToken token)
        {
            clients.TryRemove(peerId, out _);

            var releasedGuids = lockManager.ReleaseAll(peerId);
            foreach (var guid in releasedGuids)
            {
                var unlocked = EZMessage.Create(MessageType.ObjectUnlocked, EZCollabState.localPeerId,
                    new LockPayload { guid = guid, peerId = peerId });
                await BroadcastAll(unlocked, token);
                incomingMessages.Enqueue(unlocked);
            }

            var leftMsg = EZMessage.Create(MessageType.PeerLeft, EZCollabState.localPeerId,
                new PeerPayload { peerId = peerId, peerName = peerName });
            await BroadcastAll(leftMsg, token);
            incomingMessages.Enqueue(leftMsg);
        }

        async Task SendSnapshot(ConnectedClient client, CancellationToken token)
        {
            var snapshot = EZSceneSerializer.CaptureScene();
            var msg = EZMessage.Create(MessageType.SceneSnapshot, EZCollabState.localPeerId, snapshot);
            await SendInternal(client, msg, token);
        }

        async Task SendInternal(ConnectedClient client, EZMessage msg, CancellationToken token)
        {
            try
            {
                var bytes = msg.ToBytes();
                var lengthBytes = BitConverter.GetBytes(bytes.Length);
                await client.stream.WriteAsync(lengthBytes, 0, 4, token);
                await client.stream.WriteAsync(bytes, 0, bytes.Length, token);
            }
            catch { }
        }

        async Task SendTo(string peerId, EZMessage msg, CancellationToken token)
        {
            if (!clients.TryGetValue(peerId, out var client)) return;
            await SendInternal(client, msg, token);
        }

        async Task BroadcastExcept(EZMessage msg, string excludePeerId, CancellationToken token)
        {
            var bytes = msg.ToBytes();
            var lengthBytes = BitConverter.GetBytes(bytes.Length);
            foreach (var kv in clients)
            {
                if (kv.Key == excludePeerId) continue;
                try
                {
                    await kv.Value.stream.WriteAsync(lengthBytes, 0, 4, token);
                    await kv.Value.stream.WriteAsync(bytes, 0, bytes.Length, token);
                }
                catch { }
            }
        }

        async Task BroadcastAll(EZMessage msg, CancellationToken token)
        {
            var bytes = msg.ToBytes();
            var lengthBytes = BitConverter.GetBytes(bytes.Length);
            foreach (var kv in clients)
            {
                try
                {
                    await kv.Value.stream.WriteAsync(lengthBytes, 0, 4, token);
                    await kv.Value.stream.WriteAsync(bytes, 0, bytes.Length, token);
                }
                catch { }
            }
        }
    }
}
