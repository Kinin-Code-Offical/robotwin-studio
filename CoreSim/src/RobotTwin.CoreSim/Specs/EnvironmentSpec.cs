namespace RobotTwin.CoreSim.Specs
{
    public class EnvironmentSpec
    {
        public string Name { get; set; } = "DefaultEnvironment";
        public double TemperatureC { get; set; } = 22.0;
        public double VibrationAmplitude { get; set; } = 0.0;
        public double VibrationFrequencyHz { get; set; } = 0.0;
        public double Gravity { get; set; } = 9.81;
        public double AirDensity { get; set; } = 1.225;
        public double SurfaceFriction { get; set; } = 0.6;
    }
}
