using System;

namespace RobotTwin.CoreSim.Analog.Devices
{
    /// <summary>
    /// Independent voltage source with optional series resistance. The branch
    /// current is the current flowing from the + node through the source to the
    /// - node, so a source that delivers power has a negative branch current.
    /// </summary>
    public class VoltageSource : AnalogDevice
    {
        public VoltageSource(string id, int plus, int minus, Waveform waveform) : base(id)
        {
            Plus = plus;
            Minus = minus;
            Waveform = waveform ?? throw new ArgumentNullException(nameof(waveform));
        }

        public int Plus { get; }
        public int Minus { get; }
        public Waveform Waveform { get; set; }
        public double SeriesResistance { get; set; }

        /// <summary>Terminal voltage after the last accepted step.</summary>
        public double Voltage { get; protected set; }

        /// <summary>Current delivered out of the + terminal.</summary>
        public double Current { get; protected set; }

        public override int BranchCount => 1;

        /// <summary>Open-circuit voltage used for the step ending at <paramref name="time"/>.</summary>
        protected virtual double SourceVoltage(double time) => Waveform.Value(time);

        public override double NextBreakpoint(double time) => Waveform.NextBreakpoint(time);

        public override void Stamp(StampContext ctx)
        {
            int k = BranchBase;
            ctx.AddBranchCurrent(Plus, Minus, k);

            // V(+) - V(-) - Rs * i = Vsource
            ctx.AddRowNode(k, Plus, 1.0);
            ctx.AddRowNode(k, Minus, -1.0);
            if (SeriesResistance > 0.0) ctx.AddMatrix(k, k, -SeriesResistance);
            ctx.AddRhs(k, SourceVoltage(ctx.Time) * ctx.SourceScale);
        }

