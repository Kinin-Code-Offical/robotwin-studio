using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Robotics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [arm] Solid body (object frame) of each robot-cell part: the base of an arm, the tool
    /// housing, the conveyor frame, the light-curtain envelope, pallet, fence and pedestal.
    /// The 3D models use the same numbers so what you see is what collides.
    /// </summary>
    public static class CellGeometry
    {
        /// <summary>Body of a cell part, or null for other objects.</summary>
        public static WorldBodyGeometry? For(WorldObject o)
        {
            if (o.Kind != WorldObjectKind.Component || !CellParts.IsCellPart(o.PartId)) return null;
            var p = Params(o);
            double mass = CellParts.MassOf(o.PartId!, p) ?? 1;
            switch (o.PartId)
            {
                case CellParts.ArmId:
                {
                    var m = ArmCatalog.Get(p.TryGetValue("model", out var id) ? id : null);
                    var b = m.Base;
                    return Box(b.Center - b.Size * 0.5, b.Center + b.Size * 0.5, mass);
                }

                case CellParts.GripperId:
                    return Box(new Vec3(-ToolGeometry.GripperHousingX / 2, -ToolGeometry.GripperHousingY / 2, 0),
                        new Vec3(ToolGeometry.GripperHousingX / 2, ToolGeometry.GripperHousingY / 2, ToolGeometry.GripperHousingZ), mass);
                case CellParts.VacuumId:
                {
                    double d = CellParts.Num(p, "diameter", 50) / 1000;
                    int cups = (int)Math.Max(1, CellParts.Num(p, "cups", 1));
                    double half = cups == 1 ? Math.Max(0.03, d / 2) : 0.6 * d + 0.01 + d / 2;
                    double halfY = cups == 4 ? 0.45 * d + 0.008 + d / 2 : Math.Max(0.03, d / 2);
                    return Box(new Vec3(-half, -halfY, 0), new Vec3(half, halfY, ToolGeometry.VacuumMountZ + ToolGeometry.VacuumStemZ), mass);
                }

                case CellParts.ConveyorId:
                {
                    double l = CellParts.Num(p, "length", 1.6), w = CellParts.Num(p, "width", 0.3), h = CellParts.Num(p, "height", 0.6);
                    return Box(new Vec3(-l / 2, -w / 2, 0), new Vec3(l / 2, w / 2, h), mass);
                }

                case CellParts.CurtainId:
                {
                    double w = CellParts.Num(p, "width", 0.9), h = CellParts.Num(p, "height", 1.2);
                    return Box(new Vec3(-0.03, -w / 2 - 0.05, 0), new Vec3(0.03, w / 2 + 0.05, WorldPhysics.CurtainBase + h + 0.08), mass);
                }

                case CellParts.PalletId:
                {
                    double x = CellParts.Num(p, "size_x", 0.8), y = CellParts.Num(p, "size_y", 0.6), h = CellParts.Num(p, "height", 0.144);
                    return Box(new Vec3(-x / 2, -y / 2, 0), new Vec3(x / 2, y / 2, h), mass);
                }

                case CellParts.FenceId:
                {
                    double w = CellParts.Num(p, "width", 1.0), h = CellParts.Num(p, "height", 1.6);
                    return Box(new Vec3(-w / 2, -0.02, 0), new Vec3(w / 2, 0.02, h), mass);
                }

                case CellParts.PedestalId:
                {
                    double h = CellParts.Num(p, "height", 0.4), s = CellParts.Num(p, "size", 0.4);
                    return Box(new Vec3(-s / 2, -s / 2, 0), new Vec3(s / 2, s / 2, h), mass);
                }
            }

            return null;
        }

        private static WorldBodyGeometry Box(Vec3 min, Vec3 max, double mass) => new WorldBodyGeometry { Min = min, Max = max, MassKg = mass, Shape = "box" };

        private static Dictionary<string, string> Params(WorldObject o)
        {
            var lib = PartLibrary.Default;
            var def = o.PartId != null ? lib.Find(o.PartId) : null;
            if (def == null) return new Dictionary<string, string>(o.Parameters, StringComparer.OrdinalIgnoreCase);
            var preset = o.PresetId != null ? lib.FindPreset(o.PresetId) : null;
            return lib.ResolveParameters(def, preset, o.Parameters);
        }

        /// <summary>Flange pose of an arm object in the world for its start joints (where tools sit while editing).</summary>
        public static Transform3 FlangeWorld(WorldObject arm, IReadOnlyList<double>? joints = null)
        {
            var p = Params(arm);
            var m = ArmCatalog.Get(p.TryGetValue("model", out var id) ? id : null);
            var q = joints ?? ArmProgram.ParseJoints(m, p.TryGetValue("q0", out var qs) ? qs : null) ?? m.Home;
            var pose = new Transform3(arm.Pose.Position, arm.Pose.Orientation);
            return pose * ArmKinematics.Forward(m, q);
        }

        /// <summary>Approximate mass (kg) of a cell part, or null for other objects.</summary>
        public static double? MassOf(WorldObject o) =>
            o.Kind == WorldObjectKind.Component && CellParts.IsCellPart(o.PartId) ? CellParts.MassOf(o.PartId!, Params(o)) : null;

        /// <summary>Arm model of an arm object.</summary>
        public static ArmModel ModelOf(WorldObject arm)
        {
            var p = Params(arm);
            return ArmCatalog.Get(p.TryGetValue("model", out var id) ? id : null);
        }

        /// <summary>Start joints (q0) of an arm object.</summary>
        public static double[] StartJoints(WorldObject arm)
        {
            var p = Params(arm);
            var m = ArmCatalog.Get(p.TryGetValue("model", out var id) ? id : null);
            return ArmProgram.ParseJoints(m, p.TryGetValue("q0", out var qs) ? qs : null) ?? m.Home.ToArray();
        }

        /// <summary>
        /// Puts every tool mounted on the arm's flange at the flange pose for the start joints
        /// (call after changing q0 or moving the arm while editing).
        /// </summary>
        public static void SeatTools(WorldDocument world, WorldObject arm)
        {
            var f = FlangeWorld(arm);
            var (r, pi, y) = f.ToRpy();
            foreach (var j in world.Joints)
            {
                if (!string.Equals(j.ObjectB, arm.Id, StringComparison.OrdinalIgnoreCase) || !string.Equals(j.PortB, "flange", StringComparison.OrdinalIgnoreCase)) continue;
                var tool = world.Find(j.ObjectA);
                if (tool == null) continue;
                tool.Pose.X = f.P.X;
                tool.Pose.Y = f.P.Y;
                tool.Pose.Z = f.P.Z;
                tool.Pose.Roll = r * 180 / Math.PI;
                tool.Pose.Pitch = pi * 180 / Math.PI;
                tool.Pose.Yaw = y * 180 / Math.PI;
                j.AnchorX = f.P.X;
                j.AnchorY = f.P.Y;
                j.AnchorZ = f.P.Z;
            }
        }
    }
}
