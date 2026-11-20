using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware;
using RobotTwin.CoreSim.Firmware.Esp32;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// Runs a world: one analog circuit built from every board, part and cable, the
    /// firmware of every programmable board (ATmega328P emulation, ESP32 HAL model),
    /// I²C devices on their buses, and the moving parts (servo horns, motor shafts)
    /// with everything fixed onto them.
    /// </summary>
    public sealed partial class WorldSession
    {

        public const double FirmwareSlice = 50e-6;
        public const double Esp32Slice = 100e-6;

        private readonly PartLibrary _library;
        private readonly List<FirmwareCoSimulation> _avr = new List<FirmwareCoSimulation>();
        private readonly List<Esp32CoSimulation> _esp = new List<Esp32CoSimulation>();
        private readonly StringBuilder _serial = new StringBuilder();
        private readonly Stopwatch _wall = new Stopwatch();
        private readonly Dictionary<string, List<string>> _heatSignals = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _failedObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private double _lastWall;
        private double _realtime = 1.0;

        public WorldSession(PartLibrary library)
        {
            _library = library;
        }

        public WorldDocument? World { get; private set; }
        public WorldCircuit? Circuit { get; private set; }
        public AnalogSimulator? Simulator { get; private set; }
        public SimState State { get; private set; } = SimState.Stopped;
        public double Speed { get; set; } = 1.0;
        public double Time => Simulator?.Time ?? 0.0;
        public double RealtimeFactor => _realtime;
        public ObservableCollection<SimLogEntry> Log { get; } = new ObservableCollection<SimLogEntry>();
        public string SerialText => _serial.ToString();

        /// <summary>Characters ever written to the serial log (keeps growing after the buffer is trimmed).</summary>
        public long SerialTotal { get; private set; }

        /// <summary>Raised after every rendered frame of simulation.</summary>
        public event EventHandler? Updated;

        /// <summary>Raised when an object's part fails (object id, message).</summary>
        public event Action<string, string>? Failure;

        public event EventHandler? StateChanged;

        public bool IsFailed(string objectId) => _failedObjects.Contains(objectId);

        // ================================================================ start / stop

        /// <summary>Gravity, contacts, joints and actuators of the world (null when no geometry was given).</summary>
        public WorldPhysics? Physics { get; private set; }

        private int _impactLogged;

        public bool Start(WorldDocument world, Func<WorldCable, double> cableLength, out string message) => Start(world, cableLength, null, out message);

        public bool Start(WorldDocument world, Func<WorldCable, double> cableLength, Func<WorldObject, WorldBodyGeometry>? geometry, out string message)
        {
            Stop();
            Log.Clear();
            _impactLogged = 0;
            _serial.Clear();
            SerialTotal = 0;
            _failedObjects.Clear();
            World = world;
            try
            {
                Circuit = WorldCompiler.Compile(world, _library, cableLength);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is AnalogSimulationException)
            {
                message = "Dünya devresi kurulamadı: " + ex.Message;
                return false;
            }

            foreach (var w in Circuit.Compiled.Warnings) Log.Add(new SimLogEntry(0, "derleyici", w, AnalogEventKind.Warning));
            var settings = AppSettings.Current;
            Simulator = new AnalogSimulator(Circuit.Compiled.Circuit, new AnalogOptions
            {
                MaxStep = settings.MaxStep,
                RelTol = settings.RelTol,
                AmbientTempC = world.Environment.AmbientC,
                EnableDamage = settings.EnableDamage
            });
            try
            {
                Simulator.Initialize(InitialState.OperatingPoint);
            }
            catch (AnalogSimulationException)
            {
                Simulator.Initialize(InitialState.Zero);
            }

            StartRadio();   // [radio] World/WorldSession.Radio.cs
            AttachFirmware();
            if (geometry != null)
            {
                try
                {
                    Physics = WorldPhysics.Build(world, geometry, Circuit);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                {
                    Physics = null;
                    Log.Add(new SimLogEntry(0, "fizik", "Fizik sahnesi kurulamadı: " + ex.Message, AnalogEventKind.Warning));
                }

                AttachCablePhysics();   // [phys] World/WorldSession.Cables.cs
            }

            Simulator.SubstepAccepted = t =>
            {
                for (int i = 0; i < _esp.Count; i++) _esp[i].OnSubstep(t);
            };
            StartSense();   // [sense] World/WorldSession.Sense.cs
            IndexHeat();
            int parts = Circuit.Compiled.DevicesByPart.Count;
            Log.Add(new SimLogEntry(0, "dünya", $"{world.Objects.Count} nesne, {world.Cables.Count} kablo, {parts} parça tek devrede çözülüyor", AnalogEventKind.StateChange));
            Speed = settings.DefaultSpeed;
            State = SimState.Running;
            _wall.Restart();
            _lastWall = 0;
            message = string.Empty;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void Pause()
        {
            if (State != SimState.Running) return;
            State = SimState.Paused;
            _sounds.Silence();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Resume()
        {
            if (State != SimState.Paused) return;
            State = SimState.Running;
            _wall.Restart();
            _lastWall = 0;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Stop()
        {
            _sounds.Reset();
            foreach (var e in _esp) e.Dispose();
            _esp.Clear();
            _avr.Clear();
            StopRadio();   // [radio]
            StopSense();   // [sense]
            if (Simulator != null) Simulator.SubstepAccepted = null;
            Simulator = null;
            Circuit = null;
            Physics = null;
            if (State != SimState.Stopped)
            {
                State = SimState.Stopped;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Called once per rendered frame by the page while running.</summary>
        public void Frame()
        {
            if (State != SimState.Running || Simulator == null) return;
            double now = _wall.Elapsed.TotalSeconds;
            double wallDt = Math.Min(0.05, now - _lastWall);
            _lastWall = now;
            if (wallDt <= 0) return;
            var budget = Stopwatch.StartNew();
            RadioFrame();   // [radio] controller input / feedback, radio events
            double done = Advance(wallDt * Speed, budget, 0.022);
            _realtime = 0.85 * _realtime + 0.15 * (done / wallDt);
            Updated?.Invoke(this, EventArgs.Empty);
            if (State == SimState.Running && Simulator != null && World != null)
            {
                double hot = double.NaN;
                foreach (var o in World.Objects)
                {
                    double t = Temperature(o);
                    if (!double.IsNaN(t) && (double.IsNaN(hot) || t > hot)) hot = t;
                }

                _sounds.Update(Simulator.Circuit.Devices, Speed, hot);
            }
        }

        private readonly Audio.SimSounds _sounds = new Audio.SimSounds("world:");

        public void StepOnce(double seconds)
        {
            if (Simulator == null) return;
            Advance(seconds);
            RadioFrame();   // [radio]
            Updated?.Invoke(this, EventArgs.Empty);
        }

        private double Advance(double seconds, Stopwatch? budget = null, double maxWall = double.PositiveInfinity)
        {
            if (Simulator == null) return 0;
            double chunk = Math.Min(seconds, Math.Max(1e-5, Speed * 1e-3));
            if (_avr.Count > 0) chunk = Math.Min(chunk, FirmwareSlice);
            if (_esp.Count > 0) chunk = Math.Min(chunk, Esp32Slice);
            double covered = 0;
            try
            {
                while (covered < seconds - 1e-15)
                {
                    double dt = Math.Min(chunk, seconds - covered);
                    for (int i = 0; i < _avr.Count; i++) _avr[i].Advance(dt, Simulator.Time);
                    for (int i = 0; i < _esp.Count; i++) _esp[i].Advance(dt, Simulator.Time);
                    AdvanceRadio(Simulator.Time, dt);   // [radio] IR receiver outputs, radio housekeeping
                    Simulator.Step(dt);
                    Physics?.Advance(dt, Simulator);
                    AdvanceSense(dt);   // [sense] line sensors, LiDAR, GPS, IMU, compass, encoders
                    covered += dt;
                    DrainEvents();
                    if (budget != null && budget.Elapsed.TotalSeconds > maxWall) break;
                }
            }
            catch (AnalogSimulationException ex)
            {
                Log.Add(new SimLogEntry(Time, "çözücü", "Simülasyon durdu: " + ex.Message, AnalogEventKind.Failure));
                Pause();
            }

            return covered;
        }

        private void DrainEvents()
        {
            DrainCellMessages();   // [arm] World/WorldSession.Cell.cs
            if (Physics != null)
            {
                foreach (var pe in Physics.DrainEvents())
                {
                    var o = World?.Find(pe.Source);
                    // Knocks are information; only something that actually breaks or comes off is a failure.
                    bool broke = pe.Kind != RobotTwin.CoreSim.Mechanics.PhysicsEventKind.Impact;
                    if (broke) _sounds.OnEvent(AnalogEventKind.Failure, pe.Source, pe.Message);
                    else _sounds.Impact(pe.Value, pe.Source);   // [objs] heard where it hit
                    if (!broke && _impactLogged++ > 20) continue;
                    Log.Add(new SimLogEntry(Time, o?.Name ?? pe.Source, pe.Message, broke ? AnalogEventKind.Failure : AnalogEventKind.StateChange));
                    if (broke && o != null) Failure?.Invoke(o.Id, pe.Message);
                }
            }

            foreach (var e in Simulator!.DrainEvents())
            {
                string obj = ObjectOfDevice(e.DeviceId);
                string name = World?.Find(obj)?.Name ?? obj;
                string device = e.DeviceId.StartsWith("cable:", StringComparison.Ordinal)
                    ? "Kablo " + (World?.Cables.FirstOrDefault(c => "cable:" + c.Id == e.DeviceId)?.Name ?? e.DeviceId.Substring(6))
                    : name + " · " + e.DeviceId.Substring(Math.Min(e.DeviceId.Length, obj.Length + 1));
                var entry = new SimLogEntry(e.Time, device, e.Message, e.Kind);
                Log.Add(entry);
                if (e.Kind == AnalogEventKind.Failure)
                {
                    _sounds.OnEvent(e.Kind, e.DeviceId, e.Message, Simulator.Circuit.Devices.FirstOrDefault(d => d.Id == e.DeviceId));
                    _failedObjects.Add(obj);
                    Failure?.Invoke(obj, e.Message);
                    if (AppSettings.Current.PauseOnFailure) Pause();
                }
            }
        }

        private static string ObjectOfDevice(string deviceId)
        {
            if (deviceId.StartsWith("cable:", StringComparison.Ordinal)) return deviceId;
            int slash = deviceId.IndexOf('/');
            return slash > 0 ? deviceId.Substring(0, slash) : deviceId;
        }

        // ================================================================ firmware

        private void AttachFirmware()
        {
            var circuit = Circuit!;
            foreach (var prog in circuit.Programmables)
            {
                var o = World!.Find(prog.ObjectId);
                string where = (o?.Name ?? prog.ObjectId) + " " + prog.InnerRefDes;
                string? target = Firmware.ArduinoCompiler.FqbnFor(prog.Definition.Id);
                if (Firmware.ArduinoCompiler.IsEsp32(target))
                {
                    AttachEsp32(prog, where);
                    continue;
                }

                string? hex = prog.Firmware?.Hex;
                string source = prog.Firmware?.FileName ?? string.Empty;
                if (hex == null && prog.Parameters.TryGetValue("firmware", out var fw) && !string.IsNullOrEmpty(fw))
                {
                    var path = SimulationSession.ResolveFirmwarePath(fw);
                    if (path != null && System.IO.File.Exists(path) && path.EndsWith(".hex", StringComparison.OrdinalIgnoreCase))
                    {
                        hex = System.IO.File.ReadAllText(path);
                        source = System.IO.Path.GetFileName(path);
                    }
                }

                if (hex == null)
                {
                    Log.Add(new SimLogEntry(0, where, "Yüklü yazılım yok (kart boşta). Kartı düzenleyip Kod sayfasından yükleyin.", AnalogEventKind.Warning));
                    continue;
                }

                try
                {
                    var mcu = new Atmega328P();
                    mcu.LoadHex(hex);
                    string refDes = prog.RefDes;
                    var cosim = new FirmwareCoSimulation(mcu, circuit.Compiled, refDes, () => Simulator == null ? 0 : circuit.Compiled.PinVoltage(Simulator, refDes, "5V"));
                    cosim.SerialOutput += text =>
                    {
                        if (SerialRouted(cosim)) AppendSerial(where, text);   // [radio] only while the USB cable is plugged
                    };
                    _avr.Add(cosim);
                    TrackBoard(cosim, prog);   // [radio]
                    Log.Add(new SimLogEntry(0, where, $"Yazılım çalışıyor: {source} (ATmega328P @ 16 MHz)", AnalogEventKind.StateChange));
                    AttachI2c(cosim.I2c, prog);
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is System.IO.InvalidDataException || ex is FormatException)
                {
                    Log.Add(new SimLogEntry(0, where, "Yazılım yüklenemedi: " + ex.Message, AnalogEventKind.Warning));
                }
            }
        }

        private void AttachEsp32(WorldProgrammable prog, string where)
        {
            string? source = prog.Firmware?.Source;
            if (string.IsNullOrWhiteSpace(source) && prog.Parameters.TryGetValue("firmware", out var fw))
            {
                var path = SimulationSession.ResolveFirmwarePath(fw);
                if (path != null && System.IO.File.Exists(path)) source = System.IO.File.ReadAllText(path);
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                Log.Add(new SimLogEntry(0, where, "ESP32'de kod yok.", AnalogEventKind.Warning));
                return;
            }

            string? dll = Firmware.ArduinoCompiler.CachedEsp32Dll(source!);
            if (dll == null)
            {
                var build = Firmware.ArduinoCompiler.CompileEsp32(source!);
                if (!build.Success || build.Hex == null)
                {
                    Log.Add(new SimLogEntry(0, where, "ESP32 çizimi derlenemedi: " + (build.Diagnostics.FirstOrDefault()?.Message ?? "?"), AnalogEventKind.Warning));
                    return;
                }

                dll = build.Hex.Substring("esp32:".Length);
            }

            try
            {
                var cosim = new Esp32CoSimulation(dll, Circuit!.Compiled, prog.RefDes);
                cosim.SerialOutput += text =>
                {
                    if (SerialRouted(cosim)) AppendSerial(where, text);   // [radio] only while the USB cable is plugged
                };
                _esp.Add(cosim);
                TrackBoard(cosim, prog);   // [radio] radio medium bridge
                AttachCamera(cosim, prog);   // [sense] esp_camera bridge
                Log.Add(new SimLogEntry(0, where, "Yazılım çalışıyor (ESP32 HAL modeli)", AnalogEventKind.StateChange));
                AttachI2c(cosim.I2c, prog);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is BadImageFormatException || ex is InvalidOperationException || ex is System.IO.IOException)
            {
                Log.Add(new SimLogEntry(0, where, "ESP32 başlatılamadı: " + ex.Message, AnalogEventKind.Warning));
            }
        }

        private string _serialSource = string.Empty;

        private void AppendSerial(string source, string text)
        {
            if (source != _serialSource)
            {
                int before = _serial.Length;
                if (_serial.Length > 0 && _serial[_serial.Length - 1] != '\n') _serial.Append('\n');
                _serial.Append('[').Append(source).Append("] ");
                SerialTotal += _serial.Length - before;
                _serialSource = source;
            }

            _serial.Append(text);
            SerialTotal += text.Length;
            if (_serial.Length > 200_000) _serial.Remove(0, _serial.Length - 200_000);
        }

        public void SendSerial(string text)
        {
            foreach (var a in _avr) if (SerialRouted(a)) a.Mcu.WriteSerial(text);   // [radio] USB-connected boards only
            foreach (var e in _esp) if (SerialRouted(e)) e.SendSerial(text);
        }

        private readonly Dictionary<string, II2cDevice> _i2c = new Dictionary<string, II2cDevice>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Protocol model of an I²C module (display, IMU) by world designator ("O4/DS1", or the component object's part).</summary>
        public II2cDevice? I2cDeviceOf(string fullRefDes) => _i2c.TryGetValue(fullRefDes, out var d) ? d : null;

        private void AttachI2c(I2cBus bus, WorldProgrammable prog)
        {
            // On the board itself, then across cables: the two nets are joined by a wire resistor, so compare nodes instead of names.
            int sdaNode = NodeOfAny(prog.RefDes, "SDA", "A4", "IO21"), sclNode = NodeOfAny(prog.RefDes, "SCL", "A5", "IO22");
            if (sdaNode <= 0 || sclNode <= 0) return;
            var sdaReach = Reach(sdaNode);
            var sclReach = Reach(sclNode);
            foreach (var (fullRef, def, parameters) in AllParts())
            {
                if (def.FindPin("SDA") == null || def.FindPin("SCL") == null || def.Symbol == SymbolKind.Board) continue;
                int a = Circuit!.Compiled.PinNode(fullRef, "SDA"), b = Circuit.Compiled.PinNode(fullRef, "SCL");
                if (!sdaReach.Contains(a) || !sclReach.Contains(b)) continue;
                byte address = ParseAddress(parameters.TryGetValue("address", out var ad) ? ad : null);
                II2cDevice? device = def.Id switch
                {
                    "oled-ssd1306" => new RobotTwin.CoreSim.Firmware.Devices.Ssd1306(parameters.TryGetValue("height", out var h) && h == "32" ? 32 : 64, address == 0 ? (byte)0x3C : address),
                    "lcd-i2c" => new RobotTwin.CoreSim.Firmware.Devices.Pcf8574Lcd(
                        int.TryParse(parameters.TryGetValue("columns", out var cs) ? cs : "16", out var cc) ? cc : 16,
                        int.TryParse(parameters.TryGetValue("rows", out var rs) ? rs : "2", out var rr) ? rr : 2, address == 0 ? (byte)0x27 : address),
                    "mpu6050" => new RobotTwin.CoreSim.Firmware.Devices.Mpu6050(address == 0 ? (byte)0x68 : address) { AccelNoise = 0.02, GyroNoise = 0.002, AccelZ = World!.Environment.Gravity },
                    _ => SenseI2c(def.Id, parameters, address, fullRef)   // [sense] HMC5883L compass
                };
                if (device == null) continue;
                string r = fullRef;
                bus.Devices.Add(new GatedI2cDevice(device, () => Simulator != null &&
                    Circuit.Compiled.PinVoltage(Simulator, r, "VCC") - Circuit.Compiled.PinVoltage(Simulator, r, "GND") > 2.7));
                _i2c[r] = device;
                Log.Add(new SimLogEntry(0, r, $"I²C veri yoluna bağlandı ({prog.RefDes}, 0x{device.Address:X2})", AnalogEventKind.StateChange));
            }
        }

        private int NodeOfAny(string refDes, params string[] pins)
        {
            foreach (var p in pins)
            {
                int n = Circuit!.Compiled.PinNode(refDes, p);
                if (n > 0) return n;
            }

            return -1;
        }

        /// <summary>Nodes reachable from a node through cables only (wires are tiny resistors, not shared nets).</summary>
        private HashSet<int> Reach(int node)
        {
            var set = new HashSet<int> { node };
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (var cable in Circuit!.CableDevices.Values)
                {
                    if (set.Contains(cable.A) && set.Add(cable.B)) grew = true;
                    if (set.Contains(cable.B) && set.Add(cable.A)) grew = true;
                }
            }

            return set;
        }

        private IEnumerable<(string FullRef, PartDefinition Def, IReadOnlyDictionary<string, string> Parameters)> AllParts()
        {
            foreach (var o in World!.Objects)
            {
                if (o.Kind == WorldObjectKind.Board)
                {
                    var sch = World.FindDesign(o.DesignId)?.Package.Schematic;
                    if (sch == null) continue;
                    foreach (var p in sch.Parts)
                    {
                        var def = _library.Find(p.PartId);
                        if (def != null) yield return (WorldCircuit.Prefix(o.Id) + p.RefDes, def, sch.ResolveParameters(_library, p));
                    }
                }
                else if (o.Kind == WorldObjectKind.Component && o.PartId != null)
                {
                    var def = _library.Find(o.PartId);
                    var r = Circuit!.ComponentRef(o.Id);
                    if (def == null || r == null) continue;
                    var preset = o.PresetId != null ? _library.FindPreset(o.PresetId) : null;
                    yield return (r, def, _library.ResolveParameters(def, preset, o.Parameters));
                }
            }
        }

        private static byte ParseAddress(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            string t = text!.Trim();
            try
            {
                return t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToByte(t.Substring(2), 16) : byte.Parse(t, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException)
            {
                return 0;
            }
        }

        // ================================================================ readouts for the scene

        /// <summary>Angle of an object's moving port in radians (servo horn from 90° = centre, motor output shaft).</summary>
        public double PortAngle(WorldObject o)
        {
            if (Circuit == null || o.Kind != WorldObjectKind.Component) return 0;
            var r = Circuit.ComponentRef(o.Id);
            if (r == null) return 0;
            foreach (var d in Circuit.Compiled.DevicesOf(r))
            {
                if (d is Servo s) return (s.AngleDeg - 90.0) * Math.PI / 180.0;
                if (d is DcMotor m) return m.OutputAngle;
            }

            return 0;
        }

        /// <summary>LED / lamp brightness (0–1) on an object, keyed like <see cref="ObjectModel.Lights"/>.</summary>
        public double Brightness(WorldObject o, string innerRefDes)
        {
            if (Circuit == null) return 0;
            string r = o.Kind == WorldObjectKind.Component ? Circuit.ComponentRef(o.Id) ?? string.Empty : Circuit.FullRefDes(o.Id, innerRefDes);
            foreach (var d in Circuit.Compiled.DevicesOf(r))
            {
                if (d is Led led) return led.IsFailed ? 0 : Math.Min(1, led.Brightness);
                if (d is Lamp lamp) return lamp.IsFailed ? 0 : Math.Min(1, lamp.Brightness);
            }

            return 0;
        }

        private void IndexHeat()
        {
            _heatSignals.Clear();
            if (Circuit == null || World == null) return;
            foreach (var o in World.Objects)
            {
                var list = new List<string>();
                string prefix = WorldCircuit.Prefix(o.Id);
                foreach (var kv in Circuit.Compiled.DevicesByPart)
                {
                    if (!kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    foreach (var d in kv.Value) list.Add(d.Id + ":T");
                }

                _heatSignals[o.Id] = list;
            }
        }

        /// <summary>Temperature of one part on a board object (°C), or NaN.</summary>
        public double PartTemperature(WorldObject o, string innerRefDes)
        {
            if (Circuit == null || Simulator == null) return double.NaN;
            double max = double.NaN;
            foreach (var d in Circuit.Compiled.DevicesOf(Circuit.FullRefDes(o.Id, innerRefDes)))
            {
                if (Simulator.Signals.TryGetValue(d.Id + ":T", out double t) && (double.IsNaN(max) || t > max)) max = t;
            }

            return max;
        }

        /// <summary>True when a device of a part on a board object has failed.</summary>
        public bool PartFailed(WorldObject o, string innerRefDes) =>
            Circuit != null && Circuit.Compiled.DevicesOf(Circuit.FullRefDes(o.Id, innerRefDes)).Any(d => d.IsFailed);

        /// <summary>Hottest part temperature on an object (°C), or NaN.</summary>
        public double Temperature(WorldObject o)
        {
            if (Simulator == null || !_heatSignals.TryGetValue(o.Id, out var keys)) return double.NaN;
            double max = double.NaN;
            foreach (var k in keys)
            {
                if (Simulator.Signals.TryGetValue(k, out double t) && (double.IsNaN(max) || t > max)) max = t;
            }

            return max;
        }

        /// <summary>Current through a cable (A, signed A→B) and whether it burned.</summary>
        public (double Current, bool Burned, double Power) CableState(WorldCable cable)
        {
            if (Circuit == null || Simulator == null || !Circuit.CableDevices.TryGetValue(cable.Id, out var r)) return (0, false, 0);
            double v = Simulator.NodeVoltage(r.A) - Simulator.NodeVoltage(r.B);
            double i = r.IsFailed ? 0 : v / Math.Max(1e-9, r.Resistance);
            return (i, r.IsFailed, i * i * r.Resistance);
        }

        public double Voltage(CableEnd end) => Circuit == null || Simulator == null ? double.NaN : Circuit.Voltage(Simulator, end);

        /// <summary>Live screen of a display object (OLED framebuffer / LCD text), if any.</summary>
        public II2cDevice? DisplayOf(WorldObject o)
        {
            if (Circuit == null) return null;
            if (o.Kind == WorldObjectKind.Component)
            {
                var r = Circuit.ComponentRef(o.Id);
                return r == null ? null : I2cDeviceOf(r);
            }

            return null;
        }
    }
}
