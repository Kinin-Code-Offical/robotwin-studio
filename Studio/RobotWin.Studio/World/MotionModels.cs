using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media.Media3D;
using RobotWin.Studio.Models3D;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [stepper] World models of the stepper motor (its shaft turns, the shaft's tip is its port) and the A4988 carrier
    /// (each header pin a cable end), from Models3D/GenStepper.cs.
    /// </summary>
    public static class MotionModels
    {
        private const double Mm = 0.001;

        private static readonly (string Pin, string Label, double Y)[] Coil =
        {
            ("A+", "A+ (siyah)", -3.0), ("A-", "A− (yeşil)", -1.0), ("B+", "B+ (kırmızı)", 1.0), ("B-", "B− (mavi)", 3.0)
        };

        private static readonly string[] Left = { "EN", "MS1", "MS2", "MS3", "RST", "SLP", "STEP", "DIR" };
        private static readonly string[] Right = { "VMOT", "GND", "2B", "2A", "1A", "1B", "VDD", "GND2" };

        private static Model3DGroup Scaled(ComponentInstance inst)
        {
            var g = new Model3DGroup { Transform = new MatrixTransform3D(new Matrix3D(Mm, 0, 0, 0, 0, Mm, 0, 0, 0, 0, Mm, 0, 0, 0, 0, 1)) };
            g.Children.Add(inst.Root);
            return g;
        }

        public static ObjectModel? TryBuild(RobotTwin.CoreSim.Sandbox.WorldObject o, RobotTwin.CoreSim.Design.PartLibrary library)
        {
            if (o.PartId is not (RobotTwin.CoreSim.Design.MotionParts.StepperId or RobotTwin.CoreSim.Design.MotionParts.A4988Id)) return null;
            var def = library.Find(o.PartId);
            if (def == null) return null;
            var preset = o.PresetId != null ? library.FindPreset(o.PresetId) : null;
            return TryBuild(o.PartId, library.ResolveParameters(def, preset, o.Parameters));
        }

        public static ObjectModel? TryBuild(string partId, IReadOnlyDictionary<string, string> p)
        {
            switch (partId)
            {
                case RobotTwin.CoreSim.Design.MotionParts.StepperId:
                {
                    double length = p.TryGetValue("length", out var ls) && double.TryParse(ls, NumberStyles.Float, CultureInfo.InvariantCulture, out double l) && l > 5 ? l : 40;
                    var body = ComponentModels.Template(new ModelRequest("stepper-body", length.ToString(CultureInfo.InvariantCulture), b => Gen.StepperBody(b, length))).Instantiate();
                    var shaft = ComponentModels.Template(new ModelRequest("stepper-shaft", length.ToString(CultureInfo.InvariantCulture), b => Gen.StepperShaft(b, length))).Instantiate();
                    var m = new ObjectModel();
                    m.Body.Children.Add(Scaled(body));
                    var moving = new Model3DGroup();
                    moving.Children.Add(Scaled(shaft));
                    m.Moving = moving;
                    m.Port = new PortFrame("shaft", "Motor mili", new Point3D(0, 0, (length + 24) * Mm), new Vector3D(0, 0, 1));
                    double cap = Math.Min(8.5, length * 0.25);
                    foreach (var (pin, label, y) in Coil) m.Pins.Add(new PinAnchor(pin, label, new Point3D(27.0 * Mm, y * Mm, cap / 2 * Mm)));
                    m.Bounds = new Rect3D(-21.15 * Mm, -21.15 * Mm, 0, 48.0 * Mm, 42.3 * Mm, (length + 24) * Mm);
                    m.CollisionBounds = new Rect3D(-21.15 * Mm, -21.15 * Mm, 0, 42.3 * Mm, 42.3 * Mm, (length + 2) * Mm);
                    return m;
                }

                case RobotTwin.CoreSim.Design.MotionParts.A4988Id:
                {
                    var inst = ComponentModels.Template(new ModelRequest("a4988", "pololu", b => Gen.A4988Carrier(b))).Instantiate();
                    var m = new ObjectModel();
                    m.Body.Children.Add(Scaled(inst));
                    for (int k = 0; k < 8; k++)
                    {
                        double y = 8.89 - 2.54 * k;
                        m.Pins.Add(new PinAnchor(Left[k], Left[k], new Point3D(-6.35 * Mm, y * Mm, 10.4 * Mm)));
                        m.Pins.Add(new PinAnchor(Right[k], Right[k] == "GND2" ? "GND" : Right[k], new Point3D(6.35 * Mm, y * Mm, 10.4 * Mm)));
                    }

                    m.Bounds = new Rect3D(-7.62 * Mm, -10.16 * Mm, 0, 15.24 * Mm, 20.32 * Mm, 15.0 * Mm);
                    return m;
                }

                default:
                    return null;
            }
        }
    }
}
