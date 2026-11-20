using System;
using System.Collections.Generic;
using System.Globalization;
using RobotTwin.CoreSim.Aero;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [aero] Wings, tails and fins drawn in the world fly: every physics step each lifting surface
    /// sees the real local airflow at its own pose (the body's velocity and rotation minus the wind),
    /// the strip / lifting-line model (<see cref="LiftingSurface"/>) gives lift, drag and pitching
    /// moment, and they act on the body at the surface. Control surfaces (elevator, rudder, flap,
    /// ailerons) follow a fixed deflection or a servo's horn angle through a pushrod ratio, so a
    /// servo command really pitches / rolls / yaws the aircraft.
    /// </summary>
    /// <remarks>
    /// A wing object is a body with shape "wing": SizeX = root chord (x forward, leading edge at +x),
    /// SizeY = span, SizeZ = thickness; parameters (all optional): taper ("tipchord" m), "sweep",
    /// "dihedral", "incidence" (degrees), "airfoil" (clarky, naca0012, naca2412, flat), "symmetric"
    /// (false for a single fin), "control" (elevator, rudder, flap, aileron, none), "control.chord"
    /// (flap chord / chord), "control.deflection" (degrees), "control.servo" (servo object id),
    /// "control.gain" (surface degrees per servo degree), "control.offset" (degrees),
    /// "downwash" (id of the wing whose downwash a tail flies in).
    /// </remarks>
    public sealed partial class WorldPhysics
    {
        private sealed class WingLink
        {
            public Item Item = null!;
            public LiftingSurface Surface = null!;
            public List<(ControlSurface Control, double Sign)> Controls = new List<(ControlSurface, double)>();
            public string? ServoId;
            public Analog.Devices.Servo? ServoDevice;
            public double Gain = 1, Offset, Fixed;
            public AeroLoads Last;
        }

        private readonly List<WingLink> _wings = new List<WingLink>();

        public static bool IsWing(WorldObject o) => o.Kind == WorldObjectKind.Body && string.Equals(o.Shape, "wing", StringComparison.OrdinalIgnoreCase);

        private static double Param(WorldObject o, string key, double fallback) =>
            o.Parameters.TryGetValue(key, out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

        private void BuildWings()
        {
            _wings.Clear();
            foreach (var it in _items.Values)
            {
                var o = it.Object;
                if (!IsWing(o) || it.Body == null) continue;
                double chord = Math.Max(0.005, o.SizeX), span = Math.Max(0.01, o.SizeY), thick = Math.Max(0.0005, o.SizeZ);
                bool symmetric = !o.Parameters.TryGetValue("symmetric", out var sym) || !string.Equals(sym, "false", StringComparison.OrdinalIgnoreCase);
                var surface = new LiftingSurface
                {
                    Name = o.Name,
                    Root = new Vec3(chord / 4, 0, thick / 2),   // quarter chord, mid thickness (object origin: base centre)
                    Span = span,
                    RootChord = chord,
                    TipChord = Math.Max(0.003, Param(o, "tipchord", chord)),
                    Sweep = Param(o, "sweep", 0) * Math.PI / 180,
                    Dihedral = Param(o, "dihedral", 0) * Math.PI / 180,
                    Incidence = Param(o, "incidence", 0) * Math.PI / 180,
                    Symmetric = symmetric,
                    StripsPerSide = 6,
                    Airfoil = ParametricAirfoil.Preset(o.Parameters.TryGetValue("airfoil", out var af) ? af : "clarky")
                };
                if (!symmetric)
                {
                    // A single panel standing on a fuselage (vertical fin): span up, normal sideways.
                    surface.SpanAxis = Vec3.UnitZ;
                    surface.Root = new Vec3(chord / 4, 0, 0);
                }

                var link = new WingLink { Item = it, Surface = surface };
                string kind = o.Parameters.TryGetValue("control", out var k) ? k.Trim().ToLowerInvariant() : "none";
                double cr = Param(o, "control.chord", 0.3);
                if (kind == "aileron")
                {
                    var left = new ControlSurface { Name = "Sol kanatçık", EtaStart = -1, EtaEnd = -0.5, ChordRatio = cr };
                    var right = new ControlSurface { Name = "Sağ kanatçık", EtaStart = 0.5, EtaEnd = 1, ChordRatio = cr };
                    surface.Controls.Add(left);
                    surface.Controls.Add(right);
                    link.Controls.Add((left, 1));
                    link.Controls.Add((right, -1));
                }
                else if (kind != "none" && kind.Length > 0)
                {
                    var c = new ControlSurface { Name = kind, EtaStart = symmetric ? -1 : 0, EtaEnd = 1, ChordRatio = cr };
                    surface.Controls.Add(c);
                    link.Controls.Add((c, 1));
                }

                link.ServoId = o.Parameters.TryGetValue("control.servo", out var sv) && sv.Length > 0 ? sv : null;
                if (link.ServoId != null && _circuit != null && _circuit.ComponentRef(link.ServoId) is { } sref)
                    foreach (var d in _circuit.Compiled.DevicesOf(sref))
                        if (d is Analog.Devices.Servo servo) link.ServoDevice = servo;
                link.Gain = Param(o, "control.gain", 1);
                link.Offset = Param(o, "control.offset", 0);
                link.Fixed = Param(o, "control.deflection", 0);
                // The surface's own aerodynamics replace the body's generic box drag.
                it.Body.DragArea = Vec3.Zero;
                _wings.Add(link);
            }

            // Tails fly in the downwash of the wing named in "downwash".
            foreach (var w in _wings)
            {
                if (!w.Item.Object.Parameters.TryGetValue("downwash", out var src)) continue;
                foreach (var other in _wings)
                    if (string.Equals(other.Item.Object.Id, src, StringComparison.OrdinalIgnoreCase)) w.Surface.DownwashSource = other.Surface;
            }
        }

        /// <summary>
        /// Hand launch: an object with a "launch" parameter (m/s) starts moving along its own +x with
        /// that speed, together with everything joined to it (the whole aircraft).
        /// </summary>
        private void ApplyLaunches(WorldDocument world)
        {
            foreach (var it in _items.Values)
            {
                var o = it.Object;
                double speed = Param(o, "launch", 0);
                if (speed == 0 || it.Body == null || PoseOf(o.Id) is not { } pose) continue;
                var v = pose.Orientation.Rotate(Vec3.UnitX) * speed;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { o.Id };
                var queue = new Queue<string>();
                queue.Enqueue(o.Id);
                while (queue.Count > 0)
                {
                    string id = queue.Dequeue();
                    if (_items.TryGetValue(id, out var x) && x.Body != null && !x.Body.IsStatic) x.Body.Velocity = v;
                    foreach (var j in world.Joints)
                    {
                        string? other = j.ObjectA == id ? j.ObjectB : j.ObjectB == id ? j.ObjectA : null;
                        if (other != null && seen.Add(other)) queue.Enqueue(other);
                    }
                }
            }
        }

        /// <summary>Control surface deflection of a wing object right now (degrees), or 0.</summary>
        public double WingDeflection(string objectId)
        {
            foreach (var w in _wings)
                if (string.Equals(w.Item.Object.Id, objectId, StringComparison.OrdinalIgnoreCase))
                    return w.Controls.Count > 0 ? w.Controls[0].Control.Deflection * 180 / Math.PI * w.Controls[0].Sign : 0;
            return 0;
        }

        /// <summary>Last aerodynamic loads of a wing object (object frame), or null.</summary>
        public AeroLoads? WingLoads(string objectId)
        {
            foreach (var w in _wings)
                if (string.Equals(w.Item.Object.Id, objectId, StringComparison.OrdinalIgnoreCase)) return w.Last;
            return null;
        }

        private void StepWings()
        {
            if (_wings.Count == 0) return;
            double rho = Scene.AirDensity;
            if (rho <= 0) return;   // vacuum: no lift, no drag
            foreach (var w in _wings)
            {
                var body = w.Item.Body;
                if (body == null || PoseOf(w.Item.Object.Id) is not { } pose) continue;
                var (p, q) = pose;
                double delta = w.Fixed;
                if (w.ServoId != null)
                {
                    // Horn angle from the physics when something is mounted on the horn, else the
                    // servo's own geared output (a pushrod carries no body of its own).
                    bool onPhysics = _items.TryGetValue(w.ServoId, out var sit) && sit.RotorJoint != null;
                    double horn = onPhysics ? PortAngle(w.ServoId) * 180 / Math.PI : w.ServoDevice != null ? w.ServoDevice.AngleDeg - 90 : 0;
                    delta = w.Offset + w.Gain * horn;
                }
                foreach (var (c, sign) in w.Controls)
                    c.Deflection = Math.Max(-c.MaxDeflection, Math.Min(c.MaxDeflection, sign * delta * Math.PI / 180));
                var v = body.Velocity + body.AngularVelocity.Cross(p - body.Position) - Scene.WindAt(p);
                var state = new AeroState(q.InverseRotate(v), q.InverseRotate(body.AngularVelocity), rho, 343, 1.81e-5);
                var loads = w.Surface.Evaluate(state);
                w.Last = loads;
                body.AddForceAtPoint(q.Rotate(loads.Force), p);
                body.AddTorque(q.Rotate(loads.Moment));
            }
        }
    }
}
