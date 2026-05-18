using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;

namespace EZCollabTool
{
    public class EZCollabServer
    {
        HttpListener httpListener;
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
            public WebSocket socket;
        }

        public void Start(int port)
        {
            cts = new CancellationTokenSource();
            httpListener = new HttpListener();
            httpListener.Prefixes.Add($"http://*:{port}/");
            httpListener.Start();

            Task.Run(() => AcceptLoop(cts.Token));
        }

        public void Stop()
        {
            cts?.Cancel();
            httpListener?.Stop();

            foreach (var client in clients.Values)
            {
                try { client.socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "server stopping", CancellationToken.None).Wait(); }
                catch { }
            }

            clients.Clear();
        }

        async Task AcceptLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var ctx = await httpListener.GetContextAsync();

                    if (!ctx.Request.IsWebSocketRequest)
                    {
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                        continue;
                    }

                    var wsCtx = await ctx.AcceptWebSocketAsync(null);
                    _ = Task.Run(() => HandleClient(wsCtx.WebSocket, token));
                }
                catch (Exception e) when (!token.IsCancellationRequested)
                {
                    Debug.LogError($"[EZCollab] Accept error: {e.Message}");
                }
            }
        }

        async Task HandleClient(WebSocket socket, CancellationToken token)
        {
            string peerId = null;
            string peerName = null;
            var buffer = new byte[32768];

            try
            {
                // El primer mensaje que manda el cliente es PeerJoined con su info
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                var joinMsg = EZMessage.FromBytes(buffer[..result.Count]);

                if (joinMsg.type != MessageType.PeerJoined)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "expected PeerJoined", token);
                    return;
                }

                var peerInfo = joinMsg.GetPayload<PeerPayload>();
                peerId = peerInfo.peerId;
                peerName = peerInfo.peerName;

                var client = new ConnectedClient { peerId = peerId, peerName = peerName, socket = socket };
                clients[peerId] = client;

                // Mandar snapshot de escena al nuevo cliente
                await SendSnapshot(socket, token);

                // Notificar a todos que entró alguien
                var joinedMsg = EZMessage.Create(MessageType.PeerJoined, EZCollabState.localPeerId, peerInfo);
                await BroadcastExcept(joinedMsg, peerId, token);

                incomingMessages.Enqueue(joinMsg);

                // Bucle de recepción
                while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    var msg = EZMessage.FromBytes(buffer[..result.Count]);
                    await ProcessMessage(msg, peerId, token);
                }
            }
            catch (Exception e) when (!token.IsCancellationRequested)
            {
                Debug.LogWarning($"[EZCollab] Client error ({peerId}): {e.Message}");
            }
            finally
            {
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

        async Task SendSnapshot(WebSocket socket, CancellationToken token)
        {
            var snapshot = EZSceneSerializer.CaptureScene();
            var msg = EZMessage.Create(MessageType.SceneSnapshot, EZCollabState.localPeerId, snapshot);
            var bytes = msg.ToBytes();
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
        }

        async Task SendTo(string peerId, EZMessage msg, CancellationToken token)
        {
            if (!clients.TryGetValue(peerId, out var client)) return;
            try
            {
                var bytes = msg.ToBytes();
                await client.socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
            }
            catch { }
        }

        async Task BroadcastExcept(EZMessage msg, string excludePeerId, CancellationToken token)
        {
            var bytes = msg.ToBytes();
            foreach (var kv in clients)
            {
                if (kv.Key == excludePeerId) continue;
                try
                {
                    await kv.Value.socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
                }
                catch { }
            }
        }

        async Task BroadcastAll(EZMessage msg, CancellationToken token)
        {
            var bytes = msg.ToBytes();
            foreach (var kv in clients)
            {
                try
                {
                    await kv.Value.socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
                }
                catch { }
            }
        }
    }
}