        public override void Accept(AcceptContext ctx)
        {
            Voltage = ctx.V(Plus, Minus);
            Current = -ctx.Unknown(BranchBase);
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "V", Voltage);
            sink.Write(Id, "I", Current);
            sink.Write(Id, "P", Voltage * Current);
        }
    }

    /// <summary>Independent current source pushing current out of its + terminal into the circuit.</summary>
    public sealed class CurrentSource : AnalogDevice
    {
        public CurrentSource(string id, int plus, int minus, Waveform waveform) : base(id)
        {
            Plus = plus;
            Minus = minus;
            Waveform = waveform ?? throw new ArgumentNullException(nameof(waveform));
        }

        public int Plus { get; }
        public int Minus { get; }
        public Waveform Waveform { get; set; }
        public double Voltage { get; private set; }
        public double Current { get; private set; }

        public override double NextBreakpoint(double time) => Waveform.NextBreakpoint(time);

        public override void Stamp(StampContext ctx)
        {
            // Inside the source the current flows from - to +.
            ctx.AddCurrent(Minus, Plus, Waveform.Value(ctx.Time) * ctx.SourceScale);
        }

        public override void Accept(AcceptContext ctx)
        {
            Voltage = ctx.V(Plus, Minus);
            Current = Waveform.Value(ctx.Time);
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "V", Voltage);
            sink.Write(Id, "I", Current);
            sink.Write(Id, "P", Voltage * Current);
        }
    }

    public enum BatteryChemistry
    {
        Alkaline,
        NiMH,
        LithiumIon,
        LiFePO4,
        LeadAcid,
        LithiumCoin,
        ZincCarbon
    }

    /// <summary>
    /// Battery pack: open-circuit voltage follows the state of charge along a
    /// chemistry-specific curve, internal resistance rises as the cells empty,
    /// and the charge is integrated from the delivered current.
    /// </summary>
    public sealed class Battery : VoltageSource
    {
        private static readonly double[] CurveSoc = { 0.0, 0.05, 0.20, 0.50, 0.80, 1.0 };

        private double _ocv;
        private double _baseResistance;

        public Battery(string id, int plus, int minus, BatteryChemistry chemistry, int cells, double capacityAh)
            : base(id, plus, minus, new DcWaveform(0.0))
        {
            if (cells < 1) throw new ArgumentOutOfRangeException(nameof(cells));
            Chemistry = chemistry;
            Cells = cells;
            CapacityAh = capacityAh;
            _baseResistance = DefaultCellResistance(chemistry) * cells;
            SeriesResistance = _baseResistance;
            StateOfCharge = 1.0;
        }

        public BatteryChemistry Chemistry { get; }
        public int Cells { get; }
        public double CapacityAh { get; set; }

        /// <summary>Internal resistance of the fresh pack in ohms.</summary>
        public double InternalResistance
        {
            get => _baseResistance;
            set => _baseResistance = Math.Max(value, 1e-4);
        }

        /// <summary>Remaining charge, 0..1.</summary>
        public double StateOfCharge { get; set; }

        /// <summary>Charge at power-on; restored by <see cref="Reset"/>.</summary>
        public double InitialStateOfCharge { get; set; } = 1.0;

        public bool Rechargeable =>
            Chemistry == BatteryChemistry.NiMH || Chemistry == BatteryChemistry.LithiumIon ||
            Chemistry == BatteryChemistry.LiFePO4 || Chemistry == BatteryChemistry.LeadAcid;

        public double OpenCircuitVoltage => _ocv;

        public bool IsDepleted => StateOfCharge <= 0.0;

        public static double NominalCellVoltage(BatteryChemistry chemistry)
        {
            switch (chemistry)
            {
                case BatteryChemistry.NiMH: return 1.2;
                case BatteryChemistry.LithiumIon: return 3.7;
                case BatteryChemistry.LiFePO4: return 3.2;
                case BatteryChemistry.LeadAcid: return 2.0;
                case BatteryChemistry.LithiumCoin: return 3.0;
                default: return 1.5;
            }
        }

        private static double DefaultCellResistance(BatteryChemistry chemistry)
        {
            switch (chemistry)
            {
                case BatteryChemistry.NiMH: return 0.03;
                case BatteryChemistry.LithiumIon: return 0.05;
                case BatteryChemistry.LiFePO4: return 0.03;
                case BatteryChemistry.LeadAcid: return 0.01;
                case BatteryChemistry.LithiumCoin: return 15.0;
                case BatteryChemistry.ZincCarbon: return 0.5;
                default: return 0.15;
            }
        }

        // Cell voltage at 0, 5, 20, 50, 80 and 100 % charge (shared, read-only: called every time step).
        private static readonly double[] NiMHCurve = { 1.00, 1.10, 1.20, 1.25, 1.30, 1.40 };
        private static readonly double[] LithiumIonCurve = { 3.00, 3.50, 3.70, 3.82, 4.05, 4.20 };
        private static readonly double[] LiFePO4Curve = { 2.50, 3.10, 3.25, 3.30, 3.33, 3.45 };
        private static readonly double[] LeadAcidCurve = { 1.75, 1.93, 1.97, 2.02, 2.07, 2.12 };
        private static readonly double[] LithiumCoinCurve = { 2.00, 2.70, 2.90, 2.95, 3.00, 3.20 };
        private static readonly double[] ZincCarbonCurve = { 0.80, 1.00, 1.15, 1.30, 1.45, 1.55 };
        private static readonly double[] DefaultCurve = { 0.90, 1.05, 1.20, 1.30, 1.45, 1.60 };

        private static double[] CellCurve(BatteryChemistry chemistry)
        {
            switch (chemistry)
            {
                case BatteryChemistry.NiMH: return NiMHCurve;
                case BatteryChemistry.LithiumIon: return LithiumIonCurve;
                case BatteryChemistry.LiFePO4: return LiFePO4Curve;
                case BatteryChemistry.LeadAcid: return LeadAcidCurve;
                case BatteryChemistry.LithiumCoin: return LithiumCoinCurve;
                case BatteryChemistry.ZincCarbon: return ZincCarbonCurve;
                default: return DefaultCurve;
            }
        }

        public static double CellVoltage(BatteryChemistry chemistry, double soc)
        {
            double[] curve = CellCurve(chemistry);
            soc = AnalogMath.Clamp(soc, 0.0, 1.0);
            for (int i = 1; i < CurveSoc.Length; i++)
            {
                if (soc <= CurveSoc[i])
                {
                    double t = (soc - CurveSoc[i - 1]) / (CurveSoc[i] - CurveSoc[i - 1]);
                    return curve[i - 1] + (curve[i] - curve[i - 1]) * t;
                }
            }

            return curve[curve.Length - 1];
        }

        public override void Reset()
        {
            StateOfCharge = AnalogMath.Clamp(InitialStateOfCharge, 0.0, 1.0);
            Thermal.Reset(25.0);
            _polarization = 0.0;
            UpdateModel();
        }

        public override void BeginStep(StepInfo step) => UpdateModel();

        /// <summary>A replaced pack: fresh charge, cool, no short.</summary>
        public override void Repair()
        {
            base.Repair();
            _short = 0.0;
            Thermal.Reset(25.0);
            StateOfCharge = AnalogMath.Clamp(InitialStateOfCharge, 0.0, 1.0);
            UpdateModel();
        }

        private void UpdateModel()
        {
            double soc = AnalogMath.Clamp(StateOfCharge, 0.0, 1.0);
            // The last 0.2 % of the charge: the voltage falls off the knee to zero instead of dropping in one
            // step (a cliff the solver cannot follow); a lithium pack's protection board cuts on the way down.
            _ocv = soc <= 0.0 ? 0.0 : Cells * CellVoltage(Chemistry, soc) * Math.Min(1.0, soc / EmptyKnee);

            // Resistance climbs steeply over the last part of the discharge.
            double depletion = 1.0 - soc;
            SeriesResistance = _baseResistance * (1.0 + 3.0 * depletion * depletion * depletion * depletion) +
                               (soc <= 0.0 ? 1e6 : 0.0);
        }

        /// <summary>Charge fraction below which the open-circuit voltage falls linearly to zero.</summary>
        public const double EmptyKnee = 0.002;

        protected override double SourceVoltage(double time) => _ocv - _polarization;

        // [thermal] Polarization: besides the instant I·R drop, a cell's voltage keeps sagging for seconds
        // under load (diffusion in the electrolyte) and recovers slowly afterwards — an alkaline AA
        // feeding a stalled motor "collapses", a LiPo much less. First-order RC per chemistry.
        private double _polarization;

        /// <summary>Polarization resistance of the pack (Ω) and its time constant (s).</summary>
        public double PolarizationResistance { get; set; } = -1;

        public double PolarizationTime { get; set; } = -1;

        private (double R, double Tau) Polarization()
        {
            double r = PolarizationResistance >= 0 ? PolarizationResistance : Cells * Chemistry switch
            {
                BatteryChemistry.Alkaline => 0.25,
                BatteryChemistry.ZincCarbon => 0.6,
                BatteryChemistry.NiMH => 0.02,
                BatteryChemistry.LithiumIon => 0.02,
                BatteryChemistry.LiFePO4 => 0.015,
                BatteryChemistry.LeadAcid => 0.01,
                _ => 0.05
            };
            double tau = PolarizationTime > 0 ? PolarizationTime : Chemistry == BatteryChemistry.Alkaline || Chemistry == BatteryChemistry.ZincCarbon ? 15.0 : 30.0;
            return (r, tau);
        }

        /// <summary>Present polarization drop (V).</summary>
        public double PolarizationVoltage => _polarization;

        /// <summary>Fault injection: a short of this resistance across the terminals (0 = none).</summary>
        public double ShortCircuitResistance
        {
            get => _short;
            set
            {
                if (_short == value) return;
                _short = Math.Max(0.0, value);
                PendingDiscontinuity = true;
            }
        }

        private double _short;

        public override void Stamp(StampContext ctx)
        {
            base.Stamp(ctx);
            if (_short > 0.0) ctx.AddConductance(Plus, Minus, 1.0 / _short);
        }

        /// <summary>Pack temperature (internal I²R heating against its thermal mass).</summary>
        public ThermalState Thermal { get; } = new ThermalState(25.0, 15.0);

        /// <summary>Lithium packs go into thermal runaway above this temperature.</summary>
        public double RunawayTempC { get; set; } = 130.0;

        public bool IsLithium =>
            Chemistry == BatteryChemistry.LithiumIon || Chemistry == BatteryChemistry.LiFePO4 || Chemistry == BatteryChemistry.LithiumCoin;

        public override void Accept(AcceptContext ctx)
        {
            base.Accept(ctx);
            if (ctx.IsDc || CapacityAh <= 0.0) return;
            var (rp, tp) = Polarization();
            _polarization += (Current * rp - _polarization) * (1.0 - Math.Exp(-ctx.Dt / tp));   // exact first-order step

            // Heat from the internal resistance; small packs heat fast, big packs slowly.
            Thermal.ThermalCapacitance = Math.Max(1.5, 27.0 * CapacityAh * Cells);
            Thermal.ThermalResistance = Math.Max(4.0, 30.0 / Math.Sqrt(Math.Max(0.05, CapacityAh * Cells)));
            Thermal.Advance(Current * Current * SeriesResistance, ctx.Dt, ctx.AmbientTempC);
            if (ctx.EnableDamage && !IsFailed)
            {
                double limit = IsLithium ? RunawayTempC : 95.0;
                if (Thermal.TemperatureC >= limit)
                {
                    Fail(FailureMode.Open, IsLithium
                        ? $"termal kaçak: {Thermal.TemperatureC:F0} °C, hücre alev aldı ({Math.Abs(Current):F1} A)"
                        : $"aşırı ısındı ve sızdırdı ({Thermal.TemperatureC:F0} °C)", ctx);
                    StateOfCharge = 0.0;
                }
            }

            double deltaAh = Current * ctx.Dt / 3600.0;
            if (deltaAh < 0.0 && !Rechargeable) deltaAh = 0.0;
            bool wasAlive = StateOfCharge > 0.0;
            StateOfCharge = AnalogMath.Clamp(StateOfCharge - deltaAh / CapacityAh, 0.0, 1.0);
            if (wasAlive && StateOfCharge <= 0.0)
            {
                ctx.Events.Add(new AnalogEvent(ctx.Time, Id, AnalogEventKind.StateChange, "battery depleted"));
                ctx.RequestDiscontinuity();
            }
        }

        public override void Report(ISignalSink sink)
        {
            base.Report(sink);
            sink.Write(Id, "SOC", StateOfCharge);
            sink.Write(Id, "OCV", _ocv);
            sink.Write(Id, "RINT", SeriesResistance);
            sink.Write(Id, "T", Thermal.TemperatureC);
        }
    }
}
