using System;

namespace RobotTwin.CoreSim.Analog.Devices
{
    /// <summary>
    /// [stepper] A two-phase hybrid stepper motor (NEMA 17 and the like): two windings (R, L), a rotor of
    /// <see cref="RotorTeeth"/> teeth (50: 200 full steps a turn, 1.8°). Each winding's back-EMF and torque follow the rotor's
    /// electrical angle Nθ: e_A = −K·ω·sin Nθ, e_B = K·ω·cos Nθ, T = K·(−i_A·sin Nθ + i_B·cos Nθ), so the power that goes in
    /// as e·i comes out as T·ω. The magnets' detent torque pulls it to the nearest of 4N positions with no current. It loses
    /// steps when its load or the step rate asks more than the current can give, as a real one does.
    /// </summary>
    public sealed class StepperMotor : AnalogDevice
    {
        private double _ia, _ib, _w, _wDot, _va, _vb;
        private double _reqA, _veqA, _reqB, _veqB, _alpha, _wHist, _friction, _sin, _cos, _detent;

        public StepperMotor(string id, int a1, int a2, int b1, int b2) : base(id)
        {
            A1 = a1;
            A2 = a2;
            B1 = b1;
            B2 = b2;
        }

        public int A1 { get; }
        public int A2 { get; }
        public int B1 { get; }
        public int B2 { get; }

        /// <summary>Winding resistance per phase (Ω, at 25 °C) and inductance (H).</summary>
        public double Resistance { get; set; } = 1.5;

        public double Inductance { get; set; } = 2.8e-3;

        /// <summary>Torque constant per phase (N·m/A) = back-EMF constant (V·s/rad).</summary>
        public double TorqueConstant { get; set; } = 0.166;

        /// <summary>Rotor teeth: 50 for 200 steps a turn.</summary>
        public int RotorTeeth { get; set; } = 50;

        /// <summary>Peak detent (cogging) torque with no current (N·m).</summary>
        public double DetentTorque { get; set; } = 0.02;

        /// <summary>Rotor inertia (kg·m²).</summary>
        public double Inertia { get; set; } = 5.4e-6;

        public double ViscousFriction { get; set; } = 2e-5;

        public double CoulombFriction { get; set; } = 0.005;

        /// <summary>Torque the mechanism puts on the shaft, opposing positive rotation when positive (N·m).</summary>
        public double LoadTorque { get; set; }

        /// <summary>Inertia the shaft drives (kg·m²).</summary>
        public double LoadInertia { get; set; }

        public double MaxWindingTempC { get; set; } = 130.0;

        public ThermalState Thermal { get; } = new ThermalState(8.0, 120.0);

        public double CopperTempCo { get; set; } = 0.00393;

        public double HotResistance => Resistance * Math.Max(0.5, 1.0 + CopperTempCo * (Thermal.TemperatureC - 25.0));

        /// <summary>Shaft angle (rad) and speed (rad/s).</summary>
        public double Angle { get; private set; }

        public double AngularVelocity => _w;

        public double Rpm => _w * 60.0 / (2.0 * Math.PI);

        public double CurrentA => _ia;

        public double CurrentB => _ib;

        /// <summary>The electromagnetic torque on the rotor right now (N·m).</summary>
        public double Torque { get; private set; }

        /// <summary>Full steps the shaft has turned from where it started (1.8° each for 50 teeth).</summary>
        public double FullSteps => Angle * 2 * RotorTeeth / Math.PI;

        /// <summary>
        /// [stepper] In a world the rotor is a physics body: its angle and speed come from there (<see cref="Drive"/>), the
        /// torque this model computes goes to it, and the shaft's inertia, friction and load are the physics' own.
        /// </summary>
        public bool MechanicallyDriven { get; set; }

        /// <summary>The physics rotor's angle (rad) and speed (rad/s) for the next stretch of circuit time.</summary>
        public void Drive(double angle, double speed)
        {
            Angle = angle;
            _w = speed;
            _wDot = 0;
        }

