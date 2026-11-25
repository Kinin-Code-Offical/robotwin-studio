using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [drive] Reads a machine from where its parts are, as a fitter would: a shaft that passes through a bearing's bore
    /// turns in it (a hinge with that bearing's friction), a gear, pulley, collar or coupler whose bore sits on a shaft is
    /// fixed to it (its set screw), a linear bearing on a rod slides along it, two gears whose pitch circles touch on
    /// parallel shafts mesh, a nut on a lead screw is driven by it. Nothing is assumed that the parts do not show: a gear
    /// on no shaft falls, a shaft in no bearing is loose.
    /// </summary>
    public static class DriveAssembly
    {
        /// <summary>How far apart two axes may be and still be one (m), and how much they may lean (rad).</summary>
        public const double AxisTolerance = 0.0006, AngleTolerance = 0.02;

        /// <summary>How much looser than the shaft a bore may be (m): a sliding fit, not a hole it rattles in.</summary>
        public const double BoreTolerance = 0.0004;

        private sealed class Part
        {
            public WorldObject O = null!;
            public DrivePartSpec S = null!;
            public Vec3 Origin, Axis;
            public double Span;

            public Vec3 Point(double t) => Origin + Axis * t;
        }

        /// <summary>The result: each link found and each problem the parts show, in words.</summary>
        public sealed class Report
        {
            public List<string> Links { get; } = new List<string>();
            public List<string> Problems { get; } = new List<string>();
            public int Joints { get; internal set; }
            public int Couplings { get; internal set; }
        }

        /// <summary>Adds the joints and couplings the parts' places imply (those not there yet) and says what it found.</summary>
        public static Report Assemble(WorldDocument world)
        {
            var report = new Report();
            var parts = new List<Part>();
            foreach (var o in world.Objects)
            {
                if (DriveParts.Of(o) is not { } s) continue;
                var (origin, axis, span) = DriveParts.AxisOf(o, s);
                parts.Add(new Part { O = o, S = s, Origin = origin, Axis = axis, Span = span });
            }

            var nuts = new List<(Part Screw, Part Nut)>();
            foreach (var rod in parts.Where(p => p.S.IsRod))
            {
                foreach (var p in parts)
                {
                    if (p == rod || p.S.IsRod || !OnRod(rod, p)) continue;
                    double fit = p.S.Kind == DrivePartKind.Coupler ? Math.Min(Math.Abs(p.S.Bore - rod.S.Bore), Math.Abs(p.S.Bore2 - rod.S.Bore)) : p.S.Bore - rod.S.Bore;
                    if (p.S.Kind == DrivePartKind.Coupler) fit = Math.Abs(fit);
                    if (fit < -1e-6)
                    {
                        report.Problems.Add($"{p.O.Name}: deliği (Ø{Mm(p.S.Bore)}) {rod.O.Name} için dar (Ø{Mm(rod.S.Bore)}), takılamaz.");
                        continue;
                    }

                    if (fit > BoreTolerance)
                    {
                        report.Problems.Add($"{p.O.Name}: deliği (Ø{Mm(p.S.Bore)}) {rod.O.Name} için bol (Ø{Mm(rod.S.Bore)}), mil boşta kalır.");
                        continue;
                    }

                    double at = Along(rod, p);
                    switch (p.S.Kind)
                    {
                        case DrivePartKind.Gear or DrivePartKind.Pulley or DrivePartKind.Collar or DrivePartKind.Coupler:
                            if (Link(world, report, p.O, rod.O, WorldJointKind.Fixed, rod, at, null, 0))
                                report.Links.Add($"{p.O.Name}, {rod.O.Name} üzerinde setuskurla sabit.");
                            break;
                        case DrivePartKind.Bearing or DrivePartKind.BearingHousing:
                            if (Link(world, report, rod.O, p.O, WorldJointKind.Hinge, rod, at, p.S.Bearing, p.S.Bore))
                                report.Links.Add($"{rod.O.Name}, {p.O.Name} içinde döner ({Bearings.Find(p.S.Bearing)?.Name ?? "rulman"}).");
                            if (!world.Joints.Any(j => j.ObjectA == p.O.Id && j.Kind == WorldJointKind.Fixed) && !p.O.IsStatic)
                                report.Problems.Add($"{p.O.Name} hiçbir yere bağlı değil: gövdeye vidalayın ya da sabit işaretleyin, yoksa mille birlikte düşer.");
                            break;
                        case DrivePartKind.LinearBearing:
                            if (Link(world, report, p.O, rod.O, WorldJointKind.Slider, rod, at, p.S.Bearing, 0))
                                report.Links.Add($"{p.O.Name}, {rod.O.Name} boyunca kayar.");
                            break;
                        case DrivePartKind.LeadNut when rod.S.Kind == DrivePartKind.LeadScrew:
                            if (Math.Abs(p.S.Lead - rod.S.Lead) > 1e-6)
                                report.Problems.Add($"{p.O.Name} (hatve {Mm(p.S.Lead)} mm) {rod.O.Name} (hatve {Mm(rod.S.Lead)} mm) diş açısına uymaz.");
                            else nuts.Add((rod, p));
                            break;
                    }
                }
            }

            foreach (var rod in parts.Where(p => p.S.IsRod))
            {
                // Held: in bearings, sliding, bolted to the frame or to something that is not riding on it.
                bool held = rod.O.IsStatic || MovingJoint(world, rod.O, rod.Axis, WorldJointKind.Hinge) != null || MovingJoint(world, rod.O, rod.Axis, WorldJointKind.Slider) != null ||
                            world.Joints.Any(j => j.Kind == WorldJointKind.Fixed && (j.ObjectA == rod.O.Id || j.ObjectB == rod.O.Id) &&
                                                  DriveParts.Of(world.Find(j.ObjectA == rod.O.Id ? j.ObjectB : j.ObjectA)) is not { MountsOnShaft: true });
                if (!held) report.Problems.Add($"{rod.O.Name} hiçbir rulmandan geçmiyor: tutulmadığı için düşer.");
            }

            // Gears whose pitch circles touch on parallel axes mesh.
            var gears = parts.Where(p => p.S.Kind == DrivePartKind.Gear).ToList();
            for (int i = 0; i < gears.Count; i++)
            {
                for (int k = i + 1; k < gears.Count; k++)
                {
                    var a = gears[i];
                    var b = gears[k];
                    if (Math.Abs(Math.Abs(a.Axis.Dot(b.Axis)) - 1) > AngleTolerance * AngleTolerance || Math.Abs(a.S.Module - b.S.Module) > 1e-7) continue;
                    var d = b.Origin - a.Origin;
                    double along = d.Dot(a.Axis);
                    double centre = (d - a.Axis * along).Length, want = a.S.Module * (a.S.Teeth + b.S.Teeth) / 2;
                    if (Math.Abs(centre - want) > 0.3 * a.S.Module) continue;
                    // The faces must overlap along the axis.
                    double sign = a.Axis.Dot(b.Axis) >= 0 ? 1 : -1;
                    if (!Overlap(0, a.S.Length, Math.Min(along, along + sign * b.S.Length), Math.Max(along, along + sign * b.S.Length))) continue;
                    var ja = MovingJoint(world, a.O, a.Axis, WorldJointKind.Hinge);
                    var jb = MovingJoint(world, b.O, b.Axis, WorldJointKind.Hinge);
                    if (ja == null || jb == null)
                    {
                        report.Problems.Add($"{a.O.Name} ile {b.O.Name} kavrıyor ama {(ja == null ? a.O.Name : b.O.Name)} dönebileceği bir mile ya da rulmana takılı değil.");
                        continue;
                    }

                    if (Coupled(world, ja.Value.Joint, jb.Value.Joint)) continue;
                    // Meshing gears turn opposite ways about a common axis: the kind's ratio, unless the joints see it differently.
                    (world.Couplings ??= new List<WorldCoupling>()).Add(new WorldCoupling
                    {
                        Id = NextCoupling(world), Kind = WorldCoupling.Gear, JointA = ja.Value.Joint.Id, JointB = jb.Value.Joint.Id, SizeA = a.S.Teeth, SizeB = b.S.Teeth,
                        Reverse = Sense(ja.Value, a.Axis) * Sense(jb.Value, a.Axis) < 0
                    });
                    report.Couplings++;
                    report.Links.Add($"{a.O.Name} ile {b.O.Name} kavrıyor: {a.S.Teeth}:{b.S.Teeth}, eksen mesafesi {Mm(centre)} mm.");
                }
            }

            // A nut on a lead screw: the screw's turning moves whatever slides the nut.
            foreach (var (screw, nut) in nuts)
            {
                var js = MovingJoint(world, screw.O, screw.Axis, WorldJointKind.Hinge);
                var jn = MovingJoint(world, nut.O, screw.Axis, WorldJointKind.Slider);
                if (js == null || jn == null)
                {
                    report.Problems.Add(js == null
                        ? $"{screw.O.Name} dönebileceği rulmanlara takılı değil: somunu süremez."
                        : $"{nut.O.Name} dönmesini engelleyen bir kızağa bağlı değil: vidayla birlikte döner. Somunu doğrusal rulmanlı taşıyıcıya bağlayın.");
                    continue;
                }

                if (Coupled(world, js.Value.Joint, jn.Value.Joint)) continue;
                // A right-hand thread: the screw turning positively about its axis moves the nut back along it.
                (world.Couplings ??= new List<WorldCoupling>()).Add(new WorldCoupling
                {
                    Id = NextCoupling(world), Kind = WorldCoupling.Screw, JointA = js.Value.Joint.Id, JointB = jn.Value.Joint.Id, SizeA = screw.S.Lead * 1000, SizeB = screw.S.Bore * 1000,
                    Reverse = Sense(js.Value, screw.Axis) * Sense(jn.Value, screw.Axis) < 0
                });
                report.Couplings++;
                report.Links.Add($"{nut.O.Name}, {screw.O.Name} döndükçe turda {Mm(screw.S.Lead)} mm ilerler.");
            }

            return report;
        }

        private static string Mm(double m) => (m * 1000).ToString("0.##", CultureInfo.InvariantCulture);

        private static bool Overlap(double a0, double a1, double b0, double b1) => Math.Min(a1, b1) - Math.Max(a0, b0) > 1e-4;

        /// <summary>Where along the rod's axis the part's middle is (m from the rod's start).</summary>
        private static double Along(Part rod, Part p)
        {
            double mid = p.S.Kind == DrivePartKind.BearingHousing ? p.Span / 2 : p.S.Length / 2;
            return (p.Point(mid) - rod.Origin).Dot(rod.Axis);
        }

        /// <summary>The part's bore is on the rod's axis, square to it, and within the rod's length.</summary>
        private static bool OnRod(Part rod, Part p)
        {
            if (Math.Abs(Math.Abs(rod.Axis.Dot(p.Axis)) - 1) > AngleTolerance * AngleTolerance) return false;
            var d = p.Origin - rod.Origin;
            if ((d - rod.Axis * d.Dot(rod.Axis)).Length > AxisTolerance) return false;
            double t0 = d.Dot(rod.Axis), t1 = (p.Point(p.Span) - rod.Origin).Dot(rod.Axis);
            return Overlap(0, rod.Span, Math.Min(t0, t1), Math.Max(t0, t1));
        }

        private static bool Link(WorldDocument world, Report report, WorldObject child, WorldObject parent, WorldJointKind kind, Part rod, double at, string? bearing, double bore)
        {
            if (world.Joints.Any(j => (j.ObjectA == child.Id && j.ObjectB == parent.Id) || (j.ObjectA == parent.Id && j.ObjectB == child.Id))) return false;
            var anchor = rod.Point(at);
            var axis = Canonical(rod.Axis);
            world.Joints.Add(new WorldJoint
            {
                Id = world.NextId("J"), Kind = kind, ObjectA = child.Id, ObjectB = parent.Id,
                AnchorX = anchor.X, AnchorY = anchor.Y, AnchorZ = anchor.Z, AxisX = axis.X, AxisY = axis.Y, AxisZ = axis.Z,
                Fastening = kind == WorldJointKind.Fixed ? "screws" : string.Empty,
                Bearing = bearing, BearingBore = bore > 0 ? bore * 1000 : 0
            });
            report.Joints++;
            return true;
        }

        /// <summary>
        /// The hinge (or slider) that moves this part about (along) the axis: its own, or that of the shaft or carriage it is
        /// fixed to (any number of fixed joints away); and whether the part is on the joint's child side.
        /// </summary>
        private static (WorldJoint Joint, bool Child)? MovingJoint(WorldDocument world, WorldObject o, Vec3 axis, WorldJointKind kind)
        {
            var group = new HashSet<string> { o.Id };
            var queue = new Queue<string>(group);
            while (queue.Count > 0)
            {
                string id = queue.Dequeue();
                foreach (var j in world.Joints.Where(j => j.Kind == WorldJointKind.Fixed && (j.ObjectA == id || j.ObjectB == id)))
                {
                    string? other = j.ObjectA == id ? j.ObjectB : j.ObjectA;
                    if (other != null && group.Add(other)) queue.Enqueue(other);
                }
            }

            var found = world.Joints.FirstOrDefault(j => j.Kind == kind && group.Contains(j.ObjectA) != (j.ObjectB != null && group.Contains(j.ObjectB)) &&
                                                         Math.Abs(Math.Abs(AxisOf(j).Dot(axis)) - 1) < AngleTolerance);
            return found == null ? null : (found, group.Contains(found.ObjectA));
        }

        private static Vec3 AxisOf(WorldJoint j) => new Vec3(j.AxisX, j.AxisY, j.AxisZ).Normalized();

        /// <summary>+1 when the joint's positive motion moves the part positively about (along) the axis, −1 when the other way.</summary>
        private static int Sense((WorldJoint Joint, bool Child) m, Vec3 axis) => (AxisOf(m.Joint).Dot(axis) >= 0 ? 1 : -1) * (m.Child ? 1 : -1);

        /// <summary>One direction for parallel axes, whichever way the parts were turned: its largest component positive.</summary>
        private static Vec3 Canonical(Vec3 a)
        {
            double ax = Math.Abs(a.X), ay = Math.Abs(a.Y), az = Math.Abs(a.Z);
            double lead = ax >= ay && ax >= az ? a.X : ay >= az ? a.Y : a.Z;
            return lead < 0 ? a * -1 : a;
        }

        private static bool Coupled(WorldDocument world, WorldJoint a, WorldJoint b) =>
            world.Couplings?.Any(c => (c.JointA == a.Id && c.JointB == b.Id) || (c.JointA == b.Id && c.JointB == a.Id)) == true;

        private static string NextCoupling(WorldDocument world)
        {
            int n = 1;
            while (world.Couplings?.Any(c => c.Id == "couple" + n) == true) n++;
            return "couple" + n;
        }
    }
}
