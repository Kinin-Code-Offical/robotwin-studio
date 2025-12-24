using System;

namespace RobotTwin.CoreSim.Runtime
{
    public interface IWaveform
    {
        double Sample(double time);
    }

    public class ConstantWaveform : IWaveform
    {
        public double Value { get; set; }
        public ConstantWaveform(double value) { Value = value; }
        public double Sample(double time) => Value;
    }

    public class StepWaveform : IWaveform
    {
        public double InitialValue { get; set; }
        public double FinalValue { get; set; }
        public double StepTime { get; set; }

        public StepWaveform(double initial, double final, double stepTime)
        {
            InitialValue = initial;
            FinalValue = final;
            StepTime = stepTime;
        }

        public double Sample(double time)
        {
            return time >= StepTime ? FinalValue : InitialValue;
        }
    }

    public class RampWaveform : IWaveform
    {
        public double StartTime { get; set; }
        public double EndTime { get; set; }
        public double StartValue { get; set; }
        public double EndValue { get; set; }

        public RampWaveform(double startTime, double endTime, double startVal, double endVal)
        {
            StartTime = startTime;
            EndTime = endTime;
            StartValue = startVal;
            EndValue = endVal;
        }

        public double Sample(double time)
        {
            if (time <= StartTime) return StartValue;
            if (time >= EndTime) return EndValue;
            double t = (time - StartTime) / (EndTime - StartTime);
            return StartValue + t * (EndValue - StartValue);
        }
    }

    public class SineWaveform : IWaveform
    {
        public double Frequency { get; set; } // Hz
        public double Amplitude { get; set; }
        public double Offset { get; set; }
        public double Phase { get; set; } // Radians

        public SineWaveform(double freq, double amp, double offset, double phase = 0)
        {
            Frequency = freq;
            Amplitude = amp;
            Offset = offset;
            Phase = phase;
        }

        public double Sample(double time)
        {
            return Offset + Amplitude * Math.Sin(2 * Math.PI * Frequency * time + Phase);
        }
    }
}