        /// <summary>The magnets' pull at an angle with no current (N·m): toward the nearest of 4N rest positions.</summary>
        public double DetentAt(double angle) => -DetentTorque * Math.Sin(4 * RotorTeeth * angle);

        public override int BranchCount => 3;

        private double TotalInertia => Inertia + LoadInertia;

        public override void Reset()
        {
            _ia = _ib = _w = _wDot = _va = _vb = 0;
            Angle = 0;
            Torque = 0;
            Thermal.Reset(25.0);
        }

        public override void Repair()
        {
            base.Repair();
            Thermal.Reset(25.0);
        }

        public override void BeginStep(StepInfo step)
        {
            // The rotor's electrical angle at the middle of the step, predicted from its speed.
            double theta = Angle + (step.IsDc ? 0 : 0.5 * _w * step.Dt);
            double e = RotorTeeth * theta;
            _sin = Math.Sin(e);
            _cos = Math.Cos(e);
            _detent = -DetentTorque * Math.Sin(4 * e);
            _friction = CoulombFriction * Math.Tanh(_w / 0.05) + LoadTorque;
            if (step.IsDc)
            {
                _reqA = _reqB = _veqA = _veqB = _alpha = _wHist = 0;
            }
            else if (step.Trapezoidal)
            {
                _reqA = _reqB = 2.0 * Inductance / step.Dt;
                _veqA = -_reqA * _ia - _va;
                _veqB = -_reqB * _ib - _vb;
                _alpha = 2.0 / step.Dt;
                _wHist = TotalInertia * (_alpha * _w + _wDot);
            }
            else
            {
                _reqA = _reqB = Inductance / step.Dt;
                _veqA = -_reqA * _ia;
                _veqB = -_reqB * _ib;
                _alpha = 1.0 / step.Dt;
                _wHist = TotalInertia * _alpha * _w;
            }
        }

        public override void Stamp(StampContext ctx)
        {
            int ka = BranchBase, kb = BranchBase + 1, kw = BranchBase + 2;
            double k = TorqueConstant, r = HotResistance;
            ctx.AddBranchCurrent(A1, A2, ka);
            ctx.AddBranchCurrent(B1, B2, kb);
            if (Failure == FailureMode.Open)
            {
                ctx.AddMatrix(ka, ka, 1.0);
                ctx.AddMatrix(kb, kb, 1.0);
            }
            else
            {
                // V(A1) − V(A2) − (R + Req)·i_A − e_A = Veq, e_A = −K·sin(Nθ)·ω
                ctx.AddRowNode(ka, A1, 1.0);
                ctx.AddRowNode(ka, A2, -1.0);
                ctx.AddMatrix(ka, ka, -(r + _reqA));
                ctx.AddMatrix(ka, kw, k * _sin);
                ctx.AddRhs(ka, _veqA);
                // e_B = K·cos(Nθ)·ω
                ctx.AddRowNode(kb, B1, 1.0);
                ctx.AddRowNode(kb, B2, -1.0);
                ctx.AddMatrix(kb, kb, -(r + _reqB));
                ctx.AddMatrix(kb, kw, -k * _cos);
                ctx.AddRhs(kb, _veqB);
            }

            if (MechanicallyDriven)
            {
                // The physics turns the shaft: ω is what it says.
                ctx.AddMatrix(kw, kw, 1.0);
                ctx.AddRhs(kw, ctx.IsDc ? 0.0 : _w);
                return;
            }

            // (J·α + b)·ω − K·(−sin·i_A + cos·i_B) = J·history − friction − load + detent
            ctx.AddMatrix(kw, kw, TotalInertia * _alpha + ViscousFriction + (ctx.IsDc ? 1.0 : 0.0));
            ctx.AddMatrix(kw, ka, k * _sin);
            ctx.AddMatrix(kw, kb, -k * _cos);
            ctx.AddRhs(kw, ctx.IsDc ? 0.0 : _wHist - _friction + _detent);
        }

