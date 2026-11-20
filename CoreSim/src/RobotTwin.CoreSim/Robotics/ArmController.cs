using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Robotics
{
    public enum ArmState
    {
        /// <summary>Holding position, ready for jog or program start.</summary>
        Idle,

        /// <summary>Executing a manual move (jog, MoveJ/MoveL from a script).</summary>
        Moving,

        /// <summary>Running the program.</summary>
        Running,

        /// <summary>Program paused (resumable).</summary>
        Paused,

        /// <summary>Stopped by the safety circuit (light curtain, safety input); resumes after reset.</summary>
        SafetyStop,

        /// <summary>Error: following error, unreachable target, program error. Needs a reset.</summary>
        Fault
    }

    /// <summary>
    /// Robot controller: interpolates joint and linear moves with trapezoidal or S-curve
    /// profiles, runs the taught program (moves, gripper, waits, digital I/O, palletising),
    /// handles jogging, speed override, pause/step/loop, the safety stop (controlled
    /// deceleration, category 2) and following-error supervision. It produces the joint
    /// reference that the servo drives (the physics joints) track.
    /// </summary>
    public sealed class ArmController
    {
        private readonly double[] _q, _qd, _measured, _tmpQ, _tmpQd;
        private ArmMotion? _motion;
        private double _motionTime;
        private double _rate;           // time-scaling actually applied (ramps for smooth stops)
        private double _waitUntil = double.NaN;
        private double _faultTimer;
        private ArmState _resumeState = ArmState.Idle;
        private bool _stepOnce;
        private readonly List<(double Time, string Text, bool Fault)> _messages = new List<(double, string, bool)>();

        public ArmController(ArmModel model, IReadOnlyList<double>? start = null)
        {
            Model = model;
            int n = model.Dof;
            _q = new double[n];
            _qd = new double[n];
            _measured = new double[n];
            _tmpQ = new double[n];
            _tmpQd = new double[n];
            var s = start ?? model.Home;
            for (int i = 0; i < n; i++) _q[i] = _measured[i] = i < s.Count ? s[i] : 0;
            JointLoad = new double[n];
        }

        public ArmModel Model { get; }

        /// <summary>Flange → TCP of the mounted tool.</summary>
        public Transform3 Tool { get; set; } = Transform3.Identity;

        public ArmState State { get; private set; } = ArmState.Idle;
        public double Time { get; private set; }

        /// <summary>Joint reference (rad/m) and its rate.</summary>
        public IReadOnlyList<double> Reference => _q;
        public IReadOnlyList<double> ReferenceRate => _qd;

        /// <summary>Last measured joint positions (from the drives' encoders).</summary>
        public IReadOnlyList<double> Measured => _measured;

        /// <summary>Drive torque used per joint as a fraction of its rating (set by the physics coupling).</summary>
        public double[] JointLoad { get; }

        public ProfileShape Shape { get; set; } = ProfileShape.SCurve;

        /// <summary>Speed override 0.01–1 (pendant slider), applied as time scaling.</summary>
        public double Override { get; set; } = 1.0;

        /// <summary>Program speed (0.01–1), set by HIZ.</summary>
        public double ProgramSpeed { get; set; } = 1.0;

        public bool Loop { get; set; } = true;
        public int CycleCount { get; private set; }

        /// <summary>Resume automatically when the safety circuit closes again (otherwise a restart is needed).</summary>
        public bool AutoResumeAfterSafety { get; set; }

        public ArmProgram? Program { get; private set; }
        public int ProgramCounter { get; private set; }
        public ArmInstruction? CurrentInstruction => Program != null && ProgramCounter < Program.Instructions.Count ? Program.Instructions[ProgramCounter] : null;

        public bool[] DigitalIn { get; } = new bool[8];
        public bool[] DigitalOut { get; } = new bool[8];

        /// <summary>Safety circuit closed (light curtains clear, safety input high).</summary>
        public bool SafetyOk { get; set; } = true;

        /// <summary>Tool command: true = close gripper / vacuum on.</summary>
        public bool ToolCommand { get; private set; }

        /// <summary>Tool feedback: part held (gripper blocked on a part, vacuum sealed).</summary>
        public Func<bool>? ToolHolding { get; set; }

        public string FaultText { get; private set; } = string.Empty;
        public string StatusText { get; private set; } = "Hazır";

        // ---- palletising
        public int PalletX { get; private set; } = 1;
        public int PalletY { get; private set; } = 1;
        public int PalletZ { get; private set; } = 1;
        public Vec3 PalletPitch { get; private set; }
        public int PalletIndex { get; set; }
        public int PalletCount => PalletX * PalletY * PalletZ;

        public Vec3 PalletOffset
        {
            get
            {
                int ix = PalletIndex % PalletX, iy = PalletIndex / PalletX % PalletY, iz = PalletIndex / (PalletX * PalletY);
                return new Vec3(ix * PalletPitch.X, iy * PalletPitch.Y, iz * PalletPitch.Z);
            }
        }

        /// <summary>Raised when the pallet pattern wraps around (pallet full).</summary>
        public event Action? PalletFull;

        /// <summary>Messages since the last drain: (controller time, text, is a fault).</summary>
        public List<(double Time, string Text, bool Fault)> DrainMessages()
        {
            var r = _messages.ToList();
            _messages.Clear();
            return r;
        }

        private void Say(string text, bool fault = false) => _messages.Add((Time, text, fault));

        /// <summary>TCP pose of the joint reference (base frame).</summary>
        public Transform3 TcpReference => ArmKinematics.Forward(Model, _q, Tool);

        /// <summary>TCP pose of the measured joints (base frame).</summary>
        public Transform3 TcpMeasured => ArmKinematics.Forward(Model, _measured, Tool);

        public bool IsMoving => _motion != null;

        // ================================================================ program control

        public void Load(ArmProgram program)
        {
            Program = program;
            ProgramCounter = 0;
            PalletIndex = 0;
            foreach (var e in program.Errors) Say("Program: " + e, false);
        }

        /// <summary>Starts (or resumes) the program from the current line.</summary>
        public bool Start()
        {
            if (State == ArmState.Fault)
            {
                Say("Arıza var: önce sıfırlayın", true);
                return false;
            }

            if (Program == null || Program.Instructions.Count == 0)
            {
                Say("Çalıştırılacak program yok");
                return false;
            }

            if (Program.Errors.Count > 0)
            {
                Fail("Programda hata var: " + Program.Errors[0]);
                return false;
            }

            if (!SafetyOk)
            {
                State = ArmState.SafetyStop;
                _resumeState = ArmState.Running;
                Say("Güvenlik devresi açık: hücre boşaltılınca yeniden başlatın", true);
                return false;
            }

            if (ProgramCounter >= Program.Instructions.Count) ProgramCounter = 0;
            State = ArmState.Running;
            _stepOnce = false;
            StatusText = "Program çalışıyor";
            return true;
        }

        /// <summary>Executes the next program line, then pauses.</summary>
        public void Step()
        {
            if (Program == null || State == ArmState.Fault || State == ArmState.SafetyStop) return;
            if (ProgramCounter >= Program.Instructions.Count) ProgramCounter = 0;
            _stepOnce = true;
            State = ArmState.Running;
        }

        public void Pause()
        {
            if (State != ArmState.Running && State != ArmState.Moving) return;
            _resumeState = State;
            State = ArmState.Paused;
            StatusText = "Duraklatıldı";
        }

        public void Resume()
        {
            if (State == ArmState.Paused || (State == ArmState.SafetyStop && SafetyOk))
            {
                State = _resumeState == ArmState.Moving && _motion == null ? ArmState.Idle : _resumeState;
                if (State == ArmState.Running && Program == null) State = ArmState.Idle;
                StatusText = State == ArmState.Running ? "Program çalışıyor" : "Hazır";
            }
        }

        /// <summary>Stops the program and any move (decelerating), back to line 1.</summary>
        public void Stop()
        {
            if (State == ArmState.Fault) return;
            State = ArmState.Idle;
            _motion = null;
            Array.Clear(_qd, 0, _qd.Length);
            _waitUntil = double.NaN;
            ProgramCounter = 0;
            StatusText = "Durduruldu";
        }

        /// <summary>Clears a fault: the reference snaps to where the arm is.</summary>
        public void Reset()
        {
            for (int i = 0; i < _q.Length; i++)
            {
                _q[i] = _measured[i];
                _qd[i] = 0;
            }

            _motion = null;
            _waitUntil = double.NaN;
            _faultTimer = 0;
            FaultText = string.Empty;
            State = ArmState.Idle;
            StatusText = "Hazır";
        }

        private void Fail(string text)
        {
            State = ArmState.Fault;
            FaultText = text;
            StatusText = "ARIZA: " + text;
            _motion = null;
            for (int i = 0; i < _q.Length; i++)
            {
                _q[i] = _measured[i];
                _qd[i] = 0;
            }

            Say(text, true);
        }

        // ================================================================ manual motion (pendant, scripts)

        private bool CanMoveManually()
        {
            if (State == ArmState.Idle || State == ArmState.Moving) return true;
            Say(State switch
            {
                ArmState.Running => "Program çalışırken elle hareket edilemez",
                ArmState.Fault => "Arıza var: önce sıfırlayın",
                ArmState.SafetyStop => "Güvenlik durdurması aktif",
                _ => "Önce programı durdurun"
            });
            return false;
        }

        /// <summary>Joint move to the given values (speed: fraction of rated speed).</summary>
        public bool MoveJ(IReadOnlyList<double> target, double speed = 0.5)
        {
            if (!CanMoveManually()) return false;
            if (!Model.WithinLimits(target, 1e-6))
            {
                Say("Hedef eklem sınırları dışında");
                return false;
            }

            Begin(new JointMotion(Model, _q, Model.Clamp(target), speed, Shape));
            State = ArmState.Moving;
            return true;
        }

        /// <summary>Linear TCP move to a base-frame pose (speed m/s).</summary>
        public bool MoveL(Transform3 target, double speed = 0.25)
        {
            if (!CanMoveManually()) return false;
            var m = LinearMotion.Plan(Model, _q, target, Tool, speed, Shape, out var error);
            if (m == null)
            {
                Say(error);
                return false;
            }

            Begin(m);
            State = ArmState.Moving;
            return true;
        }

        /// <summary>Jogs one joint by a step (rad or m) at 25 % speed.</summary>
        public bool JogJoint(int joint, double delta)
        {
            if (joint < 0 || joint >= Model.Dof) return false;
            var target = _motion?.End.ToArray() ?? _q.ToArray();
            target[joint] = Math.Max(Model.Joints[joint].Min, Math.Min(Model.Joints[joint].Max, target[joint] + delta));
            return MoveJ(target, 0.25);
        }

        /// <summary>Jogs the TCP in the base frame (m) and about the tool Z axis (rad), linear interpolation.</summary>
        public bool JogCartesian(Vec3 delta, double rotZ = 0, double speed = 0.1)
        {
            var start = _motion != null ? ArmKinematics.Forward(Model, _motion.End, Tool) : TcpReference;
            var q = rotZ != 0 ? (start.Q * Quat.FromAxisAngle(Vec3.UnitZ, rotZ)).Normalized() : start.Q;
            return MoveL(new Transform3(start.P + delta, q), speed);
        }

        public void SetTool(bool close)
        {
            ToolCommand = close;
        }

        private void Begin(ArmMotion m)
        {
            _motion = m;
            _motionTime = 0;
        }

        // ================================================================ cyclic update

        /// <summary>
        /// One control tick: reads the measured joints, advances the program and the active
        /// move, and supervises following errors. Call before the drives use <see cref="Reference"/>.
        /// </summary>
        public void Update(double dt, IReadOnlyList<double>? measured = null)
        {
            Time += dt;
            if (measured != null)
            {
                for (int i = 0; i < _measured.Length; i++) _measured[i] = measured[i];
            }
            else
            {
                Array.Copy(_q, _measured, _q.Length);
            }

            // Safety circuit: controlled stop, keep the path.
            if (!SafetyOk && (State == ArmState.Running || State == ArmState.Moving || State == ArmState.Paused))
            {
                _resumeState = State == ArmState.Paused ? _resumeState : State;
                State = ArmState.SafetyStop;
                StatusText = "GÜVENLİK DURDURMASI";
                Say("Güvenlik devresi açıldı: kol kontrollü durduruldu", true);
            }
            else if (SafetyOk && State == ArmState.SafetyStop && AutoResumeAfterSafety)
            {
                Resume();
            }

            bool advancing = State == ArmState.Running || State == ArmState.Moving;
            double targetRate = advancing ? Math.Max(0.01, Math.Min(1, Override)) : 0;
            // Smooth stop/start: the time scaling ramps in ~0.2 s (≈ a category-2 controlled stop).
            double ramp = dt / 0.2;
            _rate = _rate < targetRate ? Math.Min(targetRate, _rate + ramp) : Math.Max(targetRate, _rate - ramp * 1.5);

            if (State == ArmState.Running && _motion == null) RunProgram();

            if (_motion != null)
            {
                _motionTime += dt * _rate;
                _motion.Sample(_motionTime, _tmpQ, _tmpQd);
                for (int i = 0; i < _q.Length; i++)
                {
                    _q[i] = _tmpQ[i];
                    _qd[i] = _tmpQd[i] * _rate;
                }

                if (_motionTime >= _motion.Duration)
                {
                    Array.Copy(_motion.End, _q, _q.Length);
                    Array.Clear(_qd, 0, _qd.Length);
                    _motion = null;
                    if (State == ArmState.Moving) State = ArmState.Idle;
                    else if (State == ArmState.Running)
                    {
                        ProgramCounter++;
                        AfterInstruction();
                    }
                }
            }
            else
            {
                Array.Clear(_qd, 0, _qd.Length);
            }

            Supervise(dt);
        }

        private void Supervise(double dt)
        {
            if (State == ArmState.Fault) return;
            if (Worst(out int worst, out double err))
            {
                _faultTimer += dt;
                if (_faultTimer > 0.3 && State != ArmState.Fault)
                {
                    var j = Model.Joints[worst];
                    double load = JointLoad[worst];
                    Fail($"{j.Name} takip hatası {j.ToDisplay(err):0.#} {j.Unit}" + (load > 0.95 ? $" — tork sınırında (%{load * 100:0}), yük fazla" : " — engel veya çarpışma"));
                }
            }
            else
            {
                _faultTimer = Math.Max(0, _faultTimer - dt);
            }

            bool Worst(out int index, out double error)
            {
                index = 0;
                error = 0;
                bool bad = false;
                for (int i = 0; i < _q.Length; i++)
                {
                    double e = Math.Abs(_measured[i] - _q[i]);
                    if (e > Model.Joints[i].TrackTolerance && e / Model.Joints[i].TrackTolerance > error / Math.Max(1e-12, Model.Joints[index].TrackTolerance))
                    {
                        index = i;
                        error = e;
                        bad = true;
                    }
                }

                return bad;
            }
        }

        private void AfterInstruction()
        {
            if (_stepOnce)
            {
                _stepOnce = false;
                if (State == ArmState.Running)
                {
                    State = ArmState.Paused;
                    _resumeState = ArmState.Running;
                    StatusText = "Adım tamam";
                }
            }
        }

        private void RunProgram()
        {
            var prog = Program!;
            for (int guard = 0; guard < 32 && State == ArmState.Running && _motion == null; guard++)
            {
                if (ProgramCounter >= prog.Instructions.Count)
                {
                    if (Loop && prog.Instructions.Count > 0)
                    {
                        ProgramCounter = 0;
                        CycleCount++;
                        continue;
                    }

                    State = ArmState.Idle;
                    StatusText = "Program bitti";
                    Say("Program bitti");
                    return;
                }

                var ins = prog.Instructions[ProgramCounter];
                StatusText = $"Satır {ins.Line}: {ins.Text}";
                if (!Execute(ins)) return;   // blocked (waiting) or a move started
                ProgramCounter++;
                AfterInstruction();
            }
        }

        /// <summary>Runs one instruction; false while it blocks or after it started a move.</summary>
        private bool Execute(ArmInstruction ins)
        {
            switch (ins.Op)
            {
                case ArmOp.MoveJ:
                case ArmOp.MoveL:
                    StartMove(ins);
                    return false;
                case ArmOp.Grip:
                    ToolCommand = true;
                    return true;
                case ArmOp.Release:
                    ToolCommand = false;
                    return true;
                case ArmOp.Wait:
                    if (double.IsNaN(_waitUntil)) _waitUntil = Time + ins.Args[0];
                    if (Time < _waitUntil) return false;
                    _waitUntil = double.NaN;
                    return true;
                case ArmOp.WaitDi:
                {
                    int ch = (int)ins.Args[0] - 1;
                    bool want = ins.Args[1] != 0;
                    if (ch < 0 || ch >= DigitalIn.Length)
                    {
                        Fail($"Satır {ins.Line}: giriş {ch + 1} yok");
                        return false;
                    }

                    StatusText = $"Satır {ins.Line}: giriş {ch + 1} = {(want ? 1 : 0)} bekleniyor";
                    return DigitalIn[ch] == want;
                }

                case ArmOp.SetDo:
                {
                    int ch = (int)ins.Args[0] - 1;
                    if (ch < 0 || ch >= DigitalOut.Length)
                    {
                        Fail($"Satır {ins.Line}: çıkış {ch + 1} yok");
                        return false;
                    }

                    DigitalOut[ch] = ins.Args[1] != 0;
                    return true;
                }

                case ArmOp.WaitGrip:
                {
                    double timeout = ins.Args.Length > 0 ? ins.Args[0] : 2.0;
                    if (double.IsNaN(_waitUntil)) _waitUntil = Time + timeout;
                    if (ToolHolding?.Invoke() == true)
                    {
                        _waitUntil = double.NaN;
                        return true;
                    }

                    if (Time >= _waitUntil)
                    {
                        _waitUntil = double.NaN;
                        Fail($"Satır {ins.Line}: parça tutulamadı ({timeout:0.#} s)");
                    }

                    return false;
                }

                case ArmOp.Pallet:
                    PalletX = Math.Max(1, (int)ins.Args[0]);
                    PalletY = Math.Max(1, (int)ins.Args[1]);
                    PalletZ = Math.Max(1, (int)ins.Args[2]);
                    PalletPitch = new Vec3(ins.Args[3], ins.Args[4], ins.Args[5]) * 0.001;
                    if (PalletIndex >= PalletCount) PalletIndex = 0;
                    return true;
                case ArmOp.PalletNext:
                    PalletIndex++;
                    if (PalletIndex >= PalletCount)
                    {
                        PalletIndex = 0;
                        Say($"Palet doldu ({PalletCount} parça): palet değiştiriliyor");
                        PalletFull?.Invoke();
                    }

                    return true;
                case ArmOp.Speed:
                    ProgramSpeed = Math.Max(0.01, Math.Min(1, ins.Args[0] / 100));
                    return true;
                case ArmOp.End:
                    ProgramCounter = Program!.Instructions.Count;
                    return false;
            }

            return true;
        }

        /// <summary>Target joints of a move instruction (with offsets / pallet slot), or null with a fault.</summary>
        public double[]? ResolveTarget(ArmInstruction ins, IReadOnlyList<double> seed, out Transform3 tcp, out string error)
        {
            error = string.Empty;
            tcp = Transform3.Identity;
            if (Program == null || ins.Point == null || !Program.Points.TryGetValue(ins.Point, out var pt))
            {
                error = $"'{ins.Point}' noktası yok";
                return null;
            }

            var offset = ins.Offset + (ins.UsePallet ? PalletOffset : Vec3.Zero);
            if (pt.Joints != null && offset.LengthSquared < 1e-18)
            {
                tcp = ArmKinematics.Forward(Model, pt.Joints, Tool);
                return pt.Joints;
            }

            var pose = pt.Pose ?? ArmKinematics.Forward(Model, pt.Joints!, Tool);
            tcp = new Transform3(pose.P + offset, pose.Q);
            var ik = ArmKinematics.Inverse(Model, tcp, seed, Tool);
            if (!ik.Success)
            {
                error = ik.Message;
                return null;
            }

            return ik.Joints;
        }

        private bool StartMove(ArmInstruction ins)
        {
            var target = ResolveTarget(ins, _q, out var tcp, out var error);
            if (target == null)
            {
                Fail($"Satır {ins.Line}: {error}");
                return false;
            }

            if (ins.Op == ArmOp.MoveJ)
            {
                double frac = (ins.Speed ?? 100) / 100 * ProgramSpeed;
                Begin(new JointMotion(Model, _q, target, frac, Shape));
                return true;
            }

            double speed = (ins.Speed ?? 250) / 1000 * ProgramSpeed;
            var lin = LinearMotion.Plan(Model, _q, tcp, Tool, speed, Shape, out var err);
            if (lin == null)
            {
                Fail($"Satır {ins.Line}: {err}");
                return false;
            }

            Begin(lin);
            return true;
        }
    }
}
