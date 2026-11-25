using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Firmware;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Instruments;
using RobotWin.Studio.Shell;
using Switch = RobotTwin.CoreSim.Analog.Devices.Switch;

namespace RobotWin.Studio.Simulation
{
    public enum SimState
    {
        Stopped,
        Running,
        Paused
    }

    public enum ProbeKind
    {
        Voltage,
        Current
    }

    /// <summary>Samples of one probed net or part current, taken at every accepted solver step.</summary>
    public sealed class ProbeChannel : ISampleSource
    {
        private readonly SampleBuffer _buffer;

        public ProbeChannel(string name, ProbeKind kind, Color color, Func<double> read, int capacity = 200_000)
        {
            Name = name;
            Kind = kind;
            Color = color;
            Read = read;
            _buffer = new SampleBuffer(capacity);
        }

        public string Name { get; }
        public ProbeKind Kind { get; }
        public Color Color { get; }
        public Func<double> Read { get; set; }
        public int Count => _buffer.Count;
        public double Last => _buffer.Last;
        public string Unit => Kind == ProbeKind.Voltage ? "V" : "A";

        /// <summary>Net name (voltage probes) or part designator (current probes).</summary>
        public string Target => Kind == ProbeKind.Voltage ? Name : Name.Substring(0, Math.Max(0, Name.IndexOf(' ')));

        public void Add(double t, double v) => _buffer.Add(t, v);

        public void Clear() => _buffer.Clear();

        public (double T, double V) this[int i] => _buffer[i];

        public int LowerBound(double time) => _buffer.LowerBound(time);

        public (double Min, double Max, double Avg, double Rms, double Freq) Measure(double from, double to)
        {
            int i0 = LowerBound(from), i1 = LowerBound(to);
            if (i1 - i0 < 2) return (double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
            double min = double.PositiveInfinity, max = double.NegativeInfinity, area = 0, area2 = 0;
            for (int i = i0; i < i1; i++)
            {
                var (t, v) = this[i];
                min = Math.Min(min, v);
                max = Math.Max(max, v);
                if (i + 1 < i1)
                {
                    var (t2, v2) = this[i + 1];
                    double dt = t2 - t;
                    area += 0.5 * (v + v2) * dt;
                    area2 += 0.5 * (v * v + v2 * v2) * dt;
                }
            }

            double span = this[i1 - 1].T - this[i0].T;
            double avg = span > 0 ? area / span : this[i0].V;
            double rms = span > 0 ? Math.Sqrt(Math.Max(0, area2 / span)) : Math.Abs(avg);

            // Frequency from rising crossings of the mid level.
            double mid = 0.5 * (min + max), first = double.NaN, last = double.NaN;
            int crossings = 0;
            if (max - min > Math.Max(5e-3, 0.02 * Math.Max(Math.Abs(min), Math.Abs(max))))
            {
                for (int i = i0 + 1; i < i1; i++)
                {
                    var (ta, va) = this[i - 1];
                    var (tb, vb) = this[i];
                    if (va < mid && vb >= mid)
                    {
                        double tc = ta + (mid - va) / (vb - va) * (tb - ta);
                        if (crossings == 0) first = tc;
                        last = tc;
                        crossings++;
                    }
                }
            }

            double freq = crossings >= 2 ? (crossings - 1) / (last - first) : double.NaN;
            return (min, max, avg, rms, freq);
        }
    }

    public sealed class SimLogEntry
    {
        public SimLogEntry(double time, string device, string message, AnalogEventKind kind)
        {
            Time = time;
            Device = device;
            Message = message;
            Kind = kind;
        }

        public double Time { get; }
        public string Device { get; }
        public string Message { get; }
        public AnalogEventKind Kind { get; }
        public string TimeText => SiValue.Format(Time, "s");
        public string KindText => Kind == AnalogEventKind.Failure ? "Hasar" : Kind == AnalogEventKind.Warning ? "Uyarı" : "Durum";
    }

    /// <summary>
    /// Drives the analog simulator in real time from the render loop, samples the
    /// probes at every solver step and exposes live values to the editors.
    /// </summary>
    public sealed class SimulationSession : INotifyPropertyChanged
    {
        private static readonly Color[] ProbeColors =
        {
            Color.FromRgb(0xED, 0xED, 0xEF), Color.FromRgb(0x6E, 0x9B, 0xFF), Color.FromRgb(0xD4, 0xA8, 0x4B),
            Color.FromRgb(0x8C, 0x8C, 0x94), Color.FromRgb(0xE5, 0x48, 0x4D), Color.FromRgb(0x5B, 0xB9, 0x8B)
        };

        private readonly Workspace _workspace;
        private readonly Stopwatch _wall = new Stopwatch();
        private double _lastWall;
        private SimState _state;
        private double _speed = 1.0;
        private double _realtimeFactor = 1.0;
        private string _message = string.Empty;
        private bool _hooked;
        private readonly List<FirmwareCoSimulation> _firmware = new List<FirmwareCoSimulation>();
        private readonly System.Text.StringBuilder _serial = new System.Text.StringBuilder();

        /// <summary>Lockstep slice between firmware and circuit while an MCU runs.</summary>
        public const double FirmwareSlice = 50e-6;

        public IReadOnlyList<FirmwareCoSimulation> Firmware => _firmware;

        /// <summary>Everything the firmware printed on Serial during this run.</summary>
        public string SerialText => _serial.ToString();

        /// <summary>Characters ever written to the serial log (keeps growing after the buffer is trimmed).</summary>
        public long SerialTotal { get; private set; }

        public event EventHandler<string>? SerialReceived;

        public SimulationSession(Workspace workspace)
        {
            _workspace = workspace;
        }

