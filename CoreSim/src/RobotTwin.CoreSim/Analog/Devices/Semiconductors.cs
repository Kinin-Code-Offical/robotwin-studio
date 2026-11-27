using System;

namespace RobotTwin.CoreSim.Analog.Devices
{
    /// <summary>
    /// PN junction diode (Shockley equation) with series resistance, reverse
    /// breakdown for Zener parts, temperature-dependent saturation current and a
    /// junction thermal model. LEDs use the same physics with a wider band gap.
    /// </summary>
    public class Diode : AnalogDevice
    {
        private const double NominalTempC = 27.0;

        private int _junction;
        private bool _setupDone;
        private double _vIter;
        private double _vAccepted;
        private double _vt;
        private double _isEff;
        private double _vCrit;
        private double _vtBreak;
        private double _vCritBreak;

        public Diode(string id, int anode, int cathode) : base(id)
        {
            Anode = anode;
            Cathode = cathode;
        }

        public int Anode { get; }
        public int Cathode { get; }

        /// <summary>Saturation current at 27 C.</summary>
        public double SaturationCurrent { get; set; } = 2.5e-9;

        /// <summary>Emission coefficient (ideality factor).</summary>
        public double Ideality { get; set; } = 1.75;

        public double SeriesResistance { get; set; }

        /// <summary>Reverse breakdown voltage (positive number); 0 means no breakdown modelled.</summary>
        public double BreakdownVoltage { get; set; }

        /// <summary>Reverse current at the breakdown voltage (the Zener test current).</summary>
        public double BreakdownCurrent { get; set; } = 5e-3;

        public double BreakdownIdeality { get; set; } = 1.0;

        /// <summary>Band gap in eV; sets how fast the forward drop falls with temperature.</summary>
        public double BandGap { get; set; } = 1.11;

        /// <summary>Zero-bias junction capacitance in farads (0 = ideal).</summary>
        public double JunctionCapacitance { get; set; }

        /// <summary>Maximum continuous forward current; 0 disables the check.</summary>
        public double MaxCurrent { get; set; }

        /// <summary>
        /// Non-repetitive peak surge current (datasheet I<sub>FSM</sub>, 8.3 ms half sine);
        /// 0 = 30 × <see cref="MaxCurrent"/>, typical of rectifier and Schottky diodes.
        /// Short inrush pulses below the matching I²t do not damage the junction.
        /// </summary>
        public double SurgeCurrent { get; set; }

        private double EffectiveSurge => SurgeCurrent > 0.0 ? SurgeCurrent : 30.0 * MaxCurrent;

        // Fusing integral of the over-current: ∫I²dt above the rating, leaking away with the junction's thermal time.
        private double _overloadI2t;

        /// <summary>Junction-to-ambient thermal resistance in K/W; 0 disables self-heating.</summary>
        public double ThermalResistance { get; set; }

        public double ThermalCapacitance { get; set; } = 1e-3;

        public double MaxJunctionTempC { get; set; } = 150.0;

        public ThermalState? Thermal { get; private set; }

        public double Voltage { get; private set; }
        public double Current { get; private set; }
        public double Power { get; private set; }

        /// <summary>Chooses the saturation current so the diode drops <paramref name="forwardVoltage"/> at <paramref name="forwardCurrent"/>.</summary>
        public void SetForwardPoint(double forwardVoltage, double forwardCurrent)
        {
            double vt = Ideality * AnalogMath.ThermalVoltage(NominalTempC);
            double vj = forwardVoltage - forwardCurrent * SeriesResistance;
            if (vj <= 0.0) throw new ArgumentException("Seri direnç üzerindeki düşüm ileri gerilimden büyük.");
            SaturationCurrent = forwardCurrent / (Math.Exp(vj / vt) - 1.0);
        }

        public override bool IsNonlinear => true;

        public override void Setup(AnalogCircuit circuit)
        {
            if (_setupDone) return;
            _setupDone = true;
            _junction = SeriesResistance > 0.0 ? circuit.InternalNode(Id, "j") : Anode;
            if (ThermalResistance > 0.0)
            {
                Thermal = new ThermalState(ThermalResistance, ThermalCapacitance);
            }

            if (JunctionCapacitance > 0.0)
            {
                circuit.Add(new Capacitor(Id + "#cj", _junction, Cathode, JunctionCapacitance));
            }
        }

