using System;

namespace RobotTwin.CoreSim.Analog.Devices
{
    internal static class Smooth
    {
        /// <summary>s * ln(1 + e^(x/s)) without overflow, and its derivative.</summary>
        public static double Softplus(double x, double s, out double d)
        {
            double z = x / s;
            if (z > 30.0)
            {
                d = 1.0;
                return x;
            }

            if (z < -30.0)
            {
                double e0 = Math.Exp(z);
                d = e0;
                return s * e0;
            }

            double e = Math.Exp(z);
            d = e / (1.0 + e);
            return s * Math.Log(1.0 + e);
        }

        /// <summary>Clamp of x into [lo, hi] with rounded corners of width s.</summary>
        public static double Clamp(double x, double lo, double hi, double s,
            out double dx, out double dlo, out double dhi)
        {
            double a = Softplus(x - lo, s, out double da);
            double b = Softplus(x - hi, s, out double db);
            dx = da - db;
            dlo = 1.0 - da;
            dhi = db;
            return lo + a - b;
        }

        public static double Tanh(double x, out double d)
        {
            double t = Math.Tanh(x);
            d = 1.0 - t * t;
            return t;
        }
    }

    internal enum StageMode
    {
        Regulating,
        CurrentLimit,
        Off
    }

    /// <summary>
    /// Output stage shared by amplifiers and regulators. In normal operation it is
    /// a voltage source (target behind an output resistance) solved through a
    /// branch current; when the load demands more than the current limit, or would
    /// need the stage to sink current it cannot sink, it switches mode with
    /// hysteresis. Each mode is smooth, which keeps Newton iteration well behaved.
    /// </summary>
    internal sealed class OutputStage
    {
        private readonly int[] _nodes = new int[3];
        private readonly double[] _gains = new double[3];

        public StageMode Mode { get; set; } = StageMode.Regulating;
        public double OutputResistance { get; set; } = 0.05;
        public double SourceLimit { get; set; } = 1.0;

        /// <summary>Maximum current the stage can sink; 0 for a source-only stage such as a regulator.</summary>
        public double SinkLimit { get; set; }

        public double LastCurrent { get; set; }

        private int _switchesThisStep;

        public void BeginStep() => _switchesThisStep = 0;

        /// <summary>
        /// Re-checks the mode against the current Newton iterate so that the solve
        /// lands on a physically consistent mode (no sinking regulator, no current
        /// beyond the limit). A bounded number of switches per step prevents chatter.
        /// </summary>
        public void Reconcile(StampContext ctx, int branch, int output, double target)
        {
            if (_switchesThisStep >= 8) return;
            double current = Mode == StageMode.Off ? 0.0 : ctx.Unknown(branch);
            double saved = LastCurrent;
            if (Update(current, ctx.V(output), target))
            {
                _switchesThisStep++;
                ctx.LimitingApplied = true;
            }
            else
            {
                LastCurrent = saved;
            }
        }

        /// <summary>
        /// Stamps the branch for a target voltage T(controls) at the output.
        /// <paramref name="supply"/> is where the delivered current comes from.
        /// </summary>
        public void Stamp(StampContext ctx, int branch, int supply, int output, double target, int controlCount)
        {
            ctx.AddBranchCurrent(supply, output, branch);
            switch (Mode)
            {
                case StageMode.CurrentLimit:
                    ctx.AddMatrix(branch, branch, 1.0);
                    ctx.AddRhs(branch, LastCurrent >= 0.0 ? SourceLimit : -SinkLimit);
                    break;
                case StageMode.Off:
                    ctx.AddMatrix(branch, branch, 1.0);
                    break;
                default:
                    // V(out) - sum(d_k * V_k) - Rout * i = T0 - sum(d_k * V_k0)
                    double rhs = target;
                    ctx.AddRowNode(branch, output, 1.0);
                    for (int i = 0; i < controlCount; i++)
                    {
                        ctx.AddRowNode(branch, _nodes[i], -_gains[i]);
                        rhs -= _gains[i] * ctx.V(_nodes[i]);
                    }

                    ctx.AddMatrix(branch, branch, -OutputResistance);
                    ctx.AddRhs(branch, rhs);
                    break;
            }
        }

        public void SetControl(int index, int node, double gain)
        {
            _nodes[index] = node;
            _gains[index] = gain;
        }

        /// <summary>Updates the mode after an accepted step. Returns true when it changed.</summary>
        public bool Update(double current, double outputVoltage, double target)
        {
            LastCurrent = current;
            StageMode next = Mode;
            switch (Mode)
            {
                case StageMode.Regulating:
                    if (current > SourceLimit * 1.001) next = StageMode.CurrentLimit;
                    else if (current < -SinkLimit * 1.001 - 1e-6) next = SinkLimit > 0.0 ? StageMode.CurrentLimit : StageMode.Off;
                    break;
                case StageMode.CurrentLimit:
                    if (current >= 0.0 ? outputVoltage > target : outputVoltage < target) next = StageMode.Regulating;
                    break;
                case StageMode.Off:
                    if (outputVoltage < target - 1e-3) next = StageMode.Regulating;
                    break;
            }

            if (next == Mode) return false;
            Mode = next;
            return true;
        }
    }

    /// <summary>
    /// Operational amplifier macro-model: transconductance input stage with slew
    /// limiting, dominant pole, rail-limited output with output resistance and
    /// short-circuit current limit. Supply current is drawn from the rail pins.
    /// </summary>
    public sealed class OpAmp : AnalogDevice
    {
        private readonly OutputStage _stage = new OutputStage();
        private int _pole;
        private bool _setupDone;
        private double _gm, _iMax, _r1, _c1;

