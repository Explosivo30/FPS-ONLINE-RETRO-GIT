using System.Threading.Tasks;
using UnityEngine;

namespace EZCollabTool
{
    public static class EZCollabSession
    {
        public static EZCollabServer server { get; private set; }
        public static EZCollabClient client { get; private set; }

        public static void StartHost(string peerName, int port)
        {
            EZCollabState.Initialize(peerName, asHost: true);
            server = new EZCollabServer();
            server.Start(port);
            Debug.Log($"[EZCollab] Session started on port {port} as \"{peerName}\"");
        }

        public static async Task<bool> JoinAsClient(string peerName, string host, int port)
        {
            EZCollabState.Initialize(peerName, asHost: false);
            client = new EZCollabClient();
            bool ok = await client.Connect(host, port);

            if (!ok)
            {
                EZCollabState.Shutdown();
                client = null;
            }

            return ok;
        }

        public static void Stop()
        {
            server?.Stop();
            client?.Disconnect();
            server = null;
            client = null;
            EZCollabState.Shutdown();
            Debug.Log("[EZCollab] Session stopped");
        }
    }
}
