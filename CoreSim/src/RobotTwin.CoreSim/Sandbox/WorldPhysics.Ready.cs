using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [objs] Ready-made parts in the world physics: propellers on motor shafts (thrust
    /// T = k_T·ω² along the hub axis, drag torque Q = k_Q·ω² on the hub that the shaft drive —
    /// and so the motor — has to overcome), omni and mecanum wheels (low-friction rims plus a
    /// traction force only along the roller axis at the contact), and ball casters.
    /// </summary>
    public sealed partial class WorldPhysics
    {
        private sealed class RollerWheel
        {
            public Item It = null!;
            public ReadyPartSpec Spec = null!;
            public HashSet<SceneBody>? Assembly;
            public double Normal;
            public double Traction;
            public bool InContact;
        }

        private sealed class PropMount
        {
            public Item It = null!;
            public ReadyPartSpec Spec = null!;
            public Propeller Prop = null!;
            public Item? Motor;
            public double LastSpeed;
            public double Load;
        }

        private readonly List<RollerWheel> _rollers = new List<RollerWheel>();
        private readonly List<(Item It, ReadyPartSpec Spec)> _propItems = new List<(Item, ReadyPartSpec)>();
        private readonly List<PropMount> _props = new List<PropMount>();

        /// <summary>Body of a ready part whose contact behaviour differs from a plain solid.</summary>
        private SceneBody ReadyBody(WorldObject o, WorldBodyGeometry g, Vec3 size, double mass, Item it)
        {
            var spec = ReadyParts.Of(o);
            switch (g.Shape)
            {
                case "prop-z":
                {
                    // The swept disc (what the spinning blades occupy), with the blades' own inertia.
                    double r = Math.Max(size.X, size.Y) / 2, h = Math.Max(0.002, size.Z);
                    var b = SceneBody.Cylinder(o.Id, r, h, mass, default, "nylon");
                    double iax = spec?.AxialInertia ?? 0.5 * mass * r * r;
                    b.Inertia = new Vec3(iax * 0.5, iax * 0.5, iax);
                    return b;
                }

                case "caster":
                {
                    // Low-friction contact points on the lower half of the ball, the housing above.
                    var b = SceneBody.Box(o.Id, size, mass, default, "ptfe");
                    double rb = (spec?.Diameter ?? size.Z * 0.75) / 2;
                    var ball = new Vec3(0, 0, rb) - it.Centre;
                    var pts = new List<Vec3> { ball - Vec3.UnitZ * rb };
                    foreach (double th in new[] { 0.5, 1.0 })
                    {
                        for (int k = 0; k < 8; k++)
                        {
                            double ph = k * Math.PI / 4 + th;
                            pts.Add(ball + new Vec3(Math.Sin(th) * Math.Cos(ph), Math.Sin(th) * Math.Sin(ph), -Math.Cos(th)) * rb);
                        }
                    }

                    double hx = size.X / 2, hy = size.Y / 2, top = size.Z / 2;
                    foreach (var (sx, sy) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) }) pts.Add(new Vec3(sx * hx, sy * hy, top));
                    b.Shape = CollisionShape.Points(Array.Empty<Vec3>(), pts);
                    return b;
                }

                default:
                {
                    // Omni / mecanum: a ring of free-rolling contact points on each roller row; grip
                    // comes only from the roller traction in DriveReady.
                    double r = Math.Max(size.X, size.Z) / 2, w = size.Y;
                    var b = SceneBody.Cylinder(o.Id, r, w, mass, default, "ptfe");
                    it.ShapeTurn = Quat.FromAxisAngle(Vec3.UnitX, Math.PI / 2);
                    var pts = new List<Vec3>();
                    const int n = 36;
                    foreach (double z in new[] { -w * 0.32, w * 0.32 })
                    {
                        for (int k = 0; k < n; k++)
                        {
                            double a = 2 * Math.PI * (k + (z > 0 ? 0.5 : 0)) / n;
                            pts.Add(new Vec3(r * Math.Cos(a), r * Math.Sin(a), z));
                        }
                    }

                    b.Shape = CollisionShape.Points(Array.Empty<Vec3>(), pts);
                    double iax = spec?.AxialInertia ?? 0.5 * mass * r * r;
                    b.Inertia = new Vec3(iax * 0.55, iax * 0.55, iax);
                    return b;
                }
            }
        }

        private void RegisterReady(WorldObject o, Item it)
        {
            var spec = ReadyParts.Of(o);
            if (spec == null) return;
            if (spec.Kind == ReadyPartKind.OmniWheel || spec.Kind == ReadyPartKind.MecanumWheel) _rollers.Add(new RollerWheel { It = it, Spec = spec });
            else if (spec.Kind == ReadyPartKind.Propeller) _propItems.Add((it, spec));
            else if (spec.IsWheel || spec.Kind == ReadyPartKind.BallCaster)
            {
                // Catalogue inertia (tyre mass sits at the rim).
                double iax = spec.AxialInertia;
                if (spec.IsWheel) it.Body.Inertia = new Vec3(iax * 0.55, iax * 0.55, iax);
            }
        }

        /// <summary>After the joints exist: propellers on their shafts, rotors sized to the motor they belong to.</summary>
        private void FinishReady(WorldDocument world)
        {
            foreach (var (it, spec) in _propItems)
            {
                var prop = new Propeller
                {
                    Body = it.Body,
                    Mount = Vec3.Zero,
                    Axis = Vec3.UnitZ,
                    Diameter = spec.Diameter,
                    ThrustCoefficient = PropellerAero.ThrustPerOmega2(spec),
                    TorqueCoefficient = PropellerAero.TorquePerOmega2(spec),
                    SpinDirection = spec.Hand
                };
                Scene.Propellers.Add(prop);
                var mount = world.Joints.FirstOrDefault(j => string.Equals(j.ObjectA, it.Object.Id, StringComparison.OrdinalIgnoreCase) && j.ObjectB != null);
                var pm = new PropMount { It = it, Spec = spec, Prop = prop, Motor = mount?.ObjectB != null && _items.TryGetValue(mount.ObjectB, out var m) ? m : null };
                _props.Add(pm);
                // The circuit starts from its DC operating point, where an unloaded motor already runs at
                // full speed; a motor carrying a propeller starts from rest together with its hub instead.
                pm.Motor?.Motor?.Reset();
            }

            // A rotor stands for the motor's own spinning parts: it is part of the motor's catalogue mass.
            var done = new HashSet<Item>();
            foreach (var j in world.Joints)
            {
                if (j.ObjectB == null || string.IsNullOrEmpty(j.PortB)) continue;
                var spec = ReadyParts.Of(world.Find(j.ObjectA));
                if (spec == null || !spec.MountsOnShaft || !_items.TryGetValue(j.ObjectB, out var parent)) continue;
                if (parent.Rotor == null || parent.PortName != j.PortB || !done.Add(parent)) continue;
                var rotor = parent.Rotor;
                if (spec.Kind == ReadyPartKind.Propeller)
                {
                    double light = Math.Max(0.0006, Math.Min(rotor.Mass, 0.2 * parent.Body.Mass));
                    if (light < rotor.Mass) rotor.SetMassProperties(MassProperties.Cylinder(light, 0.006, 0.006));
                }

                if (parent.Body.Mass > 2.5 * rotor.Mass && !parent.Body.IsStatic)
                {
                    double k = Math.Max(0.3, Math.Min(1, (parent.Body.Mass - rotor.Mass) / parent.Body.Mass));
                    parent.Body.Mass *= k;
                    parent.Body.Inertia = parent.Body.Inertia * k;
                }
            }
        }

        /// <summary>Every step: propeller speeds from their hubs, roller traction of omni/mecanum wheels.</summary>
        private void DriveReady()
        {
            foreach (var pm in _props)
            {
                var b = pm.It.Body;
                double w = b.AngularVelocity.Dot(b.Orientation.Rotate(Vec3.UnitZ));
                pm.Prop.AngularVelocity = pm.Spec.Hand * w;
            }

            foreach (var rw in _rollers) RollerTraction(rw);
        }

        /// <summary>
        /// A coreless motor and its propeller: the motor model gets the hub's inertia as load inertia, so
        /// what it is told as load torque is the drive torque minus the part that only accelerated the
        /// hub — the aerodynamic drag (plus anything the blades hit). Feeding back the raw drive torque
        /// would count the inertia twice and make the light, fast shaft chatter.
        /// </summary>
        private void FeedBackReady()
        {
            foreach (var pm in _props)
            {
                var parent = pm.Motor;
                if (parent?.Motor == null || parent.RotorJoint == null || parent.Rotor == null) continue;
                var j = parent.RotorJoint;
                var m = parent.Motor;
                double inertia = parent.Rotor.Inertia.Z + pm.It.Body.Inertia.Z;
                m.LoadInertia = inertia;
                double alpha = (j.Speed - pm.LastSpeed) / Step;
                pm.LastSpeed = j.Speed;
                double load = j.DriveTorque - inertia * alpha;
                pm.Load += (load - pm.Load) * 0.35;
                m.LoadTorque = pm.Load;
            }
        }

        private void RollerTraction(RollerWheel rw)
        {
            var b = rw.It.Body;
            rw.Assembly ??= AssemblyOf(b);
            var axle = b.Orientation.Rotate(Vec3.UnitZ);
            var down = -Vec3.UnitZ;
            var d = down - axle * axle.Dot(down);
            rw.InContact = false;
            rw.Traction = 0;
            if (d.Length < 0.3) return;   // lying flat: no roller underneath
            d = d.Normalized();
            double r = rw.Spec.Radius;
            var asm = rw.Assembly;
            if (!Scene.Raycast(b.Position, d, r + 0.004, out var hit, x => !asm.Contains(x))) return;
            if (hit.Distance > r + 0.0015) return;
            var n = hit.Normal;
            if (n.Dot(d) > 0) n = -n;
            var roll = axle.Cross(n);
            if (roll.Length < 1e-6) return;
            roll = roll.Normalized();
            var side = n.Cross(roll).Normalized();
            // The rollers can only push along their own axis where they touch: tangent to the rim for an
            // omni wheel, 45° between rim and axle for a mecanum wheel.
            double ang = rw.Spec.RollerAngleDeg * Math.PI / 180;
            var t = rw.Spec.Kind == ReadyPartKind.MecanumWheel
                ? (roll * Math.Sin(ang) + side * (rw.Spec.Hand * Math.Cos(ang))).Normalized()
                : roll;
            double normal = Math.Max(0, b.PeakContactForce);
            rw.Normal = normal;
            rw.InContact = true;
            if (normal <= 0) return;
            var v = b.PointVelocity(hit.Point);
            if (hit.Body != null && !hit.Body.IsStatic) v -= hit.Body.PointVelocity(hit.Point);
            double slip = v.Dot(t);
            double mu = hit.Body == null ? Scene.GroundFriction ?? 0.9 : 0.8;
            // Coulomb friction regularised over ±1 cm/s of slip.
            double f = -Math.Max(-1, Math.Min(1, slip / 0.01)) * mu * normal;
            rw.Traction = f;
            b.AddForceAtPoint(t * f, hit.Point);
            if (hit.Body != null && !hit.Body.IsStatic) hit.Body.AddForceAtPoint(t * -f, hit.Point);
        }

        private HashSet<SceneBody> AssemblyOf(SceneBody b)
        {
            var set = new HashSet<SceneBody> { b };
            foreach (var (x, y) in Scene.NoCollide)
            {
                if (x == b) set.Add(y);
                else if (y == b) set.Add(x);
            }

            return set;
        }

        // ================================================================ readouts

        /// <summary>Propeller on an object: speed (rpm, signed: + = its working direction), thrust (N), drag torque (N·m), shaft power (W).</summary>
        public (double Rpm, double Thrust, double Torque, double Power)? PropReadout(string objectId)
        {
            foreach (var pm in _props)
            {
                if (!string.Equals(pm.It.Object.Id, objectId, StringComparison.OrdinalIgnoreCase)) continue;
                double w = pm.Prop.AngularVelocity;
                return (w * 60 / (2 * Math.PI), pm.Prop.Thrust, pm.Prop.DragTorque, Math.Abs(pm.Prop.DragTorque * w));
            }

            return null;
        }

        /// <summary>Spin of a ready part about its hub axis (rad/s, absolute), or NaN.</summary>
        public double HubSpeed(string objectId)
        {
            if (!_items.TryGetValue(objectId, out var it)) return double.NaN;
            var spec = ReadyParts.Of(it.Object);
            if (spec == null) return double.NaN;
            var axis = it.Body.Orientation.Rotate(Vec3.UnitZ);
            return it.Body.AngularVelocity.Dot(axis);
        }

        /// <summary>Omni/mecanum wheel: normal load (N) and roller traction force (N) in the last step.</summary>
        public (double Normal, double Traction, bool Contact)? RollerReadout(string objectId)
        {
            foreach (var rw in _rollers)
            {
                if (string.Equals(rw.It.Object.Id, objectId, StringComparison.OrdinalIgnoreCase)) return (rw.Normal, rw.Traction, rw.InContact);
            }

            return null;
        }

        /// <summary>Linear velocity of an object's body (m/s), for Doppler of its sounds.</summary>
        public Vec3 VelocityOf(string objectId) => _items.TryGetValue(objectId, out var it) ? it.Body.Velocity : Vec3.Zero;
    }
}
