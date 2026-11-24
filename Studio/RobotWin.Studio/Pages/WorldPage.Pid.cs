using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Robotics;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Analysis;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [pid] Entry points to the tools for special logic: the PID tuner of a motor's speed loop (inspector and the scene
    /// tree's right-click menu), and the kinematics of an arm or a chassis from the same menu.
    /// </summary>
    public partial class WorldPage
    {
        /// <summary>A DC motor with a shaft: one line that opens the PID tuner.</summary>
        private void PidSection(WorldDocument world, WorldObject o)
        {
            if (!IsShaftMotor(o)) return;
            Inspector.Children.Add(Caption("Hız kontrolü"));
            Inspector.Children.Add(Action("PID ayarlayıcı…", () => OpenPidTuner(o.Id), "Adım testiyle motorun hız döngüsünü ölçer, PID kazançlarını hesaplar, gerçek fizikte dener ve kodu verir"));
        }

        private bool IsShaftMotor(WorldObject o) =>
            o.Kind == WorldObjectKind.Component && Scene.GeometryOf(o).PortName == "shaft"
            && (o.PartId ?? string.Empty).Contains("motor", StringComparison.OrdinalIgnoreCase);

        /// <summary>The supply a battery in the world gives (V): what PWM 255 puts on a motor; 6 V when none is found.</summary>
        private static double SupplyOf(WorldDocument world)
        {
            foreach (var o in world.Objects)
            {
                string id = (o.PresetId ?? o.PartId ?? string.Empty).ToLowerInvariant();
                if (!id.Contains("bat")) continue;
                if (id.Contains("4aa")) return 6.0;
                if (id.Contains("9v")) return 9.0;
                if (id.Contains("3s")) return 11.1;
                if (id.Contains("2s")) return 7.4;
                if (id.Contains("1s") || id.Contains("18650")) return 3.7;
                if (id.Contains("2aa")) return 3.0;
            }

            return 6.0;
        }

        public PidTunerWindow? OpenPidTuner(string motorId)
        {
            var world = World;
            if (world?.Find(motorId) == null) return null;
            if (_session.State != Simulation.SimState.Stopped) StopSimulation();   // the bench runs its own copy
            var copy = world.Clone();
            var geometry = new CapturedGeometry(copy, o => Scene.GeometryOf(world.Find(o.Id) ?? o), c => Scene.CableLength(world.Cables.FirstOrDefault(x => x.Id == c.Id) ?? c));
            var w = new PidTunerWindow(copy, geometry, motorId, SupplyOf(world)) { Owner = Window.GetWindow(this) };
            w.Show();
            return w;
        }

        /// <summary>
        /// Tool entries of an object's right-click menu in the scene tree, worked out when the menu opens: the PID tuner of
        /// a motor, the kinematics of an arm part, the drive kinematics of a chassis.
        /// </summary>
        private void AddToolItems(ContextMenu menu, string id)
        {
            var extras = new System.Collections.Generic.List<object>();
            menu.Opened += (_, _) =>
            {
                foreach (var x in extras) menu.Items.Remove(x);
                extras.Clear();
                var world = World;
                if (world?.Find(id) is not { } o) return;
                void Add(string header, Action click)
                {
                    if (extras.Count == 0)
                    {
                        var sep = new Separator();
                        menu.Items.Add(sep);
                        extras.Add(sep);
                    }

                    var item = new MenuItem { Header = header };
                    item.Click += (_, _) => click();
                    menu.Items.Add(item);
                    extras.Add(item);
                }

                if (IsShaftMotor(o)) Add("PID ayarlayıcı…", () => OpenPidTuner(id));
                var arm = WorldArm.Build(world, Scene.GeometryOf, id);
                if (arm != null && arm.ActuatedCount > 0 && (arm.Axes.Count >= 2 || o.Kind != WorldObjectKind.Component))
                {
                    Add("Kol kinematiği (ters kinematik)", () =>
                    {
                        Scene.Select(id);
                        _armShowReach = true;
                        BuildInspector();
                    });
                }

                if (WorldVehicle.Build(world, Scene.GeometryOf, id) is { } v && v.BaseId == id)
                {
                    Add("Sürüş kinematiği ve odometri", () =>
                    {
                        Scene.Select(id);
                        BuildInspector();
                    });
                }
            };
        }

        /// <summary>[pid] Snapshot option --pid "Motor": opens the tuner on that motor, runs the step test and the first rule, captures the window.</summary>
        private void PidSnapshot()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "--pid");
            if (i < 0 || i + 1 >= args.Length || World?.Objects.FirstOrDefault(o => o.Name.StartsWith(args[i + 1], StringComparison.OrdinalIgnoreCase) && IsShaftMotor(o)) is not { } motor) return;
            var w = OpenPidTuner(motor.Id);
            if (w == null) return;
            App.SnapshotTarget = w.Content as FrameworkElement;
            _ = w.RunAllForSnapshot();   // the capture waits with --wait
        }
    }
}
