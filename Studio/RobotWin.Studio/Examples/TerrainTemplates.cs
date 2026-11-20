using System;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Examples
{
    // [terrain] Worlds with sculpted terrain: an outdoor test course and an indoor workshop.
    internal static class TerrainTemplates
    {
        private static int L(string id) => TerrainLayers.IndexOf(id);

        /// <summary>Moves every object of a world (and its joint anchors) by (dx, dy).</summary>
        private static void Shift(WorldDocument w, double dx, double dy)
        {
            foreach (var o in w.Objects)
            {
                o.Pose.X += dx;
                o.Pose.Y += dy;
            }

            foreach (var j in w.Joints)
            {
                if (j.AnchorX == 0 && j.AnchorY == 0 && j.AnchorZ == 0) continue;
                j.AnchorX += dx;
                j.AnchorY += dy;
            }
        }

        private static void PaintRect(TerrainMap m, int layer, double x0, double y0, double x1, double y1)
        {
            var (c0, r0, c1, r1) = m.VertexRange(x0, y0, x1, y1);
            for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                double x = m.VertexX(c), y = m.VertexY(r);
                if (x < x0 - 1e-9 || x > x1 + 1e-9 || y < y0 - 1e-9 || y > y1 + 1e-9) continue;
                m.PaintVertex(m.Index(c, r), layer, 1);
            }

            m.NotifyChanged(c0, r0, c1, r1, false, true);
        }

        private static WorldObject Prop(WorldDocument w, string id, double x, double y, double yaw = 0, Vec3? size = null, bool align = false, string? name = null)
        {
            var o = WorldBuilding.AddProp(w, id, x, y, yaw, align, size);
            if (name != null) o.Name = w.UniqueName(name);
            return o;
        }

        private static double Smooth01(double t) => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);

        /// <summary>
        /// "Arazi parkuru": 10 × 6 m outdoor course on grass with rolling ground and two hills; an
        /// asphalt road with two speed bumps and a kerb, a gravel bed across the road, mud with a
        /// puddle, a sand pit, a wooden ramp, stairs, cones, rocks, trees and bushes. The two-wheel
        /// car stands on the flat start of the road, facing +x.
        /// </summary>
        public static WorldDocument Parkour(Func<WorldDocument> car)
        {
            var w = car();
            w.Title = "Arazi parkuru";
            w.Description = "Engebeli dış mekân test parkuru: asfalt yol, hız kesiciler, çakıl, çamur, kum, rampa.";
            w.Environment.Name = "Arazi parkuru";
            w.Environment.Width = 10;
            w.Environment.Depth = 6;
            w.Environment.Walls = false;
            Shift(w, -3.7, 0);

            var m = new TerrainMap(10, 6, 0.04, 0, 0, "grass");
            m.Fill((x, y) =>
            {
                double rolling = 0.05 * (TerrainSculpt.Fbm(x * 0.45 + 3.1, y * 0.45 - 1.7, 4) + 0.2);
                double hill1 = 0.38 * Math.Exp(-((x - 2.6) * (x - 2.6) + (y - 1.9) * (y - 1.9)) / (2 * 0.8 * 0.8));
                double hill2 = 0.22 * Math.Exp(-((x + 2.4) * (x + 2.4) + (y + 2.0) * (y + 2.0)) / (2 * 0.7 * 0.7));
                double h = rolling + hill1 + hill2;
                // The first stretch of the road (the car's start and test strip) is level at z = 0.
                double across = Smooth01((Math.Abs(y) - 0.45) / 0.5);
                double along = Smooth01((x - 0.6) / 0.8);
                double keep = Math.Max(across, along);
                return h * keep;
            });

            var b = new TerrainBrush();
            // Road: straight test strip, then curving down the slope to the right.
            TerrainSculpt.Road(m, new[] { (-4.7, 0.0), (-2.0, 0.0), (0.6, 0.0), (2.2, -0.7), (3.4, -1.9), (4.6, -2.4) }, 0.7, L("asphalt"), true);
            // Two speed bumps across the strip (12 mm × 120 mm) and a 30 mm kerb along its left edge.
            TerrainSculpt.Stamp(m, TerrainStamp.SpeedBump, -2.7, 0, 0, 0.012, 0.12, 0.8);
            TerrainSculpt.Stamp(m, TerrainStamp.SpeedBump, -1.7, 0, 0, 0.012, 0.12, 0.8);
            TerrainSculpt.Stamp(m, TerrainStamp.Kerb, -1.4, 0.40, 90, 0.03, 0.06, 1.8);
            // Gravel bed across the road after the bumps.
            b.Radius = 0.5;
            b.Hardness = 0.55;
            b.Layer = L("gravel");
            TerrainSculpt.Paint(m, b, -0.5, 0, 1);
            TerrainSculpt.Paint(m, b, -0.1, 0.05, 1);
            TerrainSculpt.Paint(m, b, 2.3, -2.0, 1);
            // Mud with a puddle hollow, a sand pit, packed-dirt track up the hill.
            b.Radius = 0.65;
            b.Layer = L("mud");
            TerrainSculpt.Paint(m, b, 1.3, 1.2, 1);
            TerrainSculpt.Stamp(m, TerrainStamp.Pothole, 1.3, 1.2, 0, 0.035, 0.7, 0.7);
            b.Layer = L("wet");
            b.Radius = 0.25;
            TerrainSculpt.Paint(m, b, 1.3, 1.2, 1);
            b.Layer = L("sand");
            b.Radius = 0.8;
            TerrainSculpt.Paint(m, b, -2.6, 2.0, 1);
            TerrainSculpt.Road(m, new[] { (0.9, 0.4), (1.9, 1.2), (2.6, 1.9) }, 0.45, L("dirt"), false);
            // Rougher ground on the far side.
            b.Radius = 1.2;
            b.Hardness = 0.2;
            b.NoiseScale = 0.18;
            TerrainSculpt.Noise(m, b, 3.8, 1.6, 0.04);
            w.Terrain = m;

            // Props.
            Prop(w, "ramp", -1.6, -1.25, 0, null, true, "Rampa");
            Prop(w, "stairs", 0.4, -1.5, 180, null, true);
            Prop(w, "step", -3.0, -1.1, 0, null, true);
            for (int i = 0; i < 5; i++) Prop(w, "cone", -0.2 + i * 0.4, -1.0 + (i % 2) * 0.25);   // slalom beside the road
            Prop(w, "pipe", 3.6, 0.3, 30, null, true);
            Prop(w, "rock", 3.3, -0.6, 20);
            Prop(w, "rock", -3.6, 2.3, 75, new Vec3(0.45, 0.35, 0.25));
            Prop(w, "rock", 0.2, 2.3, 140, new Vec3(0.2, 0.18, 0.12));
            Prop(w, "box", -4.3, -0.9, 10);
            Prop(w, "box", 4.3, -0.9, -15);
            foreach (var (x, y) in new[] { (-4.4, 2.5), (4.4, 2.6), (-0.6, 2.6), (4.5, -2.6), (-4.5, -2.6) }) Prop(w, "tree", x, y, x * 40);
            foreach (var (x, y) in new[] { (-3.2, 1.2), (3.0, 2.7), (-1.0, -2.6), (2.0, 2.6), (-4.6, 0.9) }) Prop(w, "bush", x, y, y * 30);
            return w;
        }

        /// <summary>
        /// "Kapalı atölye": 6 × 4 m indoor floor of tiles with two rubber mats, a carpet, a concrete
        /// strip at the door and a wet spill; walls, a ramp, a step, stairs and boxes. The car stands on
        /// the tiles facing the first mat.
        /// </summary>
        public static WorldDocument Workshop(Func<WorldDocument> car)
        {
            var w = car();
            w.Title = "Kapalı atölye";
            w.Description = "Düz fayans zemin; kauçuk paspas, halı, beton ve ıslak zemin bölgeleri.";
            w.Environment.Name = "Kapalı atölye";
            w.Environment.Width = 6;
            w.Environment.Depth = 4;
            w.Environment.Walls = false;
            Shift(w, -2.0, -0.6);

            var m = new TerrainMap(6, 4, 0.05, 0, 0, "tile");
            PaintRect(m, L("rubbermat"), -1.2, -1.2, 0.0, 0.0);
            PaintRect(m, L("rubbermat"), 1.4, 0.6, 2.6, 1.6);
            PaintRect(m, L("carpet"), -2.8, 0.5, -1.0, 1.8);
            PaintRect(m, L("concrete"), 2.7, -1.95, 2.95, 1.95);
            var b = new TerrainBrush { Radius = 0.35, Hardness = 0.3, Layer = L("wet") };
            TerrainSculpt.Paint(m, b, 0.9, -1.2, 1);
            TerrainSculpt.Paint(m, b, 1.15, -1.0, 0.7);
            w.Terrain = m;

            Prop(w, "wall", 0, 2.03, 0, new Vec3(6, 0.06, 0.4), name: "Duvar");
            Prop(w, "wall", 0, -2.03, 0, new Vec3(6, 0.06, 0.4), name: "Duvar");
            Prop(w, "wall", -3.03, 0, 90, new Vec3(4.1, 0.06, 0.4), name: "Duvar");
            Prop(w, "wall", 3.03, 1.4, 90, new Vec3(1.3, 0.06, 0.4), name: "Duvar");
            Prop(w, "wall", 3.03, -1.4, 90, new Vec3(1.3, 0.06, 0.4), name: "Duvar");
            Prop(w, "ramp", 0.7, 0.6, 0, new Vec3(0.5, 0.35, 0.06));
            Prop(w, "step", 1.5, 0.0, 0, new Vec3(0.3, 0.3, 0.02));
            Prop(w, "stairs", -0.3, 1.5, 90);
            Prop(w, "box", -2.6, -1.6, 15);
            Prop(w, "box", -2.0, -1.6, -5);   // side by side with a gap, not inside each other
            Prop(w, "box", 2.5, 1.85, 0, new Vec3(0.4, 0.25, 0.3), name: "Raf kutusu");
            for (int i = 0; i < 4; i++) Prop(w, "cone", -0.8 + i * 0.35, -1.6);
            return w;
        }
    }
}
