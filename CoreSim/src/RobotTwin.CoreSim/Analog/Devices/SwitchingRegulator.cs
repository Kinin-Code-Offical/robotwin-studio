using System;

namespace RobotTwin.CoreSim.Analog.Devices
{
    /// <summary>[smps] The switching converters modelled.</summary>
    public enum ConverterTopology
    {
        /// <summary>Step-down: the output below the input (LM2596, MP1584, XL4015).</summary>
        Buck,

        /// <summary>Step-up: the output above the input (MT3608, XL6009).</summary>
        Boost
    }

    /// <summary>
    /// [smps] A switching regulator module, averaged over its switching period (state-space averaging, as datasheet loop
    /// design and SPICE's average models do): the inductor current is a state; a voltage loop sets the current it asks
    /// for, and the inner current loop follows within L/G, so the module cannot deliver more than its switch current limit
    /// nor more voltage than its duty cycle allows from the input (a buck's dropout, a boost's maximum duty). A diode lets
    /// the current fall but not reverse. Below the under-voltage lockout it is off; turning on, its reference rises over
    /// the soft-start, so the output and the inrush into its capacitors are what they are on the bench. The input draws
    /// the output power plus the losses: the switch's and the inductor's resistance, the diode's drop, the switching
    /// losses and the quiescent current, which also heat it up to its thermal shutdown.
    /// </summary>
    public sealed class SwitchingRegulator : AnalogDevice
    {
        private double _i, _iPrev, _vlPrev;   // inductor current, and its history for the companion model
        private double _req, _veq;
        private bool _trap;
        private double _integral;
        private double _u;           // the switch node's average (output side), as last solved
        private double _vin, _vout;
        private int _mode;           // 0 regulating, 1 out of duty (dropout), -1 freewheeling, 2 off (no current)
        private bool _limit;         // the current limit holds the reference
        private bool _on;
        private double _onAt = double.NaN;
        private double _target;      // the soft-started reference
        private bool _hot;

        public SwitchingRegulator(string id, ConverterTopology topology, int vin, int gnd, int vout) : base(id)
        {
            Topology = topology;
            Vin = vin;
            Gnd = gnd;
            Vout = vout;
        }

        public ConverterTopology Topology { get; }
        public int Vin { get; }
        public int Gnd { get; }
        public int Vout { get; }

        /// <summary>The output it regulates to (V), as its feedback divider or trimmer sets it.</summary>
        public double OutputVoltage { get; set; } = 5.0;

        /// <summary>The switch's current limit (A, the inductor's peak).</summary>
        public double CurrentLimit { get; set; } = 3.0;

        public double Inductance { get; set; } = 33e-6;

        /// <summary>The inductor's winding resistance (Ω).</summary>
        public double InductorResistance { get; set; } = 0.03;

        /// <summary>The switch's on-resistance (Ω) and its saturation drop (V, a bipolar switch's VCE(sat)).</summary>
        public double SwitchResistance { get; set; } = 0.1;

        public double SwitchDrop { get; set; }

        /// <summary>The catch (buck) or output (boost) diode's forward drop (V); a synchronous converter has none.</summary>
        public double DiodeDrop { get; set; } = 0.45;

        public bool Synchronous { get; set; }

        public double MaxDuty { get; set; } = 0.95;
        public double Frequency { get; set; } = 150e3;

        /// <summary>The switch's rise plus fall time (s): its switching loss is ½·V·I·(tr+tf)·f.</summary>
        public double TransitionTime { get; set; } = 60e-9;

        public double QuiescentCurrent { get; set; } = 5e-3;

        /// <summary>Under-voltage lockout: on above this input (V), off again below it less the hysteresis.</summary>
        public double MinInput { get; set; } = 4.5;

        public double UvloHysteresis { get; set; } = 0.3;
        public double MaxInput { get; set; } = 40;

        /// <summary>The soft-start: the reference rises from 0 over this time (s) after the converter turns on.</summary>
        public double SoftStart { get; set; } = 1e-3;

        public double ThermalResistance { get; set; } = 40;
        public double ShutdownC { get; set; } = 150;
        public ThermalState Thermal { get; } = new ThermalState(40, 0.5);

        /// <summary>The current loop's gain (V/A): it follows its reference within L/G.</summary>
        private double Gi => Inductance / 10e-6;

