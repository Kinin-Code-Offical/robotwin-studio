using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Robotics;
using RobotTwin.CoreSim.Sandbox;
using M = RobotWin.Studio.Assembly3D.Mesh;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [arm] Robot cell in the 3D view: arm links follow the physics (or the start pose while
    /// editing), tools sit on their flange, gripper fingers / suction cups / conveyor belts move,
    /// indicator lamps show the machine state, feeder cartons and light-curtain beams are drawn.
    /// WorldScene.cs only calls <see cref="UpdateCell"/>.
    /// </summary>
    public sealed partial class WorldScene
    {
        private readonly ModelVisual3D _cellExtras = new ModelVisual3D();
        private readonly Dictionary<string, (Model3DGroup Group, MatrixTransform3D Transform)> _cartons = new Dictionary<string, (Model3DGroup, MatrixTransform3D)>(StringComparer.Ordinal);
        private Model3DGroup? _cellGroup;
        private readonly Model3DGroup _beamGroup = new Model3DGroup();

        private static readonly Color LampOff = Color.FromRgb(0x10, 0x10, 0x10);

        /// <summary>Called by UpdateKinematics after poses are resolved, before they are applied.</summary>
        private void UpdateCell()
        {
            if (World == null) return;
            bool live = Session != null && Session.State != Simulation.SimState.Stopped;
            var physics = live ? Session!.Physics : null;
            foreach (var it in _items.Values)
            {
                var v = CellModels.VisualOf(it.Model);
                if (v == null) continue;
                switch (v.PartId)
                {
                    case CellParts.ArmId:
                        UpdateArmVisual(it, v, physics);
                        break;
                    case CellParts.GripperId:
                    {
                        var tool = physics?.ToolOf(it.Object.Id);
                        var travel = tool?.FingerTravel;
                        for (int i = 0; i < v.Fingers.Length; i++)
                        {
                            double t = travel != null && i < travel.Length ? travel[i] : 0;
                            v.Fingers[i].T.OffsetY = -v.Fingers[i].Side * t;
                        }

                        break;
                    }

                    case CellParts.VacuumId:
                        if (v.Cup != null) v.Cup.OffsetZ = physics?.ToolOf(it.Object.Id)?.CupTravel ?? 0;
                        break;
                    case CellParts.ConveyorId:
                    {
                        var cv = physics?.ConveyorOf(it.Object.Id);
                        if (v.Belt != null) v.Belt.OffsetX = cv == null ? 0 : cv.Travel % v.BeltPitch;
                        if (v.Lamp != null) v.Lamp.Color = cv == null ? LampOff : cv.SensorOn ? Color.FromRgb(0xF2, 0xA3, 0x3B) : Color.FromRgb(0x24, 0x70, 0x34);
                        break;
                    }

                    case CellParts.CurtainId:
                    {
                        var lc = physics?.CurtainOf(it.Object.Id);
                        if (v.Lamp != null) v.Lamp.Color = lc == null ? LampOff : lc.Blocked ? Color.FromRgb(0xE5, 0x30, 0x30) : Color.FromRgb(0x3C, 0xD0, 0x64);
                        break;
                    }

                    case CellParts.PlcId:   // [plc] RUN green, STOP red
                    {
                        var plc = physics?.PlcOf(it.Object.Id);
                        if (v.Lamp != null) v.Lamp.Color = plc == null ? LampOff : plc.Running ? Color.FromRgb(0x3C, 0xD0, 0x64) : Color.FromRgb(0xE5, 0x30, 0x30);
                        break;
                    }

                    case CellParts.CylinderId:   // [pneu] the rod where the piston is; the coil lamp
                    {
                        var cy = physics?.CylinderOf(it.Object.Id);
                        if (v.Rod != null) v.Rod.OffsetX = cy?.Position ?? 0;
                        if (v.Lamp != null) v.Lamp.Color = cy?.Energised == true ? Color.FromRgb(0xF2, 0xA3, 0x3B) : LampOff;
                        break;
                    }

                    case CellParts.PhotoId:
                        if (v.Lamp != null) v.Lamp.Color = physics?.PhotoOf(it.Object.Id)?.Output == true ? Color.FromRgb(0xF2, 0xA3, 0x3B) : LampOff;
                        break;
                    case CellParts.ModbusIoId:   // [modbus] lit while the world runs it
                        if (v.Lamp != null) v.Lamp.Color = physics?.ModbusDevices.Any(d => d.Object.Id == it.Object.Id) == true ? Color.FromRgb(0x3C, 0xD0, 0x64) : LampOff;
                        break;
                }
            }

            if (physics == null) SeatToolVisuals();
            UpdateCellExtras(physics);
        }

        private static Matrix3D Mat(Transform3 t) => CellModels.MatrixOf(t);

        private void UpdateArmVisual(Item it, CellVisual v, WorldPhysics? physics)
        {
            var arm = physics?.ArmOf(it.Object.Id);
            var model = v.Arm!;
            if (arm != null)
            {
                var inv = arm.BasePose.Inverse();
                var frames = arm.LinkFrames();
                for (int i = 0; i < v.Links.Length && i < frames.Length; i++) v.Links[i].Matrix = Mat(inv * frames[i]);
                if (v.Lamp != null)
                {
                    v.Lamp.Color = arm.Controller.State switch
                    {
                        ArmState.Running => Color.FromRgb(0x3C, 0xD0, 0x64),
                        ArmState.Moving => Color.FromRgb(0x3C, 0xD0, 0x64),
                        ArmState.Paused => Color.FromRgb(0xF2, 0xA3, 0x3B),
                        ArmState.SafetyStop => Color.FromRgb(0xE5, 0x30, 0x30),
                        ArmState.Fault => Color.FromRgb(0xE5, 0x30, 0x30),
                        _ => Color.FromRgb(0x3E, 0x7C, 0xC8)
                    };
                }

                return;
            }

            var T = ArmKinematics.LinkTransforms(model, CellGeometry.StartJoints(it.Object));
            for (int i = 0; i < v.Links.Length && i < T.Length; i++) v.Links[i].Matrix = Mat(T[i]);
            if (v.Lamp != null) v.Lamp.Color = LampOff;
        }

        /// <summary>While editing, tools fixed to a robot flange are drawn on the flange of the start pose.</summary>
        private void SeatToolVisuals()
        {
            foreach (var j in World!.Joints)
            {
                if (j.ObjectB == null || !string.Equals(j.PortB, "flange", StringComparison.OrdinalIgnoreCase)) continue;
                if (!_items.TryGetValue(j.ObjectA, out var tool) || !_items.TryGetValue(j.ObjectB, out var arm)) continue;
                if (arm.Object.PartId != CellParts.ArmId) continue;
                var model = CellGeometry.ModelOf(arm.Object);
                var flange = ArmKinematics.Forward(model, CellGeometry.StartJoints(arm.Object));
                tool.Current = Mat(flange) * arm.Current;
            }
        }

        // ================================================================ feeder cartons, light-curtain beams

        private void UpdateCellExtras(WorldPhysics? physics)
        {
            if (physics == null || (physics.SpawnedParts.Count == 0 && physics.Curtains.Count == 0 && physics.PhotoSensors.Count == 0))
            {
                if (_cellExtras.Content != null)
                {
                    _cellExtras.Content = null;
                    _cellGroup = null;
                    _cartons.Clear();
                }

                return;
            }

            if (!_view.Children.Contains(_cellExtras)) _view.Children.Add(_cellExtras);
            if (_cellGroup == null)
            {
                _cellGroup = new Model3DGroup();
                _cellGroup.Children.Add(_beamGroup);
                _cellExtras.Content = _cellGroup;
            }

            var alive = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in physics.SpawnedParts)
            {
                alive.Add(s.Id);
                if (!_cartons.TryGetValue(s.Id, out var c))
                {
                    var t = new MatrixTransform3D();
                    var g = Carton(s.Size.X, s.Size.Y, s.Size.Z);
                    g.Transform = t;
                    _cellGroup.Children.Add(g);
                    c = (g, t);
                    _cartons[s.Id] = c;
                }

                c.Transform.Matrix = Mat(new Transform3(s.Body.Position, s.Body.Orientation));
            }

            foreach (var id in _cartons.Keys.Where(k => !alive.Contains(k)).ToList())
            {
                _cellGroup.Children.Remove(_cartons[id].Group);
                _cartons.Remove(id);
            }

            _beamGroup.Children.Clear();
            foreach (var lc in physics.Curtains)
            {
                foreach (var (from, to, cut) in lc.Beams)
                {
                    var color = cut ? Color.FromRgb(0xFF, 0x3B, 0x30) : Color.FromRgb(0x7A, 0x1E, 0x1E);
                    var pts = new List<Point3D> { new Point3D(from.X, from.Y, from.Z), new Point3D(to.X, to.Y, to.Z) };
                    _beamGroup.Children.Add(new GeometryModel3D(Tube(pts, cut ? 0.0018 : 0.0008, 5), new EmissiveMaterial(new SolidColorBrush(color))));
                    if (cut) _beamGroup.Children.Add(new GeometryModel3D(M.Sphere(0.006, to.X, to.Y, to.Z, 8), new EmissiveMaterial(new SolidColorBrush(color))));
                }
            }

            foreach (var ph in physics.PhotoSensors)   // [cell] the photoelectric sensors' light
            {
                var (from, to, hit) = ph.Beam;
                var color = ph.Present ? Color.FromRgb(0xFF, 0x3B, 0x30) : Color.FromRgb(0x7A, 0x1E, 0x1E);
                var pts = new List<Point3D> { new Point3D(from.X, from.Y, from.Z), new Point3D(to.X, to.Y, to.Z) };
                _beamGroup.Children.Add(new GeometryModel3D(Tube(pts, ph.Present ? 0.0015 : 0.0007, 5), new EmissiveMaterial(new SolidColorBrush(color))));
                if (hit) _beamGroup.Children.Add(new GeometryModel3D(M.Sphere(0.004, to.X, to.Y, to.Z, 8), new EmissiveMaterial(new SolidColorBrush(color))));
            }
        }

        /// <summary>Corrugated carton centred on its body origin: brown board, tape over the top, a white label.</summary>
        private static Model3DGroup Carton(double sx, double sy, double sz)
        {
            var g = new Model3DGroup();
            g.Children.Add(M.Model(M.Box(0, 0, 0, sx, sy, sz), M.Matte(Color.FromRgb(0xB8, 0x8A, 0x57), 0.05)));
            g.Children.Add(M.Model(M.Box(0, 0, sz / 2 + 0.0004, sx + 0.0008, Math.Min(0.05, sy * 0.35), 0.0008), M.Matte(Color.FromRgb(0xD8, 0xB8, 0x86), 0.1)));
            g.Children.Add(M.Model(M.Box(sx * 0.12, -sy / 2 - 0.0004, 0, sx * 0.45, 0.0008, sz * 0.45), M.Matte(Color.FromRgb(0xF0, 0xF0, 0xEC), 0.05)));
            g.Children.Add(M.Model(M.Box(sx * 0.12, -sy / 2 - 0.0009, -sz * 0.08, sx * 0.3, 0.0004, sz * 0.06), M.Matte(Color.FromRgb(0x22, 0x22, 0x24), 0.0)));
            return g;
        }
    }
}
