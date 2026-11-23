using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Threading;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.World;

namespace RobotWin.Studio.Automation
{
    /// <summary>
    /// [api] The app as an automation server: "RobotWinStudio.exe --api 5555" runs without a window and
    /// serves the JSON-RPC API (docs/API.md). Worlds get their bodies and cable lengths from the same 3D
    /// models the world page uses, so the physics is identical to the interactive app; ESP32 sketches are
    /// built with the app's MSVC toolchain settings. "--bake-geometry world.rwworld" writes that geometry
    /// next to the world for the cross-platform headless tool.
    /// </summary>
    public static class StudioApiHost
    {
        private static ApiServer? _server;
        private static ApiSession? _session;

        /// <summary>Handles --api / --bake-geometry; returns false when neither was given.</summary>
        public static bool TryRun(Application app, string[] args)
        {
            string? Arg(string name)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }

            if (Arg("--bake-geometry") is { } bake)
            {
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                int code = Bake(bake, Arg("--out"));
                app.Shutdown(code);
                return true;
            }

            if (Arg("--api") is not { } portText) return false;
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shell.UserLibrary.LoadAll(PartLibrary.Default);
            var dispatcher = app.Dispatcher;
            var host = new ApiHostOptions
            {
                Backend = "studio",
                Library = PartLibrary.Default,
                Token = Arg("--token"),
                Geometry = (world, path) => SceneGeometry.For(world),
                Esp32Builder = BuildEsp32,
                Shutdown = () => dispatcher.BeginInvoke(new Action(() => app.Shutdown(0)), DispatcherPriority.Background)
            };
            host.FirmwareSearchPath.Add(Path.Combine(AppContext.BaseDirectory, "Firmware"));
            _session = new ApiSession(host);
            var preload = new System.Text.Json.Nodes.JsonObject { ["seed"] = int.Parse(Arg("--seed") ?? "0", CultureInfo.InvariantCulture), ["firmware"] = !Array.Exists(args, a => a == "--no-firmware") };
            if (Arg("--world") is { } world) preload["path"] = Path.GetFullPath(world);
            else if (Arg("--project") is { } project) preload["path"] = Path.GetFullPath(project);
            else if (Arg("--example") is { } example) preload["example"] = example;
            if (preload.ContainsKey("path") || preload.ContainsKey("example"))
            {
                try
                {
                    _session.Call("world.load", preload);
                }
                catch (ApiException ex)
                {
                    Console.Error.WriteLine("Yüklenemedi: " + ex.Message);
                    app.Shutdown(2);
                    return true;
                }
            }

            var address = IPAddress.Parse(Arg("--host") ?? "127.0.0.1");
            if (!IPAddress.IsLoopback(address) && !Array.Exists(args, a => a == "--allow-remote"))
            {
                Console.Error.WriteLine("Yerel olmayan adres için --allow-remote gerekli. / Non-loopback address needs --allow-remote.");
                app.Shutdown(1);
                return true;
            }

            // Every request runs on the UI thread: the 3D models behind the geometry are WPF objects.
            _server = new ApiServer(_session) { Invoker = call => dispatcher.Invoke(call) };
            try
            {
                _server.Start(int.Parse(portText, CultureInfo.InvariantCulture), address);
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                Console.Error.WriteLine("Port açılamadı: " + ex.Message);
                app.Shutdown(3);
                return true;
            }

            app.Exit += (_, _) =>
            {
                _server?.Stop();
                _session?.Dispose();
            };
            Console.WriteLine($"ROBOTWIN_API_READY port={_server.Port} host={_server.Address} backend=studio api={ApiSession.ApiVersion}");
            Console.Out.Flush();
            if (Arg("--parent-pid") is { } pidText && int.TryParse(pidText, out int pid)) WatchParent(app, pid);
            return true;
        }

        private static void WatchParent(Application app, int pid)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) =>
            {
                bool alive;
                try
                {
                    using var p = System.Diagnostics.Process.GetProcessById(pid);
                    alive = !p.HasExited;
                }
                catch (ArgumentException)
                {
                    alive = false;
                }

                if (alive) return;
                timer.Stop();
                app.Shutdown(0);
            };
            timer.Start();
        }

        private static string? BuildEsp32(string source)
        {
            string? dll = Firmware.ArduinoCompiler.CachedEsp32Dll(source);
            if (dll != null) return dll;
            var build = Firmware.ArduinoCompiler.CompileEsp32(source);
            return build.Success && build.Hex != null && build.Hex.StartsWith("esp32:", StringComparison.Ordinal) ? build.Hex.Substring(6) : null;
        }

        private static int Bake(string worldPath, string? outPath)
        {
            try
            {
                Shell.UserLibrary.LoadAll(PartLibrary.Default);
                var world = WorldDocument.Load(worldPath);
                string json = BakedGeometry.Export(world, SceneGeometry.For(world));
                File.WriteAllText(outPath ?? worldPath + BakedGeometry.Suffix, json);
                return 0;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        /// <summary>Geometry from an off-screen <see cref="WorldScene"/>: exactly what the world page hands the physics.</summary>
        private sealed class SceneGeometry : IWorldGeometry
        {
            private readonly WorldScene _scene;

            private SceneGeometry(WorldScene scene)
            {
                _scene = scene;
            }

            public static SceneGeometry For(WorldDocument world)
            {
                var scene = new WorldScene { Library = PartLibrary.Default };
                scene.MassOf = o => EstimateMass(world, o) / 1000.0;
                scene.Load(world);
                return new SceneGeometry(scene);
            }

            public WorldBodyGeometry GeometryOf(WorldObject o) => _scene.GeometryOf(o);

            public double CableLength(WorldCable cable) => _scene.CableLength(cable);

            public CablePins? PinsOf(WorldCable cable) => _scene.CablePinsOf(cable);
        }

        /// <summary>Mass in grams — mirrors WorldPage.EstimateMass (kept in step with it).</summary>
        private static double EstimateMass(WorldDocument world, WorldObject o)
        {
            if (o.MassKg is { } m) return m * 1000;
            double density = MechatronicSystem.DensityOf(o.Material == "KAUÇUK" ? "ABS" : o.Material) * Math.Max(0.05, o.Infill);
            switch (o.Kind)
            {
                case WorldObjectKind.Body:
                    double v = o.Shape switch
                    {
                        "cylinder" => Math.PI * o.SizeX * o.SizeX / 4 * o.SizeZ,
                        "wheel" => Math.PI * o.SizeX * o.SizeX / 4 * o.SizeZ * 0.55,
                        "sphere" => Math.PI / 6 * o.SizeX * o.SizeX * o.SizeX,
                        _ => o.SizeX * o.SizeY * o.SizeZ
                    };
                    return v * density * 1000;
                case WorldObjectKind.Model when o.Mesh != null:
                    try
                    {
                        var mesh = MeshModel.FromEmbedded(o.Mesh);
                        return Math.Abs(MassProperties.FromMesh(mesh, density).Mass) * 1000;
                    }
                    catch (FormatException)
                    {
                        return 0;
                    }

                case WorldObjectKind.Component:
                    return Pages.AssemblyEstimates.ComponentMass(o) * 1000;
                case WorldObjectKind.Board:
                    var b = world.FindDesign(o.DesignId)?.Package.Schematic.Board;
                    double area = b != null ? b.Width * b.Height * 1e-6 : 0.003;
                    return (area * 0.0016 * 1850 + (world.FindDesign(o.DesignId)?.Package.Schematic.Parts.Count ?? 0) * 0.0008) * 1000;
                default:
                    return 0;
            }
        }
    }
}
