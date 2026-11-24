namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [perf] Wall-clock seconds a world's mechanics spent in each of its parts, summed since it was built: which solver
    /// holds the world back. Read two of them apart to get the cost over that stretch.
    /// </summary>
    public sealed class PhysicsTimes
    {
        /// <summary>Rigid bodies: contacts, joints, wheels, propellers (the physics scene's step).</summary>
        public double Bodies;

        /// <summary>Cables as ropes.</summary>
        public double Cables;

        /// <summary>Liquids as particles.</summary>
        public double Liquids;

        /// <summary>Heat in the parts and the air around them.</summary>
        public double Thermal;

        /// <summary>Motors, servos and machines driven by the circuit, and their load fed back to it.</summary>
        public double Drives;

        /// <summary>Wings, gas vessels, launches and parachutes.</summary>
        public double Air;

        /// <summary>User behaviours (scripts) before and after each step.</summary>
        public double Scripts;

        /// <summary>Ultrasonic ranging.</summary>
        public double Sensors;

        /// <summary>Mechanical steps taken.</summary>
        public long Steps;

        public double Total => Bodies + Cables + Liquids + Thermal + Drives + Air + Scripts + Sensors;

        public PhysicsTimes Copy() => (PhysicsTimes)MemberwiseClone();
    }
}
