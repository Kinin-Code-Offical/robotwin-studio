using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Robotics
{
    /// <summary>[mobile] How a driven wheel grips the floor.</summary>
    public enum DriveWheelKind
    {
        /// <summary>A tyre: rolls forward, does not slide sideways.</summary>
        Standard,

        /// <summary>Omni wheel: rollers across the rim let it slide along its axle.</summary>
        Omni,

        /// <summary>Mecanum wheel: rollers at 45° let it slide along one diagonal.</summary>
        Mecanum
    }

    /// <summary>[mobile] A wheel turned by a motor shaft, in the vehicle's base frame as built (x forward, y left, z up).</summary>
    public sealed class DriveWheel
    {
        public string MotorId { get; internal set; } = string.Empty;
        public string WheelId { get; internal set; } = string.Empty;
        public string Name { get; internal set; } = string.Empty;
        public DriveWheelKind Kind { get; internal set; }
        public double Radius { get; internal set; }

        /// <summary>The wheel's centre (m).</summary>
        public Vec3 Position { get; internal set; }

        /// <summary>The shaft's axis of positive rotation (horizontal unit vector).</summary>
        public Vec3 Axle { get; internal set; }

        /// <summary>Where the wheel's centre goes over the floor when the shaft turns positively: Axle × z.</summary>
        public Vec3 Roll => Axle.Cross(Vec3.UnitZ);

        /// <summary>The direction the wheel cannot slip along: forward for a tyre and an omni wheel, across its rollers for a mecanum wheel.</summary>
        public Vec3 Grip { get; internal set; }

        public bool Left => Position.Y > 0;
    }

    /// <summary>[mobile] A planar velocity of a vehicle's base: forward, leftward (m/s) and turning rate (rad/s, counter-clockwise).</summary>
    public readonly struct PlanarTwist
    {
        public PlanarTwist(double vx, double vy, double w)
        {
            Vx = vx;
            Vy = vy;
            W = w;
        }

        public double Vx { get; }
        public double Vy { get; }
        public double W { get; }

        public double Speed => Math.Sqrt(Vx * Vx + Vy * Vy);

        /// <summary>Turning radius of the base origin (m): +∞ going straight, 0 turning on the spot.</summary>
        public double TurnRadius => Math.Abs(W) < 1e-9 ? double.PositiveInfinity : Speed / Math.Abs(W);
    }

    /// <summary>
    /// [mobile] Kinematics of a wheeled robot built in a world: the motors whose shafts carry wheels, their places and
    /// how each grips. One model covers two-wheel differential drive, four-wheel skid steering, mecanum and omni
    /// bases: each wheel cannot slip along its grip direction, so a body velocity gives every shaft's speed (inverse)
    /// and the shafts' speeds give the body velocity by least squares (forward). Encoder odometry is built on it.
    /// </summary>
    public sealed class WorldVehicle
    {
        private WorldVehicle()
        {
        }

        /// <summary>The chassis: what the motors are fixed to.</summary>
        public string BaseId { get; private set; } = string.Empty;

        /// <summary>The chassis pose as built (its x forward, y left).</summary>
        public Transform3 BaseBuilt { get; private set; } = Transform3.Identity;

        public IReadOnlyList<DriveWheel> Wheels { get; private set; } = Array.Empty<DriveWheel>();

        /// <summary>True when the wheels can move the base sideways as well (mecanum, omni): any planar velocity is possible.</summary>
        public bool Holonomic { get; private set; }

        /// <summary>True when standard wheels on more than one axle must slide sideways to turn (skid steering).</summary>
        public bool Skids { get; private set; }

        /// <summary>Distance between the left and right wheels' mean lines (m), and between the front and rear wheels (m).</summary>
        public double Track { get; private set; }

        public double Wheelbase { get; private set; }

        /// <summary>How the vehicle drives, in Turkish ("diferansiyel sürüş, 2 tekerlek").</summary>
        public string Layout { get; private set; } = string.Empty;

        // ================================================================ finding the wheels

        /// <summary>
        /// The vehicle <paramref name="objectId"/> is part of, or null when fewer than two motors with wheels on their
        /// shafts are joined to it. The chassis is the object most of those motors are fixed to.
        /// </summary>
        public static WorldVehicle? Build(WorldDocument world, Func<WorldObject, WorldBodyGeometry> geometry, string objectId)
        {
            if (world.Find(objectId) == null) return null;
            var geo = new Dictionary<string, WorldBodyGeometry>(StringComparer.OrdinalIgnoreCase);
            WorldBodyGeometry G(WorldObject o)
            {
                if (!geo.TryGetValue(o.Id, out var g)) geo[o.Id] = g = geometry(o);
                return g;
            }

            // The assembly: everything joined to the object.
            var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { objectId };
            var queue = new Queue<string>();
            queue.Enqueue(objectId);
            while (queue.Count > 0)
            {
                string at = queue.Dequeue();
                foreach (var j in world.Joints)
                {
                    string? other = string.Equals(j.ObjectA, at, StringComparison.OrdinalIgnoreCase) ? j.ObjectB
                        : string.Equals(j.ObjectB, at, StringComparison.OrdinalIgnoreCase) ? j.ObjectA : null;
                    if (other != null && world.Find(other) != null && members.Add(other)) queue.Enqueue(other);
                }
            }

            // Motors with a wheel on a horizontal shaft.
            var found = new List<(WorldObject Motor, WorldObject Wheel, Vec3 Axle)>();
            foreach (var m in members.Select(world.Find).Where(o => o != null).Select(o => o!))
            {
                if (G(m) is not { PortName: "shaft" } mg) continue;
                var axle = m.Pose.Orientation.Rotate(mg.PortAxis).Normalized();
                if (Math.Abs(axle.Z) > 0.5) continue;   // a propeller or a vertical spindle
                var j = world.Joints.FirstOrDefault(x => string.Equals(x.ObjectB, m.Id, StringComparison.OrdinalIgnoreCase) && x.PortB == "shaft");
                if (j == null || world.Find(j.ObjectA) is not { } wheel) continue;
                if (ReadyParts.Of(wheel) is { } spec && spec.Kind is not (ReadyPartKind.Wheel or ReadyPartKind.OmniWheel or ReadyPartKind.MecanumWheel)) continue;
                found.Add((m, wheel, axle));
            }

            if (found.Count < 2) return null;

            // The chassis: what most of the motors are screwed to; else the heaviest part that is not a wheel or motor.
            var votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var (m, _, _) in found)
            {
                foreach (var j in world.Joints.Where(x => string.IsNullOrEmpty(x.PortB) && x.ObjectB != null))
                {
                    string? other = string.Equals(j.ObjectA, m.Id, StringComparison.OrdinalIgnoreCase) ? j.ObjectB
                        : string.Equals(j.ObjectB, m.Id, StringComparison.OrdinalIgnoreCase) ? j.ObjectA : null;
                    if (other != null) votes[other] = votes.TryGetValue(other, out int count) ? count + 1 : 1;
                }
            }

            var skip = new HashSet<string>(found.SelectMany(f => new[] { f.Motor.Id, f.Wheel.Id }), StringComparer.OrdinalIgnoreCase);
            var chassis = votes.Count > 0
                ? world.Find(votes.OrderByDescending(v => v.Value).ThenByDescending(v => Mass(world.Find(v.Key)!)).First().Key)!
                : members.Where(id => !skip.Contains(id)).Select(world.Find).Where(o => o != null).OrderByDescending(o => Mass(o!)).FirstOrDefault() ?? found[0].Motor;
            double Mass(WorldObject o) => o.MassKg ?? G(o).MassKg;

            var vehicle = new WorldVehicle { BaseId = chassis.Id, BaseBuilt = new Transform3(chassis.Pose.Position, chassis.Pose.Orientation) };
            var toBase = vehicle.BaseBuilt.Inverse();
            var wheels = new List<DriveWheel>();
            foreach (var (m, wheel, worldAxle) in found)
            {
                var wg = G(wheel);
                var spec = ReadyParts.Of(wheel);
                var axleBase = toBase.ApplyVector(worldAxle);
                var axle = new Vec3(axleBase.X, axleBase.Y, 0).Normalized();
                double radius = spec?.Radius
                                ?? (wheel.Kind == WorldObjectKind.Body && wheel.Shape is "wheel" or "cylinder" or "sphere" ? wheel.SizeX / 2 : Across(wg, wheel, worldAxle));
                var w = new DriveWheel
                {
                    MotorId = m.Id,
                    WheelId = wheel.Id,
                    Name = wheel.Name.Length > 0 ? wheel.Name : m.Name,
                    Radius = radius,
                    Position = toBase.Apply(wheel.Pose.ToWorld((wg.Min + wg.Max) * 0.5)),
                    Axle = axle,
                    Kind = spec?.Kind switch { ReadyPartKind.OmniWheel => DriveWheelKind.Omni, ReadyPartKind.MecanumWheel => DriveWheelKind.Mecanum, _ => DriveWheelKind.Standard },
                };
                w.Grip = w.Roll;
                if (w.Kind == DriveWheelKind.Mecanum && spec != null)
                {
                    // As the physics pushes: the roller's axis at the contact, from the wheel's own axle (its −y).
                    var own = toBase.ApplyVector(wheel.Pose.Orientation.Rotate(-Vec3.UnitY));
                    own = new Vec3(own.X, own.Y, 0).Normalized();
                    double a = spec.RollerAngleDeg * Math.PI / 180;
                    w.Grip = (own.Cross(Vec3.UnitZ) * Math.Sin(a) + own * (spec.Hand * Math.Cos(a))).Normalized();
                }

                wheels.Add(w);
            }

            vehicle.Wheels = wheels;
            var left = wheels.Where(x => x.Left).ToList();
            var right = wheels.Where(x => !x.Left).ToList();
            vehicle.Track = left.Count > 0 && right.Count > 0 ? left.Average(x => x.Position.Y) - right.Average(x => x.Position.Y) : 0;
            vehicle.Wheelbase = wheels.Max(x => x.Position.X) - wheels.Min(x => x.Position.X);

            // Holonomic when the grip directions alone pin down all three velocities.
            var rows = wheels.Select(Row).ToList();
            vehicle.Holonomic = wheels.Any(x => x.Kind != DriveWheelKind.Standard) && Rank3(rows);
            var standard = wheels.Where(x => x.Kind == DriveWheelKind.Standard).ToList();
            vehicle.Skids = !vehicle.Holonomic && standard.Count > 0 && standard.Max(x => x.Position.X) - standard.Min(x => x.Position.X) > 0.25 * Math.Max(0.01, vehicle.Track);
            int n = wheels.Count;
            vehicle.Layout = vehicle.Holonomic
                ? (wheels.All(x => x.Kind == DriveWheelKind.Mecanum) ? $"mecanum, {n} tekerlek: her yöne gider" : $"omni, {n} tekerlek: her yöne gider")
                : vehicle.Skids ? $"kızaklı (paletli gibi) dönüş, {n} tekerlek: dönerken tekerlekler yana kayar" : $"diferansiyel sürüş, {n} tekerlek";
            return vehicle;
        }

        private static double Across(WorldBodyGeometry g, WorldObject o, Vec3 worldAxle)
        {
            // Half the wheel's largest size across its axle.
            var local = o.Pose.Orientation.Conjugate.Rotate(worldAxle);
            var size = g.Max - g.Min;
            double best = 0;
            foreach (var (axis, s) in new[] { (Vec3.UnitX, size.X), (Vec3.UnitY, size.Y), (Vec3.UnitZ, size.Z) })
            {
                if (Math.Abs(axis.Dot(local)) < 0.5) best = Math.Max(best, s);
            }

            return Math.Max(0.005, best / 2);
        }

        /// <summary>Coefficients of (vx, vy, ω) in the velocity of a wheel's contact along a direction.</summary>
        private static double[] Along(Vec3 t, Vec3 p) => new[] { t.X, t.Y, -t.X * p.Y + t.Y * p.X };

        private static double[] Row(DriveWheel w) => Along(w.Grip, w.Position);

        private static bool Rank3(List<double[]> rows)
        {
            var a = new double[3, 3];
            double scale = 0;
            foreach (var r in rows)
            {
                for (int i = 0; i < 3; i++)
                {
                    for (int j = 0; j < 3; j++) a[i, j] += r[i] * r[j];
                }
            }

            for (int i = 0; i < 3; i++) scale = Math.Max(scale, a[i, i]);
            return scale > 0 && ArmKinematics.Determinant(a) > 1e-6 * scale * scale * scale;
        }

        // ================================================================ kinematics

        /// <summary>
        /// Inverse kinematics: each shaft's speed (rad/s) for a base velocity. Standard wheels cannot follow a
        /// sideways velocity: they get the forward part only (see <see cref="Achievable"/>).
        /// </summary>
        public double[] ShaftRates(PlanarTwist twist)
        {
            var rates = new double[Wheels.Count];
            for (int i = 0; i < Wheels.Count; i++)
            {
                var w = Wheels[i];
                var r = Row(w);
                double along = r[0] * twist.Vx + r[1] * twist.Vy + r[2] * twist.W;
                rates[i] = along / (w.Radius * w.Roll.Dot(w.Grip));
            }

            return rates;
        }

        /// <summary>The part of a wanted velocity the wheels can give without sliding: everything for a holonomic base, no sideways speed otherwise.</summary>
        public PlanarTwist Achievable(PlanarTwist twist) => Holonomic ? twist : new PlanarTwist(twist.Vx, 0, twist.W);

        /// <summary>
        /// Forward kinematics: the base velocity the shafts' speeds (rad/s) give without slip, by least squares over every
        /// wheel's grip (and, for tyres, their not sliding sideways). The same holds for shaft turns (rad) → displacement.
        /// </summary>
        public PlanarTwist Twist(IReadOnlyList<double> rates)
        {
            var ata = new double[3, 3];
            var atb = new double[3];
            void Add(double[] r, double b)
            {
                for (int i = 0; i < 3; i++)
                {
                    atb[i] += r[i] * b;
                    for (int j = 0; j < 3; j++) ata[i, j] += r[i] * r[j];
                }
            }

            for (int i = 0; i < Wheels.Count; i++)
            {
                var w = Wheels[i];
                Add(Row(w), rates[i] * w.Radius * w.Roll.Dot(w.Grip));
                if (w.Kind == DriveWheelKind.Standard) Add(Along(w.Axle, w.Position), 0);
            }

            // Exact when the wheels pin the motion down; a whisker of damping for a layout that does not.
            var x = ArmKinematics.Solve(ata, atb);
            if (x == null)
            {
                double scale = Math.Max(1e-12, Math.Max(ata[0, 0], Math.Max(ata[1, 1], ata[2, 2])));
                for (int i = 0; i < 3; i++) ata[i, i] += 1e-9 * scale;
                x = ArmKinematics.Solve(ata, atb) ?? new double[3];
            }

            return new PlanarTwist(x[0], x[1], x[2]);
        }

        // ================================================================ the physics

        /// <summary>Each shaft's angle over turns (rad), as an encoder counts it.</summary>
        public double[] ShaftAngles(WorldPhysics physics) => Wheels.Select(w => physics.PortJoint(w.MotorId)?.Angle ?? 0).ToArray();

        /// <summary>Each shaft's speed (rad/s).</summary>
        public double[] MeasuredRates(WorldPhysics physics) => Wheels.Select(w => physics.PortJoint(w.MotorId)?.Speed ?? 0).ToArray();

        /// <summary>The chassis' actual planar velocity in its own frame, at its origin (what a motion-capture system would measure).</summary>
        public PlanarTwist MeasuredTwist(WorldPhysics physics)
        {
            var body = physics.Scene.FindBody(BaseId);
            if (body == null || physics.PoseOf(BaseId) is not { } pose) return default;
            var v = pose.Orientation.Conjugate.Rotate(body.PointVelocity(pose.Position));
            var w = pose.Orientation.Conjugate.Rotate(body.AngularVelocity);
            return new PlanarTwist(v.X, v.Y, w.Z);
        }

        /// <summary>The chassis' actual position on the floor (m) and heading (rad, its x axis from world x).</summary>
        public (double X, double Y, double Heading)? PlanarPose(WorldPhysics? physics)
        {
            Vec3 p;
            Quat q;
            if (physics?.PoseOf(BaseId) is { } pose)
            {
                p = pose.Position;
                q = pose.Orientation;
            }
            else
            {
                p = BaseBuilt.P;
                q = BaseBuilt.Q;
            }

            var fwd = q.Rotate(Vec3.UnitX);
            return (p.X, p.Y, Math.Atan2(fwd.Y, fwd.X));
        }

        /// <summary>The top speed (m/s) straight ahead when every shaft turns at most <paramref name="maxRate"/> (rad/s).</summary>
        public double TopSpeed(double maxRate)
        {
            var unit = ShaftRates(new PlanarTwist(1, 0, 0));
            double worst = unit.Max(Math.Abs);
            return worst < 1e-12 ? 0 : maxRate / worst;
        }
    }

    /// <summary>
    /// [mobile] Dead reckoning from wheel encoders: each update takes the shafts' angles, turns their change into the
    /// chassis' displacement with the vehicle's forward kinematics and adds it along the mid-step heading. What it
    /// cannot see (wheel slip, skidding, bumps) builds up as drift against the true pose.
    /// </summary>
    public sealed class WheelOdometry
    {
        private double[]? _last;

        public double X { get; private set; }
        public double Y { get; private set; }
        public double Heading { get; private set; }

        /// <summary>Path length travelled by the estimate (m).</summary>
        public double Distance { get; private set; }

        public void Reset(double x, double y, double heading)
        {
            X = x;
            Y = y;
            Heading = heading;
            Distance = 0;
            _last = null;
        }

        /// <summary>Adds the motion since the last call (the first call only takes the angles).</summary>
        public void Update(WorldVehicle vehicle, IReadOnlyList<double> shaftAngles)
        {
            if (_last == null || _last.Length != shaftAngles.Count)
            {
                _last = shaftAngles.ToArray();
                return;
            }

            var delta = new double[shaftAngles.Count];
            for (int i = 0; i < delta.Length; i++) delta[i] = shaftAngles[i] - _last[i];
            _last = shaftAngles.ToArray();
            var d = vehicle.Twist(delta);
            double mid = Heading + d.W / 2;
            X += d.Vx * Math.Cos(mid) - d.Vy * Math.Sin(mid);
            Y += d.Vx * Math.Sin(mid) + d.Vy * Math.Cos(mid);
            Heading += d.W;
            Distance += d.Speed;
        }
    }
}
