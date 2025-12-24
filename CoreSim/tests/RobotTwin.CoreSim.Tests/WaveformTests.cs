using Xunit;
using RobotTwin.CoreSim.Runtime;
using System;

namespace RobotTwin.CoreSim.Tests
{
    public class WaveformTests
    {
        [Fact]
        public void ConstantWaveform_ReturnsValue()
        {
            var wf = new ConstantWaveform(5.0);
            Assert.Equal(5.0, wf.Sample(0));
            Assert.Equal(5.0, wf.Sample(10.0));
        }

        [Fact]
        public void StepWaveform_SwitchesAtTime()
        {
            var wf = new StepWaveform(0.0, 1.0, 2.0);
            Assert.Equal(0.0, wf.Sample(1.9));
            Assert.Equal(1.0, wf.Sample(2.0));
            Assert.Equal(1.0, wf.Sample(2.1));
        }

        [Fact]
        public void RampWaveform_Interpolates()
        {
            var wf = new RampWaveform(0.0, 2.0, 0.0, 10.0);
            Assert.Equal(0.0, wf.Sample(-1));
            Assert.Equal(0.0, wf.Sample(0));
            Assert.Equal(5.0, wf.Sample(1.0)); // Midpoint
            Assert.Equal(10.0, wf.Sample(2.0));
            Assert.Equal(10.0, wf.Sample(3.0));
        }

        [Fact]
        public void SineWaveform_Oscillates()
        {
            var wf = new SineWaveform(1.0, 1.0, 0.0); // 1Hz, Amp 1
            Assert.Equal(0.0, wf.Sample(0), 5);
            Assert.Equal(1.0, wf.Sample(0.25), 5); // Peak at 1/4 cycle
            Assert.Equal(0.0, wf.Sample(0.5), 5);
            Assert.Equal(-1.0, wf.Sample(0.75), 5);
        }
    }
}
