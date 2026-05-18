using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace EZCollabTool
{
    public class EZCollabClient
    {
        TcpClient client;
        NetworkStream stream;
        CancellationTokenSource cts;

        public readonly System.Collections.Concurrent.ConcurrentQueue<EZMessage> incomingMessages
            = new System.Collections.Concurrent.ConcurrentQueue<EZMessage>();

        public bool isConnected => client != null && client.Connected;

        public async Task<bool> Connect(string host, int port)
        {
            cts = new CancellationTokenSource();
            client = new TcpClient();
            client.NoDelay = true; // Desactiva el algoritmo de Nagle para tener baja latencia

            try
            {
                await client.ConnectAsync(host, port);
                stream = client.GetStream();

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
            try { client?.Close(); } catch { }
        }

        public async Task Send(EZMessage msg)
        {
            if (!isConnected) return;
            try
            {
                var bytes = msg.ToBytes();
                var lengthBytes = BitConverter.GetBytes(bytes.Length);
                await stream.WriteAsync(lengthBytes, 0, 4, cts.Token);
                await stream.WriteAsync(bytes, 0, bytes.Length, cts.Token);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EZCollab] Send error: {e.Message}");
            }
        }

        async Task<int> ReadExact(NetworkStream s, byte[] buffer, int count, CancellationToken token)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await s.ReadAsync(buffer, totalRead, count - totalRead, token);
                if (read == 0) return 0;
                totalRead += read;
            }
            return totalRead;
        }

        async Task ReceiveLoop()
        {
            var lengthBuffer = new byte[4];
            byte[] payloadBuffer = new byte[32768];

            while (!cts.Token.IsCancellationRequested && isConnected)
            {
                try
                {
                    int read = await ReadExact(stream, lengthBuffer, 4, cts.Token);
                    if (read == 0) break; // Desconectado

                    int length = BitConverter.ToInt32(lengthBuffer, 0);
                    if (length > 10 * 1024 * 1024) throw new Exception("Payload too large"); // Sanity check
                    
                    if (payloadBuffer.Length < length)
                        payloadBuffer = new byte[length];

                    read = await ReadExact(stream, payloadBuffer, length, cts.Token);
                    if (read == 0) break;

                    var msg = EZMessage.FromBytes(payloadBuffer[..length]);
                    incomingMessages.Enqueue(msg);
                }
                catch (Exception e) when (!cts.Token.IsCancellationRequested)
                {
                    if (!(e is System.IO.IOException))
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