        public override void Reset()
        {
            _vIter = 0.0;
            _vAccepted = 0.0;
            _overloadI2t = 0.0;
            Thermal?.Reset(25.0);
            Voltage = Current = Power = 0.0;
            UpdateTemperature(25.0);
        }

        /// <summary>A replacement part: cold, undamaged.</summary>
        public override void Repair()
        {
            base.Repair();
            _overloadI2t = 0.0;
            Thermal?.Reset(25.0);
        }

        private void UpdateTemperature(double tempC)
        {
            double t = tempC + AnalogMath.ZeroCelsius;
            double tNom = NominalTempC + AnalogMath.ZeroCelsius;
            double vtPhysical = AnalogMath.ThermalVoltage(tempC);
            _vt = Ideality * vtPhysical;
            double ratio = t / tNom;
            _isEff = SaturationCurrent * Math.Pow(ratio, 3.0 / Ideality) *
                     Math.Exp((ratio - 1.0) * BandGap / _vt);
            if (!(_isEff > 1e-300)) _isEff = 1e-300;
            _vCrit = AnalogMath.CriticalVoltage(_vt, _isEff);
            _vtBreak = BreakdownIdeality * vtPhysical;
            _vCritBreak = BreakdownVoltage > 0.0
                ? AnalogMath.CriticalVoltage(_vtBreak, Math.Max(BreakdownCurrent, 1e-12))
                : 0.0;
        }

        public override void BeginStep(StepInfo step)
        {
            _vIter = _vAccepted;
            UpdateTemperature(Thermal != null ? Thermal.TemperatureC : 25.0);
        }

        public override void Stamp(StampContext ctx)
        {
            if (SeriesResistance > 0.0) ctx.AddConductance(Anode, _junction, 1.0 / SeriesResistance);

            if (Failure == FailureMode.Open)
            {
                ctx.AddConductance(_junction, Cathode, ctx.Gmin);
                return;
            }

            if (Failure == FailureMode.Short)
            {
                ctx.AddConductance(_junction, Cathode, 10.0);
                return;
            }

            double vd = ctx.V(_junction, Cathode);
            bool limited = false;
            if (BreakdownVoltage > 0.0 && vd < -0.5 * BreakdownVoltage)
            {
                // Limit along the mirrored breakdown exponential.
                double vr = -(vd + BreakdownVoltage);
                double vrOld = -(_vIter + BreakdownVoltage);
                vr = AnalogMath.LimitJunction(vr, vrOld, _vtBreak, _vCritBreak, ref limited);
                vd = -(vr + BreakdownVoltage);
            }
            else
            {
                vd = AnalogMath.LimitJunction(vd, _vIter, _vt, _vCrit, ref limited);
            }

            if (limited) ctx.LimitingApplied = true;
            _vIter = vd;

            Evaluate(vd, ctx.Gmin, out double id, out double gd);
            ctx.AddConductance(_junction, Cathode, gd);
            ctx.AddCurrent(_junction, Cathode, id - gd * vd);
        }

        private void Evaluate(double vd, double gmin, out double id, out double gd)
        {
            double e = AnalogMath.SafeExp(vd / _vt, out double de);
            id = _isEff * (e - 1.0) + gmin * vd;
            gd = _isEff * de / _vt + gmin;
            if (BreakdownVoltage > 0.0)
            {
                double er = AnalogMath.SafeExp(-(vd + BreakdownVoltage) / _vtBreak, out double der);
                id -= BreakdownCurrent * er;
                gd += BreakdownCurrent * der / _vtBreak;
            }
        }

