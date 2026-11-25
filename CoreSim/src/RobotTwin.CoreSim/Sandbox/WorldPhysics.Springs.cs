using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>[spring] Springs and dampers in the world's joints (WorldJoint.SpringStiffness, Mechanics/PhysicsScene.cs).</summary>
    public sealed partial class WorldPhysics
    {
        private static void AddSpring(WorldJoint j, SceneJoint joint)
        {
            if (!j.Sprung || joint.Kind is not (JointKind.Hinge or JointKind.Slider)) return;
            joint.SpringStiffness = Math.Max(0, j.SpringStiffness);
            joint.SpringRest = joint.Kind == JointKind.Hinge ? j.SpringRest * Math.PI / 180 : j.SpringRest;   // the document keeps degrees for hinges
            joint.SpringDamping = Math.Max(0, j.SpringDamping);
        }

        /// <summary>
        /// [spring] A joint's spring and damper while running: the torque (N·m) or force (N) they put on the part, and where the
        /// joint is (° or m); null when it has none.
        /// </summary>
        public (double Load, double Position)? SpringState(string jointId)
        {
            var j = Scene.Joints.FirstOrDefault(s => s.Id == jointId);
            if (j == null || (j.SpringStiffness <= 0 && j.SpringDamping <= 0)) return null;
            return (j.SpringTorque, j.Kind == JointKind.Hinge ? j.Angle * 180 / Math.PI : j.Angle);
        }
    }
}
