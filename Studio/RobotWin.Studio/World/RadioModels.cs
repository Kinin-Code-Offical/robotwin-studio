using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Design;
using M = RobotWin.Studio.Assembly3D.Mesh;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [radio] Procedural 3D models of the wireless / USB catalogue parts (controller, PC with
    /// USB port, router, beacon, IR remote, IR receiver, DRV8833), in millimetres like
    /// ObjectModels. Called from ObjectModels.BuildComponent's default branch.
    /// </summary>
    public static class RadioModels
    {
        private const double Mm = 0.001;

        /// <summary>Extra live materials of a model (lightbar of a controller, status LED of a router).</summary>
        public sealed class Extras
        {
            public EmissiveMaterial? Lightbar;

            /// <summary>Where the USB cable leaves the PC (object-local, m).</summary>
            public Point3D? UsbPort;
        }

        private static readonly ConditionalWeakTable<ObjectModel, Extras> ExtraTable = new ConditionalWeakTable<ObjectModel, Extras>();

        public static Extras? ExtrasOf(ObjectModel m) => ExtraTable.TryGetValue(m, out var e) ? e : null;

        private static Material Mat(Color c, double spec = 0.15, EmissiveMaterial? glow = null)
        {
            var g = new MaterialGroup();
            g.Children.Add(new DiffuseMaterial(new SolidColorBrush(c)));
            if (glow != null) g.Children.Add(glow);
            if (spec > 0) g.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb((byte)(spec * 255), 255, 255, 255)), 40));
            return g;
        }

        private static void Box(Model3DGroup g, double cx, double cy, double cz, double sx, double sy, double sz, Color c, double spec = 0.15, EmissiveMaterial? glow = null) =>
            g.Children.Add(M.Model(M.Box(cx * Mm, cy * Mm, cz * Mm, sx * Mm, sy * Mm, sz * Mm), Mat(c, spec, glow)));

        private static void Cyl(Model3DGroup g, double r, double len, int axis, double cx, double cy, double cz, Color c, double spec = 0.2, EmissiveMaterial? glow = null) =>
            g.Children.Add(M.Model(M.Cylinder(r * Mm, len * Mm, axis, 24, cx * Mm, cy * Mm, cz * Mm), Mat(c, spec, glow)));

        private static void Ball(Model3DGroup g, double r, double cx, double cy, double cz, Color c, double spec = 0.25, EmissiveMaterial? glow = null) =>
            g.Children.Add(M.Model(M.Sphere(r * Mm, cx * Mm, cy * Mm, cz * Mm, 16), Mat(c, spec, glow)));

        public static bool TryBuild(ObjectModel m, PartDefinition def, IReadOnlyDictionary<string, string> p)
        {
            switch (def.Id)
            {
                case WirelessParts.GamepadId:
                    Gamepad(m, p.TryGetValue("model", out var model) ? model : "DualSense");
                    return true;
                case WirelessParts.UsbHostId:
                    Laptop(m);
                    return true;
                case WirelessParts.RouterId:
                    Router(m);
                    return true;
                case WirelessParts.BeaconId:
                    Cyl(m.Body, 15, 6, 2, 0, 0, 3, Color.FromRgb(0xF2, 0xF2, 0xF4), 0.3, m.Heat);
                    Cyl(m.Body, 4, 1, 2, 0, 0, 6.4, Color.FromRgb(0x3E, 0x8E, 0xF7), 0.4, new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x10, 0x40, 0x90))));
                    m.Bounds = new Rect3D(-15 * Mm, -15 * Mm, 0, 30 * Mm, 30 * Mm, 7 * Mm);
                    return true;
                case WirelessParts.IrRemoteId:
                    IrRemote(m);
                    return true;
                case WirelessParts.IrReceiverId:
                    Tsop(m);
                    return true;
                case WirelessParts.Drv8833Id:
                    Drv8833(m, def);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>PS5-style controller (≈ 160 × 66 mm), white shell, black centre, lightbar around the touchpad.</summary>
        private static void Gamepad(ObjectModel m, string model)
        {
            bool xbox = model.Contains("Xbox", StringComparison.OrdinalIgnoreCase);
            var shell = xbox ? Color.FromRgb(0x24, 0x24, 0x28) : Color.FromRgb(0xEE, 0xEE, 0xF0);
            var black = Color.FromRgb(0x1A, 0x1A, 0x1E);
            Box(m.Body, 0, 6, 12, 110, 46, 20, shell, 0.35, m.Heat);                  // centre body
            Cyl(m.Body, 21, 22, 2, -52, -14, 11, shell, 0.35, m.Heat);               // grips
            Cyl(m.Body, 21, 22, 2, 52, -14, 11, shell, 0.35, m.Heat);
            Ball(m.Body, 18, -55, -32, 10, shell);
            Ball(m.Body, 18, 55, -32, 10, shell);
            Box(m.Body, 0, -6, 20, 66, 30, 6, black, 0.3);                            // black centre face
            Box(m.Body, 0, 16, 23.2, 44, 22, 1.2, Color.FromRgb(0x2A, 0x2A, 0x30), 0.5);   // touchpad
            var glow = new EmissiveMaterial(new SolidColorBrush(Colors.Black));   // [phys] lit only by the running world
            Box(m.Body, -23.5, 16, 23.4, 1.8, 20, 1.2, Color.FromRgb(0x20, 0x20, 0x24), 0.2, glow);   // lightbar strips
            Box(m.Body, 23.5, 16, 23.4, 1.8, 20, 1.2, Color.FromRgb(0x20, 0x20, 0x24), 0.2, glow);
            Cyl(m.Body, 8, 6, 2, -20, -12, 24, black, 0.3);                           // sticks
            Cyl(m.Body, 8, 6, 2, 20, -12, 24, black, 0.3);
            Cyl(m.Body, 4, 8, 2, -20, -12, 27, Color.FromRgb(0x30, 0x30, 0x34), 0.3);
            Cyl(m.Body, 4, 8, 2, 20, -12, 27, Color.FromRgb(0x30, 0x30, 0x34), 0.3);
            foreach (var (x, y, c) in new[] { (52.0, 10.0, Color.FromRgb(0x3C, 0xC8, 0xA0)), (62.0, 0.0, Color.FromRgb(0xE5, 0x48, 0x4D)), (52.0, -10.0, Color.FromRgb(0x5A, 0x8C, 0xF0)), (42.0, 0.0, Color.FromRgb(0xE0, 0x7C, 0xC8)) })
            {
                Cyl(m.Body, 4.2, 3, 2, x, y, 22.5, c, 0.4);                          // face buttons
            }

            Box(m.Body, -52, 0, 22.5, 18, 5, 3, black, 0.3);                          // D-pad
            Box(m.Body, -52, 0, 22.5, 5, 18, 3, black, 0.3);
            Box(m.Body, -44, 26, 22, 22, 8, 6, black, 0.3);                           // L1 / R1
            Box(m.Body, 44, 26, 22, 22, 8, 6, black, 0.3);
            m.Bounds = new Rect3D(-75 * Mm, -52 * Mm, 0, 150 * Mm, 84 * Mm, 30 * Mm);
            ExtraTable.AddOrUpdate(m, new Extras { Lightbar = glow });
        }

        /// <summary>Laptop (300 × 205 mm) with the USB port on its right side.</summary>
        private static void Laptop(ObjectModel m)
        {
            var alu = Color.FromRgb(0x9A, 0x9E, 0xA6);
            Box(m.Body, 0, 0, 8, 300, 205, 16, alu, 0.4, m.Heat);
            Box(m.Body, 0, 20, 16.4, 270, 100, 0.8, Color.FromRgb(0x2A, 0x2C, 0x30), 0.2);   // keyboard
            Box(m.Body, 0, -60, 16.4, 100, 55, 0.6, Color.FromRgb(0x84, 0x88, 0x90), 0.5);   // touchpad
            // Screen opened ~105°, leaning back.
            var screen = new Model3DGroup();
            Box(screen, 0, 0, 105, 300, 6, 200, alu, 0.4);
            var display = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x18, 0x30, 0x58)));
            Box(screen, 0, -3.2, 107, 280, 0.6, 175, Color.FromRgb(0x08, 0x0A, 0x10), 0.6, display);
            var tilt = new Transform3DGroup();
            tilt.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), -15), new Point3D(0, 0, 0)));
            tilt.Children.Add(new TranslateTransform3D(0, 100 * Mm, 16 * Mm));
            screen.Transform = tilt;
            m.Body.Children.Add(screen);
            Box(m.Body, 150.5, -30, 8, 2, 12, 4.5, Color.FromRgb(0x10, 0x10, 0x12), 0.2);   // USB-A port
            Box(m.Body, 150.5, -50, 8, 2, 9, 3.2, Color.FromRgb(0x10, 0x10, 0x12), 0.2);    // USB-C port
            m.Bounds = new Rect3D(-150 * Mm, -103 * Mm, 0, 302 * Mm, 210 * Mm, 215 * Mm);
            ExtraTable.AddOrUpdate(m, new Extras { UsbPort = new Point3D(152 * Mm, -30 * Mm, 8 * Mm) });
        }

        private static void Router(ObjectModel m)
        {
            var body = Color.FromRgb(0x1E, 0x1E, 0x22);
            Box(m.Body, 0, 0, 15, 180, 120, 30, body, 0.3, m.Heat);
            foreach (double x in new[] { -70.0, 0.0, 70.0 })
            {
                Cyl(m.Body, 4, 150, 2, x, 55, 105, body, 0.3);   // antennas
            }

            var led = new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x20, 0xA0, 0x40)));
            for (int i = 0; i < 4; i++) Box(m.Body, -40 + i * 12, -60.5, 22, 5, 1, 2, Color.FromRgb(0x10, 0x40, 0x10), 0.2, led);
            m.Bounds = new Rect3D(-90 * Mm, -60 * Mm, 0, 180 * Mm, 125 * Mm, 180 * Mm);
        }

        /// <summary>TV-style remote (140 × 40 × 18 mm), IR LED at the +X end.</summary>
        private static void IrRemote(ObjectModel m)
        {
            Box(m.Body, 0, 0, 9, 140, 40, 18, Color.FromRgb(0x1C, 0x1C, 0x20), 0.25, m.Heat);
            for (int r = 0; r < 7; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    Box(m.Body, -45 + r * 14, -11 + c * 11, 18.6, 9, 7, 1.4, r == 0 && c == 1 ? Color.FromRgb(0xC0, 0x30, 0x30) : Color.FromRgb(0x5A, 0x5A, 0x62), 0.2);
                }
            }

            Ball(m.Body, 3, 70, 0, 9, Color.FromRgb(0x40, 0x30, 0x50), 0.6);   // IR LED
            m.Bounds = new Rect3D(-70 * Mm, -20 * Mm, 0, 143 * Mm, 40 * Mm, 20 * Mm);
        }

        /// <summary>TSOP38238: black epoxy body with a dome lens facing +X, three legs (OUT, GND, VS).</summary>
        private static void Tsop(ObjectModel m)
        {
            var black = Color.FromRgb(0x18, 0x18, 0x1C);
            Box(m.Body, 0, 0, 10, 4, 6, 7, black, 0.3, m.Heat);
            Ball(m.Body, 2.6, 2.2, 0, 10.5, Color.FromRgb(0x24, 0x20, 0x2C), 0.6);
            string[] pins = { "OUT", "GND", "VS" };
            for (int i = 0; i < 3; i++)
            {
                double y = (i - 1) * 2.54;
                Box(m.Body, 0, y, 3.3, 0.5, 0.45, 6.6, Color.FromRgb(0xC8, 0xCA, 0xCF), 0.7);
                m.Pins.Add(new PinAnchor(pins[i], pins[i] + (i == 0 ? " (çıkış)" : i == 1 ? " (toprak)" : " (besleme)"), new Point3D(0, y * Mm, 0.5 * Mm)));
            }

            m.Bounds = new Rect3D(-2 * Mm, -4 * Mm, 0, 7 * Mm, 8 * Mm, 14 * Mm);
        }

        /// <summary>DRV8833 breakout (≈ 18 × 26 mm) with a 2 × 5 header.</summary>
        private static void Drv8833(ObjectModel m, PartDefinition def)
        {
            Box(m.Body, 0, 0, 0.8, 26, 18, 1.6, Color.FromRgb(0xB0, 0x20, 0x30), 0.1, m.Heat);
            Box(m.Body, 0, 0, 2.2, 6, 5, 1.2, Color.FromRgb(0x18, 0x18, 0x1B), 0.3);
            int i = 0;
            foreach (var pin in def.Pins)
            {
                bool left = i < 6;
                double x = left ? -11.5 : 11.5;
                double y = left ? (i - 2.5) * 2.54 : (i - 6 - 1.5) * 2.54;
                Box(m.Body, x, y, 4.6, 0.64, 0.64, 6, Color.FromRgb(0xD9, 0xB0, 0x4C), 0.7);
                m.Pins.Add(new PinAnchor(pin.Name, pin.Name, new Point3D(x * Mm, y * Mm, 7.6 * Mm)));
                i++;
            }

            m.Bounds = new Rect3D(-13 * Mm, -9 * Mm, 0, 26 * Mm, 18 * Mm, 9 * Mm);
        }

        /// <summary>USB jack of a board model (object-local, m): middle of its −X edge (Uno USB-B, DevKit micro-USB end).</summary>
        public static Point3D UsbJack(ObjectModel board) =>
            board.Bounds.IsEmpty ? new Point3D(0, 0, 0.005) : new Point3D(board.Bounds.X, board.Bounds.Y + board.Bounds.SizeY / 2, 0.004);
    }
}