        public override void Accept(AcceptContext ctx)
        {
            double ia = ctx.Unknown(BranchBase), ib = ctx.Unknown(BranchBase + 1), w = ctx.Unknown(BranchBase + 2);
            double k = TorqueConstant, r = HotResistance;
            double torque = k * (-ia * _sin + ib * _cos);
            if (ctx.IsDc)
            {
                w = 0;
                _wDot = 0;
                _va = _vb = 0;
            }
            else
            {
                Angle += 0.5 * (w + _w) * ctx.Dt;
                _wDot = MechanicallyDriven ? 0 : TotalInertia > 0 ? (torque - ViscousFriction * w - _friction + _detent) / TotalInertia : 0;
                // The inductances' voltages, for the trapezoidal history.
                _va = ctx.V(A1, A2) - r * ia + k * _sin * w;
                _vb = ctx.V(B1, B2) - r * ib - k * _cos * w;
            }

            _ia = ia;
            _ib = ib;
            _w = w;
            Torque = torque;
            if (ctx.IsDc) return;
            Thermal.Advance((ia * ia + ib * ib) * r + Math.Abs(_friction * w), ctx.Dt, ctx.AmbientTempC);
            if (ctx.EnableDamage && !IsFailed && Thermal.TemperatureC > MaxWindingTempC)
                Fail(FailureMode.Open, $"sargı {Thermal.TemperatureC:F0} °C'de yandı ({Math.Max(Math.Abs(ia), Math.Abs(ib)):F2} A)", ctx);
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "IA", _ia);
            sink.Write(Id, "IB", _ib);
            sink.Write(Id, "RPM", Rpm);
            sink.Write(Id, "ANGLE", Angle);
            sink.Write(Id, "STEPS", FullSteps);
            sink.Write(Id, "TORQUE", Torque);
            sink.Write(Id, "T", Thermal.TemperatureC);
        }
    }

    /// <summary>
    /// [stepper] An A4988 stepper driver (Pololu-style carrier): its translator counts rising edges on STEP (in the
    /// direction DIR gives) at the resolution MS1–MS3 set (full to 1/16), and drives each phase's current to the sine and
    /// cosine of that position times the current limit, through an H-bridge fed from VMOT. The current regulation is the
    /// chopper's average: it holds the current while the supply can, and runs out of voltage when the winding's back-EMF
    /// and inductance ask more, so the torque falls with speed as it does on the bench. EN high, SLEEP or RESET low, VMOT
    /// under 8 V or a hot chip (165 °C) turn the bridges off.
    /// </summary>
    public sealed class A4988Driver : AnalogDevice
    {
        private readonly int[] _out1, _out2;
        private readonly double[] _i = new double[2], _u = new double[2], _ff = new double[2];
        private readonly int[] _mode = new int[2];   // 0 regulating, ±1 bridge full on
        private bool _stepHigh, _hot;
        private double _vmot;

        public A4988Driver(string id, int vmot, int gnd, int out1A, int out1B, int out2A, int out2B, int vdd, int step, int dir, int enable, int ms1, int ms2, int ms3, int sleep, int reset) : base(id)
        {
            Vmot = vmot;
            Gnd = gnd;
            _out1 = new[] { out1A, out1B };
            _out2 = new[] { out2A, out2B };
            Vdd = vdd;
            Step = step;
            Dir = dir;
            Enable = enable;
            Ms1 = ms1;
            Ms2 = ms2;
            Ms3 = ms3;
            Sleep = sleep;
            ResetPin = reset;
        }

        public int Vmot { get; }
        public int Gnd { get; }
        public int Vdd { get; }
        public int Step { get; }
        public int Dir { get; }
        public int Enable { get; }
        public int Ms1 { get; }
        public int Ms2 { get; }
        public int Ms3 { get; }
        public int Sleep { get; }
        public int ResetPin { get; }

        /// <summary>The current limit (A, the peak of each phase's sine): Vref / (8·Rsense) on the carrier.</summary>
        public double CurrentLimit { get; set; } = 1.0;

        /// <summary>The bridge's on-resistance, high side plus low side (Ω).</summary>
        public double OnResistance { get; set; } = 0.55;

        /// <summary>The averaged regulator's gain (V/A) and its integral time (s): about a chopper's settling.</summary>
        public double Gain { get; set; } = 100.0;

        public double IntegralTime { get; set; } = 2e-3;

        public double ThermalResistance { get; set; } = 40.0;

        public ThermalState Thermal { get; } = new ThermalState(40.0, 0.4);

        /// <summary>The translator's position in 1/16 steps (it moves by 16/m per STEP edge at 1/m stepping).</summary>
        public int Position { get; private set; }

        public int Microsteps { get; private set; } = 1;

        public bool Enabled { get; private set; }

        public int Steps { get; private set; }

        public double Phase1Current => _i[0];

        public double Phase2Current => _i[1];

        public override int BranchCount => 2;

        /// <summary>The phase currents the translator asks for: the cosine and sine of its electrical angle (45° at home).</summary>
        public (double I1, double I2) Targets()
        {
            if (!Enabled) return (0, 0);
            double angle = Math.PI / 4 + Position * (Math.PI / 2) / 16.0;
            return (CurrentLimit * Math.Cos(angle), CurrentLimit * Math.Sin(angle));
        }

        public override void Reset()
        {
            Array.Clear(_i, 0, 2);
            Array.Clear(_u, 0, 2);
            Array.Clear(_ff, 0, 2);
            Array.Clear(_mode, 0, 2);
            Position = 0;
            Steps = 0;
            _stepHigh = _hot = false;
            Thermal.Reset(25.0);
        }

        public override void Repair()
        {
            base.Repair();
            Thermal.Reset(25.0);
        }

        public override void BeginStep(StepInfo step)
        {
            var (t1, t2) = Targets();
            double[] target = { t1, t2 };
            for (int p = 0; p < 2; p++)
            {
                if (!Enabled)
                {
                    _mode[p] = 0;
                    continue;
                }

                // Saturate when holding the current would take more than the supply; come back only well inside it, so
                // the bridge does not flip between the two every step at the edge.
                double want = Gain * (target[p] - _i[p]) + _ff[p];
                double avail = Math.Max(0, _vmot - OnResistance * Math.Abs(_i[p]));
                int mode = _mode[p] == 0 ? (Math.Abs(want) > avail ? Math.Sign(want) : 0) : (Math.Abs(want) < 0.9 * avail || Math.Sign(want) != _mode[p] ? 0 : _mode[p]);
                if (mode != _mode[p]) PendingDiscontinuity = true;
                _mode[p] = mode;
            }
        }

        public override void Stamp(StampContext ctx)
        {
            var (t1, t2) = Targets();
            double[] target = { t1, t2 };
            for (int p = 0; p < 2; p++)
            {
                int k = BranchBase + p, a = p == 0 ? _out1[0] : _out2[0], b = p == 0 ? _out1[1] : _out2[1];
                // The phase current leaves the bridge at xA, goes through the winding and comes back at xB.
                ctx.AddBranchCurrent(b, a, k);
                if (!Enabled || Failure == FailureMode.Open)
                {
                    ctx.AddMatrix(k, k, 1.0);   // bridges off: no current
                    continue;
                }

                ctx.AddRowNode(k, a, 1.0);
                ctx.AddRowNode(k, b, -1.0);
                if (_mode[p] == 0)
                {
                    // V(xA) − V(xB) = G·(i* − i) + u_ff − R_on·i; the supply delivers that power.
                    ctx.AddMatrix(k, k, Gain + OnResistance);
                    ctx.AddRhs(k, Gain * target[p] + _ff[p]);
                    if (_vmot > 1) ctx.AddCurrent(Vmot, Gnd, _u[p] * _i[p] / _vmot);
                }
                else
                {
                    // Full on: V(xA) − V(xB) = ±VMOT − R_on·i, the current drawn from VMOT.
                    double d = _mode[p];
                    ctx.AddRowNode(k, Vmot, -d);
                    ctx.AddRowNode(k, Gnd, d);
                    ctx.AddMatrix(k, k, OnResistance);
                    if (Vmot > 0) ctx.AddMatrix(Vmot - 1, k, d);
                    if (Gnd > 0) ctx.AddMatrix(Gnd - 1, k, -d);
                }
            }

            // Logic: 8 mA from VDD, pull-downs on MS1–3 and EN, pull-ups on SLEEP and RESET (the carrier's).
            ctx.AddConductance(Vdd, Gnd, 8e-3 / 5.0);
            foreach (int pin in new[] { Ms1, Ms2, Ms3, Enable }) ctx.AddConductance(pin, Gnd, 1e-5);
            foreach (int pin in new[] { Sleep, ResetPin }) ctx.AddConductance(pin, Vdd, 1e-4);
            ctx.AddConductance(Vmot, Gnd, 1e-4);   // the carrier's quiescent draw from VMOT
        }

        public override void Accept(AcceptContext ctx)
        {
            double vdd = ctx.V(Vdd, Gnd);
            _vmot = ctx.V(Vmot, Gnd);
            bool High(int pin, bool was) => ctx.V(pin, Gnd) > (was ? 0.3 : 0.7) * Math.Max(vdd, 1e-3);
            var (t1, t2) = Targets();
            double[] target = { t1, t2 };
            for (int p = 0; p < 2; p++)
            {
                _i[p] = ctx.Unknown(BranchBase + p);
                int a = p == 0 ? _out1[0] : _out2[0], b = p == 0 ? _out1[1] : _out2[1];
                _u[p] = ctx.V(a, b);
                if (!ctx.IsDc && _mode[p] == 0 && Enabled)
                    _ff[p] = Math.Max(-_vmot, Math.Min(_vmot, _ff[p] + Gain * (target[p] - _i[p]) * ctx.Dt / IntegralTime));
                if (!Enabled) _ff[p] = 0;
            }

            bool powered = vdd > 3.0 && _vmot > 8.0;
            bool on = powered && !High(Enable, false) && High(Sleep, true) && High(ResetPin, true) && !_hot && !IsFailed;
            if (powered && !High(ResetPin, true)) Position = 0;   // RESET low: the translator goes home
            bool m1 = High(Ms1, false), m2 = High(Ms2, false), m3 = High(Ms3, false);
            Microsteps = (m1, m2, m3) switch
            {
                (false, false, false) => 1,
                (true, false, false) => 2,
                (false, true, false) => 4,
                (true, true, false) => 8,
                _ => 16
            };
            bool step = High(Step, _stepHigh);
            if (on && step && !_stepHigh && !ctx.IsDc)
            {
                int delta = 16 / Microsteps;
                // At coarser steps the translator first snaps onto that grid, as the chip's table does.
                if (Position % delta != 0) Position -= ((Position % delta) + delta) % delta;
                Position += High(Dir, false) ? delta : -delta;
                Steps += High(Dir, false) ? 1 : -1;
                // The regulator's integral follows the new targets (a chopper starts each position afresh, no overshoot).
                var (n1, n2) = Targets();
                double[] next = { n1, n2 };
                for (int p = 0; p < 2; p++) _ff[p] = Math.Abs(target[p]) > 1e-6 ? _ff[p] * next[p] / target[p] : 0;
            }

            _stepHigh = step;
            if (on != Enabled) ctx.RequestDiscontinuity();
            Enabled = on;
            if (ctx.IsDc) return;
            double loss = (_i[0] * _i[0] + _i[1] * _i[1]) * OnResistance + 0.008 * 5.0;
            Thermal.ThermalResistance = ThermalResistance;
            Thermal.Advance(loss, ctx.Dt, ctx.AmbientTempC);
            // Thermal shutdown at 165 °C, back on 15 °C cooler.
            if (!_hot && Thermal.TemperatureC > 165) _hot = true;
            else if (_hot && Thermal.TemperatureC < 150) _hot = false;
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "I1", _i[0]);
            sink.Write(Id, "I2", _i[1]);
            sink.Write(Id, "POS", Position);
            sink.Write(Id, "STEPS", Steps);
            sink.Write(Id, "EN", Enabled ? 1 : 0);
            sink.Write(Id, "T", Thermal.TemperatureC);
        }
    }
}
