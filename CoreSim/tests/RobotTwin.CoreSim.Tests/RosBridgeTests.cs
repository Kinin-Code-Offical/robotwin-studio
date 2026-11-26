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
                // its own thread: on a busy CI machine the thread pool can starve the handshake past the client's timeout
                new Thread(() => Serve().GetAwaiter().GetResult()) { IsBackground = true, Name = "fake rosbridge" }.Start();
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
            await ros.ConnectAsync(TimeSpan.FromSeconds(20));
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

        /// <summary>[ros-action] MoveIt's FollowJointTrajectory through rosbridge: a goal runs and succeeds, a wrong one is aborted, a cancel holds.</summary>
        [Fact]
        public async Task FollowJointTrajectoryGoalsRunAsTheControllerRunsThem()
        {
            using var server = new FakeRosbridge();
            var world = new WorldDocument();
            UrdfImport.Import(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rw_arm4.urdf.xacro"), world);
            using var runner = new WorldRunner(world, new RunnerOptions { Firmware = false });
            using var ros = new RosBridgeClient(new Uri($"ws://localhost:{server.Port}/"));
            await ros.ConnectAsync(TimeSpan.FromSeconds(20));
            var link = new RosWorldLink(ros, world, runner.Physics!);
            const string action = RosWorldLink.DefaultTrajectoryAction;

            JsonObject Goal(string id, string joint, double position, double seconds, double tolerance, double slack = 1.0) => new JsonObject
            {
                ["op"] = "send_action_goal", ["id"] = id, ["action"] = action, ["action_type"] = RosWorldLink.FollowJointTrajectoryType, ["feedback"] = true,
                ["args"] = new JsonObject
                {
                    ["trajectory"] = new JsonObject
                    {
                        ["joint_names"] = new JsonArray(joint),
                        ["points"] = new JsonArray(new JsonObject
                        {
                            ["positions"] = new JsonArray(position),
                            ["time_from_start"] = new JsonObject { ["sec"] = (int)seconds, ["nanosec"] = (int)((seconds - (int)seconds) * 1e9) }
                        })
                    },
                    ["goal_tolerance"] = new JsonArray(new JsonObject { ["name"] = joint, ["position"] = tolerance, ["velocity"] = 0.0, ["acceleration"] = 0.0 }),
                    ["goal_time_tolerance"] = new JsonObject { ["sec"] = (int)slack, ["nanosec"] = (int)((slack - (int)slack) * 1e9) }
                }
            };

            async Task RunUntil(Func<bool> done, double maxSeconds)
            {
                double until = runner.Time + maxSeconds;
                var wall = System.Diagnostics.Stopwatch.StartNew();
                while (!done() && runner.Time < until && wall.Elapsed.TotalSeconds < 30)
                {
                    runner.Step(0.01);
                    link.Step(runner.Time);
                    await Task.Delay(0);
                }
            }

            // A message on its way: taken without moving the world (under load the network can be slower than the stepping).
            async Task Deliver(Func<bool> arrived)
            {
                var wall = System.Diagnostics.Stopwatch.StartNew();
                while (!arrived() && wall.Elapsed.TotalSeconds < 10)
                {
                    link.Step(runner.Time);
                    await Task.Delay(5);
                }
            }

            // 1. The elbow to 0.5 rad in 1 s, within 0.05 rad: it gets there and the goal succeeds.
            await server.Send(Goal("action_goal:" + action + ":1", "j3_elbow", 0.5, 1.0, 0.05));
            await Deliver(() => link.Goals >= 1);
            await RunUntil(() => link.GoalResults.Count >= 1, 4);
            Assert.Equal(("action_goal:" + action + ":1", 4, RosWorldLink.Successful), link.GoalResults[0]);

            // 2. A joint the world does not have: aborted with INVALID_JOINTS at once.
            await server.Send(Goal("action_goal:" + action + ":2", "no_such_joint", 0.1, 1.0, 0.05));
            await Deliver(() => link.GoalResults.Count >= 2);
            await RunUntil(() => link.GoalResults.Count >= 2, 1);
            Assert.Equal((6, RosWorldLink.InvalidJoints), (link.GoalResults[1].Status, link.GoalResults[1].ErrorCode));

            // 3. A slow move cancelled half way: canceled, and the elbow stays about where it was.
            await server.Send(Goal("action_goal:" + action + ":3", "j3_elbow", -0.5, 3.0, 0.05));
            await Deliver(() => link.Goals >= 3);
            await RunUntil(() => false, 1.0);
            await server.Send(new JsonObject { ["op"] = "cancel_action_goal", ["id"] = "action_goal:" + action + ":3", ["action"] = action });
            await Deliver(() => link.GoalResults.Count >= 3);
            await RunUntil(() => link.GoalResults.Count >= 3, 1);
            Assert.Equal((5, RosWorldLink.Successful), (link.GoalResults[2].Status, link.GoalResults[2].ErrorCode));

            // 4. A target the elbow cannot reach within 0.05 s of the trajectory's end: aborted with GOAL_TOLERANCE_VIOLATED.
            await server.Send(Goal("action_goal:" + action + ":4", "j3_elbow", 1.2, 0.05, 0.001, 0.05));
            await Deliver(() => link.Goals >= 4);
            await RunUntil(() => link.GoalResults.Count >= 4, 3);
            Assert.Equal((6, RosWorldLink.GoalToleranceViolated), (link.GoalResults[3].Status, link.GoalResults[3].ErrorCode));

            for (int i = 0; i < 250 && server.Got.Count(o => (string?)o["op"] == "action_result") < 4; i++) await Task.Delay(20);
            var ops = server.Got.ToList();
            Assert.Contains(ops, o => (string?)o["op"] == "advertise_action" && (string?)o["action"] == action && (string?)o["type"] == "control_msgs/action/FollowJointTrajectory");
            var feedback = ops.Where(o => (string?)o["op"] == "action_feedback" && ((string?)o["id"])!.EndsWith(":1")).ToList();
            Assert.InRange(feedback.Count, 8, 14);   // ten a second for about a second
            Assert.Equal("j3_elbow", (string?)feedback[0]["values"]!["joint_names"]![0]);
            var first = ops.First(o => (string?)o["op"] == "action_result");
            Assert.Equal((4L, true, 0L), ((long)first["status"]!, (bool)first["result"]!, (long)first["values"]!["error_code"]!));
        }

        /// <summary>[ros-sense] The world's own sensor parts on ROS: a LiDAR's sweep, an IMU, a camera's frame, a GPS fix, a compass.</summary>
        [Fact]
        public async Task TheWorldsSensorPartsPublishWhatTheyMeasured()
        {
            using var server = new FakeRosbridge();
            var world = new WorldDocument();
            using var runner = new WorldRunner(world, new RunnerOptions { Firmware = false });
            using var ros = new RosBridgeClient(new Uri($"ws://localhost:{server.Port}/"));
            await ros.ConnectAsync(TimeSpan.FromSeconds(20));
            var link = new RosWorldLink(ros, world, runner.Physics!);
            var sensors = new RobotTwin.CoreSim.Sandbox.Sensing.WorldSensors();
            link.AttachSensors(sensors);

            // A 2D LiDAR: 4 samples clockwise from the front (0°, 90°, 180°, 270°), the one at 270° with no return.
            sensors.Publish(new RobotTwin.CoreSim.Sandbox.Sensing.LidarReading
            {
                ObjectId = "o1", Name = "Ön LiDAR", Time = 0.1,
                Scan = new RobotTwin.CoreSim.Sandbox.Sensing.LidarScan { ScanRate = 10, AnglesDeg = new[] { 0f, 90f, 180f, 270f }, Ranges = new[] { 1.0f, 2.0f, 3.0f, float.NaN } }
            });
            sensors.Publish(new RobotTwin.CoreSim.Sandbox.Sensing.ImuReading { ObjectId = "o2", Name = "MPU-6050", Time = 0.1, Accel = new RobotTwin.CoreSim.Mechanics.Vec3(0, 0, 9.8), Gyro = new RobotTwin.CoreSim.Mechanics.Vec3(0, 0, 0.5) });
            sensors.Publish(new RobotTwin.CoreSim.Sandbox.Sensing.CameraReading { ObjectId = "o3", Name = "Cam", Time = 0.1, Width = 2, Height = 1, FrameIndex = 1, Bgra = new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 } });
            sensors.Publish(new RobotTwin.CoreSim.Sandbox.Sensing.GpsReading { ObjectId = "o4", Name = "GPS", Time = 0.1, Fix = new RobotTwin.CoreSim.Sandbox.Sensing.GpsFix { Valid = true, Latitude = 41.0, Longitude = 29.0, Altitude = 50, Hdop = 1 } });
            sensors.Publish(new RobotTwin.CoreSim.Sandbox.Sensing.CompassReading { ObjectId = "o5", Name = "Pusula", Time = 0.1, Field = new RobotTwin.CoreSim.Mechanics.Vec3(0.2, 0, -0.4) });
            sensors.Publish(new RobotTwin.CoreSim.Sandbox.Sensing.ImuReading { ObjectId = "o6", Name = "Off", Time = 0.1, Powered = false });
            link.Step(0.1);
            link.Step(0.11);   // nothing new: nothing sent again

            JsonNode Msg(string topic) => server.Got.Single(o => (string?)o["op"] == "publish" && (string?)o["topic"] == topic)["msg"]!;
            for (int i = 0; i < 250 && server.Got.Count(o => (string?)o["op"] == "publish" && ((string?)o["topic"])!.StartsWith("/robotwin/")) < 5; i++) await Task.Delay(20);
            var ops = server.Got.ToList();
            Assert.Contains(ops, o => (string?)o["op"] == "advertise" && (string?)o["topic"] == "/robotwin/on_lidar/scan" && (string?)o["type"] == "sensor_msgs/msg/LaserScan");
            var scan = Msg("/robotwin/on_lidar/scan");
            Assert.Equal("on_lidar", (string?)scan["header"]!["frame_id"]);
            var ranges = ((JsonArray)scan["ranges"]!).Select(r => (double)r!).ToArray();
            Assert.Equal(4, ranges.Length);
            // counter-clockwise from −π: behind (180°) 3 m, right (90° clockwise = −π/2) 2 m, front 1 m, left: no return
            Assert.Equal(new[] { 3.0, 2.0, 1.0 }, ranges.Take(3));
            Assert.True(ranges[3] > (double)scan["range_max"]!);
            Assert.Equal(0.5, (double)Msg("/robotwin/mpu_6050/imu")["angular_velocity"]!["z"]!);
            var image = Msg("/robotwin/cam/image_raw");
            Assert.Equal(("bgra8", 8L), ((string?)image["encoding"], (long)image["step"]!));
            Assert.Equal(new byte[] { 1, 2, 3, 255, 4, 5, 6, 255 }, Convert.FromBase64String((string)image["data"]!));
            Assert.Equal(41.0, (double)Msg("/robotwin/gps/fix")["latitude"]!);
            Assert.Equal(-4e-5, (double)Msg("/robotwin/pusula/magnetic_field")["magnetic_field"]!["z"]!, 9);
            Assert.DoesNotContain(ops, o => ((string?)o["topic"])?.Contains("/off/") == true);
        }

        /// <summary>A trajectory on /joint_trajectory: the arm passes its points at their times and holds the last.</summary>
        [Fact]
        public async Task AnArmFollowsAJointTrajectoryInTime()
        {
            using var server = new FakeRosbridge();
            var world = new WorldDocument();
            UrdfImport.Import(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rw_arm4.urdf.xacro"), world);
            using var runner = new WorldRunner(world, new RunnerOptions { Firmware = false });
            using var ros = new RosBridgeClient(new Uri($"ws://localhost:{server.Port}/"));
            await ros.ConnectAsync(TimeSpan.FromSeconds(20));
            var link = new RosWorldLink(ros, world, runner.Physics!);
            string elbow = world.Joints.Single(j => j.Name == "j3_elbow").Id, shoulder = world.Joints.Single(j => j.Name == "j2_shoulder").Id;
            double Angle(string id) => runner.Physics!.Scene.Joints.Single(s => s.Id == id).Angle;

            void Run(double seconds)
            {
                double until = runner.Time + seconds;
                while (runner.Time < until - 1e-6)
                {
                    runner.Step(0.01);
                    link.Step(runner.Time);
                }
            }

            Run(0.3);
            // the trajectory goes out only once the link's subscription has reached the server (a busy CI machine is slow)
            var subscribed = System.Diagnostics.Stopwatch.StartNew();
            while (!server.Got.Any(o => (string?)o["op"] == "subscribe" && (string?)o["topic"] == RosWorldLink.JointTrajectoryTopic) && subscribed.Elapsed.TotalSeconds < 60)
            {
                link.Step(runner.Time);
                await Task.Delay(5);
            }

            JsonObject Point(double elbowRad, double shoulderRad, int sec) => new JsonObject
            {
                ["positions"] = new JsonArray(elbowRad, shoulderRad), ["velocities"] = new JsonArray(0.0, 0.0),
                ["time_from_start"] = new JsonObject { ["sec"] = sec, ["nanosec"] = 0 }
            };
            await server.Send(new JsonObject
            {
                ["op"] = "publish", ["topic"] = RosWorldLink.JointTrajectoryTopic,
                ["msg"] = new JsonObject
                {
                    ["header"] = new JsonObject { ["stamp"] = new JsonObject { ["sec"] = 0, ["nanosec"] = 0 }, ["frame_id"] = string.Empty },
                    ["joint_names"] = new JsonArray("j3_elbow", "j2_shoulder"),
                    ["points"] = new JsonArray(Point(0.6, 0.3, 1), Point(-0.3, -0.2, 2))
                }
            });
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while (link.Trajectories == 0 && waited.Elapsed.TotalSeconds < 60)
            {
                link.Step(runner.Time);
                await Task.Delay(1);
            }

            Assert.True(link.Following);
            Run(0.5);
            double half = Angle(elbow);
            Run(0.5);
            double first = Angle(elbow), firstShoulder = Angle(shoulder);
            Run(1.0);
            double second = Angle(elbow);
            Run(0.5);
            double held = Angle(elbow), heldShoulder = Angle(shoulder);
            _out.WriteLine($"/joint_trajectory: dirsek 0,5 s'de {half:F3}, 1 s'de {first:F3} (0,6), 2 s'de {second:F3} (−0,3), sonra {held:F3} rad; omuz {firstShoulder:F3} (0,3), {heldShoulder:F3} (−0,2)");
            Assert.InRange(half, 0.25, 0.35);   // half way between 0 and 0.6 on the cubic
            Assert.InRange(first, 0.55, 0.62);   // at the first point (the motor lags a step or two)
            Assert.InRange(second, -0.33, -0.25);
            Assert.InRange(held, -0.31, -0.29);   // holds the last point
            Assert.InRange(firstShoulder, 0.25, 0.32);
            Assert.InRange(heldShoulder, -0.21, -0.19);
            Assert.False(link.Following);
        }

        /// <summary>The sensors a description declares for Gazebo: the scanner sees a box ahead on /scan, the IMU feels gravity on /imu.</summary>
        [Fact]
        public async Task ADescriptionsScannerAndImuPublishWhatTheWorldShows()
        {
            using var server = new FakeRosbridge();
            var world = new WorldDocument();
            var imported = UrdfImport.Import(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rw_rover.urdf"), world);
            Assert.Equal(2, imported.Sensors);
            var scanner = world.Objects.Single(o => o.Link == "base_scan").RobotSensors!.Single();
            Assert.Equal(("lidar", "scan", "base_scan", 360, 3.5), (scanner.Kind, scanner.Topic, scanner.Frame, scanner.Samples, scanner.MaxRange));
            var imu = world.Objects.Single(o => o.Link == "base_footprint").RobotSensors!.Single();   // imu_link has no body: it rides on base_link
            Assert.Equal(("imu", "imu", "imu_link"), (imu.Kind, imu.Topic, imu.Frame));
            Assert.Equal(-0.03, imu.Pose[0], 6);

            // A wall a metre ahead of the scanner: 20 cm thick, so its face is 0.9 m away.
            var wall = world.AddBody("box", 0.2, 1.0, 0.5, new WorldPose { X = 1.0, Y = 0, Z = 0 }, "PLA");
            wall.IsStatic = true;

            using var runner = new WorldRunner(world, new RunnerOptions { Firmware = false });
            using var ros = new RosBridgeClient(new Uri($"ws://localhost:{server.Port}/"));
            await ros.ConnectAsync(TimeSpan.FromSeconds(20));
            var link = new RosWorldLink(ros, world, runner.Physics!);
            Assert.Equal(new[] { ("/imu", "imu"), ("/scan", "lidar") }, link.SensorTopics.Select(t => (t.Topic, t.Kind)).OrderBy(t => t.Topic));
            while (runner.Time < 1.0 - 1e-6)
            {
                runner.Step(0.01);
                link.Step(runner.Time);
            }

            // Until everything the second sent has come (the network may lag far behind the simulation on a busy machine).
            int Published(string topic) => server.Got.Count(x => (string?)x["op"] == "publish" && (string?)x["topic"] == topic);
            var waited = System.Diagnostics.Stopwatch.StartNew();
            while ((Published("/scan") < 5 || Published("/imu") < 95 || ros.Sent > server.Got.Count) && waited.Elapsed.TotalSeconds < 30) await Task.Delay(20);
            var ops = server.Got.ToList();
            Assert.Contains(ops, x => (string?)x["op"] == "advertise" && (string?)x["topic"] == "/scan" && (string?)x["type"] == "sensor_msgs/msg/LaserScan");
            var scans = ops.Where(x => (string?)x["op"] == "publish" && (string?)x["topic"] == "/scan").Select(x => x["msg"]!).ToList();
            Assert.InRange(scans.Count, 5, 6);   // 5 Hz for a second
            var scan = scans.Last();
            Assert.Equal("base_scan", (string?)scan["header"]!["frame_id"]);
            var ranges = ((JsonArray)scan["ranges"]!).Select(r => (double)r!).ToArray();
            Assert.Equal(360, ranges.Length);
            double ahead = ranges[0], behind = ranges[180];
            _out.WriteLine($"/scan: {scans.Count} taramada önde {ahead:F3} m, arkada {behind:F3} m");
            Assert.InRange(ahead, 0.86, 0.94);   // the wall's face, with 1 cm noise
            Assert.True(behind > 3.5);   // nothing behind within 3.5 m: past range_max
            Assert.InRange(ranges.Count(r => r < 3.5), 30, 70);   // the wall spans ±29° at 0.9 m

            var imus = ops.Where(x => (string?)x["op"] == "publish" && (string?)x["topic"] == "/imu").Select(x => x["msg"]!).ToList();
            Assert.InRange(imus.Count, 95, 101);   // 100 Hz
            // The last 0.2 s, averaged over the declared noise (σ 0.017 m/s², 0.0002 rad/s).
            var still = imus.Skip(imus.Count - 20).ToList();
            double Mean(string quantity, string axis) => still.Average(m => (double)m[quantity]![axis]!);
            double az = Mean("linear_acceleration", "z"), ax = Mean("linear_acceleration", "x"), wz = Mean("angular_velocity", "z");
            _out.WriteLine($"/imu: {imus.Count} okuma; ivme z {az:F3}, x {ax:F3} m/s², dönüş z {wz:F5} rad/s");
            Assert.InRange(az, 9.75, 9.87);   // gravity, felt as an upward specific force
            Assert.InRange(Math.Abs(ax), 0, 0.1);   // resting on two wheels and a caster: tilted a little
            Assert.InRange(Math.Abs(wz), 0, 0.001);
        }

        /// <summary>A mobile robot as ROS drives one: /cmd_vel turns its wheels, /odom and /tf say where it went.</summary>
        [Fact]
        public async Task ARoverFollowsCmdVelAndReportsOdometry()
        {
            using var server = new FakeRosbridge();
            var world = new WorldDocument();
            var imported = UrdfImport.Import(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rw_rover.urdf"), world);
            Assert.Equal((4, 2), (imported.MovingJoints, imported.MotorisedJoints));

            // The description's ros2_control names the wheels: they are speed motors, the caster turns freely.
            var byName = world.Joints.Where(j => j.Name != null).ToDictionary(j => j.Name!);
            Assert.Equal(WorldJoint.DriveSpeedMode, byName["wheel_left_joint"].Drive);
            Assert.Null(byName["caster_swivel_joint"].Drive);
            Assert.Null(byName["caster_wheel_joint"].Drive);
            var chassis = world.Objects.Single(o => o.Link == "base_footprint");   // ROS knows the robot by its root frame
            Assert.False(chassis.IsStatic);   // a geometry-less root is not the world: the rover is free

            using var runner = new WorldRunner(world, new RunnerOptions { Firmware = false });
            using var ros = new RosBridgeClient(new Uri($"ws://localhost:{server.Port}/"));
            await ros.ConnectAsync(TimeSpan.FromSeconds(20));
            var link = new RosWorldLink(ros, world, runner.Physics!);
            var rover = Assert.Single(link.Bases);
            Assert.Equal("base_footprint", rover.Frame);
            Assert.Equal(new[] { "wheel_left_joint", "wheel_right_joint" }, rover.WheelNames.OrderBy(n => n));
            Assert.InRange(rover.WheelSeparation, 0.235, 0.245);
            Assert.InRange(rover.WheelRadius, 0.048, 0.052);

            void Run(double seconds)
            {
                double until = runner.Time + seconds;
                while (runner.Time < until - 1e-6)
                {
                    runner.Step(0.01);
                    link.Step(runner.Time);
                }
            }

            // A teleop sends a twist every 0.1 s; the world waits for each to arrive (however slow the network).
            async Task Drive(double v, double w, double seconds)
            {
                for (double t = 0; t < seconds - 1e-9; t += 0.1)
                {
                    long had = rover.Twists;
                    await server.Send(new JsonObject
                    {
                        ["op"] = "publish", ["topic"] = RosWorldLink.CmdVelTopic,
                        ["msg"] = new JsonObject { ["linear"] = new JsonObject { ["x"] = v, ["y"] = 0.0, ["z"] = 0.0 }, ["angular"] = new JsonObject { ["x"] = 0.0, ["y"] = 0.0, ["z"] = w } }
                    });
                    var waited = System.Diagnostics.Stopwatch.StartNew();
                    while (rover.Twists == had && waited.Elapsed.TotalSeconds < 10)
                    {
                        link.Step(runner.Time);
                        await Task.Delay(1);
                    }

                    Run(0.1);
                }
            }

            (double X, double Y, double Yaw, double Speed) Odom()
            {
                var o = server.Got.Last(x => (string?)x["op"] == "publish" && (string?)x["topic"] == RosWorldLink.OdomTopic)["msg"]!;
                var p = o["pose"]!["pose"]!["position"]!;
                var q = o["pose"]!["pose"]!["orientation"]!;
                double yaw = 2 * Math.Atan2((double)q["z"]!, (double)q["w"]!);
                return ((double)p["x"]!, (double)p["y"]!, yaw, (double)o["twist"]!["twist"]!["linear"]!["x"]!);
            }

            async Task Settle()
            {
                double last = runner.Time;
                for (int i = 0; i < 250 && !server.Got.Any(x => (string?)x["op"] == "publish" && (string?)x["topic"] == RosWorldLink.OdomTopic && (double)(long)x["msg"]!["header"]!["stamp"]!["sec"]! + (double)(long)x["msg"]!["header"]!["stamp"]!["nanosec"]! * 1e-9 >= last - 0.021); i++)
                    await Task.Delay(20);
            }

            Run(0.5);   // lands on its wheels and caster
            await Drive(0.2, 0, 2.0);
            await Settle();
            var straight = Odom();
            _out.WriteLine($"2 s at 0.2 m/s: x {straight.X:F3} m, y {straight.Y:F3} m, heading {straight.Yaw * 180 / Math.PI:F1}°, speed {straight.Speed:F3} m/s");
            Assert.InRange(straight.X, 0.32, 0.42);   // 0.4 m, less the speeding up
            Assert.InRange(Math.Abs(straight.Y), 0, 0.03);
            Assert.InRange(straight.Speed, 0.17, 0.22);

            await Drive(0, 1.0, 1.5);
            await Settle();
            var turned = Odom();
            _out.WriteLine($"1.5 s at 1 rad/s: heading {turned.Yaw * 180 / Math.PI:F1}° (from {straight.Yaw * 180 / Math.PI:F1}°)");
            Assert.InRange(turned.Yaw - straight.Yaw, 1.15, 1.6);   // ≈ 1.5 rad, the caster scrubbing a little

            // The teleop stops sending: after the timeout the wheels stop.
            Run(1.0);
            await Settle();
            var stopped = Odom();
            Assert.InRange(Math.Abs(stopped.Speed), 0, 0.01);
            Assert.False(link.Bases[0].Speed != 0);

            var tf = server.Got.Last(x => (string?)x["op"] == "publish" && (string?)x["topic"] == RosWorldLink.TfTopic)["msg"]!["transforms"]![0]!;
            Assert.Equal("odom", (string?)tf["header"]!["frame_id"]);
            Assert.Equal("base_footprint", (string?)tf["child_frame_id"]);
            Assert.Contains(server.Got, x => (string?)x["op"] == "subscribe" && (string?)x["topic"] == "/cmd_vel" && (string?)x["type"] == "geometry_msgs/msg/Twist");
        }
    }
}
