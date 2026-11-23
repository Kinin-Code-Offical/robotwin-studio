using System;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [drag] Air drag that the generic per-body model would get wrong:
    /// <list type="bullet">
    /// <item>A part inside a closed hollow shell (a battery in a fuselage, a board in a box) is out of the
    /// airflow: the shell's own walls carry the drag, the part none.</item>
    /// <item>A propeller is not a solid disc. Spinning, its blades' forces are the propeller model's
    /// thrust and torque (which already fall with the advance ratio and turn into windmilling drag);
    /// standing still, only the blades' own planform meets the air (solidity ≈ 0.1 of the disc).
    /// Edgewise, the flow sees the blades' thickness across the diameter.</item>
    /// </list>
    /// </summary>
    public sealed partial class WorldPhysics
    {
        /// <summary>Blade area / disc area of a small two-blade fixed-pitch propeller.</summary>
        public const double PropSolidity = 0.10;

        /// <summary>Removes the drag of every moving part that lies inside a hollow shell of its own assembly.</summary>
        private void ShieldEnclosedDrag()
        {
            foreach (var shell in _items.Values)
            {
                var o = shell.Object;
                if (!IsShell(o) || shell.Body == null || PoseOf(o.Id) is not { } frame) continue;
                double wall = WallOf(o);
                double hx = o.SizeX / 2 - wall, hy = o.SizeY / 2 - wall, top = o.SizeZ;
                if (hx <= 0 || hy <= 0) continue;
                var group = AssemblyOf(shell.Body);
                foreach (var it in _items.Values)
                {
                    if (it == shell || it.Body == null || it.Body.IsStatic || !group.Contains(it.Body)) continue;
                    var local = frame.Orientation.InverseRotate(it.Body.Position - frame.Position);
                    double r = it.Body.Shape?.BoundingRadius ?? 0;
                    // Inside when its centre is in the cavity and it does not reach far out of it.
                    bool inside = Math.Abs(local.X) < hx && Math.Abs(local.Y) < hy && local.Z > wall && local.Z < top
                                  && Math.Abs(local.X) + 0.5 * r < hx + 0.005 && Math.Abs(local.Y) + 0.5 * r < hy + 0.005;
                    if (!inside) continue;
                    it.Body.DragArea = Vec3.Zero;
                    if (it.Rotor != null) it.Rotor.DragArea = Vec3.Zero;
                }
            }
        }

        /// <summary>
        /// Drag of a propeller's blades as a body, every step: edgewise the blade thickness across the
        /// diameter; axially the blade planform, fading out as the blades spin up (their spinning
        /// forces are the propeller model's).
        /// </summary>
        private void PropBladeDrag(PropMount pm)
        {
            var b = pm.It.Body;
            if (Scene.AirDensity <= 0)
            {
                b.DragArea = Vec3.Zero;
                return;
            }

            double d = pm.Spec.Diameter, r = d / 2;
            double thick = Math.Max(0.002, 0.35 * Math.Max(0.004, pm.Spec.Width));   // blade section ≈ a third of the hub height
            double edge = 1.1 * thick * d;
            double v = Math.Abs(pm.Prop.AxialInflow);
            double tip = Math.Abs(pm.Prop.AngularVelocity) * 0.75 * r;
            double still = v * v / (v * v + tip * tip + 1e-9);
            double axial = 1.1 * PropSolidity * Math.PI * r * r * still;
            b.DragArea = new Vec3(edge, edge, axial);
        }
    }
}
