using System.Windows.Media.Media3D;
using RobotWin.Studio.Models3D;

namespace RobotWin.Studio.World
{
    /// <summary>[smps] World models of the switching regulator modules (Models3D/GenPower.cs): each pad a cable end.</summary>
    public static class PowerModels
    {
        private const double Mm = 0.001;

        private static readonly string[] Pads = { "IN+", "IN-", "OUT+", "OUT-" };

        public static ObjectModel? TryBuild(RobotTwin.CoreSim.Sandbox.WorldObject o, RobotTwin.CoreSim.Design.PartLibrary library)
        {
            if (!RobotTwin.CoreSim.Design.PowerParts.IsModule(o.PartId)) return null;
            var def = library.Find(o.PartId!);
            if (def == null) return null;
            var preset = o.PresetId != null ? library.FindPreset(o.PresetId) : null;
            var p = library.ResolveParameters(def, preset, o.Parameters);
            string model = p.TryGetValue("model", out var m) && !string.IsNullOrWhiteSpace(m) ? m : "LM2596";
            var inst = Instance(model);
            var g = new Model3DGroup { Transform = new MatrixTransform3D(new Matrix3D(Mm, 0, 0, 0, 0, Mm, 0, 0, 0, 0, Mm, 0, 0, 0, 0, 1)) };
            g.Children.Add(inst.Root);
            var om = new ObjectModel { Instance = inst };
            om.Body.Children.Add(g);
            var (l, w, pads) = Gen.RegulatorBoard(model);
            for (int k = 0; k < Pads.Length; k++)
                om.Pins.Add(new PinAnchor(Pads[k], Pads[k], new Point3D(pads[k].X * Mm, pads[k].Y * Mm, 1.7 * Mm)));
            om.Bounds = new Rect3D(-l / 2 * Mm, -w / 2 * Mm, 0, l * Mm, w * Mm, 14 * Mm);
            return om;
        }

        /// <summary>A module's model in its own frame (mm).</summary>
        public static ComponentInstance Instance(string model) =>
            ComponentModels.Template(new ModelRequest("regulator-module", model, b => Gen.RegulatorModule(b, model))).Instantiate();
    }
}