        /// <summary>The voltage loop's gain (A/V) and integral time (s).</summary>
        /// <remarks>
        /// With its own 220 µF the loop crosses over near 3.6 kHz, with the integral's zero at 160 Hz: about 80° of phase
        /// margin, and still 60° with ten times the capacitance on its output.
        /// </remarks>
        public double VoltageGain { get; set; } = 5.0;

        public double IntegralTime { get; set; } = 1e-3;

        public double OutputCurrent => _i;
        public double InputVoltage => _vin;
        public double OutputVoltageNow => _vout;
        public double InputCurrent { get; private set; }
        public double Losses { get; private set; }
        public bool Running => _on && !_hot && !IsFailed;
        public bool InCurrentLimit => _limit && _mode == 0;
        public bool InDropout => _mode == 1;

        public double Efficiency => _vin * InputCurrent > 1e-6 ? Math.Max(0, Math.Min(1, _vout * _i / (_vin * InputCurrent))) : 0;

        public override int BranchCount => 1;

        public override void Reset()
        {
            _i = _iPrev = _vlPrev = _integral = _u = _vin = _vout = 0;
            _mode = 2;
            _limit = false;
            _on = false;
            _hot = false;
            _onAt = double.NaN;
            _target = 0;
            InputCurrent = Losses = 0;
            Thermal.Reset(25);
        }

        public override void Repair()
        {
            base.Repair();
            Thermal.Reset(25);
            _hot = false;
        }

        /// <summary>The average voltage the switch node can make (output side): its least and its most.</summary>
        private (double Low, double High) Bounds(double vin, double i)
        {
            double r = SwitchResistance * Math.Abs(i);
            if (Topology == ConverterTopology.Buck)
                return (Synchronous ? 0 : -DiodeDrop, Math.Max(0, MaxDuty * vin - SwitchDrop - r));
            // A boost passes the input through its diode when it does not switch, and lifts it by 1/(1 − D) at most.
            return (vin - (Synchronous ? 0 : DiodeDrop) - r, (vin - r) / (1 - MaxDuty));
        }

        /// <summary>What the voltage loop asks of the inductor, held to the switch's limit (and to no reverse current).</summary>
        private double Reference(double vout, out bool limited)
        {
            double iref = VoltageGain * (_target - vout) + _integral;
            limited = false;
            if (iref > CurrentLimit)
            {
                limited = true;
                return CurrentLimit;
            }

            return Synchronous ? Math.Max(-CurrentLimit, iref) : Math.Max(0, iref);
        }

        public override void BeginStep(StepInfo step)
        {
            _trap = !step.IsDc && step.Trapezoidal;
            if (step.IsDc)
            {
                _req = _veq = 0;
            }
            else if (_trap)
            {
                _req = 2 * Inductance / step.Dt;
                _veq = _req * _iPrev + _vlPrev;
            }
            else
            {
                _req = Inductance / step.Dt;
                _veq = _req * _iPrev;
            }

            // On above the lockout, off below it; turning on starts the soft-start.
            bool on = !_hot && !IsFailed && (_on ? _vin > MinInput - UvloHysteresis : _vin > MinInput);
            if (on && !_on)
            {
                _onAt = step.Time;
                _integral = 0;
                PendingDiscontinuity = true;
            }

            if (!on && _on) PendingDiscontinuity = true;
            _on = on;
            double ramp = !on || double.IsNaN(_onAt) ? 0 : SoftStart <= 0 ? 1 : Math.Max(0, Math.Min(1, (step.Time - _onAt) / SoftStart));
            _target = OutputVoltage * ramp;

            int mode;
            if (!on)
            {
                // Off: a buck's switch is open (its current runs out through the diode); a boost's diode still passes the input.
                mode = _i > 1e-6 || Topology == ConverterTopology.Boost ? -1 : 2;
                _limit = false;
            }
            else
            {
                double iref = Reference(_vout, out _limit);
                double want = Gi * (iref - _i) + _vout + InductorResistance * _i;
                var (low, high) = Bounds(_vin, _i);
                mode = want > high ? 1 : want < low ? -1 : 0;
                if (mode == 0 && _mode != 0 && _mode != 2 && Math.Abs(want - (_mode == 1 ? high : low)) < 0.02 * Math.Max(1, Math.Abs(want))) mode = _mode;   // no flipping at the edge
            }

            // A diode does not carry current backwards: a freewheeling current that has run out stays out.
            if (mode == -1 && !Synchronous && _i <= 1e-6 && (Topology == ConverterTopology.Buck || _vout > _vin - DiodeDrop)) mode = 2;
            if (mode != _mode) PendingDiscontinuity = true;
            _mode = mode;
        }