        public OpAmp(string id, int nonInverting, int inverting, int output, int positiveRail, int negativeRail) : base(id)
        {
            NonInverting = nonInverting;
            Inverting = inverting;
            Output = output;
            PositiveRail = positiveRail;
            NegativeRail = negativeRail;
        }

        public int NonInverting { get; }
        public int Inverting { get; }
        public int Output { get; }
        public int PositiveRail { get; }
        public int NegativeRail { get; }

        public double OpenLoopGain { get; set; } = 1e5;
        public double GainBandwidth { get; set; } = 1e6;

        /// <summary>Slew rate in V/s.</summary>
        public double SlewRate { get; set; } = 0.5e6;

        public double OutputResistance { get; set; } = 50.0;
        public double OutputCurrentLimit { get; set; } = 0.03;

        /// <summary>How close the output gets to the negative / positive rail.</summary>
        public double SwingLow { get; set; } = 0.05;
        public double SwingHigh { get; set; } = 1.5;

        public double QuiescentCurrent { get; set; } = 0.5e-3;
        public double InputResistance { get; set; } = 1e12;
        public double OffsetVoltage { get; set; }

        public double OutputVoltage { get; private set; }
        public double OutputCurrent { get; private set; }

        public override bool IsNonlinear => true;

        public override int BranchCount => 1;

        public override void Setup(AnalogCircuit circuit)
        {
            if (_setupDone) return;
            _setupDone = true;
            _pole = circuit.InternalNode(Id, "pole");
            _r1 = 1e5;
            _gm = OpenLoopGain / _r1;
            double fp = GainBandwidth / OpenLoopGain;
            _c1 = 1.0 / (2.0 * Math.PI * _r1 * fp);
            _iMax = SlewRate * _c1;
            circuit.Add(new Capacitor(Id + "#c1", _pole, AnalogCircuit.Ground, _c1));
        }

        public override void BeginStep(StepInfo step) => _stage.BeginStep();

        public override void Reset()
        {
            _stage.Mode = StageMode.Regulating;
            _stage.LastCurrent = 0.0;
            OutputVoltage = OutputCurrent = 0.0;
        }

        private double Target(double p, double vp, double vn, out double dp, out double dvp, out double dvn)
        {
            double lo = vn + SwingLow, hi = vp - SwingHigh;
            if (hi < lo)
            {
                double mid = 0.5 * (lo + hi);
                dp = 0.0;
                dvp = 0.5;
                dvn = 0.5;
                return mid;
            }

            double t = Smooth.Clamp(p, lo, hi, 0.02, out dp, out double dlo, out double dhi);
            dvn = dlo;
            dvp = dhi;
            return t;
        }

        public override void Stamp(StampContext ctx)
        {
            ctx.AddConductance(NonInverting, Inverting, 1.0 / InputResistance);
            ctx.AddConductance(PositiveRail, NegativeRail, QuiescentCurrent / 5.0);

            // Input stage: slew-limited transconductance into the pole node.
            double vd = ctx.V(NonInverting, Inverting) + OffsetVoltage;
            double t = Smooth.Tanh(_gm * vd / _iMax, out double dt);
            double i0 = _iMax * t;
            double g = _gm * dt;
            ctx.AddTransconductance(AnalogCircuit.Ground, _pole, NonInverting, Inverting, g);
            ctx.AddCurrent(AnalogCircuit.Ground, _pole, i0 - g * (vd - OffsetVoltage));
            ctx.AddConductance(_pole, AnalogCircuit.Ground, 1.0 / _r1);

            // The internal node saturates a little beyond the rails (no unbounded wind-up).
            double pv = ctx.V(_pole), vp = ctx.V(PositiveRail), vn = ctx.V(NegativeRail);
            {
                const double gc = 1.0;
                double up = Smooth.Softplus(pv - (vp + 1.0), 0.05, out double dup);
                double dn = Smooth.Softplus((vn - 1.0) - pv, 0.05, out double ddn);
                double ic = gc * (up - dn);
                double dPole = gc * (dup + ddn), dRp = -gc * dup, dRn = -gc * ddn;
                ctx.AddTransconductance(_pole, AnalogCircuit.Ground, _pole, AnalogCircuit.Ground, dPole);
                ctx.AddTransconductance(_pole, AnalogCircuit.Ground, PositiveRail, AnalogCircuit.Ground, dRp);
                ctx.AddTransconductance(_pole, AnalogCircuit.Ground, NegativeRail, AnalogCircuit.Ground, dRn);
                ctx.AddCurrent(_pole, AnalogCircuit.Ground, ic - dPole * pv - dRp * vp - dRn * vn);
            }

            // Output stage: rail-clamped copy of the pole voltage.
            double target = Target(pv, vp, vn, out double dP, out double dVp, out double dVn);
            _stage.OutputResistance = OutputResistance;
            _stage.SourceLimit = OutputCurrentLimit;
            _stage.SinkLimit = OutputCurrentLimit;
            _stage.SetControl(0, _pole, dP);
            _stage.SetControl(1, PositiveRail, dVp);
            _stage.SetControl(2, NegativeRail, dVn);
            _stage.Reconcile(ctx, BranchBase, Output, target);
            int supply = _stage.LastCurrent >= 0.0 ? PositiveRail : NegativeRail;
            _stage.Stamp(ctx, BranchBase, supply, Output, target, 3);
        }