        public override void Accept(AcceptContext ctx)
        {
            double vj = ctx.V(_junction, Cathode);
            _vAccepted = vj;
            Voltage = ctx.V(Anode, Cathode);

            if (Failure == FailureMode.Open)
            {
                Current = 0.0;
            }
            else if (Failure == FailureMode.Short)
            {
                Current = vj * 10.0;
            }
            else
            {
                Evaluate(vj, 0.0, out double id, out _);
                Current = id;
            }

            Power = Voltage * Current;
            if (ctx.IsDc) return;

            Thermal?.Advance(Math.Abs(Power), ctx.Dt, ctx.AmbientTempC);
            if (!ctx.EnableDamage || IsFailed) return;

            if (Thermal != null && Thermal.TemperatureC >= MaxJunctionTempC)
            {
                Fail(FailureMode.Open, $"jonksiyon aşırı ısındı ({Thermal.TemperatureC:F0} °C, {Current * 1000.0:F0} mA)", ctx);
            }
            else if (MaxCurrent > 0.0)
            {
                // I²t of the surge rating (half sine, 8.3 ms): I_FSM²·t/2.
                double surge = EffectiveSurge;
                double limit = surge * surge * 0.0083 * 0.5;
                double i = Math.Abs(Current);
                _overloadI2t *= Math.Exp(-ctx.Dt / 0.05);
                if (i > MaxCurrent) _overloadI2t += i * i * ctx.Dt;
                if (i > 3.0 * surge || _overloadI2t > limit)
                {
                    Fail(FailureMode.Open, $"{Current * 1000.0:F0} mA ile yandı (anma {MaxCurrent * 1000.0:F0} mA, darbe sınırı {surge:F1} A)", ctx);
                }
            }
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "V", Voltage);
            sink.Write(Id, "I", Current);
            sink.Write(Id, "P", Power);
            if (Thermal != null) sink.Write(Id, "T", Thermal.TemperatureC);
        }
    }

    /// <summary>Light-emitting diode: a diode that also reports relative brightness.</summary>
    public sealed class Led : Diode
    {
        public Led(string id, int anode, int cathode, double forwardVoltage, double ratedCurrent = 0.02)
            : base(id, anode, cathode)
        {
            RatedCurrent = ratedCurrent;
            Ideality = 2.0;
            SeriesResistance = forwardVoltage > 2.6 ? 8.0 : 4.0;
            BandGap = Math.Max(1.4, forwardVoltage * 0.95);
            MaxCurrent = ratedCurrent * 1.5;
            ThermalResistance = 350.0;
            ThermalCapacitance = 1e-3;
            MaxJunctionTempC = 125.0;
            SetForwardPoint(forwardVoltage, ratedCurrent);
        }

        /// <summary>Current at which the LED reaches its nominal brightness.</summary>
        public double RatedCurrent { get; }

        /// <summary>Light output relative to the rated current (0 = dark, 1 = nominal).</summary>
        public double Brightness =>
            IsFailed || Current <= 0.0 ? 0.0 : Math.Min(Current / RatedCurrent, 3.0);

        public override void Report(ISignalSink sink)
        {
            base.Report(sink);
            sink.Write(Id, "L", Brightness);
        }
    }

    public enum Polarity
    {
        /// <summary>NPN transistor or N-channel MOSFET.</summary>
        N = 1,

        /// <summary>PNP transistor or P-channel MOSFET.</summary>
        P = -1
    }

    /// <summary>Bipolar transistor, Ebers-Moll transport model with Early effect.</summary>
    public sealed class Bjt : AnalogDevice
    {
        private double _vbeIter, _vbcIter, _vbeAcc, _vbcAcc;
        private double _vt;
        private double _vCrit;
        private bool _setupDone;

        public Bjt(string id, int collector, int @base, int emitter, Polarity polarity = Polarity.N) : base(id)
        {
            Collector = collector;
            Base = @base;
            Emitter = emitter;
            Polarity = polarity;
        }

        public int Collector { get; }
        public int Base { get; }
        public int Emitter { get; }
        public Polarity Polarity { get; }

        public double SaturationCurrent { get; set; } = 1e-14;
        public double ForwardBeta { get; set; } = 200.0;
        public double ReverseBeta { get; set; } = 2.0;

        /// <summary>Early voltage; 0 means infinite (no base-width modulation).</summary>
        public double EarlyVoltage { get; set; }

        public double BaseEmitterCapacitance { get; set; }
        public double BaseCollectorCapacitance { get; set; }

        public double MaxCollectorCurrent { get; set; }
        public double MaxCollectorEmitterVoltage { get; set; }
        public double ThermalResistance { get; set; }
        public double ThermalCapacitance { get; set; } = 5e-3;
        public double MaxJunctionTempC { get; set; } = 150.0;
        public ThermalState? Thermal { get; private set; }

        public double CollectorCurrent { get; private set; }
        public double BaseCurrent { get; private set; }
        public double Vbe { get; private set; }
        public double Vce { get; private set; }
        public double Power { get; private set; }

        public override bool IsNonlinear => true;

        public override void Setup(AnalogCircuit circuit)
        {
            if (_setupDone) return;
            _setupDone = true;
            if (ThermalResistance > 0.0) Thermal = new ThermalState(ThermalResistance, ThermalCapacitance);
            if (BaseEmitterCapacitance > 0.0)
            {
                circuit.Add(new Capacitor(Id + "#cbe", Base, Emitter, BaseEmitterCapacitance));
            }

            if (BaseCollectorCapacitance > 0.0)
            {
                circuit.Add(new Capacitor(Id + "#cbc", Base, Collector, BaseCollectorCapacitance));
            }
        }

        public override void Reset()
        {
            _vbeIter = _vbcIter = _vbeAcc = _vbcAcc = 0.0;
            Thermal?.Reset(25.0);
            CollectorCurrent = BaseCurrent = Vbe = Vce = Power = 0.0;
            ResetJunction();
        }

        /// <summary>A replacement part: cold, undamaged.</summary>
        public override void Repair()
        {
            base.Repair();
            Thermal?.Reset(25.0);
        }

        private void ResetJunction()
        {
            _vt = AnalogMath.ThermalVoltage(25.0);
            _vCrit = AnalogMath.CriticalVoltage(_vt, SaturationCurrent);
        }

        public override void BeginStep(StepInfo step)
        {
            _vbeIter = _vbeAcc;
            _vbcIter = _vbcAcc;
            _vt = AnalogMath.ThermalVoltage(Thermal != null ? Thermal.TemperatureC : 25.0);
            _vCrit = AnalogMath.CriticalVoltage(_vt, SaturationCurrent);
        }

        private void Evaluate(double vbe, double vbc, double gmin,
            out double ibe, out double gbe, out double ibc, out double gbc,
            out double ict, out double gmf, out double gmr)
        {
            double ef = AnalogMath.SafeExp(vbe / _vt, out double def);
            double er = AnalogMath.SafeExp(vbc / _vt, out double der);
            double isf = SaturationCurrent / ForwardBeta;
            double isr = SaturationCurrent / ReverseBeta;

            ibe = isf * (ef - 1.0) + gmin * vbe;
            gbe = isf * def / _vt + gmin;
            ibc = isr * (er - 1.0) + gmin * vbc;
            gbc = isr * der / _vt + gmin;

            double early = 1.0, dEarly = 0.0;
            if (EarlyVoltage > 0.0)
            {
                early = 1.0 - vbc / EarlyVoltage;
                dEarly = -1.0 / EarlyVoltage;
                if (early < 0.1)
                {
                    early = 0.1;
                    dEarly = 0.0;
                }
            }

            double transport = SaturationCurrent * (ef - er);
            ict = transport * early;
            gmf = SaturationCurrent * def / _vt * early;
            gmr = -SaturationCurrent * der / _vt * early + transport * dEarly;
        }

        public override void Stamp(StampContext ctx)
        {
            int c = Collector, b = Base, e = Emitter;
            if (Failure == FailureMode.Short)
            {
                ctx.AddConductance(c, e, 5.0);
                ctx.AddConductance(b, e, ctx.Gmin);
                return;
            }

            if (Failure == FailureMode.Open)
            {
                ctx.AddConductance(c, e, ctx.Gmin);
                ctx.AddConductance(b, e, ctx.Gmin);
                return;
            }

            double p = (double)(int)Polarity;
            double vbe = p * ctx.V(b, e);
            double vbc = p * ctx.V(b, c);
            bool limited = false;
            vbe = AnalogMath.LimitJunction(vbe, _vbeIter, _vt, _vCrit, ref limited);
            vbc = AnalogMath.LimitJunction(vbc, _vbcIter, _vt, _vCrit, ref limited);
            if (limited) ctx.LimitingApplied = true;
            _vbeIter = vbe;
            _vbcIter = vbc;

            Evaluate(vbe, vbc, ctx.Gmin, out double ibe, out double gbe, out double ibc, out double gbc,
                out double ict, out double gmf, out double gmr);

            // Base-emitter and base-collector junctions.
            ctx.AddConductance(b, e, gbe);
            ctx.AddCurrent(b, e, p * (ibe - gbe * vbe));
            ctx.AddConductance(b, c, gbc);
            ctx.AddCurrent(b, c, p * (ibc - gbc * vbc));

            // Transport current from collector to emitter.
            ctx.AddTransconductance(c, e, b, e, gmf);
            ctx.AddTransconductance(c, e, b, c, gmr);
            ctx.AddCurrent(c, e, p * (ict - gmf * vbe - gmr * vbc));
        }

        public override void Accept(AcceptContext ctx)
        {
            double p = (double)(int)Polarity;
            double vbe = p * ctx.V(Base, Emitter);
            double vbc = p * ctx.V(Base, Collector);
            _vbeAcc = vbe;
            _vbcAcc = vbc;
            Vbe = ctx.V(Base, Emitter);
            Vce = ctx.V(Collector, Emitter);

            if (Failure == FailureMode.Short)
            {
                CollectorCurrent = Vce * 5.0;
                BaseCurrent = 0.0;
            }
            else if (Failure == FailureMode.Open)
            {
                CollectorCurrent = BaseCurrent = 0.0;
            }
            else
            {
                Evaluate(vbe, vbc, 0.0, out double ibe, out _, out double ibc, out _, out double ict, out _, out _);
                CollectorCurrent = p * (ict - ibc);
                BaseCurrent = p * (ibe + ibc);
            }

            Power = Math.Abs(Vce * CollectorCurrent) + Math.Abs(Vbe * BaseCurrent);
            if (ctx.IsDc) return;
            Thermal?.Advance(Power, ctx.Dt, ctx.AmbientTempC);
            if (!ctx.EnableDamage || IsFailed) return;

            if (Thermal != null && Thermal.TemperatureC >= MaxJunctionTempC)
            {
                Fail(FailureMode.Short, $"jonksiyon aşırı ısındı ({Thermal.TemperatureC:F0} °C, {Power:F2} W)", ctx);
            }
            else if (MaxCollectorCurrent > 0.0 && Math.Abs(CollectorCurrent) > MaxCollectorCurrent * 5.0)
            {
                Fail(FailureMode.Open, $"kollektör akımı {CollectorCurrent:F2} A, {MaxCollectorCurrent:G3} A sınırının çok üstünde: transistör yandı", ctx);
            }
            else if (MaxCollectorEmitterVoltage > 0.0 && p * Vce > MaxCollectorEmitterVoltage * 1.2)
            {
                Fail(FailureMode.Short, $"kollektör-emetör {Vce:F0} V'ta delindi", ctx);
            }
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "IC", CollectorCurrent);
            sink.Write(Id, "IB", BaseCurrent);
            sink.Write(Id, "VBE", Vbe);
            sink.Write(Id, "VCE", Vce);
            sink.Write(Id, "P", Power);
            if (Thermal != null) sink.Write(Id, "T", Thermal.TemperatureC);
        }
    }

    /// <summary>
    /// Enhancement MOSFET (Shichman-Hodges level 1) with body diode, gate
    /// capacitances and thermal/over-voltage failure.
    /// </summary>
    public sealed class Mosfet : AnalogDevice
    {
        private double _vgsIter, _vdsIter, _vgsAcc, _vdsAcc;
        private bool _setupDone;

        public Mosfet(string id, int drain, int gate, int source, Polarity polarity = Polarity.N) : base(id)
        {
            Drain = drain;
            Gate = gate;
            Source = source;
            Polarity = polarity;
        }

        public int Drain { get; }
        public int Gate { get; }
        public int Source { get; }
        public Polarity Polarity { get; }

        /// <summary>Threshold voltage magnitude.</summary>
        public double Threshold { get; set; } = 2.0;

        /// <summary>Transconductance parameter K = k' * W / L, in A/V^2.</summary>
        public double Transconductance { get; set; } = 0.5;

        public double ChannelModulation { get; set; } = 0.01;

        public bool BodyDiode { get; set; } = true;
        public double GateSourceCapacitance { get; set; }
        public double GateDrainCapacitance { get; set; }

        public double MaxDrainCurrent { get; set; }
        public double MaxDrainSourceVoltage { get; set; }
        public double MaxGateSourceVoltage { get; set; } = 20.0;
        public double ThermalResistance { get; set; }
        public double ThermalCapacitance { get; set; } = 2e-2;
        public double MaxJunctionTempC { get; set; } = 150.0;
        public ThermalState? Thermal { get; private set; }

        public double DrainCurrent { get; private set; }
        public double Vgs { get; private set; }
        public double Vds { get; private set; }
        public double Power { get; private set; }

        /// <summary>
        /// Derives K from the datasheet on-resistance measured at a given gate drive and, when given, drain current: at
        /// that current the drain sits at V_ds = R·I, and the triode equation I = K·(V_ov·V_ds − V_ds²/2)·(1 + λ·V_ds) gives
        /// K (a datasheet's R_ds(on) is at its test current: tens of amperes for a power part, where V_ds is no longer
        /// small). Without a current (or past the triode region) the small-signal value K = 1 / (R·V_ov).
        /// </summary>
        public void SetOnResistance(double rdsOn, double atGateVoltage, double atCurrent = 0.0)
        {
            double overdrive = Math.Abs(atGateVoltage) - Threshold;
            if (overdrive <= 0.0) throw new ArgumentException("Kapı gerilimi eşik geriliminden büyük olmalı.");
            double i = Math.Abs(atCurrent), vds = rdsOn * i;
            if (i > 0.0 && vds < 0.5 * overdrive)
                Transconductance = i / ((overdrive * vds - 0.5 * vds * vds) * (1.0 + ChannelModulation * vds));
            else
                Transconductance = 1.0 / (rdsOn * overdrive);
        }

        public override bool IsNonlinear => true;

        public override void Setup(AnalogCircuit circuit)
        {
            if (_setupDone) return;
            _setupDone = true;
            if (ThermalResistance > 0.0) Thermal = new ThermalState(ThermalResistance, ThermalCapacitance);
            if (BodyDiode)
            {
                bool n = Polarity == Polarity.N;
                var diode = new Diode(Id + "#body", n ? Source : Drain, n ? Drain : Source)
                {
                    SaturationCurrent = 1e-11,
                    Ideality = 1.2,
                    SeriesResistance = 0.02
                };
                circuit.Add(diode);
            }

            if (GateSourceCapacitance > 0.0)
            {
                circuit.Add(new Capacitor(Id + "#cgs", Gate, Source, GateSourceCapacitance));
            }

            if (GateDrainCapacitance > 0.0)
            {
                circuit.Add(new Capacitor(Id + "#cgd", Gate, Drain, GateDrainCapacitance));
            }
        }

        public override void Reset()
        {
            _vgsIter = _vdsIter = _vgsAcc = _vdsAcc = 0.0;
            Thermal?.Reset(25.0);
            DrainCurrent = Vgs = Vds = Power = 0.0;
        }

        /// <summary>A replacement part: cold, undamaged.</summary>
        public override void Repair()
        {
            base.Repair();
            Thermal?.Reset(25.0);
        }

        public override void BeginStep(StepInfo step)
        {
            _vgsIter = _vgsAcc;
            _vdsIter = _vdsAcc;
        }

        private void Evaluate(double vgs, double vds, out double id, out double gm, out double gds)
        {
            bool reverse = vds < 0.0;
            double vg = reverse ? vgs - vds : vgs;
            double vd = reverse ? -vds : vds;
            double vov = vg - Threshold;
            double k = Transconductance;

            // Positive temperature coefficient of the channel resistance.
            if (Thermal != null)
            {
                double t = Thermal.TemperatureC + AnalogMath.ZeroCelsius;
                k *= Math.Pow(t / (25.0 + AnalogMath.ZeroCelsius), -1.5);
            }

            double i, m, d;
            if (vov <= 0.0)
            {
                i = m = d = 0.0;
            }
            else if (vd < vov)
            {
                double lam = 1.0 + ChannelModulation * vd;
                double core = vov * vd - 0.5 * vd * vd;
                i = k * core * lam;
                m = k * vd * lam;
                d = k * (vov - vd) * lam + k * core * ChannelModulation;
            }
            else
            {
                double lam = 1.0 + ChannelModulation * vd;
                i = 0.5 * k * vov * vov * lam;
                m = k * vov * lam;
                d = 0.5 * k * vov * vov * ChannelModulation;
            }

            if (reverse)
            {
                id = -i;
                gm = -m;
                gds = m + d;
            }
            else
            {
                id = i;
                gm = m;
                gds = d;
            }
        }

        public override void Stamp(StampContext ctx)
        {
            // The gate is insulated; a leakage path keeps a floating gate solvable.
            ctx.AddConductance(Gate, Source, ctx.Gmin);

            if (Failure == FailureMode.Short)
            {
                ctx.AddConductance(Drain, Source, 20.0);
                ctx.AddConductance(Gate, Source, 0.01);
                return;
            }

            if (Failure == FailureMode.Open)
            {
                ctx.AddConductance(Drain, Source, ctx.Gmin);
                return;
            }

            double p = (double)(int)Polarity;
            double vgs = p * ctx.V(Gate, Source);
            double vds = p * ctx.V(Drain, Source);

            bool limited = false;
            double dvgs = vgs - _vgsIter;
            if (Math.Abs(dvgs) > 1.5)
            {
                vgs = _vgsIter + Math.Sign(dvgs) * 1.5;
                limited = true;
            }

            double dvds = vds - _vdsIter;
            double maxDvds = Math.Max(4.0, Math.Abs(_vdsIter));
            if (Math.Abs(dvds) > maxDvds)
            {
                vds = _vdsIter + Math.Sign(dvds) * maxDvds;
                limited = true;
            }

            if (limited) ctx.LimitingApplied = true;
            _vgsIter = vgs;
            _vdsIter = vds;

            Evaluate(vgs, vds, out double id, out double gm, out double gds);
            ctx.AddTransconductance(Drain, Source, Gate, Source, gm);
            ctx.AddConductance(Drain, Source, gds + ctx.Gmin);
            ctx.AddCurrent(Drain, Source, p * (id - gm * vgs - gds * vds));
        }

        public override void Accept(AcceptContext ctx)
        {
            double p = (double)(int)Polarity;
            Vgs = ctx.V(Gate, Source);
            Vds = ctx.V(Drain, Source);
            _vgsAcc = p * Vgs;
            _vdsAcc = p * Vds;

            if (Failure == FailureMode.Short)
            {
                DrainCurrent = Vds * 20.0;
            }
            else if (Failure == FailureMode.Open)
            {
                DrainCurrent = 0.0;
            }
            else
            {
                Evaluate(_vgsAcc, _vdsAcc, out double id, out _, out _);
                DrainCurrent = p * id;
            }

            Power = Math.Abs(Vds * DrainCurrent);
            if (ctx.IsDc) return;
            Thermal?.Advance(Power, ctx.Dt, ctx.AmbientTempC);
            if (!ctx.EnableDamage || IsFailed) return;

            if (Thermal != null && Thermal.TemperatureC >= MaxJunctionTempC)
            {
                Fail(FailureMode.Short, $"jonksiyon aşırı ısındı ({Thermal.TemperatureC:F0} °C, {Power:F1} W)", ctx);
            }
            else if (MaxGateSourceVoltage > 0.0 && Math.Abs(Vgs) > MaxGateSourceVoltage * 1.25)
            {
                Fail(FailureMode.Short, $"gate oksidi delindi (Vgs = {Vgs:F0} V)", ctx);
            }
            else if (MaxDrainSourceVoltage > 0.0 && p * Vds > MaxDrainSourceVoltage * 1.2)
            {
                Fail(FailureMode.Short, $"drain-source çığ kırılması {Vds:F0} V'ta", ctx);
            }
        }

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "ID", DrainCurrent);
            sink.Write(Id, "VGS", Vgs);
            sink.Write(Id, "VDS", Vds);
            sink.Write(Id, "P", Power);
            if (Thermal != null) sink.Write(Id, "T", Thermal.TemperatureC);
        }
    }
}
