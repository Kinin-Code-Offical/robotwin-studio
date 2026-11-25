using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>[cell] A photoelectric sensor in the world: its beam, what it sees and its output.</summary>
    public sealed class MachinePhoto
    {
        internal MachinePhoto(WorldObject o) => Object = o;

        public WorldObject Object { get; }

        internal readonly List<SceneBody> Own = new List<SceneBody>();
        internal DigitalPin? Out;
        internal string Mode = CellParts.PhotoDiffuse;
        internal double Range, Response, Delay;
        internal bool OnWhenAbsent;
        internal (MachineFaults.Fault Fault, double At, double Value)? Fault;   // [fault]
        internal bool FaultSaid;

        /// <summary>An object is there (diffuse: one returns enough light; a beam: one cuts it).</summary>
        public bool Present { get; internal set; }

        /// <summary>The output after the response time.</summary>
        public bool Output { get; internal set; }

        /// <summary>The light's path this step: from the lens to where it stopped, and whether it stopped on something.</summary>
        public (Vec3 From, Vec3 To, bool Hit) Beam { get; internal set; }

        /// <summary>The distance to what the light hits (m), or NaN.</summary>
        public double Distance { get; internal set; } = double.NaN;
    }

    /// <summary>[cell] A roller-lever limit switch: pressed when something pushes its roller past the pre-travel.</summary>
    public sealed class MachineLimit
    {
        internal MachineLimit(WorldObject o) => Object = o;

        public WorldObject Object { get; }

        internal readonly List<SceneBody> Own = new List<SceneBody>();
        internal Switch? No, Nc;
        internal double Reach, PreTravel;
        internal (MachineFaults.Fault Fault, double At, double Value)? Fault;   // [fault]
        internal bool FaultSaid;

        public bool Pressed { get; internal set; }

        /// <summary>How far the roller is pushed in (m).</summary>
        public double Travel { get; internal set; }
    }

    /// <summary>
    /// [cell] Photoelectric sensors and limit switches (Design/CellParts.cs): the world's light and contact as circuit signals.
    /// A diffuse sensor's reach shortens with the target's reflectance (the returned light falls as 1/d²: a black box is
    /// seen at about a third of a white card's distance); a retro-reflective or through-beam sensor sees whatever cuts its
    /// beam. Both answer after their response time. A limit switch's roller is pushed by whatever comes within its reach,
    /// and past the pre-travel its contacts change over (C–NO closes, C–NC opens), with half a millimetre of hysteresis.
    /// </summary>
    public sealed partial class WorldPhysics
    {
        private readonly List<MachinePhoto> _photos = new List<MachinePhoto>();
        private readonly List<MachineLimit> _limits = new List<MachineLimit>();

        public IReadOnlyList<MachinePhoto> PhotoSensors => _photos;

        public IReadOnlyList<MachineLimit> LimitSwitches => _limits;

        public MachinePhoto? PhotoOf(string objectId) => _photos.FirstOrDefault(p => p.Object.Id == objectId);

        public MachineLimit? LimitOf(string objectId) => _limits.FirstOrDefault(l => l.Object.Id == objectId);

        private void BuildPhoto(WorldObject o, Item it, Dictionary<string, string> p)
        {
            var ph = new MachinePhoto(o)
            {
                Mode = p.TryGetValue("mode", out var m) && !string.IsNullOrWhiteSpace(m) ? m.Trim() : CellParts.PhotoDiffuse,
                Range = Math.Max(0.005, CellParts.Num(p, "range", 0.3)),
                Response = Math.Max(0, CellParts.Num(p, "response", 1)) / 1000,
                OnWhenAbsent = p.TryGetValue("logic", out var l) && l.Trim() == CellParts.PhotoOnAbsent
            };
            ph.Own.Add(it.Body);
            ph.Out = IoPins(o).GetValueOrDefault("OUT");
            ph.Fault = MachineFaults.Of(o);
            _photos.Add(ph);
        }

        private void BuildLimit(WorldObject o, Item it, Dictionary<string, string> p)
        {
            var ls = new MachineLimit(o)
            {
                Reach = Math.Max(0.002, CellParts.Num(p, "reach", 25) / 1000),
                PreTravel = Math.Max(0.0002, CellParts.Num(p, "pretravel", 2) / 1000)
            };
            ls.Own.Add(it.Body);
            ls.Fault = MachineFaults.Of(o);
            if (_circuit?.ComponentRef(o.Id) is { } r)
            {
                foreach (var d in _circuit.Compiled.DevicesOf(r).OfType<Switch>())
                {
                    if (d.Id.EndsWith(".NO", StringComparison.OrdinalIgnoreCase)) ls.No = d;
                    else if (d.Id.EndsWith(".NC", StringComparison.OrdinalIgnoreCase)) ls.Nc = d;
                }
            }

            _limits.Add(ls);
        }

        /// <summary>[fault] The fault a machine has now (its id), saying so once when it begins; null before its time or without one.</summary>
        private string? FaultNow(WorldObject o, (MachineFaults.Fault Fault, double At, double Value)? fault, ref bool said)
        {
            if (fault is not { } f || Scene.Time < f.At) return null;
            if (!said) MachineSay(o.Id, "Arıza: " + f.Fault.Name, true);
            said = true;
            return f.Fault.Id;
        }

        private void StepPhoto(MachinePhoto ph, double dt)
        {
            var frame = FrameOf(ph.Object.Id);
            var g = CellGeometry.For(ph.Object);
            double front = g?.Max.X ?? 0.015, height = g != null ? (g.Min.Z + g.Max.Z) / 2 : 0.025;
            var from = frame.Apply(new Vec3(front + 0.0005, 0, height));
            var dir = frame.ApplyVector(Vec3.UnitX);
            var own = ph.Own;
            bool hit = Scene.Raycast(from, dir, ph.Range, out var h, x => !own.Contains(x) && x.SensorVisible, ground: true);
            string? fault = FaultNow(ph.Object, ph.Fault, ref ph.FaultSaid);
            double clean = fault == "lens_dirty" ? Math.Max(0, Math.Min(100, ph.Fault!.Value.Value)) / 100 : 1;
            bool present;
            if (ph.Mode == CellParts.PhotoDiffuse)
            {
                // The light a target returns falls as its reflectance over d²: rated range on a white card (90 %); a dirty
                // lens passes less of it, so the range shrinks with it.
                double reflectance = hit ? MechatronicSystem.Reflectance(h.Material ?? string.Empty) : 0;
                present = hit && h.Distance <= ph.Range * clean * Math.Sqrt(Math.Max(0, reflectance) / 0.9);
            }
            else
            {
                // The beam to the reflector or the receiver is cut; through a dirty lens too little of it comes back.
                present = hit || clean < 0.5;
            }

            ph.Distance = hit ? h.Distance : double.NaN;
            ph.Beam = (from, hit ? h.Point : from + dir * ph.Range, hit);
            if (present != ph.Present)
            {
                ph.Delay += dt;
                if (ph.Delay + 1e-12 >= ph.Response)
                {
                    ph.Present = present;
                    ph.Delay = 0;
                }
            }
            else
            {
                ph.Delay = 0;
            }

            ph.Output = fault == "output_stuck_on" || (fault != "output_dead" && ph.Present != ph.OnWhenAbsent);
            Drive(ph.Out, ph.Output);
        }

        private void StepLimit(MachineLimit ls)
        {
            var frame = FrameOf(ls.Object.Id);
            var g = CellGeometry.For(ls.Object);
            double front = g?.Max.X ?? 0.02, height = g != null ? g.Max.Z - 0.008 : 0.05;
            var dir = frame.ApplyVector(Vec3.UnitX);
            var own = ls.Own;
            double nearest = double.PositiveInfinity;
            foreach (double y in new[] { -0.005, 0.0, 0.005 })
            {
                var from = frame.Apply(new Vec3(front, y, height));
                if (Scene.Raycast(from, dir, ls.Reach, out var h, x => !own.Contains(x) && x.Collides, ground: false)) nearest = Math.Min(nearest, h.Distance);
            }

            string? fault = FaultNow(ls.Object, ls.Fault, ref ls.FaultSaid);
            if (fault == "lever_broken") nearest = double.PositiveInfinity;   // nothing reaches a broken lever
            ls.Travel = double.IsInfinity(nearest) ? 0 : ls.Reach - nearest;
            // Changes over past the pre-travel; lets go half a millimetre before it (the snap action's differential travel).
            bool pressed = ls.Pressed ? ls.Travel > ls.PreTravel - 0.0005 : ls.Travel >= ls.PreTravel;
            if (pressed != ls.Pressed) MachineSay(ls.Object.Id, pressed ? "Sınır anahtarı basıldı" : "Sınır anahtarı bırakıldı", false);
            ls.Pressed = pressed;
            bool welded = fault == "contact_welded";
            if (ls.No != null) ls.No.Closed = pressed || welded;
            if (ls.Nc != null) ls.Nc.Closed = !pressed && !welded;
        }
    }
}
