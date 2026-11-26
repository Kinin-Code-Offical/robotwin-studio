using System;
using System.Collections.Generic;
using System.Globalization;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [cforce] Unity's Constant Force: a push (N) and a twist (N·m) that act on a part on every physics step, along
    /// the world's axes or the part's own ("relative" in Unity: a rocket's thrust along its nose). The push acts at the
    /// part's centre of mass, so it moves the part without turning it; a part fixed on another carries the other
    /// along through the joint as a real one would. Parameters: "cforce" =
    /// "x,y,z" (also the component's switch), "cforce.local" = "1" for the part's axes, "ctorque" = "x,y,z",
    /// "ctorque.local".
    /// </summary>
    public sealed partial class WorldPhysics
    {
        public const string ConstantForceKey = "cforce";
        public const string ConstantForceLocalKey = "cforce.local";
        public const string ConstantTorqueKey = "ctorque";
        public const string ConstantTorqueLocalKey = "ctorque.local";

        private sealed class ConstantPush
        {
            public Item Item = null!;
            public Vec3 Force, Torque;
            public bool ForceLocal, TorqueLocal;
        }

        private List<ConstantPush>? _constantPushes;

        /// <summary>Reads "x,y,z" (invariant numbers; spaces or semicolons also separate); false when it is not three numbers.</summary>
        public static bool TryVector(string? text, out Vec3 v)
        {
            v = Vec3.Zero;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var parts = text!.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) return false;
            var n = new double[3];
            for (int i = 0; i < 3; i++)
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i]) || double.IsNaN(n[i]) || double.IsInfinity(n[i])) return false;
            v = new Vec3(n[0], n[1], n[2]);
            return true;
        }

        /// <summary>Writes a vector as the parameter text ("x,y,z").</summary>
        public static string VectorText(Vec3 v) =>
            string.Join(",", v.X.ToString("0.######", CultureInfo.InvariantCulture), v.Y.ToString("0.######", CultureInfo.InvariantCulture), v.Z.ToString("0.######", CultureInfo.InvariantCulture));

        private void BuildConstantForces()
        {
            _constantPushes = null;
            foreach (var it in _items.Values)
            {
                var o = it.Object;
                if (it.Body == null || !o.Parameters.TryGetValue(ConstantForceKey, out var fs)) continue;
                TryVector(fs, out var f);
                o.Parameters.TryGetValue(ConstantTorqueKey, out var ts);
                TryVector(ts, out var t);
                if (f.Length < 1e-12 && t.Length < 1e-12) continue;
                (_constantPushes ??= new List<ConstantPush>()).Add(new ConstantPush
                {
                    Item = it, Force = f, Torque = t, ForceLocal = Flag(o, ConstantForceLocalKey), TorqueLocal = Flag(o, ConstantTorqueLocalKey)
                });
            }
        }

        private void StepConstantForces()
        {
            if (_constantPushes == null) return;
            foreach (var c in _constantPushes)
            {
                var body = c.Item.Body;
                if (body.IsStatic || PoseOf(c.Item.Object.Id) is not { } pose) continue;
                if (c.Force.Length > 0) body.AddForce(c.ForceLocal ? pose.Orientation.Rotate(c.Force) : c.Force);   // at its centre of mass, as in Unity
                if (c.Torque.Length > 0) body.AddTorque(c.TorqueLocal ? pose.Orientation.Rotate(c.Torque) : c.Torque);
            }
        }
    }
}