        public override void Stamp(StampContext ctx)
        {
            int k = BranchBase;
            // The inductor current leaves the module's ground side and enters the output.
            ctx.AddBranchCurrent(Gnd, Vout, k);
            ctx.AddConductance(Vin, Gnd, 1e-6);
            if (Failure == FailureMode.Open || _mode == 2)
            {
                // Off with no current left in the inductor: nothing flows (it only goes off once the current is out).
                ctx.AddMatrix(k, k, 1);
                if (Failure != FailureMode.Open && _mode == 2) ctx.AddCurrent(Vin, Gnd, InputCurrent);
                return;
            }

            if (Failure == FailureMode.Short)
            {
                // A shorted switch: the input straight to the output.
                ctx.AddRowNode(k, Vout, 1);
                ctx.AddRowNode(k, Vin, -1);
                ctx.AddMatrix(k, k, 0.05);
                return;
            }

            // V(out) − V(gnd) + (R_L + R_eq)·i = u + L-history; u as the mode makes it.
            ctx.AddRowNode(k, Vout, 1);
            ctx.AddRowNode(k, Gnd, -1);
            double r = InductorResistance + _req;
            switch (_mode)
            {
                case 0:
                {
                    // Regulating: u = G·(i* − i) + V(out) + R_L·i, with i* = K·(V* − V(out)) + ∫ (or the limit): the
                    // inductor follows i*, which the output's error sets.
                    ctx.AddMatrix(k, k, r + Gi - InductorResistance);
                    // Back out the V(out) term: the row has V(out) − V(gnd) on the left already.
                    ctx.AddRowNode(k, Vout, -1);
                    ctx.AddRowNode(k, Gnd, 1);
                    if (_limit) ctx.AddRhs(k, Gi * CurrentLimit + _veq);
                    else if (!Synchronous && VoltageGain * (_target - _vout) + _integral < 0) ctx.AddRhs(k, _veq);   // asks for none
                    else
                    {
                        ctx.AddRowNode(k, Vout, Gi * VoltageGain);
                        ctx.AddRowNode(k, Gnd, -Gi * VoltageGain);
                        ctx.AddRhs(k, Gi * (VoltageGain * _target + _integral) + _veq);
                    }

                    break;
                }

                case 1:
                {
                    // Out of duty: the most the input allows.
                    double rs = SwitchResistance;
                    ctx.AddMatrix(k, k, r + rs);
                    if (Topology == ConverterTopology.Buck)
                    {
                        ctx.AddRowNode(k, Vin, -MaxDuty);
                        ctx.AddRowNode(k, Gnd, MaxDuty);
                        ctx.AddRhs(k, -SwitchDrop + _veq);
                    }
                    else
                    {
                        double gain = 1 / (1 - MaxDuty);
                        ctx.AddRowNode(k, Vin, -gain);
                        ctx.AddRowNode(k, Gnd, gain);
                        ctx.AddRhs(k, _veq);
                    }

                    break;
                }

                default:
                {
                    // Freewheeling: a buck's current through its diode against the output; a boost's input through its diode.
                    double vd = Synchronous ? 0 : DiodeDrop;
                    ctx.AddMatrix(k, k, r);
                    if (Topology == ConverterTopology.Boost)
                    {
                        ctx.AddRowNode(k, Vin, -1);
                        ctx.AddRowNode(k, Gnd, 1);
                    }

                    ctx.AddRhs(k, -vd + _veq);
                    break;
                }
            }

            // The input draws what goes out plus what is lost (as last solved: the input is a stiff supply's).
            ctx.AddCurrent(Vin, Gnd, InputCurrent);
        }

