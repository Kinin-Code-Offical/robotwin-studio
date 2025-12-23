namespace RobotTwin.CoreSim.Contracts
{
    /// <summary>
    /// Defines the electrical properties and usage of a pin/signal.
    /// </summary>
    public enum SignalType
    {
        DigitalInput,
        DigitalOutput,
        AnalogInput,
        PwmOutput,
        I2C,
        SPI,
        UART
    }
}
