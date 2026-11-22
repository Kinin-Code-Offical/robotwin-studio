using System;
using System.Collections.Generic;
using System.Globalization;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [gas] What a hot jet does to whatever it hits, and the geometry the scene and the replay need to draw it.
    /// The exhaust heats the first surface in its path that is not part of the vessel's own assembly (a launch pad,
    /// a test-stand plate, a wall): forced convection of an impinging round jet, h = ½·(k/D)·Re^½·Pr^0.4 at the
    /// stagnation region, falling off as 6·D/H beyond the potential core, over the spot the jet has spread to
    /// (≈ 11° half-angle). The heat goes into that part's node of the heat network.
    /// </summary>
    public sealed partial class WorldPhysics
    {
        private readonly Dictionary<string, double> _jetHeat = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Where an object's nozzle exit sits (object frame, origin at the bottom centre) and which way its jet leaves.</summary>
        public static (Vec3 Point, Vec3 Axis) NozzleFrame(WorldObject o)
        {
            var axis = AxisOf(o.Parameters.TryGetValue("nozzle.axis", out var ax) ? ax : null);
            double hz = o.Shape == "sphere" ? o.SizeX : o.SizeZ;
            var face = new Vec3(axis.X * o.SizeX / 2, axis.Y * (o.Shape == "cylinder" ? o.SizeX : o.SizeY) / 2, hz / 2 + axis.Z * hz / 2);
            return (face, axis);
        }

        /// <summary>Nozzle exit diameter (m) from an object's parameters (the throat when the nozzle does not expand).</summary>
        public static double NozzleExitDiameterOf(WorldObject o)
        {
            double Mm(string key) => o.Parameters.TryGetValue(key, out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
            return Math.Max(Mm("nozzle"), Mm("nozzle.exit")) / 1000;
        }

        /// <summary>Total mass (kg) of a body and everything joined to it.</summary>
        public double AssemblyMass(SceneBody body)
        {
            var seen = new HashSet<SceneBody> { body };
            var stack = new Stack<SceneBody>();
            stack.Push(body);
            double m = 0;
            while (stack.Count > 0)
            {
                var b = stack.Pop();
                m += b.Mass;
                foreach (var j in Scene.Joints)
                {
                    if (!j.Enabled || j.Broken || j.ParentBody == null) continue;
                    var other = j.ChildBody == b ? j.ParentBody : j.ParentBody == b ? j.ChildBody : null;
                    if (other != null && !other.IsStatic && seen.Add(other)) stack.Push(other);
                }
            }

            return m;
        }

        private void ImpingeJet(Vessel v, Vec3 nozzle, Vec3 jet, double dt)
        {
            if (v.MassFlow <= 1e-6 || v.WaterVolume > 1e-9) return;
            double t0 = v.Temperature;
            if (t0 < 400) return;   // a cold gas jet takes heat rather than gives it; left out
            double de = Math.Sqrt(4 * Math.Max(v.Exit, v.Throat) / Math.PI);
            var own = v.Item.Body;
            if (!Scene.Raycast(nozzle + jet * 1e-4, jet, 40 * de, out var hit, b => b != own && !SameAssembly(own, b), false)) return;
            if (hit.Body?.Tag is not WorldObject target) return;
            var mix = v.Mix;
            double h = Math.Max(de, hit.Distance), ta = _ambient + 273.15;
            double tw = (_thermal.TryGetValue(target.Id, out var tn) ? tn.T : _ambient) + 273.15;
            double decay = Math.Min(1, 6 * de / h);                      // potential core ≈ 6 exit diameters
            double tJet = ta + (0.9 * t0 - ta) * decay;                  // recovery temperature at the stagnation region
            double mu = SolidGrain.Viscosity(mix, t0), pr = SolidGrain.Prandtl(mix), k = mu * mix.Cp / pr;
            double re = 4 * v.MassFlow / (Math.PI * de * mu);
            double coef = 0.5 * k / de * Math.Sqrt(re) * Math.Pow(pr, 0.4) * decay;
            double spot = Math.PI * Math.Pow(de / 2 + 0.2 * h, 2);
            double q = Math.Max(0, coef * spot * (tJet - tw));
            _jetHeat[target.Id] = (_jetHeat.TryGetValue(target.Id, out var j) ? j : 0) + q * dt;
            _jetHeatTotal[target.Id] = (_jetHeatTotal.TryGetValue(target.Id, out var tot) ? tot : 0) + q * dt;
        }

        /// <summary>Heat a jet put into an object since the last thermal step (W over <paramref name="h"/> seconds).</summary>
        private double TakeJetHeat(string objectId, double h)
        {
            if (!_jetHeat.TryGetValue(objectId, out var j) || j <= 0) return 0;
            _jetHeat[objectId] = 0;
            return h > 0 ? j / h : 0;
        }

        /// <summary>Heat (J) a jet has put into an object so far (for tests and the inspector).</summary>
        public double JetHeatTotal(string objectId) => _jetHeatTotal.TryGetValue(objectId, out var j) ? j : 0;

        private readonly Dictionary<string, double> _jetHeatTotal = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }
}
