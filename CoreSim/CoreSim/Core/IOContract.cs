using System.Collections.Generic;

namespace CoreSim.Core
{
    public enum SignalType
    {
        Digital, // bool (0, 1)
        Analog,  // double (0.0 - 5.0 typically, or 0-1023 bits)
        PWM,     // double (duty cycle 0.0-1.0)
        Serial,  // stream
        I2C
    }

    public class Signal
    {
        public string ID { get; set; }
        public SignalType Type { get; set; }
        public object Value { get; set; }
        public double Timestamp { get; set; }
    }

    public interface IIODevice
    {
        void Write(string pin, Signal signal);
        Signal Read(string pin);
    }
}