        public override void Accept(AcceptContext ctx)
        {
            OutputVoltage = ctx.V(Output);
            OutputCurrent = ctx.Unknown(BranchBase);
            double target = Target(ctx.V(_pole), ctx.V(PositiveRail), ctx.V(NegativeRail), out _, out _, out _);
            if (_stage.Update(OutputCurrent, OutputVoltage, target)) ctx.RequestDiscontinuity();
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "VOUT", OutputVoltage);
            sink.Write(Id, "IOUT", OutputCurrent);
        }
    }

    /// <summary>
    /// Linear voltage regulator (78xx, LM1117, AMS1117...): dropout, line/load
    /// regulation, current limit, quiescent current and thermal shutdown.
    /// </summary>
    public sealed class LinearRegulator : AnalogDevice
    {
        private readonly OutputStage _stage = new OutputStage();
        private bool _shutdown;
        private bool _setupDone;

        public LinearRegulator(string id, int input, int ground, int output, double outputVoltage) : base(id)
        {
            Input = input;
            Ground = ground;
            Output = output;
            OutputVoltage = outputVoltage;
        }

        public int Input { get; }
        public int Ground { get; }
        public int Output { get; }
        public double OutputVoltage { get; set; }
        public double Dropout { get; set; } = 2.0;
        public double CurrentLimit { get; set; } = 1.5;
        public double OutputResistance { get; set; } = 0.02;
        public double QuiescentCurrent { get; set; } = 5e-3;
        public double MaxInputVoltage { get; set; } = 35.0;
        public double ThermalResistance { get; set; } = 50.0;
        public double ThermalCapacitance { get; set; } = 0.5;
        public double ShutdownTempC { get; set; } = 150.0;
        public ThermalState? Thermal { get; private set; }

        public double Vin { get; private set; }
        public double Vout { get; private set; }
        public double Current { get; private set; }
        public double Power { get; private set; }
        public bool InThermalShutdown => _shutdown;

        public override bool IsNonlinear => true;

        public override int BranchCount => 1;

        public override void Setup(AnalogCircuit circuit)
        {
            if (_setupDone) return;
            _setupDone = true;
            if (ThermalResistance > 0.0) Thermal = new ThermalState(ThermalResistance, ThermalCapacitance);
        }

        public override void BeginStep(StepInfo step) => _stage.BeginStep();

        public override void Reset()
        {
            _shutdown = false;
            _stage.Mode = StageMode.Regulating;
            _stage.LastCurrent = 0.0;
            Thermal?.Reset(25.0);
        }

        /// <summary>A replacement part: cold, undamaged.</summary>
        public override void Repair()
        {
            base.Repair();
            _shutdown = false;
            _stage.Mode = StageMode.Regulating;
            Thermal?.Reset(25.0);
        }

        /// <summary>Smooth minimum of the set point and (Vin - dropout), never below ground.</summary>
        private double Target(double vin, double vg, out double dVin, out double dVg)
        {
            double a = vg + OutputVoltage;
            double b = vin - Dropout;
            double sp = Smooth.Softplus(a - b, 0.03, out double ds);
            double t = a - sp;
            dVin = ds;
            dVg = 1.0 - ds;
            double floor = Smooth.Softplus(t - vg, 0.03, out double df);
            dVin *= df;
            dVg = dVg * df + (1.0 - df);
            return vg + floor;
        }

        public override void Stamp(StampContext ctx)
        {
            ctx.AddConductance(Output, Ground, ctx.Gmin);
            int k = BranchBase;
            if (Failure == FailureMode.Short)
            {
                ctx.AddBranchCurrent(Input, Output, k);
                ctx.AddRowNode(k, Input, 1.0);
                ctx.AddRowNode(k, Output, -1.0);
                ctx.AddMatrix(k, k, -0.5);
                return;
            }

            ctx.AddConductance(Input, Ground, QuiescentCurrent / 12.0);
            if (Failure == FailureMode.Open || _shutdown)
            {
                ctx.AddBranchCurrent(Input, Output, k);
                ctx.AddMatrix(k, k, 1.0);
                return;
            }

            double target = Target(ctx.V(Input), ctx.V(Ground), out double dVin, out double dVg);
            _stage.OutputResistance = OutputResistance;
            _stage.SourceLimit = CurrentLimit;
            _stage.SinkLimit = 0.0;
            _stage.SetControl(0, Input, dVin);
            _stage.SetControl(1, Ground, dVg);
            _stage.Reconcile(ctx, k, Output, target);
            _stage.Stamp(ctx, k, Input, Output, target, 2);
        }

        public override void Accept(AcceptContext ctx)
        {
            Vin = ctx.V(Input, Ground);
            Vout = ctx.V(Output, Ground);
            Current = ctx.Unknown(BranchBase);
            if (!_shutdown && !IsFailed)
            {
                double target = Target(ctx.V(Input), ctx.V(Ground), out _, out _);
                if (_stage.Update(Current, ctx.V(Output), target)) ctx.RequestDiscontinuity();
            }

            Power = Math.Max(0.0, (Vin - Vout) * Current) + Vin * QuiescentCurrent;
            if (ctx.IsDc) return;
            Thermal?.Advance(Power, ctx.Dt, ctx.AmbientTempC);

            if (Thermal != null)
            {
                bool hot = Thermal.TemperatureC >= ShutdownTempC;
                bool cool = Thermal.TemperatureC <= ShutdownTempC - 20.0;
                if (!_shutdown && hot)
                {
                    _shutdown = true;
                    ctx.Events.Add(new AnalogEvent(ctx.Time, Id, AnalogEventKind.Warning,
                        $"thermal shutdown at {Thermal.TemperatureC:F0} C ({Power:F1} W)"));
                    ctx.RequestDiscontinuity();
                }
                else if (_shutdown && cool)
                {
                    _shutdown = false;
                    _stage.Mode = StageMode.Regulating;
                    ctx.Events.Add(new AnalogEvent(ctx.Time, Id, AnalogEventKind.StateChange, "ısıl kapanmadan çıktı (soğudu, yeniden çalışıyor)"));
                    ctx.RequestDiscontinuity();
                }
            }

            if (ctx.EnableDamage && !IsFailed && MaxInputVoltage > 0.0 && Vin > MaxInputVoltage * 1.15)
            {
                Fail(FailureMode.Short, $"giriş aşırı gerilimi {Vin:F1} V (en çok {MaxInputVoltage:G3} V), regülatör kısa devre oldu", ctx);
            }
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "VIN", Vin);
            sink.Write(Id, "VOUT", Vout);
            sink.Write(Id, "I", Current);
            sink.Write(Id, "P", Power);
            sink.Write(Id, "SHUTDOWN", _shutdown ? 1.0 : 0.0);
            if (Thermal != null) sink.Write(Id, "T", Thermal.TemperatureC);
        }
    }

    /// <summary>
    /// Brushed DC motor with winding resistance and inductance, back-EMF,
    /// rotor inertia, viscous and Coulomb friction, gearbox and external load
    /// torque. Electrical and mechanical equations are solved together.
    /// </summary>
    public sealed class DcMotor : AnalogDevice
    {
        private double _iPrev, _vPrev, _wPrev, _wDotPrev;
        private double _req, _veq, _alpha, _wHist;
        private double _friction;

        public DcMotor(string id, int a, int b) : base(id)
        {
            A = a;
            B = b;
        }

        public int A { get; }
        public int B { get; }

        public double Resistance { get; set; } = 2.0;
        public double Inductance { get; set; } = 1e-3;

        /// <summary>Back-EMF constant (V*s/rad) = torque constant (N*m/A).</summary>
        public double MotorConstant { get; set; } = 0.01;

        /// <summary>Rotor inertia in kg*m^2 (motor side).</summary>
        public double Inertia { get; set; } = 1e-6;
        public double ViscousFriction { get; set; } = 1e-6;
        public double CoulombFriction { get; set; }

        /// <summary>Gear reduction (motor turns per output turn).</summary>
        public double GearRatio { get; set; } = 1.0;
        public double GearEfficiency { get; set; } = 1.0;

        /// <summary>[thermal] Copper's temperature coefficient (1/K): the winding resistance rises ~0.39 %/°C.</summary>
        public double CopperTempCo { get; set; } = 0.00393;

        /// <summary>Winding resistance at its present temperature (<see cref="Resistance"/> is the 25 °C value).</summary>
        public double HotResistance => Resistance * Math.Max(0.5, 1.0 + CopperTempCo * (Thermal.TemperatureC - 25.0));

        /// <summary>Copper loss in the winding right now (W), at the hot resistance.</summary>
        public double CopperLoss => Current * Current * HotResistance;

        /// <summary>Friction loss right now (W): brushes, bearings and, in a gear motor, the gear train.</summary>
        public double FrictionLoss { get; private set; }

        /// <summary>Torque applied by the mechanism at the output shaft, opposing rotation when positive.</summary>
        public double LoadTorque { get; set; }

        /// <summary>Extra inertia of the driven mechanism at the output shaft.</summary>
        public double LoadInertia { get; set; }

        public double MaxWindingTempC { get; set; } = 130.0;

        /// <summary>Shaft locked by the mechanism (jam, blocked wheel): the motor stalls.</summary>
        public bool Jammed
        {
            get => _jammed;
            set
            {
                if (_jammed == value) return;
                _jammed = value;
                PendingDiscontinuity = true;
            }
        }

        private bool _jammed;
        public ThermalState Thermal { get; } = new ThermalState(15.0, 20.0);

        public double Current { get; private set; }
        public double Voltage { get; private set; }

        /// <summary>Motor shaft speed in rad/s.</summary>
        public double AngularVelocity => _wPrev;

        /// <summary>Output (after gearbox) speed in revolutions per minute.</summary>
        public double OutputRpm => _wPrev / GearRatio * 60.0 / (2.0 * Math.PI);

        /// <summary>Output shaft angle in radians.</summary>
        public double OutputAngle { get; private set; }

        public double OutputTorque => MotorConstant * Current * GearRatio * GearEfficiency;

        public override int BranchCount => 2;

        /// <summary>Fills the model from typical datasheet numbers.</summary>
        public void SetFromDatasheet(double ratedVoltage, double noLoadRpm, double noLoadCurrent, double stallCurrent,
            double gearRatio = 1.0, double inductance = 1e-3)
        {
            GearRatio = gearRatio;
            Resistance = ratedVoltage / stallCurrent;
            double w0 = noLoadRpm * gearRatio * 2.0 * Math.PI / 60.0;
            MotorConstant = (ratedVoltage - noLoadCurrent * Resistance) / w0;
            ViscousFriction = MotorConstant * noLoadCurrent / w0 * 0.5;
            CoulombFriction = MotorConstant * noLoadCurrent * 0.5;
            Inductance = inductance;
            Inertia = Math.Max(1e-7, MotorConstant * MotorConstant * 0.02 / Resistance);
        }

        private double TotalInertia => Inertia + LoadInertia / (GearRatio * GearRatio);

        public override void Reset()
        {
            _iPrev = _vPrev = _wPrev = _wDotPrev = 0.0;
            OutputAngle = 0.0;
            Thermal.Reset(25.0);
        }

        /// <summary>A replacement part: cold, undamaged.</summary>
        public override void Repair()
        {
            base.Repair();
            Thermal.Reset(25.0);
        }

        public override void BeginStep(StepInfo step)
        {
            double load = LoadTorque / (GearRatio * Math.Max(GearEfficiency, 0.05));
            _friction = CoulombFriction * Math.Tanh(_wPrev / 0.5) + load;

            if (step.IsDc)
            {
                _req = 0.0;
                _veq = 0.0;
                _alpha = 0.0;
                _wHist = 0.0;
            }
            else if (step.Trapezoidal)
            {
                _req = 2.0 * Inductance / step.Dt;
                _veq = -_req * _iPrev - _vPrev;
                _alpha = 2.0 / step.Dt;
                _wHist = TotalInertia * (_alpha * _wPrev + _wDotPrev);
            }
            else
            {
                _req = Inductance / step.Dt;
                _veq = -_req * _iPrev;
                _alpha = 1.0 / step.Dt;
                _wHist = TotalInertia * _alpha * _wPrev;
            }
        }

        public override void Stamp(StampContext ctx)
        {
            int ki = BranchBase, kw = BranchBase + 1;
            bool open = Failure == FailureMode.Open;

            ctx.AddBranchCurrent(A, B, ki);
            if (open)
            {
                ctx.AddMatrix(ki, ki, 1.0);
            }
            else
            {
                // V(A) - V(B) - (R + Req) i - Ke w = Veq
                ctx.AddRowNode(ki, A, 1.0);
                ctx.AddRowNode(ki, B, -1.0);
                ctx.AddMatrix(ki, ki, -(HotResistance + _req));
                ctx.AddMatrix(ki, kw, -MotorConstant);
                ctx.AddRhs(ki, _veq);
            }

            if (Jammed)
            {
                // Shaft mechanically locked: w = 0, the winding sees stall current.
                ctx.AddMatrix(kw, kw, 1.0);
                return;
            }

            // (J alpha + b) w - Kt i = J history - friction
            ctx.AddMatrix(kw, kw, TotalInertia * _alpha + ViscousFriction + (ctx.IsDc ? 1e-9 : 0.0));
            ctx.AddMatrix(kw, ki, -MotorConstant);
            ctx.AddRhs(kw, _wHist - _friction);
        }

        public override void Accept(AcceptContext ctx)
        {
            double i = ctx.Unknown(BranchBase);
            double w = ctx.Unknown(BranchBase + 1);
            double v = ctx.V(A, B);
            double vL = v - HotResistance * i - MotorConstant * w;
            double wDot = TotalInertia > 0.0
                ? (MotorConstant * i - ViscousFriction * w - _friction) / TotalInertia
                : 0.0;

            if (!ctx.IsDc) OutputAngle += 0.5 * (w + _wPrev) * ctx.Dt / GearRatio;
            _iPrev = i;
            _vPrev = ctx.IsDc ? 0.0 : vL;
            _wPrev = w;
            _wDotPrev = ctx.IsDc ? 0.0 : wDot;
            Current = i;
            Voltage = v;

            if (ctx.IsDc) return;
            // Only the motor's own friction is heat here; the load torque is shaft power delivered to the load.
            FrictionLoss = Math.Abs(CoulombFriction * Math.Tanh(w / 0.5) * w) + ViscousFriction * w * w;
            Thermal.Advance(i * i * HotResistance, ctx.Dt, ctx.AmbientTempC);
            if (ctx.EnableDamage && !IsFailed && Thermal.TemperatureC > MaxWindingTempC)
            {
                Fail(FailureMode.Open, $"sargı {Thermal.TemperatureC:F0} °C'de yandı ({Math.Abs(i):F2} A, motor zorlanıyor/kilitli olabilir)", ctx);
            }
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "V", Voltage);
            sink.Write(Id, "I", Current);
            sink.Write(Id, "RPM", OutputRpm);
            sink.Write(Id, "ANGLE", OutputAngle);
            sink.Write(Id, "TORQUE", OutputTorque);
            sink.Write(Id, "T", Thermal.TemperatureC);
        }
    }

    /// <summary>Electromechanical relay: inductive coil, pull-in/drop-out hysteresis, operate delay, COM/NO/NC contacts.</summary>
    public sealed class Relay : AnalogDevice
    {
        private Inductor? _coil;
        private Resistor? _coilR;
        private Switch? _no;
        private Switch? _nc;
        private bool _energized;
        private double _pendingSince = -1.0;
        private bool _setupDone;

        public Relay(string id, int coilA, int coilB, int common, int normallyOpen, int normallyClosed) : base(id)
        {
            CoilA = coilA;
            CoilB = coilB;
            Common = common;
            NormallyOpen = normallyOpen;
            NormallyClosed = normallyClosed;
        }

        public int CoilA { get; }
        public int CoilB { get; }
        public int Common { get; }
        public int NormallyOpen { get; }
        public int NormallyClosed { get; }

        public double CoilResistance { get; set; } = 70.0;
        public double CoilInductance { get; set; } = 0.1;
        public double PullInCurrent { get; set; } = 0.05;
        public double DropOutCurrent { get; set; } = 0.015;
        public double OperateTime { get; set; } = 5e-3;
        public double ContactResistance { get; set; } = 0.05;

        public bool Energized => _energized;
        public double CoilCurrent => _coil?.Current ?? 0.0;

        public override void Setup(AnalogCircuit circuit)
        {
            if (_setupDone) return;
            _setupDone = true;
            int mid = circuit.InternalNode(Id, "coil");
            _coilR = circuit.Add(new Resistor(Id + "#rcoil", CoilA, mid, CoilResistance));
            _coil = circuit.Add(new Inductor(Id + "#lcoil", mid, CoilB, CoilInductance) { SeriesResistance = 0.0 });
            _no = circuit.Add(new Switch(Id + "#no", Common, NormallyOpen, false) { OnResistance = ContactResistance });
            _nc = circuit.Add(new Switch(Id + "#nc", Common, NormallyClosed, true) { OnResistance = ContactResistance });
        }

        public override void Reset()
        {
            _energized = false;
            _pendingSince = -1.0;
            if (_no != null) _no.Closed = false;
            if (_nc != null) _nc.Closed = true;
        }

        public override void Stamp(StampContext ctx) { }

        public override void Accept(AcceptContext ctx)
        {
            double i = Math.Abs(CoilCurrent);
            bool want = _energized ? i > DropOutCurrent : i >= PullInCurrent;
            if (want == _energized)
            {
                _pendingSince = -1.0;
                return;
            }

            if (ctx.IsDc)
            {
                SetContacts(want, ctx);
                return;
            }

            if (_pendingSince < 0.0) _pendingSince = ctx.Time;
            if (ctx.Time - _pendingSince >= OperateTime) SetContacts(want, ctx);
        }

        public override double NextBreakpoint(double time) =>
            _pendingSince >= 0.0 && _pendingSince + OperateTime > time ? _pendingSince + OperateTime : double.PositiveInfinity;

        private void SetContacts(bool energized, AcceptContext ctx)
        {
            _energized = energized;
            _pendingSince = -1.0;
            _no!.Closed = energized;
            _nc!.Closed = !energized;
            ctx.RequestDiscontinuity();
            ctx.Events.Add(new AnalogEvent(ctx.Time, Id, AnalogEventKind.StateChange, energized ? "energized" : "released"));
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "ICOIL", CoilCurrent);
            sink.Write(Id, "ON", _energized ? 1.0 : 0.0);
        }
    }

    public enum PinMode
    {
        Input,
        InputPullup,
        OutputLow,
        OutputHigh
    }

    /// <summary>
    /// Microcontroller GPIO pin: push-pull driver with output resistance from the
    /// supply rail, high-impedance input, optional pull-up, Schmitt-trigger read
    /// with hysteresis and absolute-maximum current damage.
    /// </summary>
    public sealed class DigitalPin : AnalogDevice
    {
        private PinMode _mode = PinMode.Input;
        private bool _level;

        public DigitalPin(string id, int pin, int supply, int ground) : base(id)
        {
            Pin = pin;
            Supply = supply;
            Ground = ground;
        }

        public int Pin { get; }
        public int Supply { get; }
        public int Ground { get; }

        public double DriveResistance { get; set; } = 25.0;
        public double PullupResistance { get; set; } = 35e3;
        public double InputLeakage { get; set; } = 1e-8;
        public double AbsoluteMaxCurrent { get; set; } = 0.04;

        public PinMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value) return;
                _mode = value;
                PendingDiscontinuity = true;
            }
        }

        public double Voltage { get; private set; }

        /// <summary>Current flowing out of the pin into the external circuit.</summary>
        public double Current { get; private set; }

        /// <summary>Logic level as the input buffer reads it (Schmitt trigger, 0.3/0.6 Vcc).</summary>
        public bool DigitalValue => _level;

        private double _pwmFrequency;
        private double _pwmDuty;
        private double _pwmStart;
        private bool _pwmHigh;

        public double PwmFrequency => _pwmFrequency;
        public double PwmDuty => _pwmDuty;
        public bool PwmActive => _pwmFrequency > 0.0;

        /// <summary>
        /// Hardware PWM like Arduino analogWrite: the pin drives high for
        /// duty × period, then low, starting at <paramref name="startTime"/>.
        /// </summary>
        public void SetPwm(double frequency, double duty, double startTime)
        {
            _pwmFrequency = Math.Max(0.0, frequency);
            _pwmDuty = AnalogMath.Clamp(duty, 0.0, 1.0);
            _pwmStart = startTime;
            PendingDiscontinuity = true;
        }

        public void StopPwm()
        {
            if (_pwmFrequency <= 0.0) return;
            _pwmFrequency = 0.0;
            PendingDiscontinuity = true;
        }

        private PinMode EffectiveMode
        {
            get
            {
                if (_pwmFrequency <= 0.0) return _mode;
                if (_pwmDuty <= 0.0) return PinMode.OutputLow;
                if (_pwmDuty >= 1.0) return PinMode.OutputHigh;
                return _pwmHigh ? PinMode.OutputHigh : PinMode.OutputLow;
            }
        }

        private readonly Queue<(double Time, PinMode Mode)> _scheduled = new Queue<(double, PinMode)>();

        /// <summary>
        /// Queues a mode change at an exact time inside the coming step(s), so pulses
        /// shorter than the co-simulation slice (a 10 µs trigger) reach the circuit.
        /// </summary>
        public void Schedule(double time, PinMode mode) => _scheduled.Enqueue((time, mode));

        public bool HasScheduled => _scheduled.Count > 0;

        public override void BeginStep(StepInfo step)
        {
            double stepStart = step.Time - step.Dt;
            while (_scheduled.Count > 0 && _scheduled.Peek().Time <= stepStart + 1e-12)
            {
                var (_, mode) = _scheduled.Dequeue();
                if (_mode != mode)
                {
                    _mode = mode;
                    PendingDiscontinuity = true;
                }
            }

            if (_pwmFrequency <= 0.0) return;
            double t = step.Time - step.Dt * 0.5;
            double phase = (t - _pwmStart) * _pwmFrequency;
            phase -= Math.Floor(phase);
            _pwmHigh = phase < _pwmDuty;
        }

        public override double NextBreakpoint(double time)
        {
            foreach (var (t, _) in _scheduled)
            {
                if (t > time + 1e-12) return Math.Min(t, PwmBreakpoint(time));
            }

            return PwmBreakpoint(time);
        }

        private double PwmBreakpoint(double time)
        {
            if (_pwmFrequency <= 0.0 || _pwmDuty <= 0.0 || _pwmDuty >= 1.0) return double.PositiveInfinity;
            double period = 1.0 / _pwmFrequency;
            double eps = Math.Max(1e-15, Math.Abs(time) * 1e-13);
            double k = Math.Floor((time - _pwmStart) / period);
            for (int i = 0; i < 3; i++)
            {
                double cycle = _pwmStart + (k + i) * period;
                if (cycle > time + eps) return cycle;
                double fall = cycle + _pwmDuty * period;
                if (fall > time + eps) return fall;
            }

            return double.PositiveInfinity;
        }

        public override void Stamp(StampContext ctx)
        {
            ctx.AddConductance(Pin, Ground, InputLeakage / 5.0);
            if (Failure == FailureMode.Short)
            {
                ctx.AddConductance(Pin, Ground, 1.0 / 50.0);
                return;
            }

            if (Failure == FailureMode.Open) return;
            switch (EffectiveMode)
            {
                case PinMode.OutputHigh:
                    ctx.AddConductance(Supply, Pin, 1.0 / DriveResistance);
                    break;
                case PinMode.OutputLow:
                    ctx.AddConductance(Pin, Ground, 1.0 / DriveResistance);
                    break;
                case PinMode.InputPullup:
                    ctx.AddConductance(Supply, Pin, 1.0 / PullupResistance);
                    break;
            }
        }

        public override void Accept(AcceptContext ctx)
        {
            double v = ctx.V(Pin, Ground);
            double vcc = ctx.V(Supply, Ground);
            Voltage = v;
            Current = EffectiveMode switch
            {
                PinMode.OutputHigh => (ctx.V(Supply) - ctx.V(Pin)) / DriveResistance,
                PinMode.OutputLow => -(v / DriveResistance),
                PinMode.InputPullup => (ctx.V(Supply) - ctx.V(Pin)) / PullupResistance,
                _ => 0.0
            };

            if (_level && v < 0.3 * vcc) _level = false;
            else if (!_level && v > 0.6 * vcc) _level = true;

            if (ctx.IsDc || !ctx.EnableDamage || IsFailed) return;
            if (Math.Abs(Current) > AbsoluteMaxCurrent * 2.5)
            {
                Fail(FailureMode.Open, $"pin sürücüsü {Current * 1000.0:F0} mA ile yandı (mutlak sınır {AbsoluteMaxCurrent * 1000.0:F0} mA)", ctx);
            }
            else if (Math.Abs(Current) > AbsoluteMaxCurrent)
            {
                ctx.Events.Add(new AnalogEvent(ctx.Time, Id, AnalogEventKind.Warning,
                    $"pin current {Current * 1000.0:F0} mA exceeds {AbsoluteMaxCurrent * 1000.0:F0} mA"));
            }
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "V", Voltage);
            sink.Write(Id, "I", Current);
            sink.Write(Id, "D", _level ? 1.0 : 0.0);
            sink.Write(Id, "PWM", _pwmFrequency > 0.0 ? _pwmDuty : -1.0);
        }
    }

    /// <summary>
    /// Hobby servo: measures the control pulse width on its signal pin, moves
    /// the horn toward the commanded angle at a limited speed and draws supply
    /// current according to how hard it is working.
    /// </summary>
    public sealed class Servo : AnalogDevice
    {
        private double _riseTime = -1.0;
        private bool _high;
        private double _conductance;

        public Servo(string id, int supply, int ground, int signal) : base(id)
        {
            Supply = supply;
            Ground = ground;
            Signal = signal;
        }

        public int Supply { get; }
        public int Ground { get; }
        public int Signal { get; }

        public double MinPulse { get; set; } = 544e-6;
        public double MaxPulse { get; set; } = 2400e-6;
        public double MaxAngleDeg { get; set; } = 180.0;

        /// <summary>No-load speed in degrees per second (0.1 s / 60 deg = 600).</summary>
        public double SpeedDegPerSec { get; set; } = 600.0;
        public double IdleCurrent { get; set; } = 0.006;
        public double MovingCurrent { get; set; } = 0.25;
        public double StallCurrent { get; set; } = 0.65;

        /// <summary>Set by the physics side when the horn is blocked.</summary>
        public bool Blocked { get; set; }

        /// <summary>
        /// The horn drives a simulated mechanism: <see cref="AngleDeg"/> follows
        /// <see cref="DrivenAngleDeg"/> and the supply current follows <see cref="DrivenCurrent"/>
        /// (motor torque against the real load, up to stall).
        /// </summary>
        public bool MechanicallyDriven { get; set; }

        public double DrivenAngleDeg { get; set; } = 90.0;
        public double DrivenCurrent { get; set; }

        public double TargetAngleDeg { get; private set; } = double.NaN;
        public double AngleDeg { get; private set; } = 90.0;
        public double PulseWidth { get; private set; }
        public double SupplyCurrent { get; private set; }

        public override void Reset()
        {
            _riseTime = -1.0;
            _high = false;
            TargetAngleDeg = double.NaN;
            AngleDeg = 90.0;
            _conductance = 0.0;
        }

        public override void Stamp(StampContext ctx)
        {
            ctx.AddConductance(Signal, Ground, 1.0 / 1e6);
            ctx.AddConductance(Supply, Ground, Math.Max(_conductance, 1e-9));
        }

        public override void Accept(AcceptContext ctx)
        {
            double vs = ctx.V(Supply, Ground);
            double sig = ctx.V(Signal, Ground);
            bool high = _high ? sig > 1.0 : sig > 2.0;
            if (high && !_high) _riseTime = ctx.Time;
            if (!high && _high && _riseTime >= 0.0)
            {
                double width = ctx.Time - _riseTime;
                if (width > 300e-6 && width < 3000e-6)
                {
                    PulseWidth = width;
                    double f = AnalogMath.Clamp((width - MinPulse) / (MaxPulse - MinPulse), 0.0, 1.0);
                    TargetAngleDeg = f * MaxAngleDeg;
                }
            }

            _high = high;
            if (ctx.IsDc) return;

            double current = IdleCurrent;
            bool powered = vs > 3.0;
            if (MechanicallyDriven)
            {
                AngleDeg = DrivenAngleDeg;
                current = Math.Max(IdleCurrent, DrivenCurrent);
            }
            else if (powered && !double.IsNaN(TargetAngleDeg))
            {
                double error = TargetAngleDeg - AngleDeg;
                if (Blocked && Math.Abs(error) > 1.0)
                {
                    current = StallCurrent;
                }
                else if (Math.Abs(error) > 0.05)
                {
                    double speed = SpeedDegPerSec * AnalogMath.Clamp(vs / 5.0, 0.0, 1.4);
                    double move = Math.Min(Math.Abs(error), speed * ctx.Dt);
                    AngleDeg += Math.Sign(error) * move;
                    current = MovingCurrent * AnalogMath.Clamp(Math.Abs(error) / 10.0, 0.2, 1.0);
                }
            }

            SupplyCurrent = powered ? current : 0.0;
            // The load changes gradually; no discontinuity is flagged so the step size can stay large.
            _conductance = vs > 0.5 ? SupplyCurrent / vs : 0.0;
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "ANGLE", AngleDeg);
            sink.Write(Id, "TARGET", double.IsNaN(TargetAngleDeg) ? -1.0 : TargetAngleDeg);
            sink.Write(Id, "PULSE", PulseWidth);
            sink.Write(Id, "I", SupplyCurrent);
        }
    }

    public enum LogicFunction
    {
        Buffer,
        Not,
        And,
        Nand,
        Or,
        Nor,
        Xor,
        Xnor
    }

    /// <summary>CMOS logic gate (74HC style): threshold inputs, push-pull output from the supply pin, propagation delay.</summary>
    public sealed class LogicGate : AnalogDevice
    {
        private readonly int[] _inputs;
        private bool _output;
        private bool _pending;
        private double _pendingAt = double.PositiveInfinity;

        public LogicGate(string id, LogicFunction function, int output, int supply, int ground, params int[] inputs) : base(id)
        {
            Function = function;
            Output = output;
            Supply = supply;
            Ground = ground;
            _inputs = inputs ?? Array.Empty<int>();
        }

        public LogicFunction Function { get; }
        public int Output { get; }
        public int Supply { get; }
        public int Ground { get; }
        public double PropagationDelay { get; set; } = 10e-9;
        public double OutputResistance { get; set; } = 50.0;

        public bool OutputState => _output;

        private bool Evaluate(AcceptContext ctx)
        {
            double vcc = ctx.V(Supply, Ground);
            bool acc = Function == LogicFunction.And || Function == LogicFunction.Nand;
            int ones = 0;
            foreach (int input in _inputs)
            {
                bool bit = ctx.V(input, Ground) > 0.5 * vcc;
                if (bit) ones++;
                if (Function == LogicFunction.And || Function == LogicFunction.Nand) acc &= bit;
                else acc |= bit;
            }

            switch (Function)
            {
                case LogicFunction.Buffer: return ones > 0;
                case LogicFunction.Not: return ones == 0;
                case LogicFunction.And: return acc;
                case LogicFunction.Nand: return !acc;
                case LogicFunction.Or: return acc;
                case LogicFunction.Nor: return !acc;
                case LogicFunction.Xor: return (ones & 1) == 1;
                default: return (ones & 1) == 0;
            }
        }

        public override void Reset()
        {
            _output = false;
            _pending = false;
            _pendingAt = double.PositiveInfinity;
        }

        public override void Stamp(StampContext ctx)
        {
            foreach (int input in _inputs) ctx.AddConductance(input, Ground, 1e-9);
            ctx.AddConductance(Supply, Ground, 1e-6);
            ctx.AddConductance(_output ? Supply : Ground, Output, 1.0 / OutputResistance);
        }

        public override void Accept(AcceptContext ctx)
        {
            bool next = Evaluate(ctx);
            if (ctx.IsDc)
            {
                if (_output != next) ctx.RequestDiscontinuity();
                _output = next;
                return;
            }

            if (_pending && ctx.Time >= _pendingAt - 1e-15)
            {
                _pending = false;
                _pendingAt = double.PositiveInfinity;
                if (_output != next)
                {
                    _output = next;
                    ctx.RequestDiscontinuity();
                    return;
                }
            }

            if (next != _output && !_pending)
            {
                _pending = true;
                _pendingAt = ctx.Time + PropagationDelay;
            }
            else if (next == _output && _pending)
            {
                _pending = false;
                _pendingAt = double.PositiveInfinity;
            }
        }

        public override double NextBreakpoint(double time) => _pending && _pendingAt > time ? _pendingAt : double.PositiveInfinity;

        public override void Report(ISignalSink sink) => sink.Write(Id, "OUT", _output ? 1.0 : 0.0);
    }
}