        public SimState State
        {
            get => _state;
            private set
            {
                _state = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public bool IsActive => State != SimState.Stopped;

        public CompiledCircuit? Compiled { get; private set; }
        public AnalogSimulator? Simulator { get; private set; }

        /// <summary>Simulated seconds per wall-clock second.</summary>
        public double Speed
        {
            get => _speed;
            set
            {
                _speed = Math.Max(1e-4, Math.Min(100, value));
                OnPropertyChanged();
            }
        }

        public double Time => Simulator?.Time ?? 0.0;
        public double RealtimeFactor => _realtimeFactor;

        public string StatusText => State switch
        {
            SimState.Running => _realtimeFactor < 0.95 * Speed
                ? $"Çalışıyor · t = {SiValue.Format(Time, "s")} · gerçek zamanın {_realtimeFactor:P0}'i"
                : $"Çalışıyor · t = {SiValue.Format(Time, "s")}",
            SimState.Paused => $"Duraklatıldı · t = {SiValue.Format(Time, "s")}",
            _ => string.IsNullOrEmpty(_message) ? "Simülasyon kapalı" : _message
        };

        public ObservableCollection<ProbeChannel> Probes { get; } = new ObservableCollection<ProbeChannel>();
        public ObservableCollection<SimLogEntry> Log { get; } = new ObservableCollection<SimLogEntry>();
        public List<string> CompileWarnings { get; } = new List<string>();

        /// <summary>Raised once per rendered frame while running (and on single steps).</summary>
        public event EventHandler? Updated;

        /// <summary>Raised when a part fails (burns, blows, breaks down).</summary>
        public event EventHandler<SimLogEntry>? Failure;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool Start()
        {
            Stop();
            Log.Clear();
            CompileWarnings.Clear();
            try
            {
                var settings = AppSettings.Current;
                double ambient = _workspace.Schematic.Assembly is { Arena: "custom", World: { } world } ? world.AmbientC : settings.AmbientTempC;
                AmbientC = ambient;
                Compiled = CircuitCompiler.Compile(_workspace.Schematic, _workspace.Library, new CompileOptions
                {
                    Tolerances = _workspace.Schematic.Tolerances,
                    ToleranceRun = 0,
                    AmbientC = ambient
                });
                CompileWarnings.AddRange(Compiled.Warnings);
                AttachInstruments();
                Simulator = new AnalogSimulator(Compiled.Circuit, new AnalogOptions
                {
                    MaxStep = settings.MaxStep,
                    RelTol = settings.RelTol,
                    AmbientTempC = ambient,
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
            }
            catch (AnalogSimulationException ex)
            {
                Fail(ex.Message);
                return false;
            }

            Simulator.SubstepAccepted = OnSubstep;
            ReplayTime = double.NaN;
            Stress.Clear();
            _lastStressSample = double.NegativeInfinity;
            Scope.ResetAcquisition();
            BurnTimes.Clear();
            _trajectory.Clear();
            _lastPose = double.NegativeInfinity;
            _message = string.Empty;
            AttachFirmware();
            AttachMechanics();
            foreach (var probe in Probes) probe.Clear();
            RebindProbes();
            foreach (var w in CompileWarnings) Log.Add(new SimLogEntry(0, "derleyici", w, AnalogEventKind.Warning));
            if (_workspace.Schematic.Tolerances is { Active: true } tol)
            {
                Log.Add(new SimLogEntry(0, "tolerans", $"Gerçekçi parça değerleri (tohum {tol.Seed}): {Compiled.ToleranceDraws.Sum(d => d.Value.Count)} değer toleransından çekildi", AnalogEventKind.StateChange));
            }

            State = SimState.Running;
            _wall.Restart();
            _lastWall = 0;
            Hook(true);
            SampleProbes(0);
            Updated?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void Pause()
        {
            if (State != SimState.Running) return;
            State = SimState.Paused;
            Hook(false);
            _sounds.Silence();
        }

        public void Resume()
        {
            if (State != SimState.Paused) return;
            RobotWin.Studio.Firmware.FirmwareDebugger.ReleaseHalts(this);   // [debug] a program stopped at a breakpoint goes on too
            State = SimState.Running;
            _wall.Restart();
            _lastWall = 0;
            Hook(true);
        }

        public void Stop()
        {
            Hook(false);
            RobotWin.Studio.Firmware.FirmwareDebugger.Unregister(this);   // [debug]
            _sounds.Reset();
            _firmware.Clear();
            DisposeEsp32();
            Mechanics = null;
            ProbeMeter = null;
            Array.Clear(_scopeLoads, 0, _scopeLoads.Length);
            if (Simulator != null) Simulator.SubstepAccepted = null;
            Simulator = null;
            Compiled = null;
            State = SimState.Stopped;
            Updated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Advances by a fixed interval (used by the step button and tests).</summary>
        public void StepOnce(double seconds = 1e-3)
        {
            if (Simulator == null && !Start()) return;
            if (State == SimState.Running) Pause();
            Advance(seconds);
            Updated?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(StatusText));
        }

        private void Hook(bool on)
        {
            if (on && !_hooked)
            {
                CompositionTarget.Rendering += OnFrame;
                _hooked = true;
            }
            else if (!on && _hooked)
            {
                CompositionTarget.Rendering -= OnFrame;
                _hooked = false;
            }
        }

        private void OnFrame(object? sender, EventArgs e)
        {
            if (State != SimState.Running || Simulator == null) return;
            double now = _wall.Elapsed.TotalSeconds;
            double wallDt = Math.Min(0.05, now - _lastWall);
            _lastWall = now;
            if (wallDt <= 0) return;

            double target = wallDt * Speed;
            var budget = Stopwatch.StartNew();
            double done;
            using (Shell.Perf.Measure("devre")) done = Advance(target, budget, Shell.UiHeadroom.SimulationBudget(0.022));   // room for the rest of the UI
            _realtimeFactor = 0.85 * _realtimeFactor + 0.15 * (done / wallDt);
            Updated?.Invoke(this, EventArgs.Empty);
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(Time));
            if (Compiled != null && State == SimState.Running) _sounds.Update(Compiled.Circuit.Devices, Speed);
        }

        private readonly Audio.SimSounds _sounds = new Audio.SimSounds("sch:");

        /// <summary>Simulates up to <paramref name="seconds"/>; returns the simulated time actually covered.</summary>
        private double Advance(double seconds, Stopwatch? budget = null, double maxWallSeconds = double.PositiveInfinity)
        {
            if (Simulator == null) return 0;
            double chunk = Math.Min(seconds, Math.Max(1e-5, Speed * 1e-3));
            if (_firmware.Count > 0) chunk = Math.Min(chunk, FirmwareSlice);
            if (_esp32.Count > 0) chunk = Math.Min(chunk, Esp32Slice);
            double covered = 0;
            try
            {
                while (covered < seconds - 1e-15)
                {
                    double dt = Math.Min(chunk, seconds - covered);
                    // [debug] One clock: a program stopping inside the slice stops the circuit at that very instant.
                    FirmwareCoSimulation? halted = null;
                    for (int f = 0; f < _firmware.Count; f++)
                    {
                        double ran = _firmware[f].Advance(dt, Simulator.Time);
                        if (_firmware[f].Halted && (halted == null || ran < dt))
                        {
                            halted ??= _firmware[f];
                            dt = Math.Min(dt, ran);
                        }
                    }

                    if (halted != null && dt <= 0)
                    {
                        Pause();
                        RobotWin.Studio.Firmware.FirmwareDebugger.OnHalted(halted);
                        break;
                    }

                    for (int f = 0; f < _esp32.Count; f++) _esp32[f].Advance(dt, Simulator.Time);
                    Simulator.Step(dt);
                    if (Simulator.Time - _lastStressSample >= 2e-3 || Simulator.Time < _lastStressSample)
                    {
                        Stress.Sample(Simulator.Circuit, Simulator.Time, AmbientC);
                        _lastStressSample = Simulator.Time;
                    }
                    if (Mechanics != null)
                    {
                        Mechanics.Advance(dt);
                        SamplePose();
                        UpdateImus(dt);
                    }

                    covered += dt;
                    DrainEvents();
                    if (halted != null)
                    {
                        Pause();
                        RobotWin.Studio.Firmware.FirmwareDebugger.OnHalted(halted);
                        break;
                    }

                    if (budget != null && budget.Elapsed.TotalSeconds > maxWallSeconds) break;
                }
            }
            catch (AnalogSimulationException ex)
            {
                Fail("Simülasyon durdu: " + ex.Message);
            }

            return covered;
        }

        private void DrainEvents()
        {
            if (Mechanics != null)
            {
                foreach (var m in Mechanics.DrainEvents())
                {
                    var entry = new SimLogEntry(Time, m.Source, m.Message, m.Severe ? AnalogEventKind.Failure : AnalogEventKind.StateChange);
                    Log.Add(entry);
                    if (m.Severe) Failure?.Invoke(this, entry);
                }
            }

            if (Simulator == null || Simulator.Events.Count == 0) return;
            foreach (var e in Simulator.DrainEvents())
            {
                string device = e.DeviceId == ProbeMeterId ? "Multimetre" : e.DeviceId;
                var entry = new SimLogEntry(e.Time, device, e.Message, e.Kind);
                Log.Add(entry);
                if (Log.Count > 500) Log.RemoveAt(0);
                if (e.Kind == AnalogEventKind.Failure)
                {
                    string? part = PartOfDevice(e.DeviceId);
                    if (part != null) BurnTimes[part] = _wallClock.Elapsed.TotalSeconds;
                    _sounds.OnEvent(e.Kind, e.DeviceId, e.Message, Simulator.Circuit.Devices.FirstOrDefault(d => d.Id == e.DeviceId));
                    Failure?.Invoke(this, entry);
                    if (Shell.AppSettings.Current.PauseOnFailure && State == SimState.Running) Pause();
                }
            }
        }

        private void Fail(string message)
        {
            Hook(false);
            _message = message;
            Log.Add(new SimLogEntry(Time, "solver", message, AnalogEventKind.Failure));
            Simulator = null;
            Compiled = null;
            State = SimState.Stopped;
            Updated?.Invoke(this, EventArgs.Empty);
        }

        private void SampleProbes(double time)
        {
            for (int i = 0; i < Probes.Count; i++)
            {
                var p = Probes[i];
                double v = p.Read();
                if (!double.IsNaN(v)) p.Add(time, v);
            }
        }

        // ------------------------------------------------------------------ mechanics

        /// <summary>Rigid-body world the circuit drives (robot or drone), when the project has an assembly.</summary>
        public MechatronicSystem? Mechanics { get; private set; }

        private void AttachMechanics()
        {
            Mechanics = null;
            var assembly = _workspace.Schematic.Assembly;
            if (assembly == null || assembly.Kind == VehicleKind.None || Compiled == null || Simulator == null) return;
            try
            {
                Mechanics = new MechatronicSystem(assembly, _workspace.Schematic, _workspace.Library, Compiled, Simulator);
                Log.Add(new SimLogEntry(0, "montaj",
                    $"{(assembly.Kind == VehicleKind.Quadcopter ? "Quadcopter" : "Robot araba")} · {Mechanics.TotalMass * 1000:F0} g · {Mechanics.Arena.Name}",
                    AnalogEventKind.StateChange));
            }
            catch (Exception ex)
            {
                Log.Add(new SimLogEntry(0, "montaj", "Mekanik model kurulamadı: " + ex.Message, AnalogEventKind.Warning));
            }
        }

        private readonly List<PoseSample> _trajectory = new List<PoseSample>();
        private double _lastPose = double.NegativeInfinity;

        private void SamplePose()
        {
            var m = Mechanics!;
            if (Time - _lastPose < 0.02 || _trajectory.Count > 300_000) return;
            _lastPose = Time;
            var (roll, pitch, yaw) = m.Body.Orientation.ToEuler();
            var p = m.Body.Position;
            _trajectory.Add(new PoseSample { T = Time, X = p.X, Y = p.Y, Z = p.Z, Roll = roll, Pitch = pitch, Yaw = yaw, Speed = m.Speed });
        }

        /// <summary>Snapshot of this run as a .rwsim recording (probes, log, serial, trajectory, project).</summary>
        public SimulationRecording CreateRecording()
        {
            var rec = new SimulationRecording
            {
                Title = _workspace.Schematic.Title,
                Duration = Time,
                Serial = SerialText,
                Trajectory = new List<PoseSample>(_trajectory)
            };
            foreach (var p in Probes)
            {
                var ch = new RecordedChannel { Name = p.Name, Unit = p.Unit, Color = p.Color.ToString() };
                for (int i = 0; i < p.Count; i++)
                {
                    var (t, v) = p[i];
                    ch.Time.Add(t);
                    ch.Value.Add(v);
                }

                rec.Channels.Add(ch);
            }

            foreach (var e in Log) rec.Events.Add(new RecordedEvent { Time = e.Time, Device = e.Device, Message = e.Message, Kind = e.Kind.ToString() });
            var project = new ProjectPackage
            {
                Schematic = Schematic.FromJson(_workspace.Schematic.ToJson()),
                Firmware = _workspace.Project.Firmware,
                Parts = _workspace.Project.Parts
            };
            rec.Project = project;
            return rec;
        }

        /// <summary>Shows a saved recording: waveforms in the scope, events in the log, serial text.</summary>
        public void ShowRecording(SimulationRecording rec)
        {
            Stop();
            for (int i = 0; i < ScopeProbes.Length; i++) DetachScopeChannel(i);
            Probes.Clear();
            Log.Clear();
            foreach (var ch in rec.Channels)
            {
                var color = ProbeColors[Probes.Count % ProbeColors.Length];
                try
                {
                    color = (Color)ColorConverter.ConvertFromString(ch.Color);
                }
                catch (FormatException)
                {
                }

                var probe = new ProbeChannel(ch.Name, ch.Unit == "A" ? ProbeKind.Current : ProbeKind.Voltage, color, () => double.NaN, Math.Max(40000, ch.Time.Count));
                for (int i = 0; i < ch.Time.Count; i++) probe.Add(ch.Time[i], ch.Value[i]);
                Probes.Add(probe);
            }

            foreach (var e in rec.Events)
            {
                Enum.TryParse(e.Kind, out AnalogEventKind kind);
                Log.Add(new SimLogEntry(e.Time, e.Device, e.Message, kind));
            }

            _serial.Clear();
            _serial.Append(rec.Serial);
            SerialTotal = rec.Serial.Length;
            // The first four recorded channels go back onto the scope, frozen at the end of the recording.
            ReplayTime = rec.Duration;
            Scope.ResetAcquisition();
            foreach (var probe in Probes.Take(ScopeProbes.Length).ToList()) AssignToScope(probe, null, null);
            Scope.Acquire(rec.Duration);
            _message = $"Kayıt: {rec.Title} · {SiValue.Format(rec.Duration, "s")}";
            OnPropertyChanged(nameof(StatusText));
            Updated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Puts the vehicle back at its start pose (the circuit keeps running).</summary>
        public void ResetVehicle()
        {
            Mechanics?.Reset();
            Updated?.Invoke(this, EventArgs.Empty);
        }

        // ------------------------------------------------------------------ firmware

        /// <summary>Resolves a board's "firmware" parameter: a .hex path or "builtin:name".</summary>
        public static string? ResolveFirmwarePath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (value!.StartsWith("builtin:", StringComparison.Ordinal))
            {
                return System.IO.Path.Combine(AppContext.BaseDirectory, "Firmware", value.Substring(8));
            }

            return value;
        }

        // ------------------------------------------------------------------ ESP32 (native HAL model)

        /// <summary>Lockstep slice of ESP32 boards (the sketch runs natively between circuit steps).</summary>
        public const double Esp32Slice = 100e-6;

        private readonly List<RobotTwin.CoreSim.Firmware.Esp32.IEsp32Board> _esp32 = new List<RobotTwin.CoreSim.Firmware.Esp32.IEsp32Board>();

        public IReadOnlyList<RobotTwin.CoreSim.Firmware.Esp32.IEsp32Board> Esp32Boards => _esp32;

        private void OnSubstep(double time)
        {
            SampleProbes(time);
            for (int i = 0; i < _esp32.Count; i++) _esp32[i].OnSubstep(time);
        }

        private void DisposeEsp32()
        {
            foreach (var e in _esp32) e.Dispose();
            _esp32.Clear();
        }

        private void AppendSerial(string text)
        {
            _serial.Append(text);
            SerialTotal += text.Length;
            if (_serial.Length > 200_000) _serial.Remove(0, _serial.Length - 200_000);
            SerialReceived?.Invoke(this, text);
        }

        /// <summary>Starts an ESP32 board on the project's sketch (built with MSVC on first use, then cached).</summary>
        private void AttachEsp32(SchematicPart part)
        {
            var fw = _workspace.Project.FirmwareFor(part.RefDes);
            part.Parameters.TryGetValue("firmware", out var param);
            string? source = fw?.Bundle(), fileName = fw?.FileName;   // [code] every file of the sketch
            bool useProject = string.IsNullOrEmpty(param) || param!.StartsWith("project", StringComparison.OrdinalIgnoreCase);
            if (!useProject || string.IsNullOrWhiteSpace(source))
            {
                string? path = ResolveFirmwarePath(param);
                if (path != null && path.EndsWith(".ino", StringComparison.OrdinalIgnoreCase) && System.IO.File.Exists(path))
                {
                    source = System.IO.File.ReadAllText(path);
                    fileName = System.IO.Path.GetFileName(path);
                }
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                Log.Add(new SimLogEntry(0, part.RefDes, "ESP32'de kod yok: Kod sayfasından yazıp yükleyin.", AnalogEventKind.Warning));
                return;
            }

            // [esp32hw] The hardware-compatible mode runs the real-chip build on our Xtensa emulator when there is one.
            if (RobotTwin.CoreSim.Design.Esp32Parts.IsHardwareMode(part.Parameters) && AttachEmulatedEsp32(part, source!, fileName)) return;

            string? dll = RobotWin.Studio.Firmware.ArduinoCompiler.CachedEsp32Dll(source!);
            if (dll == null)
            {
                Log.Add(new SimLogEntry(0, part.RefDes, $"{fileName} MSVC ile derleniyor…", AnalogEventKind.StateChange));
                var build = RobotWin.Studio.Firmware.ArduinoCompiler.CompileEsp32(source!);
                if (!build.Success || build.Hex == null)
                {
                    string why = build.Diagnostics.FirstOrDefault()?.Message ?? "derleme başarısız";
                    Log.Add(new SimLogEntry(0, part.RefDes, "ESP32 çizimi derlenemedi: " + why, AnalogEventKind.Warning));
                    return;
                }

                dll = build.Hex.Substring("esp32:".Length);
            }

            try
            {
                string refDes = part.RefDes;
                var cosim = new RobotTwin.CoreSim.Firmware.Esp32.Esp32CoSimulation(dll, Compiled!, refDes);
                cosim.SerialOutput += AppendSerial;
                _esp32.Add(cosim);
                Log.Add(new SimLogEntry(0, refDes, $"Firmware yüklendi: {fileName} (ESP32 HAL modeli, 240 MHz)", AnalogEventKind.StateChange));
                AttachI2c(cosim.I2c, part);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is BadImageFormatException || ex is EntryPointNotFoundException || ex is System.IO.IOException || ex is InvalidOperationException)
            {
                Log.Add(new SimLogEntry(0, part.RefDes, "ESP32 başlatılamadı: " + ex.Message, AnalogEventKind.Warning));
            }
        }

        /// <summary>
        /// [esp32hw] Starts a hardware-compatible ESP32 on the Xtensa emulator with the sketch's real-chip build; false (after
        /// saying why: being built, or no toolchain) when there is none yet, and the board runs as the fast mode.
        /// </summary>
        private bool AttachEmulatedEsp32(SchematicPart part, string source, string? fileName)
        {
            var cached = RobotWin.Studio.Firmware.ArduinoCompiler.CachedEsp32Hardware(source);
            if (cached == null)
            {
                if (RobotWin.Studio.Firmware.ArduinoCompiler.Esp32ToolchainInstalled)
                {
                    _ = RobotWin.Studio.Firmware.ArduinoCompiler.EnsureEsp32HardwareBuildAsync(source);
                    Log.Add(new SimLogEntry(0, part.RefDes, RobotTwin.CoreSim.Design.Esp32Parts.HardwareModeBuilding, AnalogEventKind.Warning));
                }
                else
                {
                    Log.Add(new SimLogEntry(0, part.RefDes, RobotTwin.CoreSim.Design.Esp32Parts.HardwareModeNoToolchain, AnalogEventKind.Warning));
                }

                return false;
            }

            try
            {
                string refDes = part.RefDes;
                byte[] elf = System.IO.File.ReadAllBytes(cached.Value.Elf);
                byte[]? image = cached.Value.Merged != null ? System.IO.File.ReadAllBytes(cached.Value.Merged) : null;
                var board = new RobotTwin.CoreSim.Firmware.Esp32.Esp32EmulatedBoard(elf, image, Compiled!, refDes);
                board.SerialOutput += AppendSerial;
                board.Log += m => Log.Add(new SimLogEntry(Simulator?.Time ?? 0, refDes, m, AnalogEventKind.StateChange));
                _esp32.Add(board);
                Log.Add(new SimLogEntry(0, refDes, $"Firmware yüklendi: {fileName} (gerçek yonga yazılımı, RobotWin Xtensa emülatörü)", AnalogEventKind.StateChange));
                Log.Add(new SimLogEntry(0, refDes, RobotTwin.CoreSim.Design.Esp32Parts.HardwareModeRunning, AnalogEventKind.StateChange));
                AttachI2c(board.I2c, part);
                return true;
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                Log.Add(new SimLogEntry(0, part.RefDes, "ESP32 emülatörü başlatılamadı: " + ex.Message, AnalogEventKind.Warning));
                return false;
            }
        }

        private void AttachFirmware()
        {
            _firmware.Clear();
            DisposeEsp32();
            _serial.Clear();
            SerialTotal = 0;
            _i2c.Clear();
            if (Compiled == null || Simulator == null) return;
            foreach (var part in _workspace.Schematic.Parts)
            {
                var def = _workspace.Library.Find(part.PartId);
                if (def?.Symbol != SymbolKind.Board) continue;
                if (RobotWin.Studio.Firmware.ArduinoCompiler.IsEsp32(RobotWin.Studio.Firmware.ArduinoCompiler.FqbnFor(def.Id)))
                {
                    AttachEsp32(part);
                    continue;
                }

                part.Parameters.TryGetValue("firmware", out var fw);
                var embedded = _workspace.Project.FirmwareFor(part.RefDes);
                string? hex = null, source = null;
                if ((string.IsNullOrEmpty(fw) || fw!.StartsWith("project", StringComparison.OrdinalIgnoreCase)) && embedded?.Hex != null)
                {
                    hex = embedded.Hex;
                    source = embedded.FileName + (embedded.IsBuildCurrent ? string.Empty : " (eski derleme)");
                }
                else if (!string.IsNullOrEmpty(fw))
                {
                    string? path = ResolveFirmwarePath(fw);
                    if (path == null) continue;
                    source = System.IO.Path.GetFileName(path);
                    try
                    {
                        hex = System.IO.File.ReadAllText(path);
                    }
                    catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
                    {
                        Log.Add(new SimLogEntry(0, part.RefDes, "Firmware okunamadı: " + ex.Message, AnalogEventKind.Warning));
                        continue;
                    }
                }

                if (hex == null) continue;
                try
                {
                    var mcu = new Atmega328P();
                    mcu.LoadHex(hex);
                    string refDes = part.RefDes;
                    var cosim = new FirmwareCoSimulation(mcu, Compiled, refDes, () => PinVoltage(refDes, "5V"));
                    cosim.SerialOutput += AppendSerial;
                    _firmware.Add(cosim);
                    RobotWin.Studio.Firmware.FirmwareDebugger.Register(this, "p:" + refDes, refDes, cosim, hex, () => Time, Resume);   // [debug]
                    Log.Add(new SimLogEntry(0, refDes, $"Firmware yüklendi: {source} (ATmega328P @ 16 MHz)", AnalogEventKind.StateChange));
                    AttachI2c(cosim.I2c, part);
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is System.IO.InvalidDataException || ex is UnauthorizedAccessException)
                {
                    Log.Add(new SimLogEntry(0, part.RefDes, "Firmware yüklenemedi: " + ex.Message, AnalogEventKind.Warning));
                }
            }
        }

        private readonly Dictionary<string, II2cDevice> _i2c = new Dictionary<string, II2cDevice>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Protocol models of I²C modules on a running board's bus (by module RefDes).</summary>
        public IReadOnlyDictionary<string, II2cDevice> I2cDevices => _i2c;

        public II2cDevice? I2cDeviceOf(string refDes) => _i2c.TryGetValue(refDes, out var d) ? d : null;

        /// <summary>Puts every I²C module whose SDA/SCL share nets with the board's I²C pins on its bus.</summary>
        private void AttachI2c(I2cBus bus, SchematicPart board)
        {
            if (Compiled == null) return;
            var netlist = Compiled.Netlist;
            string? NetOfAny(string refDes, params string[] pins) =>
                pins.Select(p => netlist.NetOf(refDes, p)?.Name).FirstOrDefault(n => n != null);
            string? sda = NetOfAny(board.RefDes, "SDA", "A4", "IO21");
            string? scl = NetOfAny(board.RefDes, "SCL", "A5", "IO22");
            if (sda == null || scl == null) return;
            foreach (var part in _workspace.Schematic.Parts)
            {
                var def = _workspace.Library.Find(part.PartId);
                if (def == null || def.FindPin("SDA") == null || def.FindPin("SCL") == null || def.Symbol == SymbolKind.Board) continue;
                if (netlist.NetOf(part.RefDes, "SDA")?.Name != sda || netlist.NetOf(part.RefDes, "SCL")?.Name != scl) continue;
                var p = _workspace.Schematic.ResolveParameters(_workspace.Library, part);
                byte address = ParseAddress(p.TryGetValue("address", out var a) ? a : null);
                II2cDevice? device = null;
                switch (def.Id)
                {
                    case "oled-ssd1306":
                        device = new RobotTwin.CoreSim.Firmware.Devices.Ssd1306(p.TryGetValue("height", out var h) && h == "32" ? 32 : 64, address == 0 ? (byte)0x3C : address);
                        break;
                    case "lcd-i2c":
                    {
                        int cols = int.TryParse(p.TryGetValue("columns", out var cs) ? cs : "16", out var cc) ? cc : 16;
                        int rows = int.TryParse(p.TryGetValue("rows", out var rs) ? rs : "2", out var rr) ? rr : 2;
                        device = new RobotTwin.CoreSim.Firmware.Devices.Pcf8574Lcd(cols, rows, address == 0 ? (byte)0x27 : address);
                        break;
                    }

                    case "mpu6050":
                        device = new RobotTwin.CoreSim.Firmware.Devices.Mpu6050(address == 0 ? (byte)0x68 : address) { AccelNoise = 0.02, GyroNoise = 0.002 };
                        break;
                }

                if (device == null) continue;
                string r = part.RefDes;
                bus.Devices.Add(new GatedI2cDevice(device, () => PinVoltage(r, "VCC") - PinVoltage(r, "GND") > 2.7));
                _i2c[r] = device;
                Log.Add(new SimLogEntry(0, r, $"I²C veri yoluna bağlandı ({board.RefDes}, adres 0x{device.Address:X2})", AnalogEventKind.StateChange));
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
            catch (FormatException)
            {
                return 0;
            }
            catch (OverflowException)
            {
                return 0;
            }
        }

        private RobotTwin.CoreSim.Mechanics.Vec3 _imuLastVelocity;

        /// <summary>Feeds the vehicle's motion into MPU-6050 modules mounted on it.</summary>
        private void UpdateImus(double dt)
        {
            if (Mechanics == null || _i2c.Count == 0 || dt <= 0) return;
            var body = Mechanics.Body;
            var accelWorld = (body.Velocity - _imuLastVelocity) / dt;
            _imuLastVelocity = body.Velocity;
            var specific = body.Orientation.InverseRotate(accelWorld - new RobotTwin.CoreSim.Mechanics.Vec3(0, 0, -9.81));
            var rates = body.BodyRates;
            foreach (var s in Mechanics.Sensors)
            {
                if (s.Mount.Kind != SensorKind.Imu || !(I2cDeviceOf(s.Mount.RefDes) is RobotTwin.CoreSim.Firmware.Devices.Mpu6050 mpu)) continue;
                mpu.AccelX = specific.X;
                mpu.AccelY = specific.Y;
                mpu.AccelZ = specific.Z;
                mpu.GyroX = rates.X;
                mpu.GyroY = rates.Y;
                mpu.GyroZ = rates.Z;
            }
        }

        public FirmwareCoSimulation? FirmwareOf(string boardRefDes) =>
            _firmware.FirstOrDefault(f => string.Equals(f.BoardRefDes, boardRefDes, StringComparison.OrdinalIgnoreCase));

        public void SendSerial(string text)
        {
            foreach (var f in _firmware) f.Mcu.WriteSerial(text);
            foreach (var e in _esp32) e.SendSerial(text);
        }

        public void ResetBoard(string boardRefDes)
        {
            FirmwareOf(boardRefDes)?.Mcu.Reset();
            _esp32.FirstOrDefault(e => string.Equals(e.BoardRefDes, boardRefDes, StringComparison.OrdinalIgnoreCase))?.Reset();
            Log.Add(new SimLogEntry(Time, boardRefDes, "Reset", AnalogEventKind.StateChange));
        }

        // ------------------------------------------------------------------ probes

        /// <summary>
        /// Clips the scope probe of a channel onto a net (the next free channel, or <paramref name="channel"/>).
        /// <paramref name="at"/> is where on the sheet the probe tip was placed.
        /// </summary>
        public ProbeChannel AddVoltageProbe(string netName, int? channel = null, GridPoint? at = null)
        {
            var probe = Probes.FirstOrDefault(p => p.Kind == ProbeKind.Voltage && p.Name == netName);
            if (probe == null)
            {
                probe = new ProbeChannel(netName, ProbeKind.Voltage, ProbeColors[Probes.Count % ProbeColors.Length], () => double.NaN);
                Probes.Add(probe);
            }

            AssignToScope(probe, channel, at);
            RebindProbes();
            return probe;
        }

        public ProbeChannel AddCurrentProbe(string refDes, int? channel = null, GridPoint? at = null)
        {
            string name = refDes + " akımı";
            var probe = Probes.FirstOrDefault(p => p.Name == name);
            if (probe == null)
            {
                probe = new ProbeChannel(name, ProbeKind.Current, ProbeColors[Probes.Count % ProbeColors.Length], () => double.NaN);
                Probes.Add(probe);
            }

            AssignToScope(probe, channel, at);
            RebindProbes();
            return probe;
        }

        public void RemoveProbe(ProbeChannel probe)
        {
            for (int i = 0; i < ScopeProbes.Length; i++)
            {
                if (ReferenceEquals(ScopeProbes[i], probe)) DetachScopeChannel(i);
            }

            Probes.Remove(probe);
        }

        /// <summary>Removes every probe and detaches all scope channels.</summary>
        public void ClearProbes()
        {
            for (int i = 0; i < ScopeProbes.Length; i++) DetachScopeChannel(i);
            Probes.Clear();
            InstrumentsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RebindProbes()
        {
            BindScopeLoads();
            foreach (var probe in Probes)
            {
                if (Compiled == null || Simulator == null)
                {
                    probe.Read = () => double.NaN;
                    continue;
                }

                var sim = Simulator;
                if (probe.Kind == ProbeKind.Voltage)
                {
                    int node = Compiled.NodeByNet.TryGetValue(probe.Name, out int n) ? n : -1;
                    probe.Read = node < 0 ? () => double.NaN : () => sim.NodeVoltage(node);
                }
                else
                {
                    string refDes = probe.Name.Substring(0, probe.Name.IndexOf(' '));
                    var device = Compiled.DevicesOf(refDes).FirstOrDefault();
                    probe.Read = device == null ? () => double.NaN : () => DeviceCurrent(device);
                }
            }
        }

        // ------------------------------------------------------------------ live values

        public static double DeviceCurrent(AnalogDevice device) => device switch
        {
            Resistor r => r.Current,
            Diode d => d.Current,
            Capacitor c => c.Current,
            Inductor l => l.Current,
            Switch s => s.Current,
            Ammeter a => a.Reading,
            VoltageSource v => v.Current,
            CurrentSource c => c.Current,
            DcMotor m => m.Current,
            Bjt q => q.CollectorCurrent,
            Mosfet m => m.DrainCurrent,
            Fuse f => f.Current,
            LinearRegulator reg => reg.Current,
            OpAmp op => op.OutputCurrent,
            Servo s => s.SupplyCurrent,
            DigitalPin p => p.Current,
            _ => double.NaN
        };

        public double NetVoltage(string net)
        {
            if (Compiled == null || Simulator == null) return double.NaN;
            return Compiled.NodeByNet.TryGetValue(net, out int node) ? Simulator.NodeVoltage(node) : double.NaN;
        }

        public double PinVoltage(string refDes, string pin) =>
            Compiled == null || Simulator == null ? double.NaN : Compiled.PinVoltage(Simulator, refDes, pin);

        public AnalogDevice? MainDevice(string refDes) => Compiled?.DevicesOf(refDes).FirstOrDefault();

        public T? Device<T>(string id) where T : AnalogDevice => Compiled?.Device<T>(id);

        // ------------------------------------------------------------------ interaction

        public void SetSwitch(string refDes, bool closed)
        {
            if (MainDevice(refDes) is Switch s) s.Closed = closed;
        }

        public void SetPotPosition(string refDes, double position)
        {
            if (MainDevice(refDes) is Potentiometer p)
            {
                p.Position = position;
                Simulator?.NotifyDiscontinuity();
            }
        }

        public void SetSensorLevel(string refDes, double level)
        {
            if (MainDevice(refDes) is ControlledSource c) c.Level = level;
        }

        public void SetEnvironment(string refDes, double value)
        {
            switch (MainDevice(refDes))
            {
                case Thermistor t:
                    t.SensedTempC = value;
                    break;
                case Photoresistor l:
                    l.Lux = value;
                    break;
                case DcMotor m:
                    m.LoadTorque = value;
                    break;
                default:
                    return;
            }

            Simulator?.NotifyDiscontinuity();
        }

        public DigitalPin? BoardPin(string refDes, string pin) => Device<DigitalPin>(refDes + "." + pin);

        public void SetBoardPin(string refDes, string pin, PinMode mode)
        {
            var p = BoardPin(refDes, pin);
            if (p == null) return;
            p.StopPwm();
            p.Mode = mode;
        }

        public void SetBoardPwm(string refDes, string pin, double duty, double frequency = 490)
        {
            var p = BoardPin(refDes, pin);
            if (p == null) return;
            p.Mode = PinMode.OutputLow;
            p.SetPwm(frequency, duty, Time);
        }

        /// <summary>Restores every damaged part (after the user fixed the circuit).</summary>
        public void RepairAll()
        {
            if (Compiled == null) return;
            foreach (var device in Compiled.Circuit.Devices)
            {
                if (device.IsFailed || device is Multimeter m && (m.MilliAmpFuseBlown || m.AmpFuseBlown)) device.Repair();
            }

            BurnTimes.Clear();
            Simulator?.NotifyDiscontinuity();
            Log.Add(new SimLogEntry(Time, "kullanıcı", "Hasarlı parçalar değiştirildi", AnalogEventKind.StateChange));
            Updated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Replaces one damaged part (all of its devices) with a new one.</summary>
        public void RepairPart(string refDes)
        {
            if (Compiled == null) return;
            foreach (var device in Compiled.DevicesOf(refDes))
            {
                if (device.IsFailed || device is Multimeter m && (m.MilliAmpFuseBlown || m.AmpFuseBlown)) device.Repair();
            }

            BurnTimes.Remove(refDes);
            Simulator?.NotifyDiscontinuity();
            Log.Add(new SimLogEntry(Time, refDes, "Parça değiştirildi (onarıldı)", AnalogEventKind.StateChange));
            Updated?.Invoke(this, EventArgs.Empty);
        }

        public bool IsPartFailed(string refDes) =>
            Compiled != null && Compiled.DevicesOf(refDes).Any(d => d.IsFailed || d is Multimeter m && (m.MilliAmpFuseBlown || m.AmpFuseBlown));

        /// <summary>Part designator a device id belongs to.</summary>
        public string? PartOfDevice(string deviceId)
        {
            if (Compiled == null) return null;
            if (Compiled.DevicesByPart.ContainsKey(deviceId)) return deviceId;
            int cut = deviceId.IndexOfAny(new[] { '.', '#' });
            string head = cut > 0 ? deviceId.Substring(0, cut) : deviceId;
            return Compiled.DevicesByPart.ContainsKey(head) ? head : null;
        }

        // ------------------------------------------------------------------ instruments

        public const string ProbeMeterId = "~DMM";

        /// <summary>End time of a recording shown in the scope (NaN when a live run or nothing is shown).</summary>
        public double ReplayTime { get; private set; } = double.NaN;

        /// <summary>Time the instruments acquire at: the live simulation time or the end of a shown recording.</summary>
        public double InstrumentTime => IsActive ? Time : double.IsNaN(ReplayTime) ? Time : ReplayTime;

        /// <summary>Ambient temperature of the present run.</summary>
        public double AmbientC { get; private set; } = 25.0;

        private double _lastStressSample = double.NegativeInfinity;

        /// <summary>Peak stresses of every part during this run (for the fault diagnosis).</summary>
        public StressTracker Stress { get; } = new StressTracker();

        /// <summary>Wall-clock seconds at which each part failed (drives the burn animation).</summary>
        public Dictionary<string, double> BurnTimes { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        private readonly Stopwatch _wallClock = Stopwatch.StartNew();

        public double WallTime => _wallClock.Elapsed.TotalSeconds;

        /// <summary>The 4-channel digital oscilloscope.</summary>
        public Oscilloscope Scope { get; } = new Oscilloscope();

        /// <summary>Which probe each scope channel is clipped to.</summary>
        public ProbeChannel?[] ScopeProbes { get; } = new ProbeChannel?[4];

        /// <summary>A probe was just clipped on: scale that channel to the signal once data arrives.</summary>
        public bool[] ScopeAutoScale { get; } = new bool[4];

        /// <summary>Where on the sheet each scope probe tip sits (for drawing the probe flags).</summary>
        public GridPoint?[] ScopeProbePoints { get; } = new GridPoint?[4];

        private readonly ScopeProbeLoad?[] _scopeLoads = new ScopeProbeLoad?[4];

        /// <summary>Scope probes load the circuit like real 1×/10× probes.</summary>
        public bool ProbeLoading { get; set; } = true;

        /// <summary>Raised when probes, leads or instrument settings change.</summary>
        public event EventHandler? InstrumentsChanged;

        public void NotifyInstrumentsChanged() => InstrumentsChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>The scope channel that receives the next probe (selected on the scope panel).</summary>
        public int SelectedScopeChannel { get; set; }

        private void AssignToScope(ProbeChannel probe, int? channel, GridPoint? at)
        {
            int existing = Array.IndexOf(ScopeProbes, probe);
            int target;
            if (channel is int c)
            {
                target = c;
            }
            else if (existing >= 0)
            {
                target = existing;
            }
            else
            {
                target = Array.FindIndex(ScopeProbes, p => p == null);
                if (target < 0) target = Math.Max(0, Math.Min(3, SelectedScopeChannel));
            }

            if (existing >= 0 && existing != target) DetachScopeChannel(existing);
            ScopeProbes[target] = probe;
            if (at != null || ScopeProbePoints[target] == null || existing != target) ScopeProbePoints[target] = at;
            var ch = Scope.Channels[target];
            ch.Source = probe;
            ch.Enabled = true;
            ch.Label = probe.Name;
            bool wasCurrent = ch.IsCurrent;
            ch.Unit = probe.Unit;
            if (wasCurrent != ch.IsCurrent) ch.Scale = ch.IsCurrent ? 0.1 : 1.0;
            SelectedScopeChannel = target;
            ScopeAutoScale[target] = true;
            Scope.ResetTriggerScan();
            InstrumentsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void DetachScopeChannel(int index)
        {
            if (index < 0 || index >= ScopeProbes.Length) return;
            ScopeProbes[index] = null;
            ScopeProbePoints[index] = null;
            Scope.Channels[index].Source = null;
            Scope.Channels[index].Label = string.Empty;
            if (_scopeLoads[index] != null) _scopeLoads[index]!.Node = -1;
            InstrumentsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Puts the scope probe loads (R ∥ C to ground) on the probed nets.</summary>
        public void BindScopeLoads()
        {
            for (int i = 0; i < _scopeLoads.Length; i++)
            {
                var load = _scopeLoads[i];
                if (load == null) continue;
                var probe = ScopeProbes[i];
                int node = -1;
                if (ProbeLoading && probe != null && probe.Kind == ProbeKind.Voltage && Compiled != null &&
                    Compiled.NodeByNet.TryGetValue(probe.Name, out int n))
                {
                    node = n;
                }

                load.SetProbe(Scope.Channels[i].ProbeAttenuation);
                load.Node = node;
            }
        }

        // ---- multimeter with test leads

        /// <summary>The handheld meter whose red/black leads the user clips onto nets.</summary>
        public Multimeter? ProbeMeter { get; private set; }

        public string? MeterRedNet { get; private set; }
        public string? MeterBlackNet { get; private set; }
        public GridPoint? MeterRedPoint { get; private set; }
        public GridPoint? MeterBlackPoint { get; private set; }
        public MultimeterJack MeterJack { get; private set; } = MultimeterJack.VoltOhm;
        public MultimeterMode MeterMode { get; private set; } = MultimeterMode.DcVoltage;

        /// <summary>Instrument shown on the multimeter panel: null = the lead meter, otherwise a placed meter part.</summary>
        public string? MeterPart { get; set; }

        public Multimeter? ActiveMeter => MeterPart == null ? ProbeMeter : MainDevice(MeterPart) as Multimeter;

        public void SetMeterLead(bool red, string? net, GridPoint? at)
        {
            if (red)
            {
                MeterRedNet = net;
                MeterRedPoint = net == null ? null : at;
            }
            else
            {
                MeterBlackNet = net;
                MeterBlackPoint = net == null ? null : at;
            }

            ConnectProbeMeter();
            InstrumentsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetMeterJack(MultimeterJack jack)
        {
            MeterJack = jack;
            if (jack != MultimeterJack.VoltOhm && !MultimeterSpec.IsCurrent(MeterMode)) SetMeterMode(MultimeterMode.DcCurrent);
            ConnectProbeMeter();
            InstrumentsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetMeterMode(MultimeterMode mode)
        {
            var meter = ActiveMeter;
            if (MeterPart == null) MeterMode = mode;
            if (meter != null) meter.Mode = mode;
            else if (MeterPart != null && _workspace.Schematic.FindPart(MeterPart) is { } part) part.Parameters["mode"] = MultimeterSpec.ModeKey(mode);
            if (meter != null && MeterPart != null && _workspace.Schematic.FindPart(MeterPart) is { } placed) placed.Parameters["mode"] = MultimeterSpec.ModeKey(mode);
            InstrumentsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ConnectProbeMeter()
        {
            if (ProbeMeter == null || Compiled == null) return;
            int Node(string? net) => net != null && Compiled.NodeByNet.TryGetValue(net, out int n) ? n : -1;
            int red = Node(MeterRedNet), black = Node(MeterBlackNet);
            if (red < 0 || black < 0) ProbeMeter.Connect(-1, -1, MeterJack);
            else ProbeMeter.Connect(red, black, MeterJack);
            Simulator?.NotifyDiscontinuity();
        }

        private void AttachInstruments()
        {
            if (Compiled == null) return;
            ProbeMeter = Compiled.Circuit.Add(new Multimeter(ProbeMeterId, -1, -1) { Mode = MeterMode });
            for (int i = 0; i < _scopeLoads.Length; i++)
            {
                _scopeLoads[i] = Compiled.Circuit.Add(new ScopeProbeLoad("~CH" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            // Leads and probes left on nets that no longer exist are taken off.
            if (MeterRedNet != null && !Compiled.NodeByNet.ContainsKey(MeterRedNet)) MeterRedNet = null;
            if (MeterBlackNet != null && !Compiled.NodeByNet.ContainsKey(MeterBlackNet)) MeterBlackNet = null;
            ConnectProbeMeter();
            if (MeterPart != null && !(Compiled.DevicesOf(MeterPart).FirstOrDefault() is Multimeter)) MeterPart = null;
        }

        /// <summary>Runs the fault diagnosis on the present circuit (with live values while simulating).</summary>
        public List<Diagnosis> Diagnose()
        {
            if (Compiled != null && Simulator != null)
            {
                var list = CircuitDiagnostics.Analyze(_workspace.Schematic, _workspace.Library, Compiled, Simulator, Stress, AmbientC);
                if (ProbeMeter != null && (ProbeMeter.MilliAmpFuseBlown || ProbeMeter.AmpFuseBlown))
                {
                    list.Insert(0, new Diagnosis(DiagnosisSeverity.Error, "METER_FUSE", "Multimetre sigortası atık",
                        "Kırmızı prob akım girişindeyken iki nokta arasına (paralel) değdirildi: multimetre kaynağı kısa devre etti ve sigortası attı.",
                        "Gerilim ölçerken kırmızı probu VΩ girişine takın. Akımı ölçmek için multimetre parçasını devreye SERİ bağlayın. Sigortayı 'Hasarlı parçaları onar' ile değiştirin."));
                }

                if (ActiveMeter is { } meter && (meter.Mode == MultimeterMode.Resistance || meter.Mode == MultimeterMode.Continuity ||
                                                 meter.Mode == MultimeterMode.Diode || meter.Mode == MultimeterMode.Capacitance) &&
                    meter.IsConnected && Multimeter.CircuitIsEnergized(Compiled.Circuit))
                {
                    list.Insert(0, new Diagnosis(DiagnosisSeverity.Warning, "METER_POWERED", "Direnç/diyot/kapasite ölçümü enerjili devrede",
                        "Multimetre Ω, diyot ve kapasite ölçerken kendi küçük test akımını kullanır. Devrede çalışan bir kaynak varsa okunan değer anlamsızdır.",
                        "Simülasyonda kaynakları kapatın (veya parçayı devreden ayırıp ölçün), sonra ölçün.", MeterPart ?? string.Empty));
                }

                return list;
            }

            return CircuitDiagnostics.Analyze(_workspace.Schematic, _workspace.Library);
        }


        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
