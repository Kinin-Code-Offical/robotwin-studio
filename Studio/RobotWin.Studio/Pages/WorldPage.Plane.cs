using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Radio;
using RobotWin.Studio.Input;

namespace RobotWin.Studio.Pages
{
    /// <summary>[ps5plane] Snapshot helpers for the DualSense-flown plane: a scripted pilot on the virtual controller and the views of it.</summary>
    public partial class WorldPage
    {
        /// <summary>
        /// --fault "ps5-plane:fly": the pilot's hands on the virtual controller by time — arm with Options, full
        /// throttle, a pull on the right stick at take-off speed, cruise power, then a right turn.
        /// </summary>
        private void PlaneSnapshotPilot(double t)
        {
            var s = VirtualGamepadSource.Instance.State;
            s.Clear();
            s.MiscButtons = t >= 2.5 && t < 2.7 ? GamepadState.MiscStart : (byte)0;
            s.Throttle = t < 3 ? 0 : t < 7.5 ? 1023 : 600;
            if (s.Throttle > 100) s.Buttons |= GamepadState.ButtonR2;
            s.AxisRY = t > 4.0 && t < 5.0 ? 200 : 0;
            s.AxisRX = t > 8 ? 400 : 0;
        }

        /// <summary>Before a "fly" snapshot runs: the controller reads the virtual pad, not a real one plugged into this PC.</summary>
        private void PlaneSnapshotSetup()
        {
            if (!_snapshotModes.Contains("fly") || World == null) return;
            foreach (var pad in World.Objects.Where(o => o.PartId == WirelessParts.GamepadId)) pad.Parameters["source"] = "sanal";
        }

        /// <summary>"plane": the editor view framed on the aircraft; "pilot": the game view from the pilot's eyes.</summary>
        private void PlaneSnapshotFinish()
        {
            if (World == null) return;
            if (_snapshotModes.Contains("plane") && World.Objects.FirstOrDefault(o => o.Name.StartsWith("Gövde")) is { } pod)
            {
                // From ahead and to the left, a little above: prop, wing, gear and tail in one view, no gizmo.
                Scene.Select(null);
                var c = new System.Windows.Media.Media3D.Point3D(pod.Pose.X - 0.2, pod.Pose.Y, pod.Pose.Z + 0.06);
                Scene.LookAt(c, 1.75, 0.42, 3.93);
            }

            if (_snapshotModes.Contains("pilot")) _game?.ShowPilot();
        }
    }
}
