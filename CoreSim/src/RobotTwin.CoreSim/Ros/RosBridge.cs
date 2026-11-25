using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace RobotTwin.CoreSim.Ros
{
    /// <summary>
    /// [ros] A client of ROS's rosbridge (the rosbridge v2 JSON protocol over a WebSocket, as <c>rosbridge_server</c> serves it
    /// on a ROS 2 machine, port 9090 by default): it advertises and publishes topics and subscribes to them. Sending and
    /// receiving run on their own tasks; <see cref="Publish"/> only queues, received messages wait in a queue for
    /// <see cref="TryReceive"/>, so the simulation never waits for the network.
    /// </summary>
    public sealed class RosBridgeClient : IDisposable
    {
        private readonly ClientWebSocket _socket = new ClientWebSocket();
        private readonly BlockingCollection<string> _outbox = new BlockingCollection<string>(new ConcurrentQueue<string>(), 4096);
        private readonly ConcurrentQueue<(string Topic, JsonNode Message)> _inbox = new ConcurrentQueue<(string, JsonNode)>();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly HashSet<string> _advertised = new HashSet<string>(StringComparer.Ordinal);
        private Task? _sender, _receiver;

        public RosBridgeClient(Uri address)
        {
            Address = address;
        }

        public Uri Address { get; }

        public bool Connected => _socket.State == WebSocketState.Open;

        /// <summary>Why the link stopped (null while it works).</summary>
        public string? Error { get; private set; }

        /// <summary>Messages sent and received since connecting.</summary>
        public long Sent => Interlocked.Read(ref _sent);

        public long Received => Interlocked.Read(ref _received);

        private long _sent, _received, _dropped;

        /// <summary>Messages left out because the network could not keep up (the queue holds 4096).</summary>
        public long Dropped => Interlocked.Read(ref _dropped);

        public async Task ConnectAsync(TimeSpan timeout)
        {
            using var limit = new CancellationTokenSource(timeout);
            using var both = CancellationTokenSource.CreateLinkedTokenSource(limit.Token, _stop.Token);
            await _socket.ConnectAsync(Address, both.Token).ConfigureAwait(false);
            _sender = Task.Run(SendLoop);
            _receiver = Task.Run(ReceiveLoop);
        }

        /// <summary>Announces a topic this side publishes (once per topic).</summary>
        public void Advertise(string topic, string type)
        {
            lock (_advertised)
                if (!_advertised.Add(topic)) return;
            Queue(new JsonObject { ["op"] = "advertise", ["topic"] = topic, ["type"] = type });
        }

        public void Publish(string topic, JsonObject message) => Queue(new JsonObject { ["op"] = "publish", ["topic"] = topic, ["msg"] = message });

        public void Subscribe(string topic, string type, int throttleMs = 0)
        {
            var op = new JsonObject { ["op"] = "subscribe", ["topic"] = topic, ["type"] = type };
            if (throttleMs > 0) op["throttle_rate"] = throttleMs;
            Queue(op);
        }

        /// <summary>The next message published on a subscribed topic, if one came.</summary>
        public bool TryReceive(out string topic, out JsonNode message)
        {
            if (_inbox.TryDequeue(out var m))
            {
                topic = m.Topic;
                message = m.Message;
                return true;
            }

            topic = string.Empty;
            message = null!;
            return false;
        }

        private void Queue(JsonObject op)
        {
            if (_outbox.IsAddingCompleted) return;   // closed
            try
            {
                if (!_outbox.TryAdd(op.ToJsonString())) Interlocked.Increment(ref _dropped);
            }
            catch (InvalidOperationException)
            {
                // closed between the check and the add
            }
        }

        private async Task SendLoop()
        {
            try
            {
                foreach (var text in _outbox.GetConsumingEnumerable(_stop.Token))
                {
                    if (_socket.State != WebSocketState.Open) break;
                    var bytes = Encoding.UTF8.GetBytes(text);
                    await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _stop.Token).ConfigureAwait(false);
                    Interlocked.Increment(ref _sent);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is WebSocketException || ex is ObjectDisposedException || ex is IOException)
            {
                if (!_stop.IsCancellationRequested) Error ??= ex.Message;
            }
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[64 * 1024];
            var message = new MemoryStream();
            try
            {
                while (_socket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
                {
                    var r = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _stop.Token).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close)
                    {
                        Error ??= "rosbridge bağlantıyı kapattı.";
                        break;
                    }

                    message.Write(buffer, 0, r.Count);
                    if (!r.EndOfMessage) continue;
                    string text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                    message.SetLength(0);
                    Take(text);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is WebSocketException || ex is ObjectDisposedException || ex is IOException)
            {
                if (!_stop.IsCancellationRequested) Error ??= ex.Message;
            }
        }

        private void Take(string text)
        {
            JsonNode? op;
            try
            {
                op = JsonNode.Parse(text);
            }
            catch (System.Text.Json.JsonException)
            {
                return;
            }

            if (op is not JsonObject o || (string?)o["op"] != "publish" || (string?)o["topic"] is not { } topic || o["msg"] is not { } msg) return;
            if (_inbox.Count > 1024) _inbox.TryDequeue(out _);   // nobody reads: keep the newest
            _inbox.Enqueue((topic, msg));
            Interlocked.Increment(ref _received);
        }

        public void Dispose()
        {
            _stop.Cancel();
            _outbox.CompleteAdding();
            try
            {
                if (_socket.State == WebSocketState.Open)
                    _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "RobotWin", CancellationToken.None).Wait(500);
            }
            catch (Exception ex) when (ex is WebSocketException || ex is AggregateException || ex is ObjectDisposedException)
            {
            }

            _socket.Dispose();
            _stop.Dispose();
        }
    }
}
