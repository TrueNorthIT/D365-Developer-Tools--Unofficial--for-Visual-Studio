using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Connection;
using D365_Developer_Tools__Unofficial__for_Visual_Studio.Persistence;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json;

namespace D365_Developer_Tools__Unofficial__for_Visual_Studio.Mcp
{
    internal sealed class BridgeState
    {
        public int Port { get; set; }
        public string Nonce { get; set; }
    }

    /// <summary>
    /// Local token-vending bridge for the standalone D365 MCP server process. Ports mcpBridge.ts:
    /// while a Dataverse connection is active, listens on a random loopback port and hands the MCP
    /// server process a fresh access token + environment URL on request (nonce-authenticated), so the
    /// MCP server never stores credentials itself and always uses this session's live token.
    /// </summary>
    internal sealed class McpBridge : IDisposable
    {
        private static readonly string BridgeFilePath = Path.Combine(JsonFileStore.RootDirectory, "mcp-bridge.json");

        private readonly ConnectionManager _connectionManager;
        private HttpListener _listener;
        private string _nonce;

        public McpBridge(ConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
        }

        public void Start()
        {
            if (_listener != null) { return; }

            int port;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");

            try
            {
                listener.Start();
            }
            catch (HttpListenerException ex)
            {
                ActivityLog.LogError("D365DeveloperTools", "McpBridge failed to start: " + ex);
                return;
            }

            _listener = listener;
            _nonce = GenerateNonce();

            ListenLoopAsync(this, listener, _nonce).FileAndForget("D365DeveloperTools/McpBridgeListenLoop");

            JsonFileStore.Save(BridgeFilePath, new BridgeState { Port = port, Nonce = _nonce });
        }

        public void Stop()
        {
            var listener = _listener;
            if (listener == null) { return; }

            _listener = null;
            try { listener.Stop(); }
            catch { /* already stopped */ }
            listener.Close();

            JsonFileStore.Delete(BridgeFilePath);
        }

        private static async Task ListenLoopAsync(McpBridge bridge, HttpListener listener, string nonce)
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch
                {
                    return; // listener stopped/disposed
                }

                bridge.HandleRequestAsync(context, nonce).FileAndForget("D365DeveloperTools/McpBridgeRequest");
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context, string nonce)
        {
            try
            {
                if (context.Request.HttpMethod != "GET" || context.Request.Url.AbsolutePath != "/token")
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }

                if (context.Request.Headers["Authorization"] != $"Bearer {nonce}")
                {
                    context.Response.StatusCode = 401;
                    context.Response.Close();
                    return;
                }

                string token;
                string environmentUrl;
                try
                {
                    token = await _connectionManager.GetAccessTokenAsync().ConfigureAwait(false);
                    environmentUrl = _connectionManager.Connection?.EnvironmentUrl;
                    if (string.IsNullOrEmpty(environmentUrl))
                    {
                        throw new InvalidOperationException("No active Dataverse connection.");
                    }
                }
                catch (Exception ex)
                {
                    WriteJson(context.Response, 503, JsonConvert.SerializeObject(new { error = ex.Message }));
                    return;
                }

                WriteJson(context.Response, 200, JsonConvert.SerializeObject(new { token, environmentUrl }));
            }
            catch
            {
                try { context.Response.Close(); } catch { /* ignore */ }
            }
        }

        private static void WriteJson(HttpListenerResponse response, int statusCode, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
            response.OutputStream.Close();
        }

        private static string GenerateNonce()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) { rng.GetBytes(bytes); }
            return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        }

        public void Dispose() => Stop();
    }
}
