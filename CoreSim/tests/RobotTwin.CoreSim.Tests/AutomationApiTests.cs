using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[api] Headless runner and JSON-RPC automation API.</summary>
    public class AutomationApiTests
    {
        private readonly ITestOutputHelper _out;

        public AutomationApiTests(ITestOutputHelper output)
        {
            _out = output;
        }

        private static string? FirmwareDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "Studio", "RobotWin.Studio", "Firmware");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }

            return null;
        }

        private static RunnerOptions Options(int seed = 0, bool firmware = true)
        {
            var o = new RunnerOptions { Seed = seed, Firmware = firmware };
            if (FirmwareDir() is { } d) o.FirmwareSearchPath.Add(d);
            return o;
        }

        private static ApiSession Session()
        {
            var host = new ApiHostOptions();
            if (FirmwareDir() is { } d) host.FirmwareSearchPath.Add(d);
            return new ApiSession(host);
        }

        private static JsonObject Call(ApiSession s, string method, object? args = null)
        {
            var req = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method };
            if (args != null) req["params"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(args));
            var resp = JsonNode.Parse(s.Handle(req.ToJsonString())!)!.AsObject();
            Assert.True(resp["error"] == null, method + ": " + resp["error"]?.ToJsonString());
            return resp;
        }

        private static Vec3 PoseOf(SimRunner r, string id)
        {
            Assert.True(r.TryGetBody(id, out var s));
            return s.Position;
        }

        [Fact]
        public void SameSeedGivesTheSameRunBitForBit()
        {
            (Vec3, string) Run(int seed)
            {
                var r = new WorldRunner(WorldExamples.CarDetailed(PartLibrary.Default), Options(seed));
                r.Sensors.Add(new SensorSpec { Name = "imu", Type = "imu", Object = "O1", Params = new JsonObject { ["accel_noise"] = 0.05 } });
                r.DriveDifferential(0.15, 0.4, new JsonObject());
                for (int i = 0; i < 60; i++) r.Step(0.01);
                return (PoseOf(r, "O1"), r.Sensors.Read("imu").ToJsonString());
            }

            var a = Run(7);
            var b = Run(7);
            var c = Run(8);
            _out.WriteLine($"seed 7: {a.Item1}  seed 8: {c.Item1}");
            Assert.Equal(a.Item1.X, b.Item1.X);
            Assert.Equal(a.Item1.Y, b.Item1.Y);
            Assert.Equal(a.Item1.Z, b.Item1.Z);
            Assert.Equal(a.Item2, b.Item2);
            Assert.NotEqual(a.Item2, c.Item2);   // the seed drives the sensor noise
        }

        [Fact]
        public void ResetRestoresTheInitialStateAndReplaysIdentically()
        {
            var r = new WorldRunner(WorldExamples.CarDetailed(PartLibrary.Default), Options(1));
            var start = PoseOf(r, "O1");
            for (int i = 0; i < 40; i++) r.Step(0.01);
            var first = PoseOf(r, "O1");
            Assert.True((first - start).Length > 1e-3, "the car must have moved");
            r.Reset();
            Assert.Equal(0.0, r.Time);
            var again = PoseOf(r, "O1");
            Assert.Equal(start.X, again.X);
            Assert.Equal(start.Y, again.Y);
            for (int i = 0; i < 40; i++) r.Step(0.01);
            var second = PoseOf(r, "O1");
            Assert.Equal(first.X, second.X);
            Assert.Equal(first.Y, second.Y);
            Assert.Equal(first.Z, second.Z);
        }

        [Fact]
        public void StepAdvancesExactlyTheRequestedSimulatedTime()
        {
            var r = new WorldRunner(WorldExamples.CarDetailed(PartLibrary.Default), Options());
            for (int i = 0; i < 7; i++) Assert.Equal(0.013, r.Step(0.013), 12);
            Assert.InRange(r.Time, 0.091 - 1e-9, 0.091 + 1e-9);
            Assert.True(r.LastStepWall > 0);
            Assert.True(double.IsFinite(r.RealtimeFactor) && r.RealtimeFactor > 0);

            // With firmware the slices are 50 µs; the requested time is still met exactly.
            var v = new VehicleRunner(VehicleTemplates.ObstacleAvoider(PartLibrary.Default), null, Options());
            Assert.Equal(0.0123, v.Step(0.0123), 12);
            Assert.InRange(v.Time, 0.0123 - 1e-9, 0.0123 + 1e-9);
        }

        [Fact]
        public void FirmwareRunsHeadlessAndPrintsOnSerial()
        {
            if (FirmwareDir() == null) return;   // repository layout not available
            var v = new VehicleRunner(VehicleTemplates.ObstacleAvoider(PartLibrary.Default), null, Options());
            Assert.Single(v.Boards);
            Assert.True(v.Boards[0].Running);
            for (int i = 0; i < 40; i++) v.Step(0.01);
            string serial = string.Concat(v.ReadSerial(null, true).Values);
            _out.WriteLine(serial);
            Assert.Contains("hazir", serial);
        }

        [Fact]
        public void PinsDriveTheRobotWhenFirmwareIsOff()
        {
            var v = new VehicleRunner(VehicleTemplates.ObstacleAvoider(PartLibrary.Default), null, Options(firmware: false));
            Assert.False(v.Boards[0].Running);
            var start = PoseOf(v, VehicleRunner.VehicleId);
            v.WritePin(null, "D5", "pwm", 0.7);
            v.WritePin(null, "D6", "pwm", 0.7);
            for (int i = 0; i < 100; i++) v.Step(0.01);
            double moved = (PoseOf(v, VehicleRunner.VehicleId) - start).Length;
            _out.WriteLine($"moved {moved:F3} m");
            Assert.True(moved > 0.05, "PWM on D5/D6 must drive the wheels through the TB6612 channels");
        }

        [Fact]
        public void DifferentialDriveGoesForwardAndTurnsLeft()
        {
            var r = new WorldRunner(WorldExamples.CarDetailed(PartLibrary.Default), Options());
            for (int i = 0; i < 30; i++) r.Step(0.01);   // settle
            Assert.True(r.TryGetBody("O1", out var s0));
            r.DriveDifferential(0.2, 0, new JsonObject());
            for (int i = 0; i < 100; i++) r.Step(0.01);
            Assert.True(r.TryGetBody("O1", out var s1));
            var fwd = s0.Orientation.Rotate(Vec3.UnitX);
            double along = (s1.Position - s0.Position).Dot(fwd);
            _out.WriteLine($"forward {along:F3} m in 1 s");
            Assert.InRange(along, 0.12, 0.30);   // open-loop helper: 0.2 m/s commanded
            r.DriveDifferential(0, 1.0, new JsonObject());
            double yaw0 = s1.Orientation.ToEuler().Yaw;
            for (int i = 0; i < 100; i++) r.Step(0.01);
            Assert.True(r.TryGetBody("O1", out var s2));
            double turned = s2.Orientation.ToEuler().Yaw - yaw0;
            _out.WriteLine($"turned {turned:F3} rad in 1 s");
            Assert.InRange(turned, 0.3, 1.3);
        }

        [Fact]
        public void QuadcopterArmsAndHoldsAltitude()
        {
            var v = new VehicleRunner(VehicleTemplates.MicroQuadcopter(PartLibrary.Default), null, Options());
            v.SetFlight(new JsonObject { ["mode"] = "altitude", ["target_altitude"] = 0.5, ["armed"] = true });
            for (int i = 0; i < 400; i++) v.Step(0.01);
            double alt = v.Mechanics!.Altitude;
            _out.WriteLine($"altitude after 4 s: {alt:F3} m");
            Assert.InRange(alt, 0.35, 0.65);
            var imu = v.Sensors.Read("imu");
            double az = J.Item((JsonArray)imu["linear_acceleration"]!, 2);
            Assert.InRange(az, 8.0, 11.5);   // hovering: the accelerometer feels ~1 g
        }

        [Fact]
        public void VirtualLidarAndCameraSeeAnObstacle()
        {
            var w = WorldExamples.CarDetailed(PartLibrary.Default);
            var wall = w.AddBody("box", 0.1, 1.0, 0.3, new WorldPose { X = 0.6 });
            wall.IsStatic = true;
            var r = new WorldRunner(w, Options());
            r.Sensors.Add(new SensorSpec { Name = "scan", Type = "lidar", Object = "O1", Offset = new Vec3(0, 0, 0.05), Params = new JsonObject { ["samples"] = 8 } });
            r.Sensors.Add(new SensorSpec { Name = "cam", Type = "camera", Object = "O1", Offset = new Vec3(0.08, 0, 0.05), Params = new JsonObject { ["width"] = 16, ["height"] = 12 } });
            var scan = r.Sensors.Read("scan");
            var ranges = (JsonArray)scan["ranges"]!;
            Assert.Equal(8, ranges.Count);
            // Beam 4 of 8 over [−π, π) points straight ahead (+x of the plate).
            double ahead = J.Item(ranges, 4);
            _out.WriteLine($"ahead {ahead:F3} m");
            Assert.InRange(ahead, 0.45, 0.6);
            var cam = r.Sensors.Read("cam");
            Assert.Equal(16 * 12 * 3, Convert.FromBase64String((string)cam["data"]!).Length);
        }

        [Fact]
        public void JsonRpcRoundTripInProcess()
        {
            using var s = Session();
            var hello = Call(s, "hello");
            Assert.Equal(ApiSession.ApiVersion, (string)hello["result"]!["api_version"]!);
            var load = Call(s, "world.load", new { example = "car", seed = 3 });
            Assert.Equal("world", (string)load["result"]!["kind"]!);
            Call(s, "sensor.add", new { type = "odometry", name = "odom", @object = "O1" });
            var step = Call(s, "sim.step", new
            {
                dt = 0.01,
                steps = 20,
                actions = new object[] { new { drive = new { v = 0.1, w = 0.0 } } },
                observe = new[] { "odom", "pose:O2", "O2.encoder" }
            })["result"]!;
            Assert.Equal(0.2, (double)step["time"]!, 9);
            Assert.Equal(20, (int)step["steps"]!);
            Assert.NotNull(step["observation"]!["odom"]!["position"]);
            Assert.NotNull(step["observation"]!["pose:O2"]!["orientation"]);
            var pose = Call(s, "object.set_pose", new { id = "O1", position = new[] { 1.0, 2.0, 0.05 }, rpy_deg = new[] { 0.0, 0.0, 90.0 } })["result"]!;
            Assert.Equal(1.0, J.Item((JsonArray)pose["position"]!, 0), 6);
            Assert.Equal(Math.PI / 2, J.Item((JsonArray)pose["rpy"]!, 2), 6);
            // The wheels moved with the chassis (whole assembly).
            var wheel = Call(s, "object.get_pose", new { id = "O4" })["result"]!;
            Assert.InRange(J.Item((JsonArray)wheel["position"]!, 1), 1.9, 2.1);
            var reset = Call(s, "world.reset", new { seed = 3, observe = new[] { "odom" } })["result"]!;
            Assert.Equal(0.0, (double)reset["time"]!);
            Assert.NotNull(Call(s, "circuit.part", new { @ref = "O2" })["result"]!["devices"]);
            Assert.True(Call(s, "methods")["result"]!.AsObject().Count > 30);

            // Errors are JSON-RPC error objects.
            var unknown = JsonNode.Parse(s.Handle("{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"nope\"}")!)!;
            Assert.Equal(ApiException.MethodNotFound, (int)unknown["error"]!["code"]!);
            var parse = JsonNode.Parse(s.Handle("{not json")!)!;
            Assert.Equal(ApiException.ParseError, (int)parse["error"]!["code"]!);
            var missing = JsonNode.Parse(s.Handle("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"sensor.read\",\"params\":{\"name\":\"nosuch\"}}")!)!;
            Assert.Equal(ApiException.NotFound, (int)missing["error"]!["code"]!);
            Assert.Null(s.Handle("{\"jsonrpc\":\"2.0\",\"method\":\"ping\"}"));   // notification: no reply
        }

        [Fact]
        public void NothingLoadedAndTokenErrors()
        {
            using var s = new ApiSession(new ApiHostOptions { Token = "secret" });
            bool auth = false;
            var denied = JsonNode.Parse(s.Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", ref auth)!)!;
            Assert.Equal(ApiException.Unauthorized, (int)denied["error"]!["code"]!);
            var ok = JsonNode.Parse(s.Handle("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"hello\",\"params\":{\"token\":\"secret\"}}", ref auth)!)!;
            Assert.Null(ok["error"]);
            Assert.True(auth);
            var notLoaded = JsonNode.Parse(s.Handle("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"sim.step\"}", ref auth)!)!;
            Assert.Equal(ApiException.NotLoaded, (int)notLoaded["error"]!["code"]!);
        }

        [Fact]
        public void BakedGeometryRoundTripsAndDrivesTheSameRun()
        {
            var world = WorldExamples.CarDetailed(PartLibrary.Default);
            var approx = new ApproximateGeometry(world, PartLibrary.Default);
            var baked = BakedGeometry.Parse(BakedGeometry.Export(world, approx), approx);
            foreach (var o in world.Objects)
            {
                var a = approx.GeometryOf(o);
                var b = baked.GeometryOf(o);
                Assert.Equal(a.Shape, b.Shape);
                Assert.Equal(a.Max.X, b.Max.X, 9);
                Assert.Equal(a.PortName, b.PortName);
            }

            var r1 = new WorldRunner(world, Options(), approx);
            var r2 = new WorldRunner(world, Options(), baked);
            for (int i = 0; i < 20; i++)
            {
                r1.Step(0.01);
                r2.Step(0.01);
            }

            Assert.Equal(PoseOf(r1, "O1").X, PoseOf(r2, "O1").X, 9);
        }

        [Fact]
        public void JsonRpcOverTcpLoopback()
        {
            using var session = Session();
            using var server = new ApiServer(session);
            server.Start(0);
            Assert.True(server.Port > 0);
            using var client = new TcpClient("127.0.0.1", server.Port);
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
            writer.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"world.load\",\"params\":{\"example\":\"quadcopter\"}}");
            var load = JsonNode.Parse(reader.ReadLine()!)!;
            Assert.Equal("vehicle", (string)load["result"]!["kind"]!);
            writer.WriteLine("[{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"flight.arm\"},{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"sim.step\",\"params\":{\"dt\":0.02,\"steps\":5}}]");
            var batch = JsonNode.Parse(reader.ReadLine()!)!.AsArray();
            Assert.Equal(2, batch.Count);
            Assert.True((bool)batch[0]!["result"]!["armed"]!);
            Assert.Equal(0.1, (double)batch[1]!["result"]!["time"]!, 9);
        }
    }
}
