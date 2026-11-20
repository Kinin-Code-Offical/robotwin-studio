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
        }

        private sealed class ThermalLink
        {
            public ThermalNode A = null!, B = null!;
            public double G;                 // W/K
        }

        private readonly Dictionary<string, ThermalNode> _thermal = new Dictionary<string, ThermalNode>(StringComparer.OrdinalIgnoreCase);
        private readonly List<ThermalLink> _thermalLinks = new List<ThermalLink>();
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
            _thermalLinks.Clear();
            _ambient = world.Environment.AmbientC;
            foreach (var it in _items.Values)
            {
                if (it.Body == null) continue;
                var o = it.Object;
                var mat = StructuralMaterial.For(o.Kind == WorldObjectKind.Board ? "FR4" : o.Material);
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
            }

            foreach (var j in world.Joints)
            {
                if (j.ObjectB == null || !_thermal.TryGetValue(j.ObjectA, out var a) || !_thermal.TryGetValue(j.ObjectB, out var b)) continue;
                double fa = Footprint(a), fb = Footprint(b);
                _thermalLinks.Add(new ThermalLink { A = a, B = b, G = ContactConductance * ContactShare * Math.Min(fa, fb) });
            }

            foreach (var sj in Scene.Joints)
                if (!double.IsInfinity(sj.BreakForce)) _baseBreak[sj] = sj.BreakForce;
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

        /// <summary>Temperature of an object (°C); ambient when unknown.</summary>
        public double TemperatureOf(string objectId) => _thermal.TryGetValue(objectId, out var n) ? n.T : _ambient;

        /// <summary>Heat currently generated in an object (W).</summary>
        public double HeatOf(string objectId) => _thermal.TryGetValue(objectId, out var n) ? n.Power : 0;

        /// <summary>Material state at the object's temperature ("katı", "yumuşamaya yaklaşıyor", "yumuşadı", "eriyor").</summary>
        public string MaterialStateOf(string objectId) => _thermal.TryGetValue(objectId, out var n) ? n.State : "katı";

        /// <summary>The structural material of an object at its current temperature.</summary>
        public StructuralMaterial? MaterialOf(string objectId) => _thermal.TryGetValue(objectId, out var n) ? n.Material : null;

        private void StepThermal(double dt, AnalogSimulator? sim)
        {
            if (_thermal.Count == 0) return;
            _thermalPending += dt;
            if (_thermalPending < ThermalStep) return;
            double h = _thermalPending;
            _thermalPending = 0;

            foreach (var n in _thermal.Values) n.Power = sim == null ? 0 : HeatOf(n, sim);

            // Backward Euler on the network, two Gauss–Seidel sweeps (unconditionally stable).
            const double sigma = 5.670374419e-8;
            for (int sweep = 0; sweep < 2; sweep++)
            {
                foreach (var n in _thermal.Values)
                {
                    var body = n.Item.Body;
                    double v = body.IsStatic ? 0 : (body.Velocity - Scene.WindAt(body.Position)).Length;
                    double hc = Math.Max(6, 10.45 - v + 10 * Math.Sqrt(v));   // natural → forced convection (W/m²K)
                    double tk = n.T + 273.15, ta = _ambient + 273.15;
                    double hr = n.Material.Emissivity * sigma * (tk * tk + ta * ta) * (tk + ta);   // linearised radiation
                    double gAmb = (hc + hr) * n.Area;
                    double num = n.Capacity / h * n.T + n.Power + gAmb * _ambient, den = n.Capacity / h + gAmb;
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

            foreach (var n in _thermal.Values) Respond(n);
        }

        /// <summary>Heat produced inside an object by its devices (W).</summary>
        private double HeatOf(ThermalNode n, AnalogSimulator sim)
        {
            double p = 0;
            foreach (var d in n.Devices)
            {
                switch (d)
                {
                    case DcMotor m:
                        p += m.Current * m.Current * m.Resistance;   // winding copper loss
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
