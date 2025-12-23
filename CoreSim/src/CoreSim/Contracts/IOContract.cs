using System.Text.Json.Serialization;

namespace RobotTwin.CoreSim.Contracts
{
    /// <summary>
    /// Defines a hardware interface contract between a Board and a Peripheral (Sensor/Actuator).
    /// </summary>
    public class IOContract
    {
        /// <summary>
        /// Unique ID of the signal (e.g., "S1_SERVO_PIN").
        /// </summary>
        public string SignalId { get; set; } = string.Empty;

        /// <summary>
        /// The electrical type of the signal.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public SignalType Type { get; set; }

        /// <summary>
        /// Optional physical unit metadata (e.g., "Hz" for PWM, "Baud" for UART).
        /// </summary>
        public string? Unit { get; set; }

        /// <summary>
        /// Optional nominal value or range (e.g., "50" for 50Hz).
        /// </summary>
        public double? NominalValue { get; set; }
    }
}
