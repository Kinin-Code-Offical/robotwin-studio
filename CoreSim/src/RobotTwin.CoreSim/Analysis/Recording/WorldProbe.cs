using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Analysis.Recording
{
    /// <summary>
    /// [analysis] Samples every signal of a running <see cref="WorldRunner"/> into a <see cref="ChannelStore"/>:
    /// rigid bodies (pose, velocity, acceleration, net contact force, energy), joints (angle, speed,
    /// reaction force/torque, drive torque), every analog device signal (currents, voltages, power,
    /// temperatures, speeds …), named circuit nodes, scene totals, firmware serial and events — and the
    /// full load set (contacts, joint reactions) of the parts selected for stress analysis.
    /// </summary>
    public sealed class WorldProbe
    {
        private sealed class BodyChannels
        {
            public SceneBody Body = null!;
            public Channel Px = null!, Py = null!, Pz = null!, Qw = null!, Qx = null!, Qy = null!, Qz = null!;
            public Channel Vx = null!, Vy = null!, Vz = null!, V = null!, Wx = null!, Wy = null!, Wz = null!, W = null!;
            public Channel Ax = null!, Ay = null!, Az = null!, A = null!, Fc = null!, Ke = null!;
            public Vec3 PrevV, CurV, PrevW, CurW;
            public double SliceDt;
        }

        private sealed class JointChannels
        {
            public SceneJoint Joint = null!;
            public Channel Angle = null!, Speed = null!, Fx = null!, Fy = null!, Fz = null!, F = null!, T = null!, Drive = null!, Power = null!;
        }

        private readonly WorldRunner _runner;
        private readonly AnalysisSettings _settings;
        private readonly AnalysisResult _result;
        private readonly ChannelStore _store;
        private readonly List<BodyChannels> _bodies = new List<BodyChannels>();
        private readonly List<JointChannels> _joints = new List<JointChannels>();
        private readonly List<(int Node, Channel Channel)> _nodes = new List<(int, Channel)>();
        private readonly List<ContactSample> _contacts = new List<ContactSample>();
        private readonly Dictionary<SceneBody, Vec3> _contactForce = new Dictionary<SceneBody, Vec3>();
        private readonly HashSet<string> _loadBodies;
        private Channel? _ke, _pe, _et, _nc, _pc;
        private readonly Dictionary<string, Channel> _signals = new Dictionary<string, Channel>(StringComparer.Ordinal);

        public WorldProbe(WorldRunner runner, AnalysisSettings settings, AnalysisResult result, IEnumerable<string> loadBodies)
        {
            _runner = runner;
            _settings = settings;
            _result = result;
            _store = result.Channels;
            _loadBodies = new HashSet<string>(loadBodies, StringComparer.OrdinalIgnoreCase);
            Discover();
            _runner.Sliced += OnSlice;
        }

        public void Detach()
        {
            _runner.Sliced -= OnSlice;
            foreach (var (board, handler, irq) in _espTaps)
            {
                board.PinChanged -= handler;
                board.InterruptHandled -= irq;
            }
        }

        private string NameOf(SceneBody b)
        {
            if (b.Tag is WorldObject o) return string.IsNullOrWhiteSpace(o.Name) ? o.Id : o.Name;
            int colon = b.Id.IndexOf(':');
            if (colon > 0 && _runner.World.Find(b.Id.Substring(0, colon)) is { } owner)
                return (string.IsNullOrWhiteSpace(owner.Name) ? owner.Id : owner.Name) + " · " + b.Id.Substring(colon + 1);
            return b.Id;
        }

        private void Discover()
        {
            var scene = _runner.Physics?.Scene;
            if (scene != null)
            {
                foreach (var b in scene.Bodies)
                {
                    if (b.IsStatic) continue;
                    if (b.Id == "obstacle") continue;
                    if (!_settings.RecordBodies && !_loadBodies.Contains(b.Id)) continue;
                    string id = b.Id, name = NameOf(b), g = "Gövdeler/" + name;
                    Channel C(string key, string label, string unit) => _store.Get("body/" + id + "/" + key, name + " · " + label, unit, g);
                    var bc = new BodyChannels
                    {
                        Body = b,
                        Px = C("px", "konum x", "m"), Py = C("py", "konum y", "m"), Pz = C("pz", "konum z", "m"),
                        Qw = C("qw", "yönelim w", ""), Qx = C("qx", "yönelim x", ""), Qy = C("qy", "yönelim y", ""), Qz = C("qz", "yönelim z", ""),
                        Vx = C("vx", "hız x", "m/s"), Vy = C("vy", "hız y", "m/s"), Vz = C("vz", "hız z", "m/s"), V = C("v", "hız |v|", "m/s"),
                        Wx = C("wx", "açısal hız x", "rad/s"), Wy = C("wy", "açısal hız y", "rad/s"), Wz = C("wz", "açısal hız z", "rad/s"), W = C("w", "açısal hız |ω|", "rad/s"),
                        Ax = C("ax", "ivme x", "m/s²"), Ay = C("ay", "ivme y", "m/s²"), Az = C("az", "ivme z", "m/s²"), A = C("a", "ivme |a|", "m/s²"),
                        Fc = C("fc", "net temas kuvveti", "N"), Ke = C("ke", "kinetik enerji", "J"),
                        CurV = b.Velocity, PrevV = b.Velocity, CurW = b.AngularVelocity, PrevW = b.AngularVelocity
                    };
                    _bodies.Add(bc);
                }

                if (_settings.RecordJoints)
                {
                    foreach (var j in scene.Joints)
                    {
                        string id = j.Id, g = "Eklemler/" + id;
                        Channel C(string key, string label, string unit) => _store.Get("joint/" + id + "/" + key, id + " · " + label, unit, g);
                        bool slider = j.Kind == JointKind.Slider;
                        _joints.Add(new JointChannels
                        {
                            Joint = j,
                            Angle = C("angle", slider ? "konum" : "açı", slider ? "m" : "rad"),
                            Speed = C("speed", "hız", slider ? "m/s" : "rad/s"),
                            Fx = C("fx", "tepki kuvveti x", "N"), Fy = C("fy", "tepki kuvveti y", "N"), Fz = C("fz", "tepki kuvveti z", "N"),
                            F = C("f", "tepki kuvveti |F|", "N"), T = C("t", "tepki momenti |M|", "N·m"),
                            Drive = C("drive", slider ? "sürücü kuvveti" : "sürücü torku", slider ? "N" : "N·m"),
                            Power = C("power", "mekanik güç", "W")
                        });
                    }
                }

                _ke = _store.Get("scene/ke", "Toplam kinetik enerji", "J", "Sahne");
                _pe = _store.Get("scene/pe", "Toplam potansiyel enerji", "J", "Sahne");
                _et = _store.Get("scene/e", "Toplam mekanik enerji", "J", "Sahne");
                _nc = _store.Get("scene/contacts", "Temas sayısı", "", "Sahne");
                _pc = _store.Get("scene/peak_contact", "En büyük temas kuvveti", "N", "Sahne");
            }

            if (_settings.RecordNodes && _runner.Compiled != null)
            {
                var names = _runner.Compiled.Circuit.NodeNames;
                int count = 0;
                for (int n = 1; n < names.Count && count < 600; n++)
                {
                    string nm = names[n];
                    if (string.IsNullOrEmpty(nm) || nm.IndexOf('#') >= 0) continue;
                    _nodes.Add((n, _store.Get("node/" + nm, nm, "V", "Devre/Düğüm gerilimleri")));
                    count++;
                }
            }

            if (_runner.Compiled != null)
            {
                foreach (var d in _runner.Compiled.Circuit.Devices)
                {
                    var part = _runner.Compiled.PartOf(d);
                    if (part != null) _result.DeviceParts[d.Id] = part;
                }
            }
        }

        private void OnSlice(double dt)
        {
            foreach (var b in _bodies)
            {
                b.PrevV = b.CurV;
                b.PrevW = b.CurW;
                b.CurV = b.Body.Velocity;
                b.CurW = b.Body.AngularVelocity;
                b.SliceDt = dt;
            }
        }

        private static string UnitOf(string key) => key switch
        {
            "V" or "VOUT" or "VIN" or "VC" or "VBAT" => "V",
            "I" or "IOUT" or "IIN" or "IC" or "IB" => "A",
            "P" or "PLOSS" => "W",
            "T" or "TC" or "TJ" => "°C",
            "RPM" => "dev/dk",
            "W" or "OMEGA" => "rad/s",
            "TORQUE" or "TAU" => "N·m",
            "SOC" => "",
            "E" or "ENERGY" => "J",
            "F" or "FREQ" => "Hz",
            _ => string.Empty
        };

        private static string LabelOf(string key) => key switch
        {
            "V" => "gerilim",
            "I" => "akım",
            "P" => "güç kaybı",
            "T" => "sıcaklık",
            "RPM" => "devir",
            "SOC" => "şarj durumu",
            _ => key
        };

        /// <summary>Records one sample at the runner's current time.</summary>
        public void Sample()
        {
            double t = _runner.Time;
            _store.BeginSample(t);
            var scene = _runner.Physics?.Scene;
            if (scene != null)
            {
                _contacts.Clear();
                _contactForce.Clear();
                scene.CopyContacts(_contacts);
                foreach (var c in _contacts)
                {
                    _contactForce[c.A] = (_contactForce.TryGetValue(c.A, out var fa) ? fa : Vec3.Zero) + c.ForceOnA;
                    if (c.B != null) _contactForce[c.B] = (_contactForce.TryGetValue(c.B, out var fb) ? fb : Vec3.Zero) - c.ForceOnA;
                }

                foreach (var b in _bodies)
                {
                    var body = b.Body;
                    var p = body.Position;
                    var q = body.Orientation;
                    var v = body.Velocity;
                    var w = body.AngularVelocity;
                    var a = b.SliceDt > 0 ? (b.CurV - b.PrevV) / b.SliceDt : Vec3.Zero;
                    _store.Set(b.Px, p.X);
                    _store.Set(b.Py, p.Y);
                    _store.Set(b.Pz, p.Z);
                    _store.Set(b.Qw, q.W);
                    _store.Set(b.Qx, q.X);
                    _store.Set(b.Qy, q.Y);
                    _store.Set(b.Qz, q.Z);
                    _store.Set(b.Vx, v.X);
                    _store.Set(b.Vy, v.Y);
                    _store.Set(b.Vz, v.Z);
                    _store.Set(b.V, v.Length);
                    _store.Set(b.Wx, w.X);
                    _store.Set(b.Wy, w.Y);
                    _store.Set(b.Wz, w.Z);
                    _store.Set(b.W, w.Length);
                    _store.Set(b.Ax, a.X);
                    _store.Set(b.Ay, a.Y);
                    _store.Set(b.Az, a.Z);
                    _store.Set(b.A, a.Length);
                    _store.Set(b.Fc, _contactForce.TryGetValue(body, out var fc) ? fc.Length : 0);
                    _store.Set(b.Ke, body.KineticEnergy);
                }

                foreach (var j in _joints)
                {
                    var jt = j.Joint;
                    _store.Set(j.Angle, jt.Angle);
                    _store.Set(j.Speed, jt.Speed);
                    var f = jt.ReactionForce;
                    _store.Set(j.Fx, f.X);
                    _store.Set(j.Fy, f.Y);
                    _store.Set(j.Fz, f.Z);
                    _store.Set(j.F, f.Length);
                    _store.Set(j.T, jt.ReactionTorque.Length);
                    double drive = jt.DriveTorque + jt.MotorTorque;
                    _store.Set(j.Drive, drive);
                    _store.Set(j.Power, drive * jt.Speed);
                }

                double ke = scene.KineticEnergy(), pe = scene.PotentialEnergy();
                _store.Set(_ke!, ke);
                _store.Set(_pe!, pe);
                _store.Set(_et!, ke + pe);
                _store.Set(_nc!, _contacts.Count);
                _store.Set(_pc!, scene.PeakContactForce);
                RecordLoads(scene, t);
            }

            var sim = _runner.Simulator;
            if (sim != null)
            {
                if (_settings.RecordCircuit)
                {
                    foreach (var kv in sim.Signals)
                    {
                        if (!_signals.TryGetValue(kv.Key, out var ch))
                        {
                            int colon = kv.Key.LastIndexOf(':');
                            string dev = colon > 0 ? kv.Key.Substring(0, colon) : kv.Key;
                            string key = colon > 0 ? kv.Key.Substring(colon + 1) : string.Empty;
                            string obj = dev.IndexOf('/') > 0 ? dev.Substring(0, dev.IndexOf('/')) : dev;
                            string objName = _runner.World.Find(obj)?.Name ?? obj;
                            string label = dev + " · " + LabelOf(key.ToUpperInvariant());
                            ch = _store.Get("dev/" + kv.Key, label, UnitOf(key.ToUpperInvariant()), "Devre/" + objName);
                            while (ch.Count < _store.Count) ch.Append(double.NaN);
                            _signals[kv.Key] = ch;
                        }

                        _store.Set(ch, kv.Value);
                    }
                }

                foreach (var (node, ch) in _nodes) _store.Set(ch, sim.NodeVoltage(node));
            }

            foreach (var kv in _runner.ReadSerial(null, true))
            {
                if (kv.Value.Length > 0) _result.Serial.Add(new SerialLine { Time = t, Board = kv.Key, Text = kv.Value });
            }

            TapFirmware();   // [unified-timeline]
            foreach (var e in _runner.DrainEvents())
            {
                if (_result.Events.Count < 20000) _result.Events.Add(new EventLine { Time = e.Time, Source = e.Source, Kind = e.Kind.ToString().ToLowerInvariant(), Message = e.Message });
            }
        }

        // ---------------------------------------------------------------- [unified-timeline] the boards' pins and interrupts

        private sealed class FirmwareTap
        {
            public FirmwareTap(BoardInfo board, Firmware.FirmwareCoSimulation avr, Firmware.McuTrace trace, string name)
            {
                Board = board;
                Avr = avr;
                Trace = trace;
                Name = name;
            }

            public BoardInfo Board { get; }
            public Firmware.FirmwareCoSimulation Avr { get; }
            public Firmware.McuTrace Trace { get; }
            public string Name { get; }
            public long Done;
            public readonly LogicTrack?[] Pins = new LogicTrack?[Firmware.McuTrace.Pins];
            public readonly bool[] Initial = new bool[Firmware.McuTrace.Pins];
            public double Start;
            public readonly Stack<(int Vector, double Start)> Open = new Stack<(int, double)>();
        }

        private readonly List<FirmwareTap> _taps = new List<FirmwareTap>();
        private readonly List<(Firmware.Esp32.IEsp32Board Board, Action<double, int, bool> Handler, Action<double, double, string, int> Irq)> _espTaps =
            new List<(Firmware.Esp32.IEsp32Board, Action<double, int, bool>, Action<double, double, string, int>)>();
        private const int MaxEdgesPerPin = 200_000, MaxInterrupts = 400_000;

        /// <summary>
        /// Every running ATmega328P board's chip records its pin changes and interrupts (<see cref="Firmware.McuTrace"/>);
        /// what it recorded since the last sample becomes the run's logic tracks and interrupt spans, timed on the board's
        /// own clock as the run sees it. A board's trace starts the first time it is seen running.
        /// </summary>
        private void TapFirmware()
        {
            foreach (var b in _runner.Boards)
            {
                if (b.RefDes.Length > 0 && !_result.BoardNames.ContainsKey(b.RefDes)) _result.BoardNames[b.RefDes] = string.IsNullOrWhiteSpace(b.Name) ? b.RefDes : b.Name;
                if (b.Esp32 is { } esp && !_espTaps.Any(x => ReferenceEquals(x.Board, esp))) TapEsp32(esp, string.IsNullOrWhiteSpace(b.Name) ? b.RefDes : b.Name);
                if (b.Avr == null || !b.Running || _taps.Any(x => ReferenceEquals(x.Board, b))) continue;
                var trace = b.Avr.Mcu.StartTrace(65536);
                var tap = new FirmwareTap(b, b.Avr, trace, string.IsNullOrWhiteSpace(b.Name) ? b.RefDes : b.Name) { Done = trace.Total, Start = b.Avr.ProgramTime };
                for (int p = 0; p < Firmware.McuTrace.Pins; p++) tap.Initial[p] = trace.LevelAtStart(p);
                _taps.Add(tap);
            }

            foreach (var tap in _taps)
            {
                var trace = tap.Trace;
                var mcu = tap.Avr.Mcu;
                double now = tap.Avr.ProgramTime, hz = mcu.ClockHz;
                long first = trace.Total - trace.Count;
                long from = Math.Max(tap.Done, first);
                for (long n = from; n < trace.Total; n++)
                {
                    var e = trace[(int)(n - first)];
                    double t = now - ((double)mcu.Cycles - e.Cycle) / hz;
                    switch (e.Kind)
                    {
                        case Firmware.TraceKind.Pin:
                        {
                            var track = tap.Pins[e.Index];
                            if (track == null)
                            {
                                track = new LogicTrack { Board = tap.Name, Pin = e.Index < 14 ? "D" + e.Index : "A" + (e.Index - 14), Start = tap.Start, Initial = tap.Initial[e.Index] };
                                tap.Pins[e.Index] = track;
                                _result.Logic.Add(track);
                            }

                            if (track.Edges.Count < MaxEdgesPerPin) track.Edges.Add(t);
                            else track.Truncated = true;
                            break;
                        }

                        case Firmware.TraceKind.Enter:
                            tap.Open.Push((e.Index, t));
                            break;
                        case Firmware.TraceKind.Return when tap.Open.Count > 0:
                        {
                            var (v, start) = tap.Open.Pop();
                            if (_result.Interrupts.Count < MaxInterrupts)
                                _result.Interrupts.Add(new InterruptSpan { Board = tap.Name, Vector = Firmware.AvrRegisters.VectorName(v), Start = start, End = t });
                            break;
                        }
                    }
                }

                tap.Done = trace.Total;
            }
        }

        /// <summary>
        /// An ESP32 board (either mode) reports each pad the firmware drives as it changes; each report is an edge, so the
        /// pad's level before its first report was the opposite. In the hardware-compatible mode it reports each interrupt
        /// handler too (entry to return, on either core: the APP core's marked), as the ATmega328P's chip does.
        /// </summary>
        private void TapEsp32(Firmware.Esp32.IEsp32Board esp, string name)
        {
            var pins = new Dictionary<int, LogicTrack>();
            void Handler(double t, int gpio, bool level)
            {
                if (!pins.TryGetValue(gpio, out var track))
                {
                    track = new LogicTrack { Board = name, Pin = "GPIO" + gpio, Start = 0, Initial = !level };
                    pins[gpio] = track;
                    _result.Logic.Add(track);
                }

                if (track.Edges.Count < MaxEdgesPerPin) track.Edges.Add(t);
                else track.Truncated = true;
            }

            void Irq(double start, double end, string what, int core)
            {
                if (_result.Interrupts.Count < MaxInterrupts)
                    _result.Interrupts.Add(new InterruptSpan { Board = name, Vector = core == 1 ? what + " (APP)" : what, Start = start, End = end });
            }

            esp.PinChanged += Handler;
            esp.InterruptHandled += Irq;
            _espTaps.Add((esp, Handler, Irq));
        }

        /// <summary>Contacts and joint reactions on each stress part, in its body frame.</summary>
        private void RecordLoads(PhysicsScene scene, double t)
        {
            if (_loadBodies.Count == 0) return;
            foreach (string id in _loadBodies)
            {
                var body = scene.FindBody(id);
                if (body == null) continue;
                var q = body.Orientation;
                var x = body.Position;
                Vec3 Local(Vec3 p) => q.InverseRotate(p - x);
                Vec3 Dir(Vec3 f) => q.InverseRotate(f);
                var ls = new LoadSample
                {
                    Time = t,
                    Gravity = Dir(scene.Gravity),
                    Omega = Dir(body.AngularVelocity),
                    Acceleration = Dir(_bodies.FirstOrDefault(b => b.Body == body) is { } bc && bc.SliceDt > 0 ? (bc.CurV - bc.PrevV) / bc.SliceDt : Vec3.Zero)
                };
                foreach (var c in _contacts)
                {
                    if (c.A == body) ls.Loads.Add((Local(c.Point), Dir(c.ForceOnA), LoadSample.KindContact));
                    else if (c.B == body) ls.Loads.Add((Local(c.Point), Dir(-c.ForceOnA), LoadSample.KindContact));
                }

                foreach (var j in scene.Joints)
                {
                    if (!j.Enabled || j.Broken) continue;
                    double sign;
                    if (ReferenceEquals(j.ChildBody, body)) sign = 1;
                    else if (ReferenceEquals(j.ParentBody, body)) sign = -1;
                    else continue;
                    var anchor = j.WorldAnchor;
                    var f = j.ReactionForce * sign;
                    var torque = j.ReactionTorque * sign;
                    if (j.Kind == JointKind.Hinge) torque += j.WorldAxis * (j.MotorTorque * sign);
                    ls.Loads.Add((Local(anchor), Dir(f), LoadSample.KindJoint));
                    // A couple as two opposite forces ±T×n/(2a) a lever 2a apart (a = 5 mm) about the anchor.
                    double tm = torque.Length;
                    if (tm > 1e-9)
                    {
                        var n = torque / tm;
                        var perp = n.AnyPerpendicular().Normalized();
                        const double arm = 0.005;
                        var force = n.Cross(perp) * (tm / (2 * arm));
                        ls.Loads.Add((Local(anchor + perp * arm), Dir(force), LoadSample.KindJointTorque));
                        ls.Loads.Add((Local(anchor - perp * arm), Dir(-force), LoadSample.KindJointTorque));
                    }
                }

                if (!_result.Loads.TryGetValue(id, out var list)) _result.Loads[id] = list = new List<LoadSample>();
                list.Add(ls);
            }
        }
    }
}
