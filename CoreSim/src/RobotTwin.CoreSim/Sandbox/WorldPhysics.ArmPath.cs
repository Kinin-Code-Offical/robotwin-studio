using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Robotics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>[pathcheck] What a planned arm move would hit, and where along it.</summary>
    public sealed class ArmPathHit
    {
        /// <summary>Seconds into the move.</summary>
        public double Time { get; internal set; }

        /// <summary>The part of the arm that hits (a link, the tool, the part it carries).</summary>
        public string Moving { get; internal set; } = "";

        /// <summary>What it hits: another object, one of the arm's own links, or the floor.</summary>
        public string Other { get; internal set; } = "";

        /// <summary>How far the boxes overlap there (m).</summary>
        public double Depth { get; internal set; }

        public double[] Joints { get; internal set; } = Array.Empty<double>();

        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "{0} ↔ {1}, hareketin {2:0.00} s'sinde ({3:0} mm iç içe)", Moving, Other, Time, Depth * 1000);
    }

    public sealed partial class WorldPhysics
    {
        /// <summary>
        /// [pathcheck] A planned move checked along its whole path before the arm starts it, as a collision-aware planner
        /// does: the move is sampled finely enough that no joint turns more than 1° (or a prismatic one moves 2 mm) between
        /// samples, and at each sample the arm's link boxes, its tool and the part it carries (where they ride with the
        /// flange) are tested against every other solid in the world as it stands now, against the floor, and against the
        /// arm's own links two or more joints away. Contacts that already exist at the start (the part it picks still on
        /// the conveyor, a link resting on its stop), and the tool's or the carried part's contacts where the move ends (the
        /// cup on the box it picks, the box on the pallet), are not counted; any other overlap deeper than
        /// <paramref name="tolerance"/> is.
        /// </summary>
        public ArmPathHit? CheckArmPath(MachineArm arm, ArmMotion motion, double tolerance = 0.002)
        {
            var model = arm.Model;
            int dof = model.Dof;
            var q = new double[dof];
            var qd = new double[dof];
            var start = new double[dof];
            motion.Sample(0, start, qd);

            // What rides with the flange (tool, fingers, cup, the carried part), as boxes in the flange's frame.
            var flangeNow = arm.BasePose * ArmKinematics.Forward(model, start);
            var inverse = flangeNow.Inverse();
            var riders = new List<(string Name, (Vec3 C, Vec3[] Axes, Vec3 Half) Box)>();
            var moving = new HashSet<SceneBody>(arm.Links) { arm.BaseBody };
            if (arm.Tool is { } tool)
            {
                var bodies = new List<(SceneBody Body, string Name)> { (tool.Body, tool.Object.Name) };
                bodies.AddRange(tool.Fingers.Select(f => (f, tool.Object.Name)));
                if (tool.Cup != null) bodies.Add((tool.Cup, tool.Object.Name));
                if (tool.Grip is { } grip)
                {
                    var held = tool.Own.Contains(grip.ChildBody) ? grip.ParentBody : grip.ChildBody;
                    if (held != null) bodies.Add((held, (held.Tag as WorldObject)?.Name ?? held.Id));
                }

                foreach (var (body, name) in bodies)
                {
                    if (!moving.Add(body) || body.EffectiveShape == null) continue;
                    foreach (var b in Obbs(body))
                        riders.Add((name, (inverse.Apply(b.C), b.Axes.Select(a => inverse.ApplyVector(a)).ToArray(), b.Half)));
                }

                foreach (var b in tool.Own) moving.Add(b);
            }

            // Everything else that is solid, as it stands now; pieces bolted to the arm's base or links (its pedestal) are not obstacles.
            var attached = new HashSet<SceneBody>();
            foreach (var (a, b) in Scene.NoCollide)
            {
                if (moving.Contains(a)) attached.Add(b);
                if (moving.Contains(b)) attached.Add(a);
            }

            var obstacles = new List<(string Name, (Vec3 C, Vec3[] Axes, Vec3 Half) Box)>();
            foreach (var body in Scene.Bodies)
            {
                if (!body.Collides || body.EffectiveShape == null || moving.Contains(body) || attached.Contains(body)) continue;
                string name = (body.Tag as WorldObject)?.Name ?? body.Id;
                foreach (var b in Obbs(body)) obstacles.Add((name, b));
            }

            (Vec3 C, Vec3[] Axes, Vec3 Half) LinkBox(Transform3 frame, ArmLinkSpec spec) =>
                (frame.Apply(spec.Center), new[] { frame.XAxis, frame.YAxis, frame.ZAxis }, spec.Size * 0.5);

            (Vec3 C, Vec3[] Axes, Vec3 Half) Ride(Transform3 flange, (Vec3 C, Vec3[] Axes, Vec3 Half) b) =>
                (flange.Apply(b.C), b.Axes.Select(a => flange.ApplyVector(a)).ToArray(), b.Half);

            static double Below((Vec3 C, Vec3[] Axes, Vec3 Half) b) =>
                b.C.Z - Math.Abs(b.Axes[0].Z) * b.Half.X - Math.Abs(b.Axes[1].Z) * b.Half.Y - Math.Abs(b.Axes[2].Z) * b.Half.Z;

            // The boxes at a configuration: links (index = link) and riders (index = links + rider).
            List<(string Name, int Link, (Vec3 C, Vec3[] Axes, Vec3 Half) Box)> Boxes(double[] joints)
            {
                var frames = ArmKinematics.LinkTransforms(model, joints);
                var list = new List<(string, int, (Vec3, Vec3[], Vec3))>(model.Links.Count + riders.Count);
                for (int i = 0; i < model.Links.Count && i < frames.Length; i++)
                    list.Add((string.IsNullOrWhiteSpace(model.Links[i].Name) ? "L" + (i + 1).ToString(CultureInfo.InvariantCulture) : model.Links[i].Name, i, LinkBox(arm.BasePose * frames[i], model.Links[i])));
                var flange = arm.BasePose * ArmKinematics.Forward(model, joints);
                foreach (var (name, box) in riders) list.Add((name, model.Links.Count, Ride(flange, box)));
                return list;
            }

            double floor = Scene.GroundHeight(arm.BasePose.P.X, arm.BasePose.P.Y);
            // Pairs touching at the start, by name: a contact the move begins in is not a collision of the move.
            var before = new HashSet<(string, string)>();
            var first = Boxes(start);
            foreach (var m in first)
            {
                foreach (var o in obstacles)
                    if (BoxOverlap(m.Box, o.Box) > 0) before.Add((m.Name, o.Name));
                if (Below(m.Box) < floor) before.Add((m.Name, ""));
            }

            // What the tool or the part it carries touches where the move ends is what the move goes to: the cup pressing
            // onto the box it picks, the box set down on the pallet. Those contacts are the move's purpose, not a collision;
            // a link touching anything at the end still is.
            var end = motion.End;
            foreach (var m in Boxes(end).Where(b => b.Link >= model.Links.Count))
                foreach (var o in obstacles)
                    if (BoxOverlap(m.Box, o.Box) > 0) before.Add((m.Name, o.Name));

            // Fine enough that no joint moves more than 1° (2 mm) between samples, and at least every 10 ms.
            double travel = 0;
            for (int j = 0; j < dof; j++)
                travel = Math.Max(travel, Math.Abs(end[j] - start[j]) / (model.Joints[j].IsPrismatic ? 0.002 : Math.PI / 180));
            int n = Math.Min(4000, Math.Max(2, (int)Math.Ceiling(Math.Max(travel, motion.Duration / 0.01))));
            for (int k = 1; k <= n; k++)
            {
                double t = motion.Duration * k / n;
                motion.Sample(t, q, qd);
                var boxes = Boxes(q);
                foreach (var m in boxes)
                {
                    foreach (var o in obstacles)
                    {
                        double depth = BoxOverlap(m.Box, o.Box);
                        if (depth > tolerance && !before.Contains((m.Name, o.Name)))
                            return new ArmPathHit { Time = t, Moving = m.Name, Other = o.Name, Depth = depth, Joints = (double[])q.Clone() };
                    }

                    double under = floor - Below(m.Box);
                    if (under > tolerance && !before.Contains((m.Name, "")))
                        return new ArmPathHit { Time = t, Moving = m.Name, Other = "zemin", Depth = under, Joints = (double[])q.Clone() };
                }

                // The arm against itself: links two or more joints apart, and what it carries against the links behind the wrist.
                for (int a = 0; a < boxes.Count; a++)
                {
                    for (int b = a + 1; b < boxes.Count; b++)
                    {
                        int la = boxes[a].Link, lb = boxes[b].Link;
                        if (Math.Abs(la - lb) < 2 || (la >= model.Links.Count && lb >= model.Links.Count)) continue;
                        if (Math.Max(la, lb) >= model.Links.Count && Math.Min(la, lb) >= model.Links.Count - 3) continue;   // the wrist holds the tool
                        double depth = BoxOverlap(boxes[a].Box, boxes[b].Box);
                        if (depth > tolerance && BoxOverlap(first[a].Box, first[b].Box) <= tolerance)
                            return new ArmPathHit { Time = t, Moving = boxes[a].Name, Other = boxes[b].Name, Depth = depth, Joints = (double[])q.Clone() };
                    }
                }
            }

            return null;
        }
    }
}
