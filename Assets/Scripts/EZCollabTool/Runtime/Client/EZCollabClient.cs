using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EZCollabTool
{
    public class EZCollabClient
    {
        ClientWebSocket socket;
        CancellationTokenSource cts;

        public readonly System.Collections.Concurrent.ConcurrentQueue<EZMessage> incomingMessages
            = new System.Collections.Concurrent.ConcurrentQueue<EZMessage>();

        public bool isConnected => socket?.State == WebSocketState.Open;

        public async Task<bool> Connect(string host, int port)
        {
            cts = new CancellationTokenSource();
            socket = new ClientWebSocket();

            try
            {
                await socket.ConnectAsync(new Uri($"ws://{host}:{port}/"), cts.Token);

                // Primer mensaje: presentarse al servidor
                var joinMsg = EZMessage.Create(MessageType.PeerJoined, EZCollabState.localPeerId,
                    new PeerPayload { peerId = EZCollabState.localPeerId, peerName = EZCollabState.localPeerName });
                await Send(joinMsg);

                _ = Task.Run(ReceiveLoop);
                _ = Task.Run(HeartbeatLoop);

                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[EZCollab] Connection failed: {e.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            cts?.Cancel();
            try { socket?.CloseAsync(WebSocketCloseStatus.NormalClosure, "client leaving", CancellationToken.None).Wait(); }
            catch { }
        }

        public async Task Send(EZMessage msg)
        {
            if (!isConnected) return;
            try
            {
                var bytes = msg.ToBytes();
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EZCollab] Send error: {e.Message}");
            }
        }

        async Task ReceiveLoop()
        {
            var buffer = new byte[65536];

            while (!cts.Token.IsCancellationRequested && isConnected)
            {
                try
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    var msg = EZMessage.FromBytes(buffer[..result.Count]);
                    incomingMessages.Enqueue(msg);
                }
                catch (Exception e) when (!cts.Token.IsCancellationRequested)
                {
                    Debug.LogWarning($"[EZCollab] Receive error: {e.Message}");
                    break;
                }
            }
        }

        async Task HeartbeatLoop()
        {
            while (!cts.Token.IsCancellationRequested && isConnected)
            {
                await Task.Delay(5000, cts.Token);
                var ping = EZMessage.Create(MessageType.Heartbeat, EZCollabState.localPeerId, null);
                await Send(ping);
            }
        }
    }
}