        public override void Accept(AcceptContext ctx)
        {
            _vin = ctx.V(Vin, Gnd);
            _vout = ctx.V(Vout, Gnd);
            _i = ctx.Unknown(BranchBase);
            if (ctx.IsDc)
            {
                _i = 0;
                _vlPrev = 0;
            }
            else _vlPrev = _req > 0 ? _req * (_i - _iPrev) - (_trap ? _vlPrev : 0) : 0;   // the inductor's voltage over the step

            _iPrev = _i;
            // The switch node's average, from the inductor's equation: u = V(out) + R_L·i + L·di/dt.
            _u = _vout + InductorResistance * _i + _vlPrev;
            // The integral runs while regulating, and outside it only towards regulating again (conditional
            // integration); a converter with a diode never asks for reverse current, so its integral does not go below
            // zero. Without both, a boost whose soft-start begins under its diode-passed input winds down and never starts.
            double error = _target - _vout;
            bool integrate = _mode == 0 ? !_limit : (_mode == -1 && error > 0) || (_mode == 1 && error < 0) || (_mode == 2 && error > 0);
            if (!ctx.IsDc && _on && integrate)
                _integral = Math.Max(Synchronous ? -CurrentLimit : 0, Math.Min(CurrentLimit, _integral + VoltageGain * error * ctx.Dt / IntegralTime));

            // The duty cycle, for the losses; the input current from the power balance.
            double vin = Math.Max(0.1, _vin);
            double vd = Synchronous ? 0 : DiodeDrop;
            double d, iSwitch, pLoss;
            double iOut = Math.Max(0, _i);
            if (Topology == ConverterTopology.Buck)
            {
                d = Math.Max(0, Math.Min(1, (_u + vd) / (vin + vd)));
                iSwitch = iOut;
                pLoss = d * iOut * iOut * SwitchResistance + d * SwitchDrop * iOut + (1 - d) * (Synchronous ? iOut * iOut * SwitchResistance : vd * iOut);
            }
            else
            {
                d = Math.Max(0, Math.Min(MaxDuty, 1 - vin / Math.Max(vin, _u + vd)));
                iSwitch = iOut / Math.Max(1 - MaxDuty, 1 - d);
                pLoss = d * iSwitch * iSwitch * SwitchResistance + vd * iOut;
            }

            double vSwitched = Topology == ConverterTopology.Buck ? vin : Math.Max(vin, _u);
            double pSwitching = _on && _mode != 2 ? 0.5 * vSwitched * iSwitch * TransitionTime * Frequency : 0;
            double pQuiescent = _on ? vin * QuiescentCurrent : 0;
            Losses = pLoss + pSwitching + pQuiescent;
            double pOut = Math.Max(0, _u * _i);
            InputCurrent = _mode == 2 ? (_on ? QuiescentCurrent : 0) : (pOut + pLoss + pSwitching) / vin + (_on ? QuiescentCurrent : 0);
            if (Topology == ConverterTopology.Boost && !_on) InputCurrent = iOut;   // the diode passes it through

            if (ctx.IsDc) return;
            Thermal.ThermalResistance = ThermalResistance;
            Thermal.Advance(Losses + InductorResistance * _i * _i, ctx.Dt, ctx.AmbientTempC);
            if (!_hot && Thermal.TemperatureC > ShutdownC)
            {
                _hot = true;
                ctx.Events.Add(new AnalogEvent(ctx.Time, Id, AnalogEventKind.Warning, $"ısıl kapanma: {Thermal.TemperatureC:F0} °C ({Losses:F1} W kayıp)"));
                ctx.RequestDiscontinuity();
            }
            else if (_hot && Thermal.TemperatureC < ShutdownC - 20)
            {
                _hot = false;
                ctx.RequestDiscontinuity();
            }

            if (ctx.EnableDamage && !IsFailed && MaxInput > 0 && _vin > MaxInput * 1.15)
                Fail(FailureMode.Short, $"giriş aşırı gerilimi {_vin:F1} V (en çok {MaxInput:G3} V): anahtar kısa devre oldu", ctx);
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "VIN", _vin);
            sink.Write(Id, "VOUT", _vout);
            sink.Write(Id, "IOUT", _i);
            sink.Write(Id, "IIN", InputCurrent);
            sink.Write(Id, "EFF", Efficiency);
            sink.Write(Id, "T", Thermal.TemperatureC);
        }
    }
}
