using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Sensing;
using M = RobotWin.Studio.Assembly3D.Mesh;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [sense] Detailed models of the sensing parts in real dimensions, built so that their
    /// optical points match <see cref="SensingParts"/>: line sensors hang optics-down with the
    /// optical face at z = 0, cameras look along +X, LiDAR heads spin about +Z, encoder motor
    /// shafts point along +Y.
    /// </summary>
    public static class SenseModels
    {
        private const double Mm = 0.001;
        private static readonly Color Blue = Color.FromRgb(0x1D, 0x4F, 0x9E);
        private static readonly Color Green = Color.FromRgb(0x24, 0x5E, 0x38);
        private static readonly Color Black = Color.FromRgb(0x16, 0x16, 0x19);
        private static readonly Color Chip = Color.FromRgb(0x22, 0x22, 0x25);
        private static readonly Color Gold = Color.FromRgb(0xD9, 0xB0, 0x4C);
        private static readonly Color Metal = Color.FromRgb(0xB8, 0xBC, 0xC4);

        public static ObjectModel? TryBuild(WorldObject o, PartLibrary library)
        {
            if (o.Kind != WorldObjectKind.Component || o.PartId == null) return null;
            var def = library.Find(o.PartId);
            if (def == null) return null;
            var preset = o.PresetId != null ? library.FindPreset(o.PresetId) : null;
            var p = library.ResolveParameters(def, preset, o.Parameters);
            string model = p.TryGetValue("model", out var md) ? md : string.Empty;
            var m = new ObjectModel();
            switch (def.Id)
            {
                case SensingParts.Tcrt5000Id:
                    Tcrt(m);
                    break;
                case SensingParts.Line5Id:
                    Line5(m);
                    break;
                case SensingParts.Qtr8aId:
                case SensingParts.Qtr8rcId:
                    Qtr(m, def.Id == SensingParts.Qtr8rcId);
                    break;
                case SensingParts.CameraId:
                    Camera(m, CameraModels.Find(model).Id);
                    break;
                case SensingParts.LidarId:
                    Lidar(m, LidarSpec.For(model).Id);
                    break;
                case SensingParts.GpsId:
                    Gps(m);
                    break;
                case SensingParts.CompassId:
                    Compass(m);
                    break;
                case SensingParts.EncoderMotorId:
                    EncoderMotor(m, model.Contains("JGA", StringComparison.OrdinalIgnoreCase) || model.Contains("25", StringComparison.Ordinal));
                    break;
                default:
                    return null;
            }

            return m;
        }

        // ================================================================ helpers (millimetres)

        private static Material Mat(Color c, double spec = 0.1, EmissiveMaterial? glow = null)
        {
            var g = new MaterialGroup();
            g.Children.Add(new DiffuseMaterial(new SolidColorBrush(c)));
            if (glow != null) g.Children.Add(glow);
            if (spec > 0) g.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb((byte)(spec * 255), 255, 255, 255)), 40));
            return g;
        }

        private static void Box(Model3DGroup g, double cx, double cy, double cz, double sx, double sy, double sz, Color c, double spec = 0.1, EmissiveMaterial? glow = null) =>
            g.Children.Add(M.Model(M.Box(cx * Mm, cy * Mm, cz * Mm, sx * Mm, sy * Mm, sz * Mm), Mat(c, spec, glow)));

        private static void Cyl(Model3DGroup g, double r, double len, int axis, double cx, double cy, double cz, Color c, double spec = 0.2, int seg = 24, EmissiveMaterial? glow = null) =>
            g.Children.Add(M.Model(M.Cylinder(r * Mm, len * Mm, axis, seg, cx * Mm, cy * Mm, cz * Mm), Mat(c, spec, glow)));

        /// <summary>Straight header pins standing up from <paramref name="z"/>, one anchor per pin at the tip.</summary>
        private static void Header(ObjectModel m, string[] pins, double x0, double y0, double dx, double dy, double z, double up = 8.5)
        {
            for (int i = 0; i < pins.Length; i++)
            {
                double x = x0 + dx * i, y = y0 + dy * i;
                Box(m.Body, x, y, z + 1.25, 2.5, 2.5, 2.5, Black, 0.05);
                Box(m.Body, x, y, z + up / 2, 0.64, 0.64, up, Gold, 0.7);
                m.Pins.Add(new PinAnchor(pins[i], pins[i], new Point3D(x * Mm, y * Mm, (z + up) * Mm)));
            }
        }

        private static Rect3D R(double x0, double y0, double z0, double x1, double y1, double z1) =>
            new Rect3D(x0 * Mm, y0 * Mm, z0 * Mm, (x1 - x0) * Mm, (y1 - y0) * Mm, (z1 - z0) * Mm);

        /// <summary>A TCRT5000 hanging optics-down at (x, y): its lens tips at z = 0, emitter and detector along <paramref name="alongY"/>.</summary>
        private static void TcrtPart(Model3DGroup g, double x, double y, bool alongY)
        {
            double lx = alongY ? 5.8 : 10.2, ly = alongY ? 10.2 : 5.8;
            Box(g, x, y, 1.0 + 3.0, lx, ly, 6.0, Black, 0.15);
            double ox = alongY ? 0 : 2.5, oy = alongY ? 2.5 : 0;
            // Emitter: blue-tinted clear lens; detector: black lens.
            Cyl(g, 2.1, 1.2, 2, x - ox, y - oy, 0.6, Color.FromRgb(0x58, 0x7C, 0xC8), 0.5, 18);
            Cyl(g, 2.1, 1.2, 2, x + ox, y + oy, 0.6, Color.FromRgb(0x0C, 0x0C, 0x0E), 0.5, 18);
        }

        private static void Trimpot(Model3DGroup g, double x, double y, double z)
        {
            Box(g, x, y, z + 2.2, 6.5, 6.5, 4.4, Color.FromRgb(0x2E, 0x6F, 0xD8), 0.2);
            Cyl(g, 1.6, 0.8, 2, x, y, z + 4.8, Color.FromRgb(0xE0, 0xC0, 0x70), 0.5, 14);
        }

        // ================================================================ line sensors

        private static void Tcrt(ObjectModel m)
        {
            const double pcbZ = 7.0;
            Box(m.Body, 0, 0, pcbZ + 0.8, 32, 14, 1.6, Blue, 0.1, m.Heat);
            TcrtPart(m.Body, 11.5, 0, true);
            Box(m.Body, 0, 2, pcbZ + 1.6 + 0.75, 5, 4, 1.5, Chip, 0.3);   // LM393
            Trimpot(m.Body, -5, -3, pcbZ + 1.6);
            var glow = new EmissiveMaterial(new SolidColorBrush(Colors.Black));
            Box(m.Body, 4, -4.5, pcbZ + 1.6 + 0.4, 1.6, 0.8, 0.8, Color.FromRgb(0xE5, 0x48, 0x4D), 0.4, glow);   // signal LED
            m.Lights[string.Empty] = glow;
            Box(m.Body, 7, -4.5, pcbZ + 1.6 + 0.4, 1.6, 0.8, 0.8, Color.FromRgb(0x8A, 0x20, 0x20), 0.4);         // power LED
            Header(m, new[] { "VCC", "GND", "DO", "AO" }, -14, -3.81, 0, 2.54, pcbZ + 1.6);
            m.Bounds = R(-16, -7, 0, 16, 7, pcbZ + 1.6 + 8.5);
            m.CollisionBounds = R(-16, -7, 0, 16, 7, pcbZ + 1.6);
        }

        private static void Line5(ObjectModel m)
        {
            const double pcbZ = 7.0;
            Box(m.Body, 0, 0, pcbZ + 0.8, 20, 80, 1.6, Black, 0.1, m.Heat);
            for (int i = 0; i < 5; i++) TcrtPart(m.Body, 6, (2 - i) * 15, false);
            Box(m.Body, -2, 18, pcbZ + 1.6 + 0.75, 8.7, 4, 1.5, Chip, 0.3);   // LM339
            Trimpot(m.Body, -2, -18, pcbZ + 1.6);
            Header(m, new[] { "VCC", "GND", "OUT1", "OUT2", "OUT3", "OUT4", "OUT5" }, -7, 7.62, 0, -2.54, pcbZ + 1.6);
            m.Bounds = R(-10, -40, 0, 10, 40, pcbZ + 1.6 + 8.5);
            m.CollisionBounds = R(-10, -40, 0, 10, 40, pcbZ + 1.6);
        }

        private static void Qtr(ObjectModel m, bool rc)
        {
            const double pcbZ = 1.7;
            Box(m.Body, 0, 0, pcbZ + 0.8, 12.7, 74.9, 1.6, Green, 0.1, m.Heat);
            for (int i = 0; i < 8; i++)
            {
                double y = (3.5 - i) * 9.525;
                Box(m.Body, 1.5, y, 0.85, 2.9, 3.6, 1.7, Black, 0.2);
                Box(m.Body, 1.5, y - 0.9, 0.05, 1.2, 1.2, 0.1, Color.FromRgb(0x58, 0x7C, 0xC8), 0.5);
                if (rc) Box(m.Body, -2.5, y, pcbZ + 1.6 + 0.4, 1.6, 0.8, 0.8, Color.FromRgb(0x8A, 0x6A, 0x40), 0.2);   // 2.2 nF
            }

            var pins = new List<string> { "VCC", "GND", "LEDON" };
            for (int i = 1; i <= 8; i++) pins.Add("OUT" + i);
            Header(m, pins.ToArray(), -4.6, 12.7, 0, -2.54, pcbZ + 1.6);
            m.Bounds = R(-6.35, -37.45, 0, 6.35, 37.45, pcbZ + 1.6 + 8.5);
            m.CollisionBounds = R(-6.35, -37.45, 0, 6.35, 37.45, pcbZ + 1.6);
        }

        // ================================================================ cameras (look along +X)

        private static void Camera(ObjectModel m, string id)
        {
            var lens = SensingParts.CameraLens(id);
            double lx = lens.X / Mm, lz = lens.Z / Mm;
            var glass = Color.FromRgb(0x10, 0x1A, 0x2C);
            switch (id)
            {
                case "OV7670":
                    Box(m.Body, 0.8, 0, 17, 1.6, 34, 34, Blue, 0.1, m.Heat);
                    Box(m.Body, 4.5, 0, lz, 6, 14, 14, Black, 0.15);
                    Cyl(m.Body, 6, 9, 0, 11.5, 0, lz, Black, 0.2, 28);
                    Cyl(m.Body, 4.2, 1, 0, lx - 0.5, 0, lz, glass, 0.9, 28);
                    Header(m, new[] { "VCC", "GND", "SDA", "SCL" }, -3, -3.81, 0, 2.54, 0, 6);
                    m.Bounds = R(-4.5, -17, 0, lx, 17, 34);
                    break;
                case "USB720":
                    Box(m.Body, 0, 0, 13, 30, 34, 26, Black, 0.1, m.Heat);                     // clip / foot
                    Box(m.Body, 0, 0, lz, 25, 70, 26, Color.FromRgb(0x24, 0x24, 0x28), 0.25);
                    Cyl(m.Body, 7, 2.5, 0, lx - 1.25, 0, lz, Black, 0.4, 28);
                    Cyl(m.Body, 4.5, 0.6, 0, lx - 0.1, 0, lz, glass, 0.9, 28);
                    Header(m, new[] { "VCC", "GND", "SDA", "SCL" }, -14, -3.81, 0, 2.54, 0, 5);
                    m.Bounds = R(-15, -35, 0, lx, 35, lz + 13);
                    break;
                case "IMX219":
                    Box(m.Body, 0.5, 0, 12.5, 1.0, 23.86, 25, Green, 0.1, m.Heat);
                    Box(m.Body, 3.5, 0, lz, 5, 8.5, 8.5, Black, 0.2);
                    Cyl(m.Body, 2.6, 3.5, 0, lx - 1.75, 0, lz, Black, 0.3, 20);
                    Cyl(m.Body, 1.7, 0.4, 0, lx - 0.1, 0, lz, glass, 0.9, 20);
                    Box(m.Body, -0.5, 0, 0.5, 2, 16, 1, Color.FromRgb(0xE8, 0xC8, 0x80), 0.3);   // ribbon
                    Header(m, new[] { "VCC", "GND", "SDA", "SCL" }, -3, -3.81, 0, 2.54, 0, 5);
                    m.Bounds = R(-4.5, -12, 0, lx, 12, 25);
                    break;
                default:
                    // OV2640 on a small carrier (ESP32-CAM style FPC module with an 8 mm lens).
                    Box(m.Body, 0.8, 0, 12, 1.6, 22, 24, Black, 0.1, m.Heat);
                    Box(m.Body, 2.6, 0, lz, 2, 8.5, 8.5, Color.FromRgb(0x30, 0x30, 0x34), 0.3);
                    Cyl(m.Body, 4, 4.5, 0, lx - 2.25, 0, lz, Black, 0.3, 24);
                    Cyl(m.Body, 2.6, 0.4, 0, lx - 0.1, 0, lz, glass, 0.9, 24);
                    Box(m.Body, 0.8, 0, 1.5, 1.8, 10, 3, Color.FromRgb(0xC8, 0x8A, 0x30), 0.3);   // FPC
                    Header(m, new[] { "VCC", "GND", "SDA", "SCL" }, -3, -3.81, 0, 2.54, 0, 5);
                    m.Bounds = R(-4.5, -11, 0, lx, 11, 24);
                    break;
            }

            m.CollisionBounds = new Rect3D(m.Bounds.X + 4 * Mm, m.Bounds.Y, m.Bounds.Z, m.Bounds.SizeX - 4 * Mm, m.Bounds.SizeY, m.Bounds.SizeZ);
        }

        // ================================================================ LiDAR

        private static void Lidar(ObjectModel m, string id)
        {
            switch (id)
            {
                case "VLP16":
                    Cyl(m.Body, 51.65, 15, 2, 0, 0, 7.5, Metal, 0.5, 48);
                    Cyl(m.Body, 51.65, 30, 2, 0, 0, 37.7, Color.FromRgb(0x10, 0x10, 0x12), 0.7, 48);
                    Cyl(m.Body, 51.65, 26.7, 2, 0, 0, 58.35, Metal, 0.5, 48);
                    Header(m, new[] { "VCC", "GND", "TX", "RX", "MOTOCTL" }, -45, -5.08, 0, 2.54, 0, 4);
                    m.Bounds = R(-51.65, -51.65, 0, 51.65, 51.65, 71.7);
                    break;
                case "LD06":
                    Box(m.Body, 0, 0, 7, 38.6, 38.6, 14, Black, 0.15, m.Heat);
                    Cyl(m.Body, 19, 20.8, 2, 0, 0, 24.4, Color.FromRgb(0x20, 0x20, 0x24), 0.4, 36);
                    Box(m.Body, 18, 0, 26, 2, 14, 6, Color.FromRgb(0x30, 0x10, 0x14), 0.8);   // window
                    Header(m, new[] { "VCC", "GND", "TX", "RX", "MOTOCTL" }, -16, -5.08, 0, 2.54, 14, 4);
                    m.Bounds = R(-19.3, -19.3, 0, 19.3, 19.3, 34.8);
                    break;
                default:
                    // A1-class: base 96.8 × 70.3 mm, round scanning head, ≈ 55 mm tall.
                    Box(m.Body, -13, 0, 5, 96.8, 70.3, 10, Color.FromRgb(0x2A, 0x2A, 0x2E), 0.15, m.Heat);
                    Cyl(m.Body, 35, 3, 2, 0, 0, 11.5, Color.FromRgb(0x50, 0x50, 0x56), 0.3, 48);
                    Cyl(m.Body, 33, 30, 2, 0, 0, 28, Black, 0.4, 48);
                    Box(m.Body, 31.5, 0, 45, 4, 22, 9, Color.FromRgb(0x30, 0x10, 0x14), 0.8);   // optical window
                    Cyl(m.Body, 33, 12, 2, 0, 0, 49, Color.FromRgb(0x1E, 0x1E, 0x22), 0.4, 48);
                    Header(m, new[] { "VCC", "GND", "TX", "RX", "MOTOCTL" }, -56, -5.08, 0, 2.54, 2, 5);
                    m.Bounds = R(-61.4, -35.15, 0, 35.4, 35.15, 55);
                    break;
            }

            m.CollisionBounds = m.Bounds;
        }

        // ================================================================ GPS / compass

        private static void Gps(ObjectModel m)
        {
            Box(m.Body, 0, 0, 0.8, 25, 35, 1.6, Blue, 0.1, m.Heat);
            Box(m.Body, 0, 3, 2.8, 16, 12.2, 2.4, Metal, 0.6);                                 // NEO module shield
            Box(m.Body, 0, -6, 1.6 + 2.5 + 4, 25, 25, 8, Color.FromRgb(0xC8, 0xB4, 0x8C), 0.3);   // ceramic patch antenna
            Box(m.Body, 0, -6, 1.6 + 2.5 + 8.1, 13, 13, 0.3, Color.FromRgb(0xD8, 0xD8, 0xDC), 0.8);
            Box(m.Body, 0, -6, 1.6 + 1.25, 24, 24, 2.5, Color.FromRgb(0xE8, 0xE0, 0x90), 0.1);    // foam tape
            Header(m, new[] { "VCC", "RX", "TX", "GND" }, -3.81, 16, 2.54, 0, 1.6);
            m.Bounds = R(-12.5, -17.5, 0, 12.5, 17.5, 14);
            m.CollisionBounds = m.Bounds;
        }

        private static void Compass(ObjectModel m)
        {
            Box(m.Body, 0, 0, 0.8, 14, 18, 1.6, Blue, 0.1, m.Heat);
            Box(m.Body, 0, 2, 2.05, 3, 3, 0.9, Chip, 0.3);
            Header(m, new[] { "VCC", "GND", "SCL", "SDA", "DRDY" }, -5.08, -7.5, 2.54, 0, 1.6);
            m.Bounds = R(-7, -9, 0, 7, 9, 10.1);
            m.CollisionBounds = R(-7, -9, 0, 7, 9, 3);
        }

        // ================================================================ encoder motors (shaft along +Y)

        private static void EncoderMotor(ObjectModel m, bool jga)
        {
            var shaft = new Model3DGroup();
            var pins = new[] { "M1", "GND", "C1", "C2", "VCC", "M2" };
            if (jga)
            {
                const double c = 12.5;
                Cyl(m.Body, 12.5, 21, 1, 0, -10.5, c, Color.FromRgb(0xB0, 0xB4, 0xBC), 0.5, 32, m.Heat);    // gearbox
                Cyl(m.Body, 12.2, 31, 1, 0, -36.5, c, Color.FromRgb(0xC8, 0xCA, 0xCF), 0.6, 32, m.Heat);   // motor can
                Box(m.Body, 0, -55, c, 25, 6, 25, Green, 0.1);                                               // encoder board
                Cyl(m.Body, 6, 3, 1, 0, -59.5, c, Color.FromRgb(0x30, 0x30, 0x34), 0.3, 20);                // magnet disc
                Cyl(shaft, 2, 9.5, 1, 0, 4.75, c, Metal, 0.7, 12);
                m.Port = new PortFrame("shaft", "Motor mili", new Point3D(0, 9.5 * Mm, c * Mm), new Vector3D(0, 1, 0));
                for (int i = 0; i < 6; i++)
                {
                    double x = -6.35 + i * 2.54;
                    Box(m.Body, x, -58, c - 10, 0.64, 6, 0.64, Gold, 0.7);
                    m.Pins.Add(new PinAnchor(pins[i], pins[i], new Point3D(x * Mm, -61 * Mm, (c - 10) * Mm)));
                }

                m.Bounds = R(-12.5, -61, 0, 12.5, 9.5, 25);
                m.CollisionBounds = R(-12.5, -58, 0, 12.5, 0, 25);
            }
            else
            {
                const double c = 5;
                Box(m.Body, 0, -4.5, c, 12, 9, 10, Color.FromRgb(0xC0, 0xA0, 0x50), 0.5, m.Heat);           // brass gearbox
                Box(m.Body, 0, -16.5, c, 12, 15, 10, Color.FromRgb(0xC8, 0xCA, 0xCF), 0.6, m.Heat);         // motor
                Box(m.Body, 0, -24.8, c, 13, 1.6, 13, Green, 0.1);                                            // encoder PCB
                Cyl(m.Body, 3, 2, 1, 0, -26.6, c, Color.FromRgb(0x30, 0x30, 0x34), 0.3, 16);                // magnet
                Cyl(shaft, 1.5, 10, 1, 0, 5, c, Metal, 0.7, 12);
                m.Port = new PortFrame("shaft", "Motor mili", new Point3D(0, 10 * Mm, c * Mm), new Vector3D(0, 1, 0));
                for (int i = 0; i < 6; i++)
                {
                    double x = -5 + i * 2.0;
                    Box(m.Body, x, -26, c + 7.5, 0.5, 0.5, 5, Gold, 0.7);
                    m.Pins.Add(new PinAnchor(pins[i], pins[i], new Point3D(x * Mm, -26 * Mm, (c + 10) * Mm)));
                }

                m.Bounds = R(-6.5, -27.6, 0, 6.5, 10, 15);
                m.CollisionBounds = R(-6, -25.6, 0, 6, 0, 10);
            }

            m.Moving = shaft;
        }
    }
}
