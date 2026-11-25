using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Design;
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

            /// <summary>A stepper's shaft: it turns on the motor's own bearings, and what is fixed to it rides its rotor.</summary>
            public bool Motor;

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

            // Stepper shafts: the Ø5 D-shaft above the pilot boss, from the part's own sizes.
            foreach (var o in world.Objects)
            {
                if (o.Kind != WorldObjectKind.Component || o.PartId != MotionParts.StepperId) continue;
                double body = StepperLength(o);
                parts.Add(new Part
                {
                    O = o, S = MotorShaft, Origin = o.Pose.ToWorld(new Vec3(0, 0, body + 0.002)), Axis = o.Pose.Orientation.Rotate(Vec3.UnitZ).Normalized(), Span = 0.022, Motor = true
                });
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
                            if (Link(world, report, p.O, rod.O, WorldJointKind.Fixed, rod, at, null, 0, rod.Motor ? "shaft" : string.Empty))
                                report.Links.Add(rod.Motor ? $"{p.O.Name}, {rod.O.Name} motor miline setuskurla sabit." : $"{p.O.Name}, {rod.O.Name} üzerinde setuskurla sabit.");
                            break;
                        case var _ when rod.Motor:
                            break;   // only hubs go on a motor's shaft
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

            foreach (var rod in parts.Where(p => p.S.IsRod && !p.Motor))
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
                    Mesh(a, b);
                    report.Couplings++;
                    report.Links.Add($"{a.O.Name} ile {b.O.Name} kavrıyor: {a.S.Teeth}:{b.S.Teeth}, eksen mesafesi {Mm(centre)} mm.");
                }
            }

            // A pinion on a rack: its pitch circle on the rack's pitch line, its axis across the rack.
            foreach (var rack in parts.Where(p => p.S.Kind == DrivePartKind.Rack))
            {
                var u = rack.Axis;
                var up = rack.O.Pose.Orientation.Rotate(Vec3.UnitZ).Normalized();
                var across = rack.O.Pose.Orientation.Rotate(Vec3.UnitY).Normalized();
                foreach (var g in gears)
                {
                    if (Math.Abs(g.S.Module - rack.S.Module) > 1e-7 || Math.Abs(Math.Abs(g.Axis.Dot(across)) - 1) > AngleTolerance * AngleTolerance) continue;
                    var rel = g.Point(g.S.Length / 2) - rack.Origin;
                    double along = rel.Dot(u), height = rel.Dot(up), side = rel.Dot(across), r = g.S.PitchDiameter / 2;
                    if (along < 0 || along > rack.S.Length || Math.Abs(height - r) > 0.3 * rack.S.Module || Math.Abs(side) > (rack.S.Width + g.S.Length) / 2 - 1e-4) continue;
                    var jg = MovingJoint(world, g.O, g.Axis, WorldJointKind.Hinge);
                    if (jg == null)
                    {
                        report.Problems.Add($"{g.O.Name} {rack.O.Name} ile kavrıyor ama dönebileceği bir mile ya da rulmana takılı değil.");
                        continue;
                    }

                    // The rack slides and the pinion turns in place; or the rack is fixed and the pinion rolls along it,
                    // carrying the carriage its shaft turns in.
                    var jr = MovingJoint(world, rack.O, u, WorldJointKind.Slider);
                    bool rolling = false;
                    if (jr == null)
                    {
                        string? carrier = jg.Value.Child ? jg.Value.Joint.ObjectB : jg.Value.Joint.ObjectA;
                        jr = carrier != null && world.Find(carrier) is { } c ? MovingJoint(world, c, u, WorldJointKind.Slider) : null;
                        rolling = true;
                    }

                    if (jr == null)
                    {
                        report.Problems.Add($"{g.O.Name} {rack.O.Name} ile kavrıyor ama ne kremayer ne de pinyonu taşıyan araba bir kızakta kayıyor.");
                        continue;
                    }

                    if (Coupled(world, jg.Value.Joint, jr.Value.Joint)) continue;
                    // The rack's teeth go with the pinion's pitch circle where they touch: v = r·ω·(n × d)·u, d from the pinion
                    // down to the rack; rolling on a fixed rack, the carriage goes the other way.
                    var d = up * -1;
                    double way = g.Axis.Cross(d).Dot(u) * (rolling ? -1 : 1);
                    (world.Couplings ??= new List<WorldCoupling>()).Add(new WorldCoupling
                    {
                        Id = NextCoupling(world), Kind = WorldCoupling.Gear, JointA = jg.Value.Joint.Id, JointB = jr.Value.Joint.Id, SizeA = g.S.PitchDiameter * 1000,
                        Reverse = Sense(jg.Value, g.Axis) * Sense(jr.Value, u) * Math.Sign(way) < 0, PulleyA = g.O.Id, PulleyB = rack.O.Id
                    });
                    MeshRack(g, rack, d, along);
                    report.Couplings++;
                    report.Links.Add(rolling
                        ? $"{g.O.Name}, {rack.O.Name} üzerinde yuvarlanır: turda {Mm(Math.PI * g.S.PitchDiameter)} mm."
                        : $"{g.O.Name} {rack.O.Name} ile kavrıyor: turda {Mm(Math.PI * g.S.PitchDiameter)} mm.");
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

        /// <summary>
        /// Turns gear B about its own axis so that where the pitch circles touch its tooth space holds A's tooth: the
        /// gears start meshed, not tooth on tooth, and keep meshing as the coupling turns them (Mechanics/GearGeometry.cs).
        /// </summary>
        private static void Mesh(Part a, Part b)
        {
            var n = a.Axis;
            var line = b.Origin - a.Origin;
            line = (line - n * line.Dot(n)).Normalized();
            double Tooth(Part g, Vec3 towards)
            {
                var x = g.O.Pose.Orientation.Rotate(Vec3.UnitX);
                x = (x - n * x.Dot(n)).Normalized();
                return Math.Atan2(n.Dot(towards.Cross(x)), towards.Dot(x));
            }

            double turn = GearGeometry.MeshTurn(a.S.Teeth, Tooth(a, line), b.S.Teeth, Tooth(b, line * -1));
            if (Math.Abs(turn) < 1e-9) return;
            var (roll, pitch, yaw) = (Quat.FromAxisAngle(n, turn) * b.O.Pose.Orientation).ToEuler();
            b.O.Pose.Roll = roll * 180 / Math.PI;
            b.O.Pose.Pitch = pitch * 180 / Math.PI;
            b.O.Pose.Yaw = yaw * 180 / Math.PI;
        }

        /// <summary>
        /// The length of an open belt round two pulleys (m): both straight spans, tangent to the pitch circles, and the arcs
        /// it wraps: 2·√(C² − (r₂ − r₁)²) + r₁(π − 2φ) + r₂(π + 2φ), φ = asin((r₂ − r₁)/C).
        /// </summary>
        public static double BeltLength(double r1, double r2, double centres)
        {
            if (r1 > r2) (r1, r2) = (r2, r1);
            double phi = Math.Asin(Math.Min(1, (r2 - r1) / centres));
            return 2 * Math.Sqrt(Math.Max(0, centres * centres - (r2 - r1) * (r2 - r1))) + r1 * (Math.PI - 2 * phi) + r2 * (Math.PI + 2 * phi);
        }

        /// <summary>Closed GT2 belts sold as loops (mm), so the nearest one at or above the length can be named.</summary>
        public static readonly int[] Gt2Loops = { 100, 110, 112, 122, 140, 158, 160, 180, 188, 200, 202, 220, 232, 240, 250, 260, 280, 300, 310, 350, 400, 420, 450, 500, 550, 600, 610, 700, 752, 800, 852, 1000 };

        /// <summary>
        /// Puts a GT2 belt round two pulleys: their axes parallel and their grooves in one plane. The shafts' joints are
        /// coupled at the ratio of the pitch diameters, turning the same way; the result says the belt's length and the
        /// closed belt to buy, or why it cannot go on.
        /// </summary>
        public static (WorldCoupling? Belt, string Message) Belt(WorldDocument world, WorldObject pulleyA, WorldObject pulleyB)
        {
            var sa = DriveParts.Of(pulleyA);
            var sb = DriveParts.Of(pulleyB);
            if (sa?.Kind != DrivePartKind.Pulley || sb?.Kind != DrivePartKind.Pulley) return (null, "Kayış yalnız iki kasnağın arasına takılır.");
            var (oa, na, _) = DriveParts.AxisOf(pulleyA, sa);
            var (ob, nb, _) = DriveParts.AxisOf(pulleyB, sb);
            if (Math.Abs(Math.Abs(na.Dot(nb)) - 1) > AngleTolerance * AngleTolerance) return (null, $"{pulleyA.Name} ile {pulleyB.Name} paralel değil: kayış kasnaktan çıkar.");
            // The grooves' middles must be in one plane square to the axes.
            var ga = oa + na * (sa.Length / 2);
            var gb = ob + nb * (sb.Length / 2);
            var d = gb - ga;
            double offset = Math.Abs(d.Dot(na)), centres = (d - na * d.Dot(na)).Length;
            if (offset > 0.001) return (null, $"{pulleyA.Name} ile {pulleyB.Name} aynı düzlemde değil ({Mm(offset)} mm kaçık): kayış yürümez.");
            double ra = sa.PitchDiameter / 2, rb = sb.PitchDiameter / 2;
            if (centres <= ra + rb + 0.002) return (null, $"{pulleyA.Name} ile {pulleyB.Name} birbirine çok yakın: araları en az {Mm(ra + rb + 0.002)} mm olmalı.");
            var ja = MovingJoint(world, pulleyA, na, WorldJointKind.Hinge);
            var jb = MovingJoint(world, pulleyB, nb, WorldJointKind.Hinge);
            if (ja == null || jb == null)
                return (null, $"{(ja == null ? pulleyA.Name : pulleyB.Name)} dönebileceği bir mile ya da rulmana takılı değil: önce mekanizmayı kurun.");
            if (Coupled(world, ja.Value.Joint, jb.Value.Joint)) return (null, $"{pulleyA.Name} ile {pulleyB.Name} zaten bağlı.");
            double length = BeltLength(ra, rb, centres);
            var belt = new WorldCoupling
            {
                Id = NextCoupling(world), Kind = WorldCoupling.Belt, JointA = ja.Value.Joint.Id, JointB = jb.Value.Joint.Id,
                SizeA = sa.PitchDiameter * 1000, SizeB = sb.PitchDiameter * 1000, PulleyA = pulleyA.Id, PulleyB = pulleyB.Id,
                // A belt turns both pulleys the same way about a common axis.
                Reverse = Sense(ja.Value, na) * Sense(jb.Value, na) < 0
            };
            (world.Couplings ??= new List<WorldCoupling>()).Add(belt);
            double mm = length * 1000;
            int loop = Gt2Loops.FirstOrDefault(l => l >= mm - 0.5);
            string buy = loop > 0 ? $"; kapalı GT2 kayış {loop} mm ({loop / 2} diş)" + (loop - mm > 1 ? $", gergi için {Mm((loop - mm) / 2000)} mm kaydırın" : string.Empty) : string.Empty;
            return (belt, $"{pulleyA.Name} → {pulleyB.Name}: GT2 kayış {Mm(length)} mm ({(mm / 2).ToString("0.#", CultureInfo.InvariantCulture)} diş){buy}.");
        }

        /// <summary>
        /// Turns the pinion about its axis so that where it touches the rack its tooth sits in a space of the rack's (the
        /// rack's teeth are centred at (k + ½)·πm from its left end, its spaces at k·πm).
        /// </summary>
        private static void MeshRack(Part g, Part rack, Vec3 d, double along)
        {
            var n = g.Axis;
            var x = g.O.Pose.Orientation.Rotate(Vec3.UnitX);
            x = (x - n * x.Dot(n)).Normalized();
            int z = g.S.Teeth;
            double pitchAngle = 2 * Math.PI / z, r = g.S.PitchDiameter / 2, p = rack.S.CircularPitch;
            double tooth = Math.Atan2(n.Dot(d.Cross(x)), d.Dot(x));   // tooth 0 from the line down to the rack
            double off = tooth - pitchAngle * Math.Round(tooth / pitchAngle);
            double tu = n.Cross(d).Dot(rack.Axis);
            double at = along + r * off * tu;
            double error = at - p * Math.Round(at / p);   // from the nearest space
            if (Math.Abs(tu) < 1e-6 || Math.Abs(error) < 1e-9) return;
            double turn = -error / (r * tu);
            var (roll, pitch, yaw) = (Quat.FromAxisAngle(n, turn) * g.O.Pose.Orientation).ToEuler();
            g.O.Pose.Roll = roll * 180 / Math.PI;
            g.O.Pose.Pitch = pitch * 180 / Math.PI;
            g.O.Pose.Yaw = yaw * 180 / Math.PI;
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

        private static readonly DrivePartSpec MotorShaft = new DrivePartSpec { Id = "motor-shaft", Kind = DrivePartKind.Shaft, Bore = 0.005, Diameter = 0.005, Length = 0.022 };

        /// <summary>A stepper's body length (m), from its part's parameters.</summary>
        private static double StepperLength(WorldObject o)
        {
            var lib = PartLibrary.Default;
            var def = lib.Find(o.PartId!);
            if (def == null) return 0.040;
            var p = lib.ResolveParameters(def, o.PresetId != null ? lib.FindPreset(o.PresetId) : null, o.Parameters);
            return p.TryGetValue("length", out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double mm) && mm > 5 ? mm / 1000 : 0.040;
        }

        private static bool Link(WorldDocument world, Report report, WorldObject child, WorldObject parent, WorldJointKind kind, Part rod, double at, string? bearing, double bore, string port = "")
        {
            if (world.Joints.Any(j => (j.ObjectA == child.Id && j.ObjectB == parent.Id) || (j.ObjectA == parent.Id && j.ObjectB == child.Id))) return false;
            var anchor = rod.Point(at);
            var axis = Canonical(rod.Axis);
            world.Joints.Add(new WorldJoint
            {
                Id = world.NextId("J"), Kind = kind, ObjectA = child.Id, ObjectB = parent.Id, PortB = port,
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
                foreach (var j in world.Joints.Where(j => j.Kind == WorldJointKind.Fixed && string.IsNullOrEmpty(j.PortB) && (j.ObjectA == id || j.ObjectB == id)))
                {
                    string? other = j.ObjectA == id ? j.ObjectB : j.ObjectA;
                    if (other != null && group.Add(other)) queue.Enqueue(other);
                }
            }

            var found = world.Joints.FirstOrDefault(j => j.Kind == kind && string.IsNullOrEmpty(j.PortB) && group.Contains(j.ObjectA) != (j.ObjectB != null && group.Contains(j.ObjectB)) &&
                                                         Math.Abs(Math.Abs(AxisOf(j).Dot(axis)) - 1) < AngleTolerance);
            if (found != null) return (found, group.Contains(found.ObjectA));
            // Fixed to a stepper's shaft: it turns with the motor's rotor, the scene's joint "motor:shaft" about the motor's axis.
            if (kind != WorldJointKind.Hinge) return null;
            var mount = world.Joints.FirstOrDefault(j => j.Kind == WorldJointKind.Fixed && j.PortB == "shaft" && group.Contains(j.ObjectA) &&
                                                         world.Find(j.ObjectB)?.PartId == MotionParts.StepperId);
            if (mount == null) return null;
            var motor = world.Find(mount.ObjectB)!;
            var motorAxis = motor.Pose.Orientation.Rotate(Vec3.UnitZ).Normalized();
            if (Math.Abs(Math.Abs(motorAxis.Dot(axis)) - 1) > AngleTolerance) return null;
            return (new WorldJoint { Id = motor.Id + ":shaft", Kind = WorldJointKind.Hinge, ObjectA = mount.ObjectA, ObjectB = motor.Id, AxisX = motorAxis.X, AxisY = motorAxis.Y, AxisZ = motorAxis.Z }, true);
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
