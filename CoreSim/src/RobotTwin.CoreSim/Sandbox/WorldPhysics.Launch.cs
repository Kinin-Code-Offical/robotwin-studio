using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Aero;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [launch] What a launch needs besides the motor:
    /// <list type="bullet">
    /// <item>Switches and buttons in the world that close and open on a schedule — "close.at" / "open.at"
    /// in seconds, like a launch controller's countdown — or when <see cref="SetSwitch"/> is called (a click
    /// in the world, the automation API).</item>
    /// <item>Recovery parachutes on any body ("parachute" = canopy diameter in m): deployed at apogee
    /// ("parachute.deploy" = "apogee", the default — the altimeter sees the climb stop) or at a set time
    /// (seconds). The canopy fills as Knacke's model gives (<see cref="ParachuteModel"/>); its drag acts at
    /// the body's top, so the opening shock and the swing under the canopy come out of the rigid-body physics.</item>
    /// </list>
    /// </summary>
    public sealed partial class WorldPhysics
    {
        private readonly Dictionary<string, bool> _switchOverride = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private sealed class Chute
        {
            public Item Item = null!;
            public ParachuteModel Model = null!;
            public double DeployAt = double.NaN;   // s; NaN = at apogee
            public double StartZ, PeakZ;
            public bool FullReported;
            public Vec3 Attach, Pull;              // world: where the lines meet the body, which way the canopy pulls
        }

        private List<Chute>? _chutes;

        private static bool TryNum(WorldObject o, string key, out double v)
        {
            v = double.NaN;
            return o.Parameters.TryGetValue(key, out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        /// <summary>Closes or opens a switch or button in the world (and keeps it so). False when the object has none.</summary>
        public bool SetSwitch(string objectId, bool closed)
        {
            if (_circuit?.ComponentRef(objectId) is not { } r) return false;
            var switches = _circuit.Compiled.DevicesOf(r).OfType<Switch>().ToList();
            if (switches.Count == 0) return false;
            foreach (var s in switches) s.Closed = closed;
            _switchOverride[objectId] = closed;
            return true;
        }

        /// <summary>Whether a world switch or button is closed (null when the object has none).</summary>
        public bool? SwitchState(string objectId)
        {
            if (_circuit?.ComponentRef(objectId) is not { } r) return null;
            var s = _circuit.Compiled.DevicesOf(r).OfType<Switch>().FirstOrDefault();
            return s?.Closed;
        }

        private void StepSwitches()
        {
            if (_circuit == null) return;
            foreach (var it in _items.Values)
            {
                var o = it.Object;
                if (o.Kind != WorldObjectKind.Component || _switchOverride.ContainsKey(o.Id)) continue;
                bool hasClose = TryNum(o, "close.at", out double closeAt), hasOpen = TryNum(o, "open.at", out double openAt);
                if (!hasClose && !hasOpen) continue;
                bool closed = (!hasClose || Scene.Time >= closeAt) && !(hasOpen && Scene.Time >= openAt);
                if (_circuit.ComponentRef(o.Id) is not { } r) continue;
                foreach (var s in _circuit.Compiled.DevicesOf(r).OfType<Switch>())
                {
                    if (s.Closed == closed) continue;
                    s.Closed = closed;
                    Scene.Report(new PhysicsEvent(Scene.Time, o.Id, PhysicsEventKind.Impact, $"{o.Name}: {(closed ? "kapandı" : "açıldı")}", 0, false));
                }
            }
        }

        private void BuildChutes()
        {
            _chutes = null;
            foreach (var it in _items.Values)
            {
                var o = it.Object;
                if (it.Body == null || !TryNum(o, "parachute", out double d) || d <= 0) continue;
                var c = new Chute { Item = it, Model = new ParachuteModel { Diameter = d }, StartZ = it.Body.Position.Z, PeakZ = it.Body.Position.Z };
                if (o.Parameters.TryGetValue("parachute.deploy", out var when) && double.TryParse(when, NumberStyles.Float, CultureInfo.InvariantCulture, out double at)) c.DeployAt = at;
                if (TryNum(o, "parachute.cd", out double cd) && cd > 0) c.Model.DragCoefficient = cd;
                (_chutes ??= new List<Chute>()).Add(c);
            }
        }

        private void StepChutes()
        {
            if (_chutes == null) return;
            foreach (var c in _chutes)
            {
                var body = c.Item.Body;
                var o = c.Item.Object;
                if (PoseOf(o.Id) is not { } pose) continue;
                double z = body.Position.Z;
                c.PeakZ = Math.Max(c.PeakZ, z);
                if (!c.Model.Deployed)
                {
                    bool now = !double.IsNaN(c.DeployAt)
                        ? Scene.Time >= c.DeployAt
                        : c.PeakZ > c.StartZ + 3 && body.Velocity.Z < 0 && z < c.PeakZ - 0.2;   // the altimeter sees it falling
                    if (!now) continue;
                    c.Model.Deploy(Scene.Time);
                    Scene.Report(new PhysicsEvent(Scene.Time, o.Id, PhysicsEventKind.Impact,
                        $"{o.Name}: paraşüt atıldı ({c.PeakZ - c.StartZ:0.0} m tepe, {body.Velocity.Length:0.0} m/s)", 0, false));
                }

                // Lines meet the body at its top (object frame +z); the canopy pulls along the relative air.
                var top = pose.Position + pose.Orientation.Rotate(new Vec3(0, 0, o.Shape == "sphere" ? o.SizeX : o.SizeZ));
                var air = Scene.WindAt(top) - (body.Velocity + body.AngularVelocity.Cross(top - body.Position));
                var f = c.Model.DragForce(Scene.Time, air, Scene.AirDensity);
                c.Attach = top;
                c.Pull = air.Length > 1e-6 ? air.Normalized() : Vec3.UnitZ;
                if (f.Length > 1e-9) body.AddForceAtPoint(f, top);
                if (!c.FullReported && c.Model.FullyOpen(Scene.Time))
                {
                    c.FullReported = true;
                    Scene.Report(new PhysicsEvent(Scene.Time, o.Id, PhysicsEventKind.Impact,
                        $"{o.Name}: paraşüt tam açıldı (açılma şoku en çok {c.Model.PeakForce:0.0} N)", c.Model.PeakForce, false));
                }
            }
        }

        /// <summary>A body's parachute: deployed, how far open (0…1), where its lines meet the body, which way it pulls, diameter.</summary>
        public (bool Deployed, double Open, Vec3 Attach, Vec3 Pull, double Diameter)? ParachuteOf(string objectId)
        {
            var c = _chutes?.FirstOrDefault(x => string.Equals(x.Item.Object.Id, objectId, StringComparison.OrdinalIgnoreCase));
            if (c == null) return null;
            var m = c.Model;
            double open = !m.Deployed || double.IsNaN(m.FillTime) ? 0 : Math.Min(1, Math.Pow(Math.Max(0, Scene.Time - m.DeployTime - m.DeployDelay) / m.FillTime, 2));
            return (c.Model.Deployed, open, c.Attach, c.Pull, c.Model.Diameter);
        }
    }
}
