using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Ros;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[ros] A world on ROS 2 through rosbridge: clock and joint states out, joint commands in (against a stand-in server).</summary>
    public class RosBridgeTests
    {
        private readonly ITestOutputHelper _out;

        public RosBridgeTests(ITestOutputHelper output) => _out = output;

        /// <summary>A stand-in for rosbridge_server: takes one client, records what it sends, sends what it is given.</summary>
        private sealed class FakeRosbridge : IDisposable
        {
            private readonly HttpListener _http = new HttpListener();
            private WebSocket? _socket;
            public readonly ConcurrentQueue<JsonObject> Got = new ConcurrentQueue<JsonObject>();
            public readonly int Port;

            public FakeRosbridge()
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                Port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                _http.Prefixes.Add($"http://localhost:{Port}/");
                _http.Start();
                _ = Task.Run(Serve);
            }

            private async Task Serve()
            {
                var ctx = await _http.GetContextAsync();
                var ws = await ctx.AcceptWebSocketAsync(null);
                _socket = ws.WebSocket;
                var buffer = new byte[65536];
                var text = new StringBuilder();
                try
                {
                    while (_socket.State == WebSocketState.Open)
                    {
                        var r = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        if (r.MessageType == WebSocketMessageType.Close) break;
                        text.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                        if (!r.EndOfMessage) continue;
                        if (JsonNode.Parse(text.ToString()) is JsonObject o) Got.Enqueue(o);
                        text.Clear();
                    }
                }
                catch (Exception ex) when (ex is WebSocketException || ex is HttpListenerException || ex is ObjectDisposedException)
                {
                }
            }

            public async Task Send(JsonObject op)
            {
                for (int i = 0; i < 200 && _socket == null; i++) await Task.Delay(10);
                var bytes = Encoding.UTF8.GetBytes(op.ToJsonString());
                await _socket!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }

            public void Dispose()
            {
                try
                {
                    _http.Stop();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        [Fact]
        public async Task AWorldPublishesClockAndJointStatesAndFollowsJointCommands()
        {
            using var server = new FakeRosbridge();
            var world = new WorldDocument();
            UrdfImport.Import(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rw_arm4.urdf.xacro"), world);
            using var runner = new WorldRunner(world, new RunnerOptions { Firmware = false });
            using var ros = new RosBridgeClient(new Uri($"ws://localhost:{server.Port}/"));
            await ros.ConnectAsync(TimeSpan.FromSeconds(5));
            var link = new RosWorldLink(ros, world, runner.Physics!);
            Assert.Contains("j3_elbow", link.JointNames);   // the URDF's own names

            // ROS asks the elbow for 0.5 rad.
            await server.Send(new JsonObject
            {
                ["op"] = "publish", ["topic"] = RosWorldLink.JointCommandTopic,
                ["msg"] = new JsonObject { ["name"] = new JsonArray("j3_elbow", "no_such_joint"), ["position"] = new JsonArray(0.5, 1.0) }
            });
            // The world runs until the command has come (however busy the computer), then a second more for the motor.
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (link.Commands == 0 && waited.Elapsed.TotalSeconds < 10)
            {
                runner.Step(0.01);
                link.Step(runner.Time);
                await Task.Delay(1);
            }

            double commanded = runner.Time;
            while (runner.Time < commanded + 1.0)
            {
                runner.Step(0.01);
                link.Step(runner.Time);
            }

            // Until the stand-in has the last clock the world sent (the network may lag behind the simulation).
            double LastClock() => server.Got.Where(o => (string?)o["op"] == "publish" && (string?)o["topic"] == "/clock").Select(o => (long)o["msg"]!["clock"]!["sec"]! + (long)o["msg"]!["clock"]!["nanosec"]! * 1e-9).DefaultIfEmpty(-1).Max();
            for (int i = 0; i < 250 && LastClock() < runner.Time - 0.011; i++) await Task.Delay(20);
            var ops = server.Got.ToList();
            _out.WriteLine($"rosbridge'e giden: {ops.Count} işlem; komut {link.Commands}, bilinmeyen: {string.Join(", ", link.UnknownNames)}");
            Assert.Contains(ops, o => (string?)o["op"] == "advertise" && (string?)o["topic"] == "/clock" && (string?)o["type"] == "rosgraph_msgs/msg/Clock");
            Assert.Contains(ops, o => (string?)o["op"] == "subscribe" && (string?)o["topic"] == "/joint_command");
            Assert.Equal(1, link.Commands);
            Assert.Contains("no_such_joint", link.UnknownNames);

            var last = ops.Last(o => (string?)o["op"] == "publish" && (string?)o["topic"] == RosWorldLink.JointStatesTopic)["msg"]!;
            var names = ((JsonArray)last["name"]!).Select(n => (string)n!).ToList();
            double elbow = (double)last["position"]![names.IndexOf("j3_elbow")]!;
            _out.WriteLine($"joint_states: dirsek {elbow:F3} rad");
            Assert.InRange(elbow, 0.45, 0.55);   // the motor took it there and ROS hears it
            Assert.InRange(LastClock(), runner.Time - 0.011, runner.Time + 1e-9);   // the clock is the world's
        }
    }
}
