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
    public sealed partial class WorldSession : ISolverCosts, ISimThreadWork
    {

        public const double FirmwareSlice = 50e-6;
        public const double Esp32Slice = 100e-6;

        private readonly PartLibrary _library;
        private readonly List<FirmwareCoSimulation> _avr = new List<FirmwareCoSimulation>();
        private readonly List<IEsp32Board> _esp = new List<IEsp32Board>();   // [esp32hw] fast-mode and emulated boards
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

        /// <summary>[perf] Where the last start went, step by step (ms), for --bench and the event log.</summary>
        public string LastStartProfile { get; private set; } = string.Empty;

        public bool Start(WorldDocument world, Func<WorldCable, double> cableLength, Func<WorldObject, WorldBodyGeometry>? geometry, out string message)
        {
            using var gate = SimThread.Hold();
            var clock = Stopwatch.StartNew();
            var steps = new List<string>();
            void Mark(string step)
            {
                steps.Add($"{step} {clock.Elapsed.TotalMilliseconds:0}");
                clock.Restart();
            }

            Stop();
            Log.Clear();
            _impactLogged = 0;
            _serial.Clear();
            SerialTotal = 0;
            ProbeReset();   // [unified-timeline]
            _failedObjects.Clear();
            world = world.ForRun();   // [active] [empty] switched-off objects (and what rides on them) are not in the run
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

            Mark("devre");
            foreach (var w in Circuit.Compiled.Warnings) AddLog(new SimLogEntry(0, "derleyici", w, AnalogEventKind.Warning));
            var settings = AppSettings.Current;
            Simulator = new AnalogSimulator(Circuit.Compiled.Circuit, new AnalogOptions
            {
                MaxStep = settings.MaxStep,
                RelTol = settings.RelTol,
                AmbientTempC = world.Environment.AmbientC,
                EnableDamage = settings.EnableDamage,
                SplitIndependentParts = true   // [split] robots that share only the reference are solved apart, each at its own step
            });
            try
            {
                Simulator.Initialize(InitialState.OperatingPoint);
            }
            catch (AnalogSimulationException)
            {
                Simulator.Initialize(InitialState.Zero);
            }

            Mark("çalışma noktası");
            StartRadio();   // [radio] World/WorldSession.Radio.cs
            AttachFirmware();
            Mark("yazılım");
            if (geometry != null) PrepareSdk(world);   // [sdk] World/WorldSession.Sdk.cs: plugins, scripts
            if (geometry != null)
            {
                try
                {
                    Physics = WorldPhysics.Build(world, geometry, Circuit);
                    Mark("fizik");
                    // Parts that start inside each other would be pushed apart violently: say which ones.
                    foreach (var (oa, ob, depth) in Physics.FindInterpenetrations(0.001))
                    {
                        AddLog(new SimLogEntry(0, "fizik", $"{world.Find(oa)?.Name ?? oa} ile {world.Find(ob)?.Name ?? ob} iç içe başlıyor ({depth * 1000:F1} mm); konumlarını ayırın.", AnalogEventKind.Warning));
                    }

                    // [audit] Joined parts do not collide with each other, so one sunk into the other would go unnoticed.
                    foreach (var issue in Physics.JoinedOverlaps())
                        AddLog(new SimLogEntry(0, "fizik", issue.Message + "; gerçekte böyle monte edilemez, konumunu düzeltin.", AnalogEventKind.Warning));
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                {
                    Physics = null;
                    AddLog(new SimLogEntry(0, "fizik", "Fizik sahnesi kurulamadı: " + ex.Message, AnalogEventKind.Warning));
                }

                Mark("denetim");
                AttachCablePhysics();   // [phys] World/WorldSession.Cables.cs
                if (Physics != null)
                {
                    var (exits, routes, laying, relaxing) = Physics.LastCableTimes;
                    Mark($"kablolar [çıkış {exits:0} · yol {routes:0} · serme {laying:0} · gevşetme {relaxing:0}]");
                }
            }

            // [split] each ESP32 board time-stamps its input edges at the steps of the part(s) its pins are in
            foreach (var board in _esp) Simulator.Observe(BoardNodes(board.BoardRefDes), board.OnSubstep);
            StartSense();   // [sense] World/WorldSession.Sense.cs
            StartSupply();   // [supply] World/WorldSession.Supply.cs
            AttachRos();   // [ros] a connection made before the run: its link to this physics
            StartModbus();   // [modbus] World/WorldSession.Modbus.cs
            IndexHeat();
            Mark("sensörler");
            LastStartProfile = string.Join(" · ", steps) + " ms";
            int parts = Circuit.Compiled.DevicesByPart.Count;
            AddLog(new SimLogEntry(0, "dünya", $"{world.Objects.Count} nesne, {world.Cables.Count} kablo, {parts} parça tek devrede çözülüyor", AnalogEventKind.StateChange));
            Speed = settings.DefaultSpeed;
            State = SimState.Running;
            _wall.Restart();
            _lastWall = 0;
            _costAvr = _costEsp = _costRadio = _costCircuit = _costPhysics = _costSense = _simulated = 0;
            Perf.Solvers = this;   // [perf] the performance indicator shows this run's solvers
            _threaded = SimThread.Enabled;
            RestartClock();
            if (_threaded) SimThread.Add(this);
            message = string.Empty;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void Pause()
        {
            using var gate = SimThread.Hold();
            if (State != SimState.Running) return;
            State = SimState.Paused;
            _sounds.Silence();
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Resume()
        {
            using var gate = SimThread.Hold();
            if (State != SimState.Paused) return;
            Firmware.FirmwareDebugger.ReleaseHalts(this);   // [debug] a program stopped at a breakpoint goes on too
            State = SimState.Running;
            _wall.Restart();
            _lastWall = 0;
            RestartClock();
            if (_threaded) SimThread.Add(this);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Stop()
        {
            using var gate = SimThread.Hold();
            SimThread.Remove(this);
            Firmware.FirmwareDebugger.Unregister(this);   // [debug]
            _sounds.Reset();
            foreach (var e in _esp) e.Dispose();
            _esp.Clear();
            _avr.Clear();
            StopRadio();   // [radio]
            StopSupply();   // [supply] the power report, while the circuit is still there
            StopSense();   // [sense]
            StopSdk();   // [sdk]
            _rosLink = null;   // [ros] the connection stays for the next run
            StopModbus();   // [modbus]
            Simulator?.ClearObservers();
            Simulator = null;
            Circuit = null;
            Physics = null;
            if (ReferenceEquals(Perf.Solvers, this)) Perf.Solvers = null;
            if (State != SimState.Stopped)
            {
                State = SimState.Stopped;
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // ================================================================ [perf] the simulation thread (Simulation/SimThread.cs)

        private bool _threaded;                          // this run advances on the simulation thread
        private double _owed;                            // simulated seconds it is behind the wall clock (simulation thread)
        private double _threadWall;                      // wall clock at its last turn
        private double _frameWall, _frameSim;            // wall clock and simulated time at the last frame (UI)
        private FirmwareCoSimulation? _haltedOnThread;   // a program stopped at a breakpoint there; the UI pauses and shows it
        private bool _stopOnThread;                      // a part failed ("pause on failure") or the solver gave up there
        private int _eventsSeen;                         // circuit events already looked at for failures (simulation thread)
        private readonly List<SimLogEntry> _logFromThread = new List<SimLogEntry>();   // the log lines it wrote, for the UI

        private void RestartClock()
        {
            _owed = 0;
            _threadWall = _frameWall = _wall.Elapsed.TotalSeconds;
            _frameSim = Simulator?.Time ?? 0;
            _haltedOnThread = null;
            _stopOnThread = false;
        }

        /// <summary>A log line from wherever the run is advancing: on the simulation thread it waits for the next frame.</summary>
        private void AddLog(SimLogEntry entry)
        {
            ProbeLog(entry);   // [unified-timeline] and its events
            if (SimThread.OnSimThread || System.Threading.Tasks.Task.CurrentId != null)
            {
                lock (_boardOutput) _logFromThread.Add(entry);   // [bigscene] a board on a worker thread too
            }
            else Log.Add(entry);
        }

        bool ISimThreadWork.AdvanceOnSimThread(double maxWall)
        {
            if (!_threaded || State != SimState.Running || Simulator == null || _haltedOnThread != null || _stopOnThread) return false;
            double now = _wall.Elapsed.TotalSeconds;
            // Never more than 50 ms of the wall clock behind: a run the computer cannot keep up with runs as fast as it can.
            _owed = Math.Min(_owed + (now - _threadWall) * Speed, 0.05 * Speed);
            _threadWall = now;
            if (_owed < 1e-4 * Speed) return false;
            _owed -= Advance(_owed, Stopwatch.StartNew(), maxWall);
            return _owed >= 1e-4 * Speed && State == SimState.Running && _haltedOnThread == null && !_stopOnThread;
        }

        void ISimThreadWork.FailedOnSimThread(Exception error)
        {
            Pause();
            Log.Add(new SimLogEntry(Time, "çözücü", "Simülasyon durdu: " + error.Message, AnalogEventKind.Failure));
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();   // recorded like any other error
        }

        /// <summary>
        /// A part failed in what the simulation thread just advanced and the run should pause at it ("pause on failure"):
        /// the events stay for the UI, which logs them and pauses.
        /// </summary>
        private bool FailureToPauseAt()
        {
            if (!AppSettings.Current.PauseOnFailure || Simulator == null) return false;
            var events = Simulator.Events;
            bool failed = false;
            for (int i = _eventsSeen; i < events.Count; i++) failed |= events[i].Kind == AnalogEventKind.Failure;
            _eventsSeen = events.Count;
            return failed;
        }

        /// <summary>A frame of a run on the simulation thread (UI): what it left — log lines, events, a stop — and the clock.</summary>
        private void ThreadedFrame()
        {
            foreach (var entry in _logFromThread) Log.Add(entry);
            _logFromThread.Clear();
            RadioFrame();   // [radio] controller input / feedback, radio events
            NetworkNotices();   // [opcua-sec]
            DrainEvents();
            if (_haltedOnThread is { } halted)
            {
                _haltedOnThread = null;
                Pause();
                Firmware.FirmwareDebugger.OnHalted(halted);
            }
            else if (_stopOnThread)
            {
                _stopOnThread = false;
                Pause();
            }

            double now = _wall.Elapsed.TotalSeconds, sim = Time;
            if (now - _frameWall >= 0.02)
            {
                _realtime = 0.85 * _realtime + 0.15 * ((sim - _frameSim) / (now - _frameWall));
                _frameWall = now;
                _frameSim = sim;
            }

            SimThread.Kick();
        }

        /// <summary>Called once per rendered frame by the page while running.</summary>
        public void Frame()
        {
            if (State != SimState.Running || Simulator == null) return;
            if (_threaded)
            {
                ThreadedFrame();
                FrameEnd();
                return;
            }

            double now = _wall.Elapsed.TotalSeconds;
            double wallDt = Math.Min(0.05, now - _lastWall);
            _lastWall = now;
            if (wallDt <= 0) return;
            var budget = Stopwatch.StartNew();
            RadioFrame();   // [radio] controller input / feedback, radio events
            NetworkNotices();   // [opcua-sec]
            double done = Advance(wallDt * Speed, budget, Shell.UiHeadroom.SimulationBudget(0.022));   // room for the rest of the UI
            _realtime = 0.85 * _realtime + 0.15 * (done / wallDt);
            if (_logFromThread.Count > 0)
            {
                // [bigscene] what boards running side by side logged in this frame
                foreach (var entry in _logFromThread) Log.Add(entry);
                _logFromThread.Clear();
            }
            FrameEnd();
        }

        private void FrameEnd()
        {
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

        // [perf] Wall-clock seconds each solver took since the start, for the performance indicator (Shell/SolverCosts.cs).
        private double _costAvr, _costEsp, _costRadio, _costCircuit, _costPhysics, _costSense, _simulated;

        private static long Lap(ref double seconds, long since)
        {
            long now = Stopwatch.GetTimestamp();
            seconds += (now - since) / (double)Stopwatch.Frequency;
            return now;
        }

        double ISolverCosts.SimulatedSeconds => _simulated;

        void ISolverCosts.Costs(List<(string Name, double Seconds)> into)
        {
            into.Add(("Arduino yazılımı", _costAvr));
            into.Add(("ESP32 yazılımı", _costEsp));
            into.Add(("radyo", _costRadio));
            into.Add(("devre", _costCircuit));
            into.Add(("sensörler", _costSense));
            if (Physics?.Times is not { } t)
            {
                into.Add(("katı cisimler", _costPhysics));
                return;
            }

            // The mechanics by part (its physics is built per run, as these costs start); the little it spent between
            // its parts goes with the rigid bodies.
            into.Add(("katı cisimler", t.Bodies + Math.Max(0, _costPhysics - t.Total)));
            into.Add(("kablolar", t.Cables));
            into.Add(("sıvılar", t.Liquids));
            into.Add(("ısı", t.Thermal));
            into.Add(("motorlar ve sürücüler", t.Drives));
            into.Add(("hava ve gaz", t.Air));
            into.Add(("betikler", t.Scripts));
            into.Add(("mesafe sensörleri", t.Sensors));
        }

        string? ISolverCosts.Detail
        {
            get
            {
                if (Physics is not { } ph) return null;
                var parts = new List<string> { Lang.T("fizik adımı") + " " + SiValue.Format(ph.Step, "s") };
                foreach (var l in ph.Liquids)
                    parts.Add(Lang.T(l.Object.Name) + ": " + l.Body.Count + " " + Lang.T("parçacık") + ", " + l.Body.LastSubsteps + " " + Lang.T("alt adım") + (l.Body.Asleep ? " (" + Lang.T("uykuda") + ")" : string.Empty));
                return string.Join(" · ", parts);
            }
        }

        public void StepOnce(double seconds)
        {
            using var gate = SimThread.Hold();
            if (Simulator == null) return;
            Advance(seconds);
            foreach (var entry in _logFromThread) Log.Add(entry);
            _logFromThread.Clear();
            RadioFrame();   // [radio]
            NetworkNotices();   // [opcua-sec]
            Updated?.Invoke(this, EventArgs.Empty);
        }

        private double Advance(double seconds, Stopwatch? budget = null, double maxWall = double.PositiveInfinity)
        {
            if (Simulator == null) return 0;
            double chunk = Math.Min(seconds, Math.Max(1e-5, Speed * 1e-3));
            if (_avr.Count > 0) chunk = Math.Min(chunk, FirmwareSlice);
            if (_esp.Count > 0) chunk = Math.Min(chunk, Esp32Slice);
            double covered = 0;
            // [perf] On the simulation thread the UI does what only it may (the log, the debugger, pausing) at its next frame.
            bool onThread = SimThread.OnSimThread;
            try
            {
                while (covered < seconds - 1e-15)
                {
                    double dt = Math.Min(chunk, seconds - covered);
                    // [debug] One clock: a program that stops at a breakpoint inside the slice stops the world at that
                    // instant (the circuit, the mechanics and the other boards go exactly as far as it did).
                    FirmwareCoSimulation? halted = null;
                    long t0 = Stopwatch.GetTimestamp();
                    if (BoardsInParallel())
                    {
                        // [bigscene] Each Uno runs its own chip on its own pins and only reads the circuit, so within one slice
                        // they are independent: spread over the cores (no breakpoints here, so none stops inside the slice).
                        double slice = dt, at = Simulator.Time;
                        RobotTwin.CoreSim.Mechanics.WorkTeam.Each(_avr.Count, i => _avr[i].Advance(slice, at));
                    }
                    else
                    {
                        for (int i = 0; i < _avr.Count; i++)
                        {
                            double ran = _avr[i].Advance(dt, Simulator.Time);
                            if (_avr[i].Halted && (halted == null || ran < dt))
                            {
                                halted ??= _avr[i];
                                dt = Math.Min(dt, ran);
                            }
                        }
                    }

                    long t1 = Lap(ref _costAvr, t0);
                    if (dt > 0)
                    {
                        for (int i = 0; i < _esp.Count; i++) _esp[i].Advance(dt, Simulator.Time);
                        t1 = Lap(ref _costEsp, t1);
                        AdvanceRadio(Simulator.Time, dt);   // [radio] IR receiver outputs, radio housekeeping
                        t1 = Lap(ref _costRadio, t1);
                        Simulator.Step(dt);
                        SampleSupply();   // [supply] brown-outs as they happen
                        t1 = Lap(ref _costCircuit, t1);
                        Physics?.Advance(dt, Simulator);   // its parts' costs: Physics.Times
                        StepRos();   // [ros] clock, joint states out; joint commands in
                        t1 = Lap(ref _costPhysics, t1);
                        AdvanceSense(dt);   // [sense] line sensors, LiDAR, GPS, IMU, compass, encoders
                        Lap(ref _costSense, t1);
                        Sliced?.Invoke(dt);   // [unified-timeline] a live recording samples here, on this thread
                        covered += dt;
                        _simulated += dt;
                    }

                    if (onThread)
                    {
                        if (halted != null)
                        {
                            _haltedOnThread = halted;
                            break;
                        }

                        if (FailureToPauseAt())
                        {
                            _stopOnThread = true;
                            break;
                        }

                        if (SimThread.UiWaiting) break;
                    }
                    else
                    {
                        DrainEvents();
                        if (halted != null)
                        {
                            Pause();
                            Firmware.FirmwareDebugger.OnHalted(halted);
                            break;
                        }
                    }

                    if (budget != null && budget.Elapsed.TotalSeconds > maxWall) break;
                }
            }
            catch (AnalogSimulationException ex)
            {
                AddLog(new SimLogEntry(Time, "çözücü", "Simülasyon durdu: " + ex.Message, AnalogEventKind.Failure));
                if (onThread) _stopOnThread = true;
                else Pause();
            }

            return covered;
        }

        private void DrainEvents()
        {
            DrainCellMessages();   // [arm] World/WorldSession.Cell.cs
            DrainSdkEvents();   // [sdk]
            if (Physics != null)
            {
                foreach (var pe in Physics.DrainEvents())
                {
                    var o = World?.Find(pe.Source);
                    // Knocks are information; only something that actually breaks or comes off is a failure.
                    // [trigger] a zone's comings and goings are information: logged, not heard, never a failure
                    bool trigger = pe.Kind is RobotTwin.CoreSim.Mechanics.PhysicsEventKind.TriggerEnter or RobotTwin.CoreSim.Mechanics.PhysicsEventKind.TriggerExit;
                    bool broke = !trigger && pe.Kind != RobotTwin.CoreSim.Mechanics.PhysicsEventKind.Impact;
                    if (broke) _sounds.OnEvent(AnalogEventKind.Failure, pe.Source, pe.Message);
                    else if (!trigger) _sounds.Impact(pe.Value, pe.Source);   // [objs] heard where it hit
                    if (!broke && !trigger && _impactLogged++ > 20) continue;
                    AddLog(new SimLogEntry(Time, o?.Name ?? pe.Source, pe.Message, broke ? AnalogEventKind.Failure : AnalogEventKind.StateChange));
                    if (broke && o != null) Failure?.Invoke(o.Id, pe.Message);
                }
            }

            var circuitEvents = Simulator!.DrainEvents();
            _eventsSeen = 0;
            foreach (var e in circuitEvents)
            {
                string obj = ObjectOfDevice(e.DeviceId);
                string name = World?.Find(obj)?.Name ?? obj;
                string device = e.DeviceId.StartsWith("cable:", StringComparison.Ordinal)
                    ? "Kablo " + (World?.Cables.FirstOrDefault(c => "cable:" + c.Id == e.DeviceId)?.Name ?? e.DeviceId.Substring(6))
                    : name + " · " + e.DeviceId.Substring(Math.Min(e.DeviceId.Length, obj.Length + 1));
                var entry = new SimLogEntry(e.Time, device, e.Message, e.Kind);
                AddLog(entry);
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
                    AddLog(new SimLogEntry(0, where, "Yüklü yazılım yok (kart boşta). Kartı düzenleyip Kod sayfasından yükleyin.", AnalogEventKind.Warning));
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
                    Firmware.FirmwareDebugger.Register(this, DebugKeyOf(prog), where, cosim, hex, () => Time, Resume);   // [debug]
                    TrackBoard(cosim, prog);   // [radio]
                    AddLog(new SimLogEntry(0, where, $"Yazılım çalışıyor: {source} (ATmega328P @ 16 MHz)", AnalogEventKind.StateChange));
                    AttachI2c(cosim.I2c, prog);
                    AttachSpi(cosim.Spi, prog, n => n < Atmega328P.PinCount ? RobotTwin.CoreSim.Firmware.AvrRegisters.PinName(n) : null);   // [spi]
                    AttachLedStrips(cosim, prog);   // [neopixel-avr]
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is System.IO.InvalidDataException || ex is FormatException)
                {
                    AddLog(new SimLogEntry(0, where, "Yazılım yüklenemedi: " + ex.Message, AnalogEventKind.Warning));
                }
            }
        }

        /// <summary>[debug] The code page's key for a board of this world (its breakpoints are kept under it).</summary>
        private string DebugKeyOf(WorldProgrammable prog)
        {
            var world = World!;
            var o = world.Find(prog.ObjectId);
            if (o == null) return "w:" + prog.ObjectId;
            if (world.IsAssembly && o.ProjectRef is { } pr) return "p:" + pr.Substring(pr.IndexOf('/') + 1);   // [assembly] a Montaj runs its project's boards
            if (o.Kind == WorldObjectKind.Board && world.FindDesign(o.DesignId) is { } design)
            {
                if (world.IsAssembly && design.Package.Schematic.ProjectId is { Length: > 0 } pid && world.SourceOf(pid) != null) return "p:" + prog.InnerRefDes;
                var first = world.Objects.FirstOrDefault(x => x.DesignId == o.DesignId) ?? o;
                return "d:" + first.Id + ":" + prog.InnerRefDes;
            }

            return "w:" + o.Id;
        }

        private void AttachEsp32(WorldProgrammable prog, string where)
        {
            string? source = prog.Firmware?.Bundle();   // [code] every file of the sketch
            if (string.IsNullOrWhiteSpace(source) && prog.Parameters.TryGetValue("firmware", out var fw))
            {
                var path = SimulationSession.ResolveFirmwarePath(fw);
                if (path != null && System.IO.File.Exists(path)) source = System.IO.File.ReadAllText(path);
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                AddLog(new SimLogEntry(0, where, "ESP32'de kod yok.", AnalogEventKind.Warning));
                return;
            }

            // [esp32hw] The hardware-compatible mode runs the real-chip build on our Xtensa emulator when there is one.
            if (RobotTwin.CoreSim.Design.Esp32Parts.IsHardwareMode(prog.Parameters) && AttachEmulatedEsp32(prog, where, source!)) return;

            string? dll = Firmware.ArduinoCompiler.CachedEsp32Dll(source!);
            if (dll == null)
            {
                var build = Firmware.ArduinoCompiler.CompileEsp32(source!);
                if (!build.Success || build.Hex == null)
                {
                    AddLog(new SimLogEntry(0, where, "ESP32 çizimi derlenemedi: " + (build.Diagnostics.FirstOrDefault()?.Message ?? "?"), AnalogEventKind.Warning));
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
                AddLog(new SimLogEntry(0, where, "Yazılım çalışıyor (ESP32 HAL modeli)", AnalogEventKind.StateChange));
                AttachI2c(cosim.I2c, prog);
                AttachSpi(cosim, prog);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is BadImageFormatException || ex is InvalidOperationException || ex is System.IO.IOException)
            {
                AddLog(new SimLogEntry(0, where, "ESP32 başlatılamadı: " + ex.Message, AnalogEventKind.Warning));
            }
        }

        /// <summary>
        /// [esp32hw] Starts a hardware-compatible ESP32 of the world on the Xtensa emulator with the sketch's real-chip build;
        /// false (after saying why) when there is none yet, and the board runs as the fast mode. The radio and the camera
        /// stay with the fast mode for now.
        /// </summary>
        private bool AttachEmulatedEsp32(WorldProgrammable prog, string where, string source)
        {
            if (RobotTwin.CoreSim.Design.Esp32Parts.EmulatorGap(source) is { } gap)
            {
                AddLog(new SimLogEntry(0, where, string.Format(System.Globalization.CultureInfo.InvariantCulture, RobotTwin.CoreSim.Design.Esp32Parts.HardwareModeGapNotice, gap), AnalogEventKind.Warning));
                return false;
            }

            var cached = Firmware.ArduinoCompiler.CachedEsp32Hardware(source);
            if (cached == null)
            {
                if (Firmware.ArduinoCompiler.FailedEsp32HardwareBuild(source) is { } failed)
                {
                    AddLog(new SimLogEntry(0, where, string.Format(System.Globalization.CultureInfo.InvariantCulture, RobotTwin.CoreSim.Design.Esp32Parts.HardwareModeBuildFailed, Firmware.ArduinoCompiler.FirstError(failed)), AnalogEventKind.Warning));
                }
                else if (Firmware.ArduinoCompiler.Esp32ToolchainInstalled)
                {
                    AddLog(new SimLogEntry(0, where, RobotTwin.CoreSim.Design.Esp32Parts.HardwareModeBuilding, AnalogEventKind.Warning));
                    Firmware.ArduinoCompiler.WhenEsp32HardwareBuilt(source, r => AddLog(Simulation.SimulationSession.BuildEnded(where, r, Simulator?.Time ?? 0)));
                }
                else
                {
                    AddLog(new SimLogEntry(0, where, RobotTwin.CoreSim.Design.Esp32Parts.HardwareModeNoToolchain, AnalogEventKind.Warning));
                }

                return false;
            }

            try
            {
                byte[] elf = System.IO.File.ReadAllBytes(cached.Value.Elf);
                byte[]? image = cached.Value.Merged != null ? System.IO.File.ReadAllBytes(cached.Value.Merged) : null;
                var board = new Esp32EmulatedBoard(elf, image, Circuit!.Compiled, prog.RefDes);
                board.SerialOutput += text =>
                {
                    if (SerialRouted(board)) AppendSerial(where, text);   // [radio] only while the USB cable is plugged
                };
                board.Log += m => AddLog(new SimLogEntry(Simulator?.Time ?? 0, where, m, AnalogEventKind.StateChange));
                _esp.Add(board);
                TrackBoard(board, prog);
                AddLog(new SimLogEntry(0, where, "Yazılım çalışıyor (gerçek yonga yazılımı, RobotWin Xtensa emülatörü)", AnalogEventKind.StateChange));
                AddLog(new SimLogEntry(0, where, RobotTwin.CoreSim.Design.Esp32Parts.HardwareModeRunning, AnalogEventKind.StateChange));
                AttachI2c(board.I2c, prog);
                AttachSpi(board, prog);
                return true;
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                AddLog(new SimLogEntry(0, where, "ESP32 emülatörü başlatılamadı: " + ex.Message, AnalogEventKind.Warning));
                return false;
            }
        }

        private string _serialSource = string.Empty;

        /// <summary>[bigscene] From how many Unos their slices run in parallel (below it, the hand-over costs more than it saves).</summary>
        public const int ParallelBoardsFrom = 4;


        /// <summary>The serial monitor and the log, written by boards running side by side.</summary>
        private readonly object _boardOutput = new object();

        /// <summary>
        /// [bigscene] Whether this slice's Unos may run side by side: enough of them, none stopped, and no breakpoint set in
        /// any (a program that stops at one must stop the world at that instant, which only the one-by-one loop can do).
        /// </summary>
        private bool BoardsInParallel()
        {
            if (_avr.Count < ParallelBoardsFrom || Environment.ProcessorCount < 2) return false;
            foreach (var b in _avr) if (b.Halted) return false;
            foreach (var p in Firmware.FirmwareDebugger.Running)
                if (Firmware.FirmwareDebugger.CountOf(p.Key) > 0) return false;
            return true;
        }

        private void AppendSerial(string source, string text)
        {
            lock (_boardOutput) AppendSerialCore(source, text);
        }

        private void AppendSerialCore(string source, string text)
        {
            ProbeSerial(source, text);   // [unified-timeline] a live recording's serial lines
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
            Note(RobotTwin.CoreSim.Automation.InputEvent.Serial, string.Empty, 0, text);   // [replay]
            foreach (var a in _avr) if (SerialRouted(a)) a.Mcu.WriteSerial(text);   // [radio] USB-connected boards only
            foreach (var e in _esp) if (SerialRouted(e)) e.SendSerial(text);
        }

        private readonly Dictionary<string, II2cDevice> _i2c = new Dictionary<string, II2cDevice>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Protocol model of an I²C module (display, IMU) by world designator ("O4/DS1", or the component object's part).</summary>
        public II2cDevice? I2cDeviceOf(string fullRefDes) => _i2c.TryGetValue(fullRefDes, out var d) ? d : null;

        private RobotTwin.CoreSim.Firmware.SpiWiring.Context SensorContext() => new RobotTwin.CoreSim.Firmware.SpiWiring.Context
        {
            PinVolts = (r, p) => Simulator == null || Circuit == null ? 0 : Circuit.Compiled.PinVoltage(Simulator, r, p),
            AmbientC = () => World?.Environment.AmbientC ?? 25,
            AltitudeOf = fullRef =>
            {
                int slash = fullRef.IndexOf('/');
                string id = slash > 0 ? fullRef.Substring(0, slash) : fullRef;
                return Physics?.PoseOf(id)?.Position.Z ?? World?.Find(id)?.Pose.Z ?? 0;
            },
            Seed = 17
        };

        /// <summary>SPI devices (MCP3008, BMP280 in SPI mode …) wired to an ESP32's GPIOs.</summary>
        private void AttachSpi(IEsp32Board esp, WorldProgrammable prog)
        {
            AttachSpi(esp.Spi, prog, g => "IO" + g);
            AttachLedStrips(esp, prog);
        }

        /// <summary>SPI devices wired to a board's pins; <paramref name="gpioPin"/> names the board pin of a bus GPIO number (an Uno's D13/D12/D11 and its chip selects, an ESP32's IOn).</summary>
        private void AttachSpi(RobotTwin.CoreSim.Firmware.SpiBus bus, WorldProgrammable prog, Func<int, string?> gpioPin)
        {
            var c = Circuit!.Compiled;
            var slaves = RobotTwin.CoreSim.Firmware.SpiWiring.Attach(AllParts(), c.PinNode, g => gpioPin(g) is { } pin ? c.PinNode(prog.RefDes, pin) : -1, Reach,
                r =>
                {
                    if (Simulator == null) return false;
                    double vcc = c.PinNode(r, "VDD") > 0 ? c.PinVoltage(Simulator, r, "VDD") - c.PinVoltage(Simulator, r, "DGND") : c.PinVoltage(Simulator, r, "VCC") - c.PinVoltage(Simulator, r, "GND");
                    return vcc > 2.5;
                },
                SensorContext(), m => AddLog(new SimLogEntry(0, prog.RefDes, m, AnalogEventKind.StateChange)), gpioPin);
            bus.Slaves.AddRange(slaves);
        }

        /// <summary>[neopixel-avr] The WS2812B strips whose DIN an Uno / Nano pin drives (directly or across cables).</summary>
        private void AttachLedStrips(FirmwareCoSimulation cosim, WorldProgrammable prog)
        {
            var c = Circuit!.Compiled;
            var strips = RobotTwin.CoreSim.Firmware.LedStripWiring.Attach(AllParts().Select(p => (p.FullRef, p.Def)), c.PinNode,
                g => g < Atmega328P.PinCount ? c.PinNode(prog.RefDes, RobotTwin.CoreSim.Firmware.AvrRegisters.PinName(g)) : -1, Reach,
                r => c.DevicesOf(r).OfType<RobotTwin.CoreSim.Firmware.Devices.Ws2812Load>().FirstOrDefault());
            foreach (var (pin, strip) in strips)
            {
                cosim.AttachLedStrip(pin, strip);
                AddLog(new SimLogEntry(0, strip.Id, $"LED şeridi {prog.RefDes} {RobotTwin.CoreSim.Firmware.AvrRegisters.PinName(pin)} hattına bağlandı ({strip.Strip.Count} LED)", AnalogEventKind.StateChange));
            }

            cosim.Log += m => AddLog(new SimLogEntry(0, prog.RefDes, m, AnalogEventKind.Warning));
        }

        /// <summary>[neopixel] The WS2812B strips whose DIN an ESP32 GPIO drives (directly or across cables): its frames reach them.</summary>
        private void AttachLedStrips(IEsp32Board esp, WorldProgrammable prog)
        {
            var c = Circuit!.Compiled;
            var strips = RobotTwin.CoreSim.Firmware.LedStripWiring.Attach(AllParts().Select(p => (p.FullRef, p.Def)), c.PinNode,
                g => c.PinNode(prog.RefDes, "IO" + g), Reach,
                r => c.DevicesOf(r).OfType<RobotTwin.CoreSim.Firmware.Devices.Ws2812Load>().FirstOrDefault());
            foreach (var (gpio, strip) in strips)
            {
                esp.AttachLedStrip(gpio, strip);
                AddLog(new SimLogEntry(0, strip.Id, $"LED şeridi {prog.RefDes} IO{gpio} hattına bağlandı ({strip.Strip.Count} LED)", AnalogEventKind.StateChange));
            }
        }

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
                    "bmp280" => RobotTwin.CoreSim.Firmware.SpiWiring.Barometer(fullRef, parameters, SensorContext()),
                    _ => SenseI2c(def.Id, parameters, address, fullRef)   // [sense] HMC5883L compass
                };
                if (device == null) continue;
                string r = fullRef;
                bus.Devices.Add(new GatedI2cDevice(device, () => Simulator != null &&
                    Circuit.Compiled.PinVoltage(Simulator, r, "VCC") - Circuit.Compiled.PinVoltage(Simulator, r, "GND") > 2.7));
                _i2c[r] = device;
                AddLog(new SimLogEntry(0, r, $"I²C veri yoluna bağlandı ({prog.RefDes}, 0x{device.Address:X2})", AnalogEventKind.StateChange));
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

        /// <summary>
        /// Resistance the circuit sees in a cable right now (copper + both plug contacts, rising
        /// while a plug is pulled) and the voltage across it (V, end A minus end B).
        /// </summary>
        public (double Resistance, double Drop) CableElectrical(WorldCable cable)
        {
            if (Circuit == null || Simulator == null || !Circuit.CableDevices.TryGetValue(cable.Id, out var r)) return (double.NaN, 0);
            return (r.EffectiveResistance, r.IsFailed ? 0 : Simulator.NodeVoltage(r.A) - Simulator.NodeVoltage(r.B));
        }

        public double Voltage(CableEnd end) => Circuit == null || Simulator == null ? double.NaN : Circuit.Voltage(Simulator, end);

        /// <summary>[split] The circuit nodes of a board's pins.</summary>
        private IEnumerable<int> BoardNodes(string refDes)
        {
            if (Circuit == null) yield break;
            string prefix = refDes + ".";
            foreach (var kv in Circuit.Compiled.NodeByPin)
                if (kv.Value > 0 && kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) yield return kv.Value;
        }

        /// <summary>Live screen of a display object (OLED framebuffer / LCD text), if any.</summary>
        /// <summary>[neopixel] The WS2812B strip a component object is, while the world runs (null otherwise).</summary>
        public RobotTwin.CoreSim.Firmware.Devices.Ws2812Load? LedStripOf(WorldObject o)
        {
            if (Circuit == null || o.Kind != WorldObjectKind.Component) return null;
            var r = Circuit.ComponentRef(o.Id);
            return r == null ? null : Circuit.Compiled.DevicesOf(r).OfType<RobotTwin.CoreSim.Firmware.Devices.Ws2812Load>().FirstOrDefault();
        }

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
