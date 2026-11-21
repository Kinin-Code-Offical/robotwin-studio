using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware;
using RobotTwin.CoreSim.Firmware.Esp32;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Automation
{
    public enum RunnerEventKind
    {
        Info,
        Warning,
        Failure,
        Impact
    }

    /// <summary>Something that happened during the run (log line, part failure, crash, knock).</summary>
    public sealed class RunnerEvent
    {
        public RunnerEvent(double time, string source, RunnerEventKind kind, string message, double value = double.NaN)
        {
            Time = time;
            Source = source;
            Kind = kind;
            Message = message;
            Value = value;
        }

        public double Time { get; }
        public string Source { get; }
        public RunnerEventKind Kind { get; }
        public string Message { get; }
        public double Value { get; }

        public JsonObject ToJson() => new JsonObject
        {
            ["time"] = J.N(Time),
            ["source"] = Source,
            ["kind"] = Kind.ToString().ToLowerInvariant(),
            ["message"] = Message,
            ["value"] = J.N(Value)
        };
    }

    /// <summary>World-frame state of an object's frame (origin = its pose point).</summary>
    public readonly struct BodyState
    {
        public BodyState(Vec3 position, Quat orientation, Vec3 velocity, Vec3 angularVelocity)
        {
            Position = position;
            Orientation = orientation;
            Velocity = velocity;
            AngularVelocity = angularVelocity;
        }

        public Vec3 Position { get; }
        public Quat Orientation { get; }
        public Vec3 Velocity { get; }
        public Vec3 AngularVelocity { get; }
    }

    /// <summary>Settings shared by the headless runners.</summary>
    public sealed class RunnerOptions
    {
        public PartLibrary Library { get; set; } = PartLibrary.Default;

        /// <summary>Seed of everything random (sensor noise, MPU-6050 noise, ESP32 random(), reset randomisation).</summary>
        public int Seed { get; set; }

        /// <summary>Run the boards' firmware; false = boards are idle and the client drives their pins.</summary>
        public bool Firmware { get; set; } = true;

        /// <summary>Largest analog solver step (the app's default, Ayarlar → Simülasyon).</summary>
        public double MaxStep { get; set; } = 2e-4;

        public double RelTol { get; set; } = 1e-3;

        /// <summary>Parts overheat, burn and fail like in the app.</summary>
        public bool EnableDamage { get; set; } = true;

        /// <summary>Tolerance analysis: draw part values from their bands (run index = <see cref="ToleranceRun"/>).</summary>
        public Design.ToleranceSettings? Tolerances { get; set; }

        public int ToleranceRun { get; set; }

        /// <summary>Where "builtin:xxx.hex" firmware references are looked up (the app's Firmware folder).</summary>
        public List<string> FirmwareSearchPath { get; } = new List<string>();

        /// <summary>Native DLL for an ESP32 board (sketch source in, DLL path out); null = ESP32 boards stay idle.</summary>
        public Func<string, string?>? Esp32Builder { get; set; }

        public string? ResolveFirmware(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (value!.StartsWith("builtin:", StringComparison.Ordinal))
            {
                string name = value.Substring(8);
                foreach (var dir in FirmwareSearchPath)
                {
                    string p = System.IO.Path.Combine(dir, name);
                    if (System.IO.File.Exists(p)) return p;
                }

                return System.IO.Path.Combine(AppContext.BaseDirectory, "Firmware", name);
            }

            return value;
        }
    }

    /// <summary>A board running firmware in the run.</summary>
    public sealed class BoardInfo
    {
        public string ObjectId { get; set; } = string.Empty;
        public string RefDes { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PartId { get; set; } = string.Empty;
        public string Mcu { get; set; } = string.Empty;
        public string Firmware { get; set; } = string.Empty;
        public bool Running { get; set; }
        internal FirmwareCoSimulation? Avr;
        internal Esp32CoSimulation? Esp;

        public JsonObject ToJson() => new JsonObject
        {
            ["object"] = ObjectId,
            ["refdes"] = RefDes,
            ["name"] = Name,
            ["part"] = PartId,
            ["mcu"] = Mcu,
            ["firmware"] = Firmware,
            ["running"] = Running
        };
    }

    /// <summary>
    /// Headless, deterministic stepping of a RobotWin simulation: the analog circuit, the firmware of
    /// every programmable board and the mechanics advance in lockstep with exactly the slicing the app
    /// uses (1 ms chunks, 50 µs with an ATmega328P, 100 µs with an ESP32). No UI, no wall clock: a
    /// step takes as long as the computer needs, so runs can be much faster (or slower) than real time
    /// and two runs with the same seed and the same commands give the same result.
    /// </summary>
    public abstract class SimRunner : IDisposable
    {
        public const double FirmwareSlice = 50e-6;
        public const double Esp32Slice = 100e-6;
        private const int MaxEvents = 5000;
        private const int MaxSerial = 1_000_000;

        private readonly List<RunnerEvent> _events = new List<RunnerEvent>();
        private readonly Dictionary<string, StringBuilder> _serial = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<DigitalPin, PinCommand> _pinOverrides = new Dictionary<DigitalPin, PinCommand>();
        private readonly Dictionary<AveragedPwmDriver, (double Duty, int Control)> _dutyOverrides = new Dictionary<AveragedPwmDriver, (double, int)>();
        protected readonly List<FirmwareCoSimulation> Avr = new List<FirmwareCoSimulation>();
        protected readonly List<Esp32CoSimulation> Esp = new List<Esp32CoSimulation>();
        protected readonly Dictionary<string, II2cDevice> I2cDevices = new Dictionary<string, II2cDevice>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Firmware replacing a board's own (board refdes or object id → Intel HEX text), applied on reset.</summary>
        public Dictionary<string, string> FirmwareOverrides { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        protected SimRunner(RunnerOptions options)
        {
            Options = options ?? new RunnerOptions();
            Seed = Options.Seed;
            Sensors = new SensorRegistry(this);
        }

        public RunnerOptions Options { get; }
        public PartLibrary Library => Options.Library;
        public int Seed { get; protected set; }

        /// <summary>"world" (a .rwworld sandbox) or "vehicle" (a project with a robot car / drone assembly).</summary>
        public abstract string Kind { get; }

        public abstract string Title { get; }
        public AnalogSimulator? Simulator { get; protected set; }
        public CompiledCircuit? Compiled { get; protected set; }
        public double Time => Simulator?.Time ?? 0;
        public SensorRegistry Sensors { get; }
        public List<BoardInfo> Boards { get; } = new List<BoardInfo>();

        /// <summary>Set when the solver gave up (the app pauses); reset to continue.</summary>
        public bool Halted { get; protected set; }

        public string HaltReason { get; protected set; } = string.Empty;

        /// <summary>Objects with a failed (burned, broken) part.</summary>
        public HashSet<string> Failed { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public long StepCalls { get; private set; }
        public double LastStepWall { get; private set; }
        public double LastStepSim { get; private set; }
        public double TotalWall { get; private set; }
        public double TotalSim { get; private set; }

        /// <summary>Simulated seconds per wall-clock second of the last step call.</summary>
        public double RealtimeFactor => LastStepWall > 0 ? LastStepSim / LastStepWall : double.NaN;

        public double AverageRealtimeFactor => TotalWall > 0 ? TotalSim / TotalWall : double.NaN;

        /// <summary>Called after every lockstep slice (dt) — sensors that integrate over time hook in here.</summary>
        public event Action<double>? Sliced;

        // ================================================================ build / reset

        /// <summary>Rebuilds the run from the loaded document: everything back to its initial state.</summary>
        public void Reset(int? seed = null)
        {
            if (seed.HasValue) Seed = seed.Value;
            Teardown();
            Halted = false;
            HaltReason = string.Empty;
            Failed.Clear();
            _events.Clear();
            _serial.Clear();
            _pinOverrides.Clear();
            _dutyOverrides.Clear();
            Boards.Clear();
            I2cDevices.Clear();
            StepCalls = 0;
            TotalSim = TotalWall = LastStepSim = LastStepWall = 0;
            Build();
            Sensors.Rebuild();
        }

        /// <summary>Compiles the circuit, starts the solver, attaches firmware and mechanics.</summary>
        protected abstract void Build();

        protected virtual void Teardown()
        {
            foreach (var e in Esp) e.Dispose();
            Esp.Clear();
            Avr.Clear();
            if (Simulator != null) Simulator.SubstepAccepted = null;
            Simulator = null;
            Compiled = null;
        }

        public void Dispose()
        {
            Teardown();
            GC.SuppressFinalize(this);
        }

        protected AnalogSimulator StartSolver(AnalogCircuit circuit, double ambientC)
        {
            var sim = new AnalogSimulator(circuit, new AnalogOptions
            {
                MaxStep = Options.MaxStep,
                RelTol = Options.RelTol,
                AmbientTempC = ambientC,
                EnableDamage = Options.EnableDamage
            });
            try
            {
                sim.Initialize(InitialState.OperatingPoint);
            }
            catch (AnalogSimulationException)
            {
                sim.Initialize(InitialState.Zero);
            }

            sim.SubstepAccepted = t =>
            {
                for (int i = 0; i < Esp.Count; i++) Esp[i].OnSubstep(t);
            };
            return sim;
        }

        // ================================================================ stepping

        /// <summary>
        /// Advances the simulation by <paramref name="seconds"/> of simulated time (slicing exactly like
        /// the app's world session). Returns the simulated time covered (less only when the solver halts).
        /// </summary>
        public double Step(double seconds)
        {
            var sim = Simulator ?? throw new ApiException(ApiException.NotLoaded, "Yüklü dünya yok");
            if (Halted || seconds <= 0) return 0;
            var watch = Stopwatch.StartNew();
            double chunk = Math.Min(seconds, 1e-3);
            if (Avr.Count > 0) chunk = Math.Min(chunk, FirmwareSlice);
            if (Esp.Count > 0) chunk = Math.Min(chunk, Esp32Slice);
            double covered = 0;
            try
            {
                while (covered < seconds - 1e-15)
                {
                    double dt = Math.Min(chunk, seconds - covered);
                    for (int i = 0; i < Avr.Count; i++) Avr[i].Advance(dt, sim.Time);
                    for (int i = 0; i < Esp.Count; i++) Esp[i].Advance(dt, sim.Time);
                    ApplyOverrides(sim);
                    sim.Step(dt);
                    AdvanceMechanics(dt);
                    covered += dt;
                    Sliced?.Invoke(dt);
                    DrainSolverEvents(sim);
                    DrainMechanicsEvents();
                }
            }
            catch (AnalogSimulationException ex)
            {
                Halted = true;
                HaltReason = ex.Message;
                AddEvent(new RunnerEvent(sim.Time, "çözücü", RunnerEventKind.Failure, "Simülasyon durdu: " + ex.Message));
            }

            StepCalls++;
            LastStepWall = watch.Elapsed.TotalSeconds;
            LastStepSim = covered;
            TotalWall += LastStepWall;
            TotalSim += covered;
            return covered;
        }

        protected abstract void AdvanceMechanics(double dt);

        protected virtual void DrainMechanicsEvents()
        {
        }

        private void DrainSolverEvents(AnalogSimulator sim)
        {
            if (sim.Events.Count == 0) return;
            foreach (var e in sim.DrainEvents())
            {
                string obj = ObjectOfDevice(e.DeviceId);
                var kind = e.Kind == AnalogEventKind.Failure ? RunnerEventKind.Failure : e.Kind == AnalogEventKind.Warning ? RunnerEventKind.Warning : RunnerEventKind.Info;
                AddEvent(new RunnerEvent(e.Time, e.DeviceId, kind, e.Message));
                if (kind == RunnerEventKind.Failure) Failed.Add(obj);
            }
        }

        /// <summary>Object (world) or part (vehicle) a device belongs to.</summary>
        protected virtual string ObjectOfDevice(string deviceId)
        {
            int dot = deviceId.IndexOf('.');
            return dot > 0 ? deviceId.Substring(0, dot) : deviceId;
        }

        public void AddEvent(RunnerEvent e)
        {
            _events.Add(e);
            if (_events.Count > MaxEvents) _events.RemoveRange(0, _events.Count - MaxEvents);
        }

        public List<RunnerEvent> DrainEvents(int max = int.MaxValue)
        {
            int n = Math.Min(max, _events.Count);
            var list = _events.GetRange(0, n);
            _events.RemoveRange(0, n);
            return list;
        }

        public int PendingEvents => _events.Count;

        // ================================================================ firmware

        protected FirmwareCoSimulation? AttachAvr(BoardInfo board, string hex, Func<double> supply)
        {
            try
            {
                var mcu = new Atmega328P();
                mcu.LoadHex(hex);
                var cosim = new FirmwareCoSimulation(mcu, Compiled!, board.RefDes, supply);
                string key = board.RefDes;
                cosim.SerialOutput += text => AppendSerial(key, text);
                Avr.Add(cosim);
                board.Avr = cosim;
                board.Running = true;
                board.Mcu = "atmega328p";
                AddEvent(new RunnerEvent(0, board.Name, RunnerEventKind.Info, $"Yazılım çalışıyor: {board.Firmware} (ATmega328P @ 16 MHz)"));
                return cosim;
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is System.IO.InvalidDataException || ex is FormatException || ex is UnauthorizedAccessException)
            {
                AddEvent(new RunnerEvent(0, board.Name, RunnerEventKind.Warning, "Yazılım yüklenemedi: " + ex.Message));
                return null;
            }
        }

        protected Esp32CoSimulation? AttachEsp32(BoardInfo board, string? source)
        {
            board.Mcu = "esp32";
            if (string.IsNullOrWhiteSpace(source))
            {
                AddEvent(new RunnerEvent(0, board.Name, RunnerEventKind.Warning, "ESP32'de kod yok."));
                return null;
            }

            string? dll = Options.Esp32Builder?.Invoke(source!);
            if (dll == null)
            {
                AddEvent(new RunnerEvent(0, board.Name, RunnerEventKind.Warning, "ESP32 çizimi bu modda derlenemiyor (MSVC yok) — kart boşta."));
                return null;
            }

            try
            {
                var cosim = new Esp32CoSimulation(dll, Compiled!, board.RefDes, null, new Esp32Options { RandomSeed = (ulong)(uint)Seed * 2654435761UL + 1 });
                string key = board.RefDes;
                cosim.SerialOutput += text => AppendSerial(key, text);
                Esp.Add(cosim);
                board.Esp = cosim;
                board.Running = true;
                AddEvent(new RunnerEvent(0, board.Name, RunnerEventKind.Info, "Yazılım çalışıyor (ESP32 HAL modeli)"));
                return cosim;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is BadImageFormatException || ex is InvalidOperationException || ex is System.IO.IOException)
            {
                AddEvent(new RunnerEvent(0, board.Name, RunnerEventKind.Warning, "ESP32 başlatılamadı: " + ex.Message));
                return null;
            }
        }

        public static bool IsEsp32Part(string partId) => partId.StartsWith("esp32", StringComparison.OrdinalIgnoreCase);

        /// <summary>Every part of the run with its full designator (for I²C discovery and part queries).</summary>
        protected abstract IEnumerable<(string FullRef, PartDefinition Def, IReadOnlyDictionary<string, string> Parameters)> AllParts();

        /// <summary>Nodes electrically joined to <paramref name="node"/> by wires (cables in a world).</summary>
        protected virtual HashSet<int> Reach(int node) => new HashSet<int> { node };

        /// <summary>Puts the I²C modules wired to a board's SDA/SCL on its bus (same rules as the app).</summary>
        protected void AttachI2c(I2cBus bus, string boardRef, double gravity)
        {
            var c = Compiled!;
            int sda = NodeOfAny(boardRef, "SDA", "A4", "IO21"), scl = NodeOfAny(boardRef, "SCL", "A5", "IO22");
            if (sda <= 0 || scl <= 0) return;
            var sdaReach = Reach(sda);
            var sclReach = Reach(scl);
            int index = 0;
            foreach (var (fullRef, def, parameters) in AllParts())
            {
                if (def.FindPin("SDA") == null || def.FindPin("SCL") == null || def.Symbol == SymbolKind.Board) continue;
                int a = c.PinNode(fullRef, "SDA"), b = c.PinNode(fullRef, "SCL");
                if (!sdaReach.Contains(a) || !sclReach.Contains(b)) continue;
                byte address = ParseAddress(parameters.TryGetValue("address", out var ad) ? ad : null);
                II2cDevice? device = def.Id switch
                {
                    "oled-ssd1306" => new Firmware.Devices.Ssd1306(parameters.TryGetValue("height", out var h) && h == "32" ? 32 : 64, address == 0 ? (byte)0x3C : address),
                    "lcd-i2c" => new Firmware.Devices.Pcf8574Lcd(
                        int.TryParse(parameters.TryGetValue("columns", out var cs) ? cs : "16", out var cc) ? cc : 16,
                        int.TryParse(parameters.TryGetValue("rows", out var rs) ? rs : "2", out var rr) ? rr : 2, address == 0 ? (byte)0x27 : address),
                    "mpu6050" => SeededMpu(address == 0 ? (byte)0x68 : address, gravity, J.StableHash(fullRef) ^ (Seed * 7919 + ++index)),
                    "bmp280" => Firmware.SpiWiring.Barometer(fullRef, parameters, new Firmware.SpiWiring.Context { AmbientC = () => AmbientC, AltitudeOf = AltitudeOf, Seed = Seed * 7919 + 17 }),
                    _ => null
                };
                if (device == null) continue;
                string r = fullRef;
                bus.Devices.Add(new GatedI2cDevice(device, () => Simulator != null &&
                    c.PinVoltage(Simulator, r, "VCC") - c.PinVoltage(Simulator, r, "GND") > 2.7));
                I2cDevices[r] = device;
                AddEvent(new RunnerEvent(0, r, RunnerEventKind.Info, $"I²C veri yoluna bağlandı ({boardRef}, 0x{device.Address:X2})"));
            }
        }

        /// <summary>Ambient temperature for sensor models (°C).</summary>
        protected virtual double AmbientC => 25;

        /// <summary>Height above sea level of the part with this designator (m), for barometers.</summary>
        protected virtual double AltitudeOf(string fullRef) => 0;

        /// <summary>Puts the SPI devices wired to an ESP32's GPIOs on its SPI master (same rules as the app).</summary>
        protected void AttachSpi(Firmware.Esp32.Esp32CoSimulation esp, string boardRef)
        {
            var c = Compiled!;
            var ctx = new Firmware.SpiWiring.Context
            {
                PinVolts = (r, p) => Simulator == null ? 0 : c.PinVoltage(Simulator, r, p),
                AmbientC = () => AmbientC,
                AltitudeOf = AltitudeOf,
                Seed = Seed * 7919 + 17
            };
            var slaves = Firmware.SpiWiring.Attach(AllParts(), c.PinNode, g => c.PinNode(boardRef, "IO" + g), Reach,
                r =>
                {
                    if (Simulator == null) return false;
                    double vcc = c.PinNode(r, "VDD") > 0 ? c.PinVoltage(Simulator, r, "VDD") - c.PinVoltage(Simulator, r, "DGND") : c.PinVoltage(Simulator, r, "VCC") - c.PinVoltage(Simulator, r, "GND");
                    return vcc > 2.5;
                },
                ctx, m => AddEvent(new RunnerEvent(0, boardRef, RunnerEventKind.Info, m)));
            esp.Spi.Slaves.AddRange(slaves);
        }

        private static Firmware.Devices.Mpu6050 SeededMpu(byte address, double gravity, int seed)
        {
            var m = new Firmware.Devices.Mpu6050(address) { AccelNoise = 0.02, GyroNoise = 0.002, NoiseSeed = seed };
            m.Reset();
            m.AccelZ = gravity;
            return m;
        }

        public II2cDevice? I2cDevice(string fullRef) => I2cDevices.TryGetValue(fullRef, out var d) ? d : null;

        public IReadOnlyDictionary<string, II2cDevice> I2cDeviceMap => I2cDevices;

        private int NodeOfAny(string refDes, params string[] pins)
        {
            foreach (var p in pins)
            {
                int n = Compiled!.PinNode(refDes, p);
                if (n > 0) return n;
            }

            return -1;
        }

        private static byte ParseAddress(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            string t = text!.Trim();
            try
            {
                return t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToByte(t.Substring(2), 16) : byte.Parse(t, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException || ex is OverflowException)
            {
                return 0;
            }
        }

        // ================================================================ serial

        private void AppendSerial(string board, string text)
        {
            if (!_serial.TryGetValue(board, out var sb)) _serial[board] = sb = new StringBuilder();
            sb.Append(text);
            if (sb.Length > MaxSerial) sb.Remove(0, sb.Length - MaxSerial);
        }

        /// <summary>Serial output since the last read (one board, or every board as "refdes" → text).</summary>
        public Dictionary<string, string> ReadSerial(string? board, bool clear)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _serial)
            {
                if (board != null && !string.Equals(kv.Key, ResolveBoard(board).RefDes, StringComparison.OrdinalIgnoreCase)) continue;
                result[kv.Key] = kv.Value.ToString();
                if (clear) kv.Value.Clear();
            }

            return result;
        }

        public void WriteSerial(string text, string? board)
        {
            int sent = 0;
            foreach (var b in Boards)
            {
                if (board != null && b != ResolveBoard(board)) continue;
                if (b.Avr != null)
                {
                    b.Avr.Mcu.WriteSerial(text);
                    sent++;
                }
                else if (b.Esp != null)
                {
                    b.Esp.SendSerial(text);
                    sent++;
                }
            }

            if (sent == 0) throw new ApiException(ApiException.NotFound, "Yazılımı çalışan kart yok");
        }

        /// <summary>A board by object id, object name, refdes (full or inner) — or the only board.</summary>
        public BoardInfo ResolveBoard(string? key)
        {
            if (string.IsNullOrEmpty(key))
            {
                if (Boards.Count == 1) return Boards[0];
                throw ApiException.BadParam("board", Boards.Count == 0 ? "programlanabilir kart yok" : "birden çok kart var, birini seçin");
            }

            foreach (var b in Boards)
            {
                if (string.Equals(b.RefDes, key, StringComparison.OrdinalIgnoreCase) || string.Equals(b.ObjectId, key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(b.Name, key, StringComparison.OrdinalIgnoreCase) || b.RefDes.EndsWith("/" + key, StringComparison.OrdinalIgnoreCase))
                    return b;
            }

            throw ApiException.Missing("Kart '" + key + "'");
        }

        // ================================================================ pins and drivers (electrical actuation)

        private sealed class PinCommand
        {
            public PinMode Mode;
            public double Frequency;
            public double Duty;
        }

        /// <summary>The pin device of a board pin ("D5", "A0", "IO21" …); boards without that pin throw.</summary>
        public DigitalPin PinDevice(string? board, string pin)
        {
            var b = ResolveBoard(board);
            var dev = Compiled!.Device<DigitalPin>(b.RefDes + "." + pin) ?? Compiled.Device<DigitalPin>(b.RefDes + "." + pin.ToUpperInvariant());
            return dev ?? throw ApiException.Missing("Pin '" + pin + "' (" + b.RefDes + ")");
        }

        /// <summary>
        /// Holds a board pin at a level / PWM regardless of the firmware (re-applied every slice).
        /// mode: high, low, input, pullup, pwm (duty 0–1, frequency Hz), servo (angle 0–180°, 50 Hz), release.
        /// </summary>
        public void WritePin(string? board, string pin, string mode, double duty = 0, double frequency = 490, double angle = 90)
        {
            var dev = PinDevice(board, pin);
            switch ((mode ?? string.Empty).ToLowerInvariant())
            {
                case "release":
                case "none":
                    _pinOverrides.Remove(dev);
                    dev.StopPwm();
                    return;
                case "high":
                case "1":
                case "true":
                    _pinOverrides[dev] = new PinCommand { Mode = PinMode.OutputHigh };
                    break;
                case "low":
                case "0":
                case "false":
                    _pinOverrides[dev] = new PinCommand { Mode = PinMode.OutputLow };
                    break;
                case "input":
                    _pinOverrides[dev] = new PinCommand { Mode = PinMode.Input };
                    break;
                case "pullup":
                case "input_pullup":
                    _pinOverrides[dev] = new PinCommand { Mode = PinMode.InputPullup };
                    break;
                case "pwm":
                    _pinOverrides[dev] = new PinCommand { Mode = PinMode.OutputLow, Frequency = Math.Max(1, frequency), Duty = Math.Max(0, Math.Min(1, duty)) };
                    break;
                case "servo":
                    // Arduino Servo library: 544–2400 µs over 0–180°, 50 Hz.
                    double pulse = 544e-6 + (2400e-6 - 544e-6) * Math.Max(0, Math.Min(180, angle)) / 180.0;
                    _pinOverrides[dev] = new PinCommand { Mode = PinMode.OutputLow, Frequency = 50, Duty = pulse * 50 };
                    break;
                default:
                    throw ApiException.BadParam("mode", "high | low | input | pullup | pwm | servo | release");
            }

            if (Simulator != null) ApplyOverrides(Simulator);
        }

        public JsonObject ReadPin(string? board, string pin)
        {
            var dev = PinDevice(board, pin);
            return new JsonObject
            {
                ["voltage"] = J.N(dev.Voltage),
                ["level"] = dev.DigitalValue,
                ["mode"] = dev.Mode.ToString().ToLowerInvariant(),
                ["current"] = J.N(dev.Current),
                ["pwm_frequency"] = J.N(dev.PwmFrequency),
                ["pwm_duty"] = J.N(dev.PwmActive ? dev.PwmDuty : double.NaN),
                ["overridden"] = _pinOverrides.ContainsKey(dev)
            };
        }

        /// <summary>Holds a PWM switch (motor driver channel, ESC, FC MOSFET) at a duty, ignoring its logic input.</summary>
        public void SetDriverDuty(AveragedPwmDriver driver, double duty)
        {
            int control = _dutyOverrides.TryGetValue(driver, out var old) ? old.Control : driver.ControlNode;
            _dutyOverrides[driver] = (Math.Max(0, Math.Min(1, duty)), control);
            driver.ControlNode = -1;
            driver.Duty = _dutyOverrides[driver].Duty;
        }

        public void ReleaseDriver(AveragedPwmDriver driver)
        {
            if (!_dutyOverrides.TryGetValue(driver, out var old)) return;
            driver.ControlNode = old.Control;
            _dutyOverrides.Remove(driver);
        }

        public bool DriverOverridden(AveragedPwmDriver driver) => _dutyOverrides.ContainsKey(driver);

        public virtual void ReleaseAll()
        {
            foreach (var d in _dutyOverrides.Keys.ToList()) ReleaseDriver(d);
            foreach (var p in _pinOverrides.Keys.ToList())
            {
                p.StopPwm();
                _pinOverrides.Remove(p);
            }
        }

        private void ApplyOverrides(AnalogSimulator sim)
        {
            if (_pinOverrides.Count > 0)
            {
                foreach (var kv in _pinOverrides)
                {
                    var pin = kv.Key;
                    var cmd = kv.Value;
                    if (cmd.Frequency > 0)
                    {
                        if (!pin.PwmActive || Math.Abs(pin.PwmFrequency - cmd.Frequency) > 1e-9 || Math.Abs(pin.PwmDuty - cmd.Duty) > 1e-9)
                            pin.SetPwm(cmd.Frequency, cmd.Duty, sim.Time);
                        pin.Mode = PinMode.OutputLow;
                    }
                    else
                    {
                        if (pin.PwmActive) pin.StopPwm();
                        pin.Mode = cmd.Mode;
                    }
                }
            }

            foreach (var kv in _dutyOverrides)
            {
                kv.Key.ControlNode = -1;
                kv.Key.Duty = kv.Value.Duty;
            }
        }

        // ================================================================ circuit readouts

        /// <summary>Voltage of a net/node by name (or "object:pin" in a world).</summary>
        public virtual double NodeVoltage(string node)
        {
            var sim = Simulator ?? throw new ApiException(ApiException.NotLoaded, "Yüklü dünya yok");
            return sim.NodeVoltage(node);
        }

        /// <summary>Devices of a part (full refdes) with their live signals (current, voltage, temperature …).</summary>
        public JsonObject PartState(string fullRef)
        {
            var sim = Simulator ?? throw new ApiException(ApiException.NotLoaded, "Yüklü dünya yok");
            var devices = Compiled!.DevicesOf(fullRef);
            if (devices.Count == 0) throw ApiException.Missing("Parça '" + fullRef + "'");
            var list = new JsonArray();
            double hottest = double.NaN;
            foreach (var d in devices)
            {
                var signals = new JsonObject();
                string prefix = d.Id + ":";
                foreach (var kv in sim.Signals)
                {
                    if (!kv.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    signals[kv.Key.Substring(prefix.Length)] = J.N(kv.Value);
                    if (kv.Key.EndsWith(":T", StringComparison.Ordinal) && J.IsFinite(kv.Value) && (double.IsNaN(hottest) || kv.Value > hottest)) hottest = kv.Value;
                }

                list.Add(new JsonObject
                {
                    ["id"] = d.Id,
                    ["type"] = d.GetType().Name,
                    ["failed"] = d.IsFailed,
                    ["failure"] = d.IsFailed ? d.Failure.ToString().ToLowerInvariant() + ": " + d.FailureReason : null,
                    ["signals"] = signals
                });
            }

            return new JsonObject { ["refdes"] = fullRef, ["temperature"] = J.N(hottest), ["failed"] = devices.Any(d => d.IsFailed), ["devices"] = list };
        }

        /// <summary>Raw solver signals whose key starts with a prefix (device id) — up to <paramref name="max"/>.</summary>
        public JsonObject Signals(string prefix, int max)
        {
            var sim = Simulator ?? throw new ApiException(ApiException.NotLoaded, "Yüklü dünya yok");
            var o = new JsonObject();
            int n = 0;
            foreach (var kv in sim.Signals.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (!kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                o[kv.Key] = J.N(kv.Value);
                if (++n >= max) break;
            }

            return o;
        }

        // ================================================================ bodies (for sensors and poses)

        /// <summary>Movable things a client can query (world objects; the vehicle and arena objects).</summary>
        public abstract IEnumerable<string> ObjectIds();

        public abstract bool TryGetBody(string objectId, out BodyState state);

        /// <summary>First hit along a ray (ground, bodies, obstacles); <paramref name="ignoreObject"/>'s whole assembly is skipped.</summary>
        public abstract bool Raycast(Vec3 origin, Vec3 direction, double maxDistance, string? ignoreObject, out SceneRayHit hit);

        public abstract Vec3 Gravity { get; }

        /// <summary>Floor appearance (line tracks) for cameras; null = plain floor.</summary>
        public virtual Arena? Floor => null;

        /// <summary>Colour (0–1 RGB) a camera sees on a body.</summary>
        public virtual (double R, double G, double B) ColorOf(SceneBody? body) => (0.6, 0.6, 0.62);

        // ================================================================ describe / actuators (per mode)

        public abstract JsonObject Describe();

        public abstract JsonArray ListActuators();

        /// <summary>Commands an actuator: see docs/API.md (actuator.set).</summary>
        public abstract JsonObject SetActuator(string name, JsonObject p);

        public abstract void ReleaseActuator(string name);

        /// <summary>Differential-drive helper: forward speed (m/s) and turn rate (rad/s).</summary>
        public abstract JsonObject DriveDifferential(double v, double w, JsonObject p);

        public abstract void SetPose(string objectId, Vec3? position, Quat? orientation, bool zeroVelocity);

        /// <summary>Small random perturbation of the starting state on reset (domain randomisation).</summary>
        public abstract void Randomize(NoiseSource noise, double positionSigma, double yawSigmaRad);

        public JsonObject Status() => new JsonObject
        {
            ["kind"] = Kind,
            ["title"] = Title,
            ["time"] = J.N(Time),
            ["seed"] = Seed,
            ["halted"] = Halted,
            ["halt_reason"] = HaltReason,
            ["failed"] = J.Strs(Failed.OrderBy(x => x, StringComparer.Ordinal)),
            ["rtf"] = J.N(RealtimeFactor),
            ["rtf_average"] = J.N(AverageRealtimeFactor),
            ["wall_time"] = J.N(TotalWall),
            ["step_calls"] = StepCalls,
            ["events_pending"] = PendingEvents,
            ["boards"] = new JsonArray(Boards.Select(b => (JsonNode)b.ToJson()).ToArray())
        };
    }
}
