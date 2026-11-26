using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Analysis.Fem;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [thermal] Every part of the world has a temperature. Its electrical losses heat it (a stalled
    /// servo, a motor winding I²R, a regulator, resistors, LEDs…); heat conducts through joints into
    /// whatever is screwed or glued to it; convection (faster when air flows past) and radiation cool
    /// it. The materials respond: plastics soften around their glass transition (PLA ~60 °C), lose
    /// strength (joint break forces scale with it) and melt (a melted part lets go of its joints).
    /// </summary>
    public sealed partial class WorldPhysics
    {
        private sealed class ThermalNode
        {
            public Item Item = null!;
            public StructuralMaterial Material = null!;
            public double Capacity;          // J/K
            public double Area;              // m² exposed
            public double T;                 // °C
            public double Power;             // W heating it right now
            public List<AnalogDevice> Devices = new List<AnalogDevice>();
            public string State = "katı";
            public bool Melted;

            /// <summary>What this node is inside its part ("dişli kutusu", "motor gövdesi", "sargı"…); empty for a one-piece part.</summary>
            public string Label = string.Empty;

            /// <summary>An inner node (a winding, a can inside a gearbox): no joints, no structural response.</summary>
            public bool Inner;

            /// <summary>Extra air speed over this node's surface (m/s), e.g. an outrunner's spinning bell.</summary>
            public double SelfAir;

            /// <summary>Conductance to the ambient air at the last step (W/K).</summary>
            public double GAmbient;
        }

        private sealed class ThermalLink
        {
            public ThermalNode A = null!, B = null!;
            public double G;                 // W/K
        }

        /// <summary>
        /// [thermal] A motor seen as the parts heat really flows through: the winding (copper loss at its
        /// hot resistance), the can or stator it sits in, and — on a yellow TT gear motor — the plastic
        /// gearbox that the mounting screws actually go through. Friction losses heat the can (brushes,
        /// bearings) and the gearbox (gear mesh).
        /// </summary>
        private sealed class MotorThermal
        {
            public DcMotor Motor = null!;
            public ThermalNode Winding = null!, Can = null!, Mount = null!;
            public double GearShare;          // share of the friction loss that is in the gear train
            public double BellRadius;         // outrunner: the rotating bell cools itself
        }

        private readonly Dictionary<string, ThermalNode> _thermal = new Dictionary<string, ThermalNode>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ThermalNode> _thermalNodes = new List<ThermalNode>();
        private readonly List<ThermalLink> _thermalLinks = new List<ThermalLink>();
        private readonly List<MotorThermal> _motorThermal = new List<MotorThermal>();
        private readonly List<(Battery Battery, ThermalNode Node)> _batteryThermal = new List<(Battery, ThermalNode)>();
        private readonly Dictionary<SceneJoint, double> _baseBreak = new Dictionary<SceneJoint, double>();
        private double _thermalPending;
        private double _ambient = 25;

        /// <summary>Thermal network time step (s): heat moves slowly; 20 Hz is plenty and cheap.</summary>
        public const double ThermalStep = 0.05;

        /// <summary>Screwed / clamped contact conductance between parts (W/m²K) and the share of the smaller footprint in contact.</summary>
        public const double ContactConductance = 400, ContactShare = 0.15;

        private void BuildThermal(WorldDocument world)
        {
            _thermal.Clear();
            _thermalNodes.Clear();
            _thermalLinks.Clear();
            _motorThermal.Clear();
            _batteryThermal.Clear();
            _ambient = world.Environment.AmbientC;
            foreach (var it in _items.Values)
            {
                if (it.Body == null) continue;
                var o = it.Object;
                var mat = StructuralMaterial.For(o.Kind == WorldObjectKind.Board ? "FR4" : o.Material);
                mat.Emissivity = SurfaceFinish.EmissivityOf(o, mat.Emissivity);   // [surface] polished, anodised, painted…
                var size = SizeOf(it.Body);
                double area = 2 * (size.X * size.Y + size.Y * size.Z + size.Z * size.X);
                double mass = it.Body.IsStatic ? mat.Density * Math.Max(1e-7, size.X * size.Y * size.Z) : it.Body.Mass;
                var node = new ThermalNode { Item = it, Material = mat, Capacity = Math.Max(0.05, mass * mat.SpecificHeat), Area = Math.Max(1e-5, area), T = Param(o, "temperature", _ambient) };
                if (_circuit != null)
                {
                    string prefix = WorldCircuit.Prefix(o.Id);
                    foreach (var kv in _circuit.Compiled.DevicesByPart)
                        if (kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) node.Devices.AddRange(kv.Value);
                }

                _thermal[o.Id] = node;
                _thermalNodes.Add(node);
                if (node.Devices.OfType<DcMotor>().FirstOrDefault() is { } motor) SplitMotor(o, node, motor, mass);
                if (node.Devices.OfType<Battery>().FirstOrDefault() is { } battery)
                {
                    // A cell stack: ~1 J/gK whatever the wrapping is.
                    node.Capacity = Math.Max(0.05, mass * 1000);
                    battery.Thermal.Driven = true;
                    battery.Thermal.Set(node.T);
                    _batteryThermal.Add((battery, node));
                }
            }

            foreach (var j in world.Joints)
            {
                if (j.ObjectB == null || !_thermal.TryGetValue(j.ObjectA, out var a) || !_thermal.TryGetValue(j.ObjectB, out var b)) continue;
                double fa = Footprint(a), fb = Footprint(b);
                var fastening = j.Kind == WorldJointKind.Fixed && j.Fastening.Length > 0 ? Fastening.For(j.Fastening) : null;   // [fasten] glue conducts far better than screw heads
                _thermalLinks.Add(new ThermalLink { A = a, B = b, G = (fastening?.Conductance ?? ContactConductance) * (fastening?.ContactShare ?? ContactShare) * Math.Min(fa, fb) });
            }

            foreach (var sj in Scene.Joints)
                if (!double.IsInfinity(sj.BreakForce)) _baseBreak[sj] = sj.BreakForce;
        }

        /// <summary>The motor's inner nodes by its construction (see <see cref="MotorThermal"/>).</summary>
        private void SplitMotor(WorldObject o, ThermalNode mount, DcMotor motor, double mass)
        {
            string model = ((o.Parameters.TryGetValue("model", out var m) ? m : string.Empty) + " " + (o.PresetId ?? o.PartId ?? string.Empty)).ToUpperInvariant();
            bool outrunner = model.Contains("KV") || model.Contains("BRUSHLESS") || model.Contains("BLDC") || model.Contains("2204") || model.Contains("2212");
            bool coreless = model.Contains("8520") || model.Contains("0720") || model.Contains("CORELESS");
            bool metalGear = model.Contains("N20") || model.Contains("25GA") || model.Contains("JGA") || model.Contains("370");
            bool tt = !outrunner && !coreless && !metalGear;
            var copper = StructuralMaterial.For("BAKIR");
            var steel = StructuralMaterial.For("ÇELİK");
            var mt = new MotorThermal { Motor = motor, Mount = mount };
            ThermalNode Inner(string label, StructuralMaterial mat, double kg, double cp, double areaM2) =>
                new ThermalNode { Item = mount.Item, Material = mat, Capacity = Math.Max(0.02, kg * cp), Area = areaM2, T = mount.T, Label = label, Inner = true };
            void Link(ThermalNode a, ThermalNode b, double g) => _thermalLinks.Add(new ThermalLink { A = a, B = b, G = g });

            if (outrunner)
            {
                // Stator (copper + laminations) pressed onto the aluminium base the screws go into; the bell around it.
                mount.Material = StructuralMaterial.For("ALÜMİNYUM");
                mount.Label = "gövde (alüminyum)";
                double stator = 0.45 * mass;
                mount.Capacity = Math.Max(0.05, (mass - stator) * mount.Material.SpecificHeat);
                mt.Winding = Inner("stator sargısı", copper, stator, 420, 1e-5);
                mt.Can = mount;
                Link(mt.Winding, mount, 0.6 * mass / 0.028);
                mt.BellRadius = 0.0135;
            }
            else if (tt)
            {
                // Yellow TT (or a bare 130 can): a steel can clipped into an ABS gearbox; the screws hold the gearbox.
                mount.Material = StructuralMaterial.For("ABS");
                mount.Label = "dişli kutusu (ABS)";
                mount.Capacity = Math.Max(0.05, 0.53 * mass * mount.Material.SpecificHeat);
                mount.Area *= 0.7;
                mt.Can = Inner("motor gövdesi (çelik)", steel, 0.33 * mass, 470, 1.9e-3);
                mt.Winding = Inner("sargı", copper, 0.14 * mass, 420, 1e-5);
                Link(mt.Winding, mt.Can, 0.12);   // ≈ 8 K/W winding to can, a 130-size motor
                Link(mt.Can, mount, 0.02);        // through the plastic clip and bosses: k(ABS) = 0.17 W/mK
                mt.GearShare = 0.5;
            }
            else
            {
                // Coreless or metal gear motor: the can / steel gearbox is what is clamped.
                mount.Material = steel;
                mount.Label = coreless ? "motor gövdesi (çelik)" : "gövde + metal dişli kutusu";
                double w = (coreless ? 0.15 : 0.12) * mass;
                mount.Capacity = Math.Max(0.02, (mass - w) * steel.SpecificHeat);
                mt.Winding = Inner("sargı", copper, w, 420, 1e-5);
                mt.Can = mount;
                Link(mt.Winding, mount, coreless ? 0.04 : 0.10 * Math.Max(0.3, mass / 0.03));   // ≈ 25 K/W in an 8520, ≈ 10 K/W in a 25GA
                mt.GearShare = metalGear ? 0.5 : 0;
            }

            if (mt.Can != mount) _thermalNodes.Add(mt.Can);
            _thermalNodes.Add(mt.Winding);
            motor.Thermal.Driven = true;
            motor.Thermal.Set(mt.Winding.T);
            motor.DriveCaseTemperature(mt.Can.T);   // [motor-thermal] the magnets sit in the can
            _motorThermal.Add(mt);
        }

        private static Vec3 SizeOf(SceneBody body)
        {
            var solid = Analysis.Geometry.Solid.FromShape(body.EffectiveShape);
            var size = solid?.Bounds.Size ?? new Vec3(0.02, 0.02, 0.02);
            return new Vec3(Math.Max(1e-3, size.X), Math.Max(1e-3, size.Y), Math.Max(1e-3, size.Z));
        }

        private static double Footprint(ThermalNode n)
        {
            var s = SizeOf(n.Item.Body);
            return Math.Max(s.X * s.Y, Math.Max(s.Y * s.Z, s.Z * s.X));
        }

        /// <summary>Temperature of an object (°C): its hottest outer surface (a TT motor's can, not its gearbox); ambient when unknown.</summary>
        public double TemperatureOf(string objectId)
        {
            if (!_thermal.TryGetValue(objectId, out var n)) return _ambient;
            double t = n.T;
            foreach (var mt in _motorThermal)
                if (mt.Mount == n) t = Math.Max(t, mt.Can.T);
            return t;
        }

        /// <summary>Every thermal node of an object with its temperature (gearbox, can, winding …).</summary>
        public IReadOnlyList<(string Label, double T)> PartTemperatures(string objectId)
        {
            var list = new List<(string, double)>();
            if (!_thermal.TryGetValue(objectId, out var n)) return list;
            list.Add((n.Label.Length > 0 ? n.Label : n.Material.Name, n.T));
            foreach (var mt in _motorThermal)
            {
                if (mt.Mount != n) continue;
                if (mt.Can != n) list.Add((mt.Can.Label, mt.Can.T));
                list.Add((mt.Winding.Label, mt.Winding.T));
            }

            return list;
        }

        /// <summary>Temperature of the part's mounting face (what its screws touch: a TT's gearbox), °C.</summary>
        public double MountTemperatureOf(string objectId) => _thermal.TryGetValue(objectId, out var n) ? n.T : _ambient;

        /// <summary>Heat currently generated in an object, all its nodes together (W).</summary>
        public double HeatOf(string objectId)
        {
            if (!_thermal.TryGetValue(objectId, out var n)) return 0;
            double p = n.Power;
            foreach (var mt in _motorThermal)
                if (mt.Mount == n) p += mt.Winding.Power + (mt.Can != n ? mt.Can.Power : 0);
            return p;
        }

        /// <summary>Material state at the object's temperature ("katı", "yumuşamaya yaklaşıyor", "yumuşadı", "eriyor").</summary>
        public string MaterialStateOf(string objectId) => _thermal.TryGetValue(objectId, out var n) ? n.State : "katı";

        /// <summary>The structural material of an object at its current temperature.</summary>
        public StructuralMaterial? MaterialOf(string objectId) => _thermal.TryGetValue(objectId, out var n) ? n.Material : null;

        /// <summary>Air speed over a part's surface (m/s): its motion through the wind plus any propeller slipstream it sits in.</summary>
        public double AirSpeedOver(string objectId) => _thermal.TryGetValue(objectId, out var n) ? AirSpeedAt(n) : 0;

        private double AirSpeedAt(ThermalNode n)
        {
            var body = n.Item.Body;
            var p = body.Position;
            double v = body.IsStatic ? Scene.WindAt(p).Length : (body.Velocity - Scene.WindAt(p)).Length;
            double rho = Scene.AirDensity;
            if (rho <= 0) return 0;
            foreach (var pr in Scene.Propellers)
            {
                if (pr.Body == null || pr.Detached || Math.Abs(pr.Thrust) < 1e-4) continue;
                var axis = pr.Body.Orientation.Rotate(pr.Axis).Normalized();
                var dir = axis * -Math.Sign(pr.Thrust);   // the slipstream leaves against the thrust
                double r = pr.Diameter / 2, area = Math.PI * r * r;
                double vi = Math.Sqrt(Math.Abs(pr.Thrust) / (2 * rho * area));   // induced velocity at the disc
                var d = p - pr.Body.Position;
                double along = d.Dot(dir);
                if (along < -0.02 || along > 8 * pr.Diameter) continue;
                double radial = (d - dir * along).Length;
                if (radial > 1.1 * r) continue;
                // Actuator disc: v_i at the disc, rising to 2·v_i in the contracted wake.
                double slip = vi * (1 + Math.Max(0, along) / Math.Sqrt(along * along + r * r));
                v = Math.Max(v, slip);
            }

            return v + n.SelfAir;
        }

        private void StepThermal(double dt, AnalogSimulator? sim)
        {
            if (_thermal.Count == 0) return;
            _thermalPending += dt;
            if (_thermalPending < ThermalStep) return;
            double h = _thermalPending;
            _thermalPending = 0;
            if (_fastened.Count > 0) WeakenFastenings();   // [fasten] from the temperatures of the last thermal step

            foreach (var n in _thermalNodes) n.Power = sim == null || n.Inner ? 0 : HeatOf(n, sim);
            if (sim != null)
            {
                foreach (var mt in _motorThermal)
                {
                    mt.Winding.Power = mt.Motor.CopperLoss;
                    double friction = mt.Motor.FrictionLoss;
                    mt.Can.Power += friction * (1 - mt.GearShare);
                    mt.Mount.Power += friction * mt.GearShare + mt.Motor.GearLoss;   // [phys] a loaded gear train heats its gearbox
                    if (mt.BellRadius > 0) mt.Mount.SelfAir = 0.3 * Math.Abs(mt.Motor.AngularVelocity) * mt.BellRadius;
                }

                foreach (var (b, n) in _batteryThermal)
                    n.Power += Math.Abs(b.Current) * (Math.Abs(b.Current) * b.SeriesResistance + Math.Abs(b.PolarizationVoltage));
            }

            if (_vessels.Count > 0)
                foreach (var n in _thermalNodes)
                    if (!n.Inner) n.Power += TakeGasHeat(n.Item.Object.Id, h) + TakeJetHeat(n.Item.Object.Id, h);   // [gas] hot gas heats the vessel wall, a jet what it hits

            // Convection (natural → forced by the real air speed) and linearised radiation to ambient.
            const double sigma = 5.670374419e-8;
            foreach (var n in _thermalNodes)
            {
                double v = AirSpeedAt(n);
                double hc = Math.Max(6, 10.45 - v + 10 * Math.Sqrt(v));
                double tk = n.T + 273.15, ta = _ambient + 273.15;
                double hr = n.Material.Emissivity * sigma * (tk * tk + ta * ta) * (tk + ta);
                n.GAmbient = (hc + hr) * n.Area;
            }

            // Backward Euler on the network, Gauss–Seidel sweeps (unconditionally stable).
            for (int sweep = 0; sweep < 3; sweep++)
            {
                foreach (var n in _thermalNodes)
                {
                    double num = n.Capacity / h * n.T + n.Power + n.GAmbient * _ambient, den = n.Capacity / h + n.GAmbient;
                    foreach (var l in _thermalLinks)
                    {
                        if (l.A == n)
                        {
                            num += l.G * l.B.T;
                            den += l.G;
                        }
                        else if (l.B == n)
                        {
                            num += l.G * l.A.T;
                            den += l.G;
                        }
                    }

                    n.T = num / den;
                }
            }

            foreach (var mt in _motorThermal)
            {
                mt.Motor.Thermal.Set(mt.Winding.T);   // the copper resistance follows the winding
                mt.Motor.DriveCaseTemperature(mt.Can.T);   // [motor-thermal] the magnets' flux the can
            }
            foreach (var (b, n) in _batteryThermal) b.Thermal.Set(n.T);
            foreach (var n in _thermalNodes)
                if (!n.Inner) Respond(n);
        }

        /// <summary>Heat produced inside an object by its devices (W); motors and batteries are handled with their inner nodes.</summary>
        private double HeatOf(ThermalNode n, AnalogSimulator sim)
        {
            double p = 0;
            foreach (var d in n.Devices)
            {
                switch (d)
                {
                    case DcMotor:
                    case Battery:
                        break;
                    case Servo s:
                        // Micro servos turn ~85 % of their input into heat (motor, gears, driver); at stall all of it.
                        if (_circuit != null && _circuit.ComponentRef(n.Item.Object.Id) is { } r)
                        {
                            double v = _circuit.Compiled.PinVoltage(sim, r, "V+") - _circuit.Compiled.PinVoltage(sim, r, "GND");
                            p += 0.85 * Math.Max(0, v) * Math.Abs(s.SupplyCurrent);
                        }

                        break;
                    default:
                        p += ReportedLoss(d);
                        break;
                }
            }

            return Math.Max(0, p);
        }

        private readonly LossSink _lossSink = new LossSink();

        private double ReportedLoss(AnalogDevice d)
        {
            _lossSink.Power = 0;
            d.Report(_lossSink);
            return _lossSink.Power;
        }

        /// <summary>Collects the "P" / "PLOSS" signals a device reports (its dissipation).</summary>
        private sealed class LossSink : ISignalSink
        {
            public double Power;

            public void Write(string device, string key, double value)
            {
                if ((key == "P" || key == "PLOSS") && double.IsFinite(value)) Power += Math.Abs(value);
            }
        }

        /// <summary>Material response to the new temperature: softening events, weaker joints, melting.</summary>
        private void Respond(ThermalNode n)
        {
            string state = n.Material.StateAt(n.T);
            var o = n.Item.Object;
            if (state != n.State)
            {
                string mat = n.Material.Name;
                string msg = state switch
                {
                    "yumuşamaya yaklaşıyor" => $"{o.Name} ({mat}) {n.T:0} °C: camsı geçişe ({n.Material.GlassTransition:0} °C) yaklaşıyor, sertliği %{n.Material.ModulusFactor(n.T) * 100:0}'e düştü",
                    "yumuşadı" => $"{o.Name} ({mat}) {n.T:0} °C: camsı geçişi aştı ve yumuşadı (sertlik %{n.Material.ModulusFactor(n.T) * 100:0}); vidalı/geçme bağlantıları zayıfladı",
                    "eriyor" => $"{o.Name} ({mat}) {n.T:0} °C: eriyor — bağlantıları artık tutmuyor",
                    _ => $"{o.Name} ({mat}) soğudu: {n.T:0} °C"
                };
                bool severe = state == "yumuşadı" || state == "eriyor";
                Scene.Report(new PhysicsEvent(Scene.Time, o.Id, severe ? PhysicsEventKind.BodyBroken : PhysicsEventKind.Impact, msg, n.T, severe));
                n.State = state;
            }

            double factor = n.Material.YieldFactor(n.T);
            foreach (var sj in Scene.Joints)
            {
                if (sj.A != n.Item.Body && sj.B != n.Item.Body) continue;
                if (_baseBreak.TryGetValue(sj, out double baseForce)) sj.BreakForce = baseForce * Math.Max(0.01, factor);
                if (state == "eriyor" && !n.Melted && sj.Enabled)
                {
                    sj.Enabled = false;
                    Scene.Report(new PhysicsEvent(Scene.Time, o.Id, PhysicsEventKind.JointBroken, $"{o.Name}: eriyen malzeme bağlantıyı bıraktı", n.T, true));
                }
            }

            if (state == "eriyor") n.Melted = true;
        }
    }
}
