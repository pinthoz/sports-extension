using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SportsOverlayApp.Models;

namespace SportsOverlayApp.Services
{
    /// <summary>
    /// Local WebSocket server the browser extension connects to.
    /// Listens on ws://127.0.0.1:{port}/ and raises GamesReceived whenever
    /// the extension pushes a fresh scrape of the starred FlashScore games.
    /// </summary>
    public class ScoreServer : IDisposable
    {
        private HttpListener? listener;
        private CancellationTokenSource? cts;
        private readonly ConcurrentDictionary<Guid, WebSocket> clients = new();

        public event Action<List<GameData>>? GamesReceived;
        public event Action<bool>? ConnectionChanged;

        public bool HasClients => !clients.IsEmpty;

        public void Start(int port)
        {
            Stop();
            cts = new CancellationTokenSource();
            listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            _ = AcceptLoopAsync(cts.Token);
        }

        public void Stop()
        {
            cts?.Cancel();
            foreach (var socket in clients.Values)
            {
                try { socket.Abort(); } catch { }
            }
            clients.Clear();
            try { listener?.Stop(); } catch { }
            listener = null;
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            var srv = listener;
            while (srv != null && srv.IsListening && !token.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await srv.GetContextAsync();
                }
                catch
                {
                    break; // listener stopped
                }

                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    continue;
                }

                _ = HandleClientAsync(context, token);
            }
        }

        private async Task HandleClientAsync(HttpListenerContext context, CancellationToken token)
        {
            WebSocket socket;
            try
            {
                var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
                socket = wsContext.WebSocket;
            }
            catch
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
                return;
            }

            var id = Guid.NewGuid();
            clients[id] = socket;
            ConnectionChanged?.Invoke(true);

            var buffer = new byte[64 * 1024];
            var message = new StringBuilder();
            try
            {
                while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", token);
                        break;
                    }

                    message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    if (!result.EndOfMessage)
                        continue;

                    HandleMessage(message.ToString());
                    message.Clear();
                }
            }
            catch
            {
                // client dropped; fall through to cleanup
            }
            finally
            {
                clients.TryRemove(id, out _);
                try { socket.Dispose(); } catch { }
                ConnectionChanged?.Invoke(HasClients);
            }
        }

        private void HandleMessage(string json)
        {
            try
            {
                var root = JObject.Parse(json);
                if (root["type"]?.ToString() != "scores")
                    return;

                GamesReceived?.Invoke(GameParser.FromJArray(root["games"] as JArray));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ScoreServer parse error: {ex.Message}");
            }
        }

        public void Dispose() => Stop();
    }
}
