using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [pneu] A double-acting pneumatic cylinder with its 5/2 solenoid valve: two air chambers on either side of the piston,
    /// filled and emptied through the valve's ports, the piston rod a body of its own on a slider in the cylinder.
    /// </summary>
    public sealed class MachineCylinder
    {
        internal MachineCylinder(WorldObject o) => Object = o;

        public WorldObject Object { get; }

        internal SceneBody Rod = null!;
        internal SceneJoint Slide = null!;
        internal Resistor? Coil;
        internal DigitalPin? ExtendedPin, RetractedPin;
        internal double Bore, RodDiameter, Stroke, Supply, Conductance, ExhaustShare, DeadVolume, SealFriction, CoilVolts;
        internal double MassA, MassB, TempA, TempB, LastX = double.NaN, RatedSupply;
        internal (MachineFaults.Fault Fault, double At, double Value)? Fault;   // [fault]
        internal bool FaultSaid;

        /// <summary>[pneu] The second coil of a double-solenoid valve (SOL2: back); null on a single-coil valve.</summary>
        internal Resistor? Coil2;

        /// <summary>[pneu] The valve has two coils and no spring: it stays where it was last sent.</summary>
        public bool Bistable { get; internal set; }

        /// <summary>[pneu] The compressor it draws from (null: a supply that never runs out).</summary>
        public MachineCompressor? Source { get; internal set; }

        internal string SourceName = string.Empty;

        /// <summary>[pneu] The supply pressure it gets now, absolute (Pa): its regulator's, or less when the tank has run down.</summary>
        public double SupplyPressure => Supply;

        /// <summary>The valve sends the supply to the cap end (the piston goes out).</summary>
        public bool Energised { get; internal set; }

        /// <summary>Absolute pressures in the cap-end (A) and rod-end (B) chambers (Pa).</summary>
        public double PressureA { get; internal set; }

        public double PressureB { get; internal set; }

        /// <summary>Piston position from the retracted end (m) and speed (m/s).</summary>
        public double Position => Slide.Angle;

        public double Speed { get; internal set; }

        /// <summary>The net air force on the piston (N, + extends).</summary>
        public double Force { get; internal set; }

        /// <summary>The end-position reed switches.</summary>
        public bool Extended { get; internal set; }

        public bool Retracted { get; internal set; }

        /// <summary>Air drawn from the supply since the start (normal litres, at 1.013 bar and 20 °C).</summary>
        public double AirUsed { get; internal set; }

        internal double AreaA => Math.PI / 4 * Bore * Bore;

        internal double AreaB => Math.PI / 4 * (Bore * Bore - RodDiameter * RodDiameter);
    }

    /// <summary>
    /// [pneu] A compressor with its tank: the air in the tank is a mass of gas at room temperature (a steel tank's walls keep
    /// it there), its pressure that mass's. The pressure switch starts the motor at the lower pressure and stops it at the
    /// upper; running, the pump delivers its free air (its rating falls a little as the pressure it pumps against rises).
    /// The cylinders that name it as their air take what they use from the tank.
    /// </summary>
    public sealed class MachineCompressor
    {
        internal MachineCompressor(WorldObject o) => Object = o;

        public WorldObject Object { get; }

        internal double Volume, OnPressure, OffPressure, Delivery, Mass;
        internal double Drawn;   // kg the cylinders took since the last step
        internal DigitalPin? RunPin, PressurePin;

        /// <summary>Tank pressure, absolute (Pa).</summary>
        public double Pressure { get; internal set; }

        /// <summary>The motor runs (the pressure switch has cut in).</summary>
        public bool Running { get; internal set; }

        /// <summary>Seconds the motor has run, and seconds since the start (its duty).</summary>
        public double RunSeconds { get; internal set; }

        public double Seconds { get; internal set; }

        /// <summary>Air pumped and air drawn since the start (normal litres).</summary>
        public double AirPumped { get; internal set; }

        public double AirDrawn { get; internal set; }

        /// <summary>Times the pressure switch started the motor.</summary>
        public int Starts { get; internal set; }

        public double MotorWatts { get; internal set; }
    }

    /// <summary>
    /// [pneu] Pneumatic cylinders (Design/CellParts.cs): the air in each chamber is a gas of its own, its mass and temperature
    /// followed through every exchange (ISO 6358 flow through the valve: choked below the critical pressure ratio, elliptic
    /// above; filling heats it, expansion cools it, the wall brings it back to the room). The pressures push the piston
    /// through a slider whose seals rub; the end stops are the slider's limits. The coil is a real load on its circuit and
    /// switches the valve above 70 % of its rated voltage, as a solenoid pulls in.
    /// </summary>
    public sealed partial class WorldPhysics
    {
        private const double AirR = 287.05, AirGamma = 1.4, Atmosphere = 101325, RoomK = 293.15, NormalDensity = 1.204;

        private readonly List<MachineCylinder> _cylinders = new List<MachineCylinder>();

        public IReadOnlyList<MachineCylinder> Cylinders => _cylinders;

        public MachineCylinder? CylinderOf(string objectId) => _cylinders.FirstOrDefault(c => c.Object.Id == objectId);

        private readonly List<MachineCompressor> _compressors = new List<MachineCompressor>();

        public IReadOnlyList<MachineCompressor> Compressors => _compressors;

        public MachineCompressor? CompressorOf(string objectId) => _compressors.FirstOrDefault(c => c.Object.Id == objectId);

        private void BuildCompressor(WorldObject o, Dictionary<string, string> p)
        {
            var c = new MachineCompressor(o)
            {
                Volume = Math.Max(0.5, CellParts.Num(p, "tank", 24)) / 1000,
                OnPressure = Atmosphere + Math.Max(0, CellParts.Num(p, "on", 6)) * 1e5,
                // free air delivery: normal litres a minute, as a mass flow
                Delivery = Math.Max(0, CellParts.Num(p, "fad", 60)) / 60 / 1000 * NormalDensity,
                MotorWatts = Math.Max(0, CellParts.Num(p, "motor_w", 750)),
            };
            c.OffPressure = Math.Max(c.OnPressure + 0.2e5, Atmosphere + Math.Max(0, CellParts.Num(p, "off", 8)) * 1e5);
            c.Pressure = Atmosphere + Math.Max(0, CellParts.Num(p, "start", 8)) * 1e5;
            c.Mass = c.Pressure * c.Volume / (AirR * RoomK);
            var pins = IoPins(o);
            c.RunPin = pins.GetValueOrDefault("RUN");
            c.PressurePin = pins.GetValueOrDefault("PS");
            _compressors.Add(c);
        }

        /// <summary>The cylinders that name a compressor as their air find it (by the object's name, or its id).</summary>
        private void LinkAir()
        {
            foreach (var cy in _cylinders)
            {
                if (cy.SourceName.Length == 0) continue;
                cy.Source = _compressors.FirstOrDefault(c => string.Equals(c.Object.Name, cy.SourceName, StringComparison.OrdinalIgnoreCase))
                            ?? _compressors.FirstOrDefault(c => string.Equals(c.Object.Id, cy.SourceName, StringComparison.OrdinalIgnoreCase));
                if (cy.Source == null) MachineSay(cy.Object.Id, $"Hava kaynağı '{cy.SourceName}' adlı kompresör bulunamadı: sabit besleme kullanılıyor", true);
            }
        }

        private void StepCompressor(MachineCompressor c, double dt)
        {
            c.Seconds += dt;
            c.Mass = Math.Max(Atmosphere * c.Volume / (AirR * RoomK), c.Mass - c.Drawn);
            c.AirDrawn += c.Drawn / NormalDensity * 1000;
            c.Drawn = 0;
            c.Pressure = c.Mass * AirR * RoomK / c.Volume;
            if (c.Running && c.Pressure >= c.OffPressure)
            {
                c.Running = false;
                MachineSay(c.Object.Id, $"Kompresör durdu: tank {(c.Pressure - Atmosphere) / 1e5:0.0} bar", false);
            }
            else if (!c.Running && c.Pressure <= c.OnPressure)
            {
                c.Running = true;
                c.Starts++;
                MachineSay(c.Object.Id, $"Kompresör çalıştı: tank {(c.Pressure - Atmosphere) / 1e5:0.0} bar", false);
            }

            if (c.Running)
            {
                // a piston pump's delivery falls with the pressure it works against (volumetric efficiency)
                double gauge = Math.Max(0, (c.Pressure - Atmosphere) / 1e5);
                double flow = c.Delivery * Math.Max(0.3, 1 - 0.03 * gauge);
                c.Mass += flow * dt;
                c.AirPumped += flow * dt / NormalDensity * 1000;
                c.RunSeconds += dt;
                c.Pressure = c.Mass * AirR * RoomK / c.Volume;
            }

            Drive(c.RunPin, c.Running);
            Drive(c.PressurePin, c.Pressure > c.OnPressure);
        }

        private void BuildCylinder(WorldObject o, Item it, Dictionary<string, string> p)
        {
            var cy = new MachineCylinder(o)
            {
                Bore = Math.Max(0.006, CellParts.Num(p, "bore", 20) / 1000),
                Stroke = Math.Max(0.005, CellParts.Num(p, "stroke", 100) / 1000),
                Supply = Atmosphere + Math.Max(0, CellParts.Num(p, "supply", 6)) * 1e5,
                // ISO 6358 sonic conductance, given in L/(s·bar) as catalogues do.
                Conductance = Math.Max(1e-3, CellParts.Num(p, "flow", 0.4)) * 1e-3 / 1e5,
                ExhaustShare = Math.Max(0.02, Math.Min(1, CellParts.Num(p, "speed", 100) / 100)),
                CoilVolts = Math.Max(1, CellParts.Num(p, "coil_v", 24))
            };
            cy.RatedSupply = cy.Supply;
            cy.Bistable = p.TryGetValue("valve", out var valve) && valve.Trim() == CellParts.ValveDouble;
            cy.SourceName = p.TryGetValue("air", out var air) ? air.Trim() : string.Empty;
            cy.Fault = MachineFaults.Of(o);
            cy.RodDiameter = Math.Min(cy.Bore * 0.8, Math.Max(0.003, CellParts.Num(p, "rod", 8) / 1000));
            cy.DeadVolume = cy.AreaA * 0.004 + 1.5e-6;   // 4 mm of end space and the port's tubing
            cy.SealFriction = 0.03 * (cy.Supply - Atmosphere) * cy.AreaA + 3;   // seals: a few % of the full force

            // The rod: a steel bar with the piston, sliding along the part's +X out of the front face.
            var frame = FrameOf(o.Id);
            var g = CellGeometry.For(o);
            double front = g?.Max.X ?? 0.08, axisZ = g != null ? (g.Min.Z + g.Max.Z) / 2 : cy.Bore;
            double rodLength = cy.Stroke + 0.03;
            double rodMass = 7850 * Math.PI / 4 * cy.RodDiameter * cy.RodDiameter * rodLength + 7850 * cy.AreaA * 0.008;
            var rodCentre = frame.Apply(new Vec3(front + 0.02 - rodLength / 2, 0, axisZ));
            var rod = SceneBody.Cylinder(o.Id + ":rod", cy.RodDiameter / 2, rodLength, rodMass, rodCentre, "steel");
            rod.Orientation = frame.Q * Quat.FromAxisAngle(Vec3.UnitY, Math.PI / 2);   // the cylinder's z along the part's x
            Scene.AddBody(rod);
            Scene.NoCollide.Add((it.Body, rod));
            var slide = Scene.AddJoint(JointKind.Slider, it.Body, rod, rodCentre, frame.ApplyVector(Vec3.UnitX), o.Id + ":slide");
            slide.Lower = 0;
            slide.Upper = cy.Stroke;
            cy.Rod = rod;
            cy.Slide = slide;

            // Both chambers start at room pressure and temperature.
            cy.TempA = cy.TempB = RoomK;
            cy.MassA = Atmosphere * cy.DeadVolume / (AirR * RoomK);
            cy.MassB = Atmosphere * (cy.DeadVolume + cy.AreaB * cy.Stroke) / (AirR * RoomK);
            cy.PressureA = cy.PressureB = Atmosphere;
            cy.Retracted = true;   // it starts at the back: only a return is announced

            if (_circuit?.ComponentRef(o.Id) is { } r)
            {
                cy.Coil = _circuit.Compiled.DevicesOf(r).OfType<Resistor>().FirstOrDefault(d => d.Id.EndsWith(".COIL", StringComparison.OrdinalIgnoreCase));
                if (cy.Bistable) cy.Coil2 = _circuit.Compiled.DevicesOf(r).OfType<Resistor>().FirstOrDefault(d => d.Id.EndsWith(".COIL2", StringComparison.OrdinalIgnoreCase));
                var pins = IoPins(o);
                cy.ExtendedPin = pins.GetValueOrDefault("EXT");
                cy.RetractedPin = pins.GetValueOrDefault("RET");
            }

            _cylinders.Add(cy);
        }

        /// <summary>ISO 6358 mass flow (kg/s) from <paramref name="up"/> to <paramref name="down"/> (absolute Pa) through conductance C (m³/(s·Pa)).</summary>
        internal static double IsoFlow(double conductance, double up, double down, double upTemp, double critical = 0.3)
        {
            if (up <= down) return 0;
            double r = down / up;
            double q = conductance * up * NormalDensity * Math.Sqrt(RoomK / Math.Max(50, upTemp));
            if (r <= critical) return q;
            double x = (r - critical) / (1 - critical);
            return q * Math.Sqrt(Math.Max(0, 1 - x * x));
        }

        private void StepCylinder(MachineCylinder cy, AnalogSimulator? sim, double dt)
        {
            // The coil pulls the spool in above 70 % of its rated voltage (and drops out below 40 %).
            string? fault = FaultNow(cy.Object, cy.Fault, ref cy.FaultSaid);
            double volts = cy.Coil != null && sim != null ? Math.Abs(sim.NodeVoltage(cy.Coil.A) - sim.NodeVoltage(cy.Coil.B)) : 0;
            if (fault != "valve_stuck")   // a stuck spool stays where it was, whatever the coil does
            {
                if (cy.Bistable)
                {
                    // two coils, no spring: a coil that pulls in sends the spool its way; with both or neither it stays put
                    double back = cy.Coil2 != null && sim != null ? Math.Abs(sim.NodeVoltage(cy.Coil2.A) - sim.NodeVoltage(cy.Coil2.B)) : 0;
                    bool out_ = volts >= 0.7 * cy.CoilVolts, in_ = back >= 0.7 * cy.CoilVolts;
                    if (out_ && !in_) cy.Energised = true;
                    else if (in_ && !out_) cy.Energised = false;
                }
                else if (volts >= 0.7 * cy.CoilVolts) cy.Energised = true;
                else if (volts <= 0.4 * cy.CoilVolts) cy.Energised = false;
            }

            // [pneu] the supply: the compressor's tank through the cylinder's regulator, or a supply that never runs out
            double rated = cy.Source != null ? Math.Min(cy.RatedSupply, cy.Source.Pressure) : cy.RatedSupply;
            cy.Supply = fault == "low_supply" ? Math.Min(rated, Atmosphere + Math.Max(0, cy.Fault!.Value.Value) * 1e5) : rated;
            double leak = fault == "leak" ? Math.Max(0, cy.Fault!.Value.Value) * 1e-3 / 1e5 : 0;   // a worn seal: cap end to atmosphere

            // The piston's travel this step, from where it was to where the slider put it (the joint's own speed is the
            // solver's prediction before the end stops act, not how far it went).
            double now = Math.Max(0, Math.Min(cy.Stroke, cy.Slide.Angle));
            double x = double.IsNaN(cy.LastX) ? now : cy.LastX;
            double v = dt > 0 ? (now - x) / dt : 0;
            cy.LastX = now;
            cy.Speed = v;
            const int sub = 10;
            double h = dt / sub;
            double cv = AirR / (AirGamma - 1), cp = AirGamma * cv;
            double wallRate = 4.0;   // 1/s: how fast the chamber walls bring the air back to room temperature
            for (int k = 0; k < sub; k++)
            {
                double va = cy.DeadVolume + cy.AreaA * x, vb = cy.DeadVolume + cy.AreaB * (cy.Stroke - x);
                double pa = cy.MassA * AirR * cy.TempA / va, pb = cy.MassB * AirR * cy.TempB / vb;
                // 5/2 valve: energised, supply → A and B → exhaust; at rest, supply → B and A → exhaust (through the throttle).
                double inA, inB, outA, outB;
                if (cy.Energised)
                {
                    inA = IsoFlow(cy.Conductance, cy.Supply, pa, RoomK);
                    outA = IsoFlow(cy.Conductance, pa, cy.Supply, cy.TempA);   // back to supply if A is above it (never, in practice)
                    inB = 0;
                    outB = IsoFlow(cy.Conductance * cy.ExhaustShare, pb, Atmosphere, cy.TempB);
                }
                else
                {
                    inB = IsoFlow(cy.Conductance, cy.Supply, pb, RoomK);
                    outB = IsoFlow(cy.Conductance, pb, cy.Supply, cy.TempB);
                    inA = 0;
                    outA = IsoFlow(cy.Conductance * cy.ExhaustShare, pa, Atmosphere, cy.TempA);
                }

                outA += IsoFlow(leak, pa, Atmosphere, cy.TempA);
                cy.AirUsed += (inA + inB) * h / NormalDensity * 1000;
                if (cy.Source != null) cy.Source.Drawn += (inA + inB) * h;   // [pneu] out of the tank
                // Energy of each chamber: enthalpy in and out, the piston's work, the wall's heat.
                double dva = cy.AreaA * v, dvb = -cy.AreaB * v;
                double ua = cy.MassA * cv * cy.TempA + (inA * cp * RoomK - outA * cp * cy.TempA - pa * dva) * h;
                double ub = cy.MassB * cv * cy.TempB + (inB * cp * RoomK - outB * cp * cy.TempB - pb * dvb) * h;
                cy.MassA = Math.Max(1e-12, cy.MassA + (inA - outA) * h);
                cy.MassB = Math.Max(1e-12, cy.MassB + (inB - outB) * h);
                cy.TempA = Math.Max(150, ua / (cy.MassA * cv));
                cy.TempB = Math.Max(150, ub / (cy.MassB * cv));
                cy.TempA += (RoomK - cy.TempA) * Math.Min(1, wallRate * h);
                cy.TempB += (RoomK - cy.TempB) * Math.Min(1, wallRate * h);
                x = Math.Max(0, Math.Min(cy.Stroke, x + v * h));
            }

            double fa = cy.MassA * AirR * cy.TempA / (cy.DeadVolume + cy.AreaA * x);
            double fb = cy.MassB * AirR * cy.TempB / (cy.DeadVolume + cy.AreaB * (cy.Stroke - x));
            cy.PressureA = fa;
            cy.PressureB = fb;
            // The air pushes the piston; the atmosphere pushes back on the rod's own section.
            cy.Force = fa * cy.AreaA - fb * cy.AreaB - Atmosphere * (cy.AreaA - cy.AreaB);
            cy.Slide.MotorForce = cy.Force;
            cy.Slide.Friction = cy.SealFriction;

            bool extended = cy.Slide.Angle >= cy.Stroke - 0.002, retracted = cy.Slide.Angle <= 0.002;
            if (extended && !cy.Extended) MachineSay(cy.Object.Id, "Silindir ileride", false);
            if (retracted && !cy.Retracted) MachineSay(cy.Object.Id, "Silindir geride", false);
            cy.Extended = extended;
            cy.Retracted = retracted;
            Drive(cy.ExtendedPin, extended);
            Drive(cy.RetractedPin, retracted);
        }
    }
}
