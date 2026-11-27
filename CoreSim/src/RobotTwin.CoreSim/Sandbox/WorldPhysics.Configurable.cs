using System;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [d6] Unity's character and configurable joints in a world: both are the scene's configurable joint (each of the joint
    /// frame's six axes locked, limited or free, Mechanics/PhysicsScene.Configurable.cs). A character joint holds its
    /// anchor, twists between its lower and upper angles and swings within an elliptic cone; a configurable joint takes
    /// each axis as the document says, with its linear limit and its drives.
    /// </summary>
    public sealed partial class WorldPhysics
    {
        /// <summary>Unity's character joint's limits when a new one is made: twist −20° … 70°, swings 40°.</summary>
        public const double CharacterLowTwist = -20, CharacterHighTwist = 70, CharacterSwing = 40;

        private static void SixAxis(WorldJoint j, SceneJoint s)
        {
            const double rad = Math.PI / 180;
            var swingAxis = new Vec3(j.SwingAxisX, j.SwingAxisY, j.SwingAxisZ);
            if (swingAxis.Length > 1e-9) s.SetSecondaryAxis(swingAxis);
            s.Swing1Limit = Math.Max(0, j.SwingLimit1) * rad;
            s.Swing2Limit = Math.Max(0, j.SwingLimit2) * rad;
            if (j.Kind == WorldJointKind.Character)
            {
                s.MotionX = s.MotionY = s.MotionZ = AxisMotion.Locked;
                s.AngularMotionX = j.Lower.HasValue || j.Upper.HasValue ? AxisMotion.Limited : AxisMotion.Free;
                s.AngularMotionY = s.AngularMotionZ = AxisMotion.Limited;   // a 0° swing locks it
                return;
            }

            s.MotionX = j.MotionX;
            s.MotionY = j.MotionY;
            s.MotionZ = j.MotionZ;
            s.AngularMotionX = j.AngularMotionX;
            s.AngularMotionY = j.AngularMotionY;
            s.AngularMotionZ = j.AngularMotionZ;
            s.LinearLimit = Math.Max(0, j.LinearLimit);
            s.LinearDriveStiffness = Math.Max(0, j.LinearDriveStiffness);
            s.LinearDriveDamping = Math.Max(0, j.LinearDriveDamping);
            s.TargetOffset = new Vec3(j.TargetX, j.TargetY, j.TargetZ);
            s.AngularDriveStiffness = Math.Max(0, j.AngularDriveStiffness);
            s.AngularDriveDamping = Math.Max(0, j.AngularDriveDamping);
            s.TargetRotation = SceneJoint.FromSwingTwist(j.TargetTwist * rad, j.TargetSwing1 * rad, j.TargetSwing2 * rad);
        }

        /// <summary>
        /// [d6] A character or configurable joint while running: its twist and swings (°) and how far its anchor has moved
        /// in the joint frame (m); null for any other joint.
        /// </summary>
        public (double Twist, double Swing1, double Swing2, Vec3 Offset)? SixAxisState(string jointId)
        {
            foreach (var j in Scene.Joints)
            {
                if (j.Id != jointId || j.Kind != JointKind.Configurable) continue;
                const double deg = 180 / Math.PI;
                return (j.Twist * deg, j.Swing1 * deg, j.Swing2 * deg, j.LinearOffset);
            }

            return null;
        }
    }
}
