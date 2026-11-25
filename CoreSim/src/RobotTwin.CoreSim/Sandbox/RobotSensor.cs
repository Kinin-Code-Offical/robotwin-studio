using System;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [ros] A sensor a robot description declares on a part (Gazebo's <c>&lt;sensor&gt;</c> in a URDF): a laser scanner or an
    /// IMU, simulated from this world and published on ROS while it runs (<see cref="Ros.RosWorldLink"/>). Kept with the part,
    /// so a saved world keeps its robot's sensors.
    /// </summary>
    public sealed class RobotSensor
    {
        /// <summary>"lidar" (a planar laser scanner) or "imu".</summary>
        public string Kind { get; set; } = "lidar";

        public string Name { get; set; } = string.Empty;

        /// <summary>The ROS topic (relative to the robot's namespace, e.g. "scan") and the frame its messages carry.</summary>
        public string Topic { get; set; } = string.Empty;

        public string Frame { get; set; } = string.Empty;

        /// <summary>Messages per second.</summary>
        public double Rate { get; set; } = 10;

        /// <summary>Where it sits in the part's frame: x, y, z (m), roll, pitch, yaw (rad).</summary>
        public double[] Pose { get; set; } = new double[6];

        /// <summary>A scanner's rays: how many, from which angle to which (rad, counter-clockwise about its z), and its range (m).</summary>
        public int Samples { get; set; } = 360;

        public double MinAngle { get; set; } = -Math.PI;

        public double MaxAngle { get; set; } = Math.PI;

        public double MinRange { get; set; } = 0.1;

        public double MaxRange { get; set; } = 10;

        /// <summary>Gaussian noise σ: of the range (m) for a scanner, of the angular rate (rad/s) for an IMU.</summary>
        public double Noise { get; set; }

        /// <summary>An IMU's acceleration noise σ (m/s²).</summary>
        public double AccelNoise { get; set; }
    }
}
