using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Radio;
using M = RobotWin.Studio.Assembly3D.Mesh;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [radio] Radio visualisation and USB cables in the world view. WorldScene.cs only
    /// calls InitRadio() (constructor), RadioFrame() (every frame) and AppendUsbCables()
    /// (end of RebuildCables).
    /// </summary>
    public sealed partial class WorldScene
    {
        private RadioOverlay? _radioOverlay;

        /// <summary>The radio layer (toggle rings / links).</summary>
        public RadioOverlay? RadioLayer => _radioOverlay;

        private void InitRadio()
        {
            _radioOverlay = new RadioOverlay { Project = Project };
            Children.Insert(Math.Max(0, Children.IndexOf(_hint)), _radioOverlay);
        }

        /// <summary>World-space centre of an object as drawn now (node ids "O3/U1" map to object "O3").</summary>
        public Point3D? LivePosition(string nodeId)
        {
            int slash = nodeId.IndexOf('/');
            string id = slash > 0 ? nodeId.Substring(0, slash) : nodeId;
            if (!_items.TryGetValue(id, out var it)) return null;
            var b = it.Model.Bounds;
            var local = b.IsEmpty ? new Point3D(0, 0, 0.01) : new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + Math.Min(b.SizeZ, 0.03));
            return it.Current.Transform(local);
        }

        /// <summary>Moves an object's visual after its pose changed without a full rebuild (radio range slider while simulating).</summary>
        public void RefreshPose(string objectId)
        {
            if (World == null || !_items.TryGetValue(objectId, out var it)) return;
            it.Placed = PoseMatrix(it.Object.Pose);
            UpdateKinematics();
            RebuildCables();
            RebuildOverlay();
        }

        private void RadioFrame()
        {
            var medium = Session != null && Session.State != Simulation.SimState.Stopped ? Session.Radio : null;
            _radioOverlay?.Update(medium, LivePosition);
            if (World == null) return;
            double t = medium?.Time ?? 0;
            foreach (var it in _items.Values)
            {
                if (RadioModels.ExtrasOf(it.Model) is not { Lightbar: { } bar }) continue;
                var pad = medium?.Find(it.Object.Id) as GamepadNode;
                Color c;
                if (pad == null || !pad.PoweredOn) c = Colors.Black;   // [phys] dark until the world runs and the pad is on
                else if (pad.PairingMode) c = ((int)(t * 8) % 2 == 0) ? Color.FromRgb(0xC0, 0xD8, 0xFF) : Color.FromRgb(0x10, 0x20, 0x60);   // fast double blink
                else if (pad.Connected && pad.Feedback.ColorSet) c = Color.FromRgb(pad.Feedback.Red, pad.Feedback.Green, pad.Feedback.Blue);
                else if (pad.Connected) c = Color.FromRgb(0x20, 0x50, 0xFF);
                else c = ((int)(t * 2) % 2 == 0) ? Color.FromRgb(0x10, 0x30, 0x90) : Color.FromRgb(0x04, 0x08, 0x20);   // searching
                ((SolidColorBrush)bar.Brush).Color = c;
            }
        }

        /// <summary>USB cables from every PC object's port to its target board's USB jack (or lying on the floor when unplugged).</summary>
        private void AppendUsbCables(Model3DGroup g)
        {
            if (World == null) return;
            foreach (var (host, targetId, _, plugged) in WorldUsb.Hosts(World))
            {
                if (!_items.TryGetValue(host.Id, out var hi)) continue;
                var port = RadioModels.ExtrasOf(hi.Model)?.UsbPort ?? new Point3D(0, 0, 0.01);
                var a = hi.Current.Transform(port);
                Point3D b;
                bool live = plugged;
                if (plugged && _items.TryGetValue(targetId, out var ti))
                {
                    b = ti.Current.Transform(RadioModels.UsbJack(ti.Model));
                    // A metal plug body sticking into the jack.
                    var dir = ti.Current.Transform(new Vector3D(1, 0, 0));
                    dir.Normalize();
                    var plug = b - dir * 0.006;
                    g.Children.Add(M.Model(M.Box(plug.X, plug.Y, plug.Z, 0.012, 0.012, 0.007), M.Matte(Color.FromRgb(0x1A, 0x1A, 0x1D), 0.2)));
                    b -= dir * 0.012;
                }
                else
                {
                    var outward = hi.Current.Transform(new Vector3D(1, 0, 0));
                    outward.Normalize();
                    b = new Point3D(a.X + outward.X * 0.12, a.Y + outward.Y * 0.12, 0.004);
                    live = false;
                    g.Children.Add(M.Model(M.Box(b.X, b.Y, b.Z, 0.012, 0.012, 0.007), M.Matte(Color.FromRgb(0x1A, 0x1A, 0x1D), 0.2)));
                }

                var mat = new MaterialGroup();
                mat.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x28))));
                mat.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)), 30));
                if (live && Session != null && Session.State != Simulation.SimState.Stopped)
                {
                    mat.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x08, 0x1C, 0x30))));
                }

                g.Children.Add(new GeometryModel3D(Tube(CablePath(a, b), 0.0018, 8), mat));
                g.Children.Add(M.Model(M.Box(a.X, a.Y, a.Z, 0.010, 0.014, 0.006), M.Matte(Color.FromRgb(0x1A, 0x1A, 0x1D), 0.2)));
            }
        }
    }
}
