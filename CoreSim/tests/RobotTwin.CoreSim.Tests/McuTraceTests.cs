using System;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Firmware;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [timeline] The chip's own logic analyzer on a real avr-gcc build (the servo arm: D9 and D10 pulse 544 + angle·1856/180 µs
    /// every loop with the angle stepping by 6°, delay(16) between, Timer 0 overflowing every 1.024 ms for millis()). A pulse is
    /// that long plus digitalWrite's few microseconds, plus the millis() interrupt's ~6 µs when one lands inside it.
    /// </summary>
    public class McuTraceTests
    {
        private readonly ITestOutputHelper _out;

        public McuTraceTests(ITestOutputHelper output) => _out = output;

        private static Atmega328P ServoArm(int capacity = 32768)
        {
            var mcu = new Atmega328P();
            mcu.LoadHex(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "servo-arm-debug.hex")));
            mcu.StartTrace(capacity);
            return mcu;
        }

        private static double Us(Atmega328P mcu, ulong cycles) => cycles / mcu.ClockHz * 1e6;

        [Fact]
        public void ServoPulsesAreOnTheirPinsWithTheirWidths()
        {
            var mcu = ServoArm();
            mcu.Run(0.1);
            var trace = mcu.Trace!;
            var d9 = trace.Edges(9, 0, mcu.Cycles);
            var d10 = trace.Edges(10, 0, mcu.Cycles);
            Assert.True(d9.Count >= 10, $"D9 has {d9.Count} edges");
            Assert.True(d9[0].Level && !d9[1].Level, "a pulse is a rise then a fall");
            double first = Us(mcu, d9[1].Cycle - d9[0].Cycle), second = Us(mcu, d9[3].Cycle - d9[2].Cycle);
            double elbow = Us(mcu, d10[1].Cycle - d10[0].Cycle);
            double period = Us(mcu, d9[2].Cycle - d9[0].Cycle) / 1000;
            _out.WriteLine($"D9 {first:F1} µs then {second:F1} µs, D10 {elbow:F1} µs, every {period:F2} ms; {trace.Count} events");
            Assert.InRange(first, 544, 560);                    // angle 0
            Assert.InRange(second, 605, 620);                   // angle 6: 544 + 6·1856/180 = 605.9
            Assert.InRange(elbow, 1008, 1030);                  // 45 + 0/2 → 1008
            Assert.InRange(period, 17.2, 18.2);                 // delay(16) + both pulses
            Assert.True(trace.LevelAt(9, (d9[0].Cycle + d9[1].Cycle) / 2));
            Assert.False(trace.LevelAt(9, (d9[1].Cycle + d9[2].Cycle) / 2));
        }

        [Fact]
        public void Timer0OverflowsEveryMillisecondAndReturns()
        {
            var mcu = ServoArm();
            mcu.Run(0.1);
            var isrs = mcu.Trace!.Interrupts(0, mcu.Cycles);
            var t0 = isrs.Where(i => AvrRegisters.VectorName(i.Vector) == "TIMER0_OVF").ToList();
            var longest = t0.Where(i => i.Return != ulong.MaxValue).Max(i => Us(mcu, i.Return - i.Enter));
            _out.WriteLine($"{t0.Count} TIMER0_OVF in 100 ms, longest {longest:F2} µs; others: "
                + string.Join(", ", isrs.Where(i => i.Vector != 16).GroupBy(i => AvrRegisters.VectorName(i.Vector)).Select(g => g.Key + " ×" + g.Count())));
            Assert.InRange(t0.Count, 95, 99);                   // 100 ms / 1.024 ms
            Assert.InRange(longest, 2, 20);
            Assert.All(t0.Skip(1).Zip(t0, (b, a) => Us(mcu, b.Enter - a.Enter)), gap => Assert.InRange(gap, 1000, 1050));
            Assert.Contains(isrs, i => AvrRegisters.VectorName(i.Vector) == "USART_UDRE");   // setup()'s println
        }

        [Fact]
        public void AFullRingStillKnowsEveryLevel()
        {
            var small = ServoArm(64);
            var big = ServoArm();
            small.Run(0.2);
            big.Run(0.2);
            var s = small.Trace!;
            Assert.Equal(64, s.Count);
            Assert.True(s.StartCycle > 0);
            ulong last = s[s.Count - 1].Cycle;
            // The small ring forgot all but its last 64 events, yet every pin's level after them matches the full record.
            for (int pin = 0; pin < McuTrace.Pins; pin++) Assert.Equal(big.Trace!.LevelAt(pin, last), s.LevelAt(pin, last));
            Assert.True(Enumerable.Range(0, McuTrace.Pins).Any(p => big.Trace!.Edges(p, 0, s.StartCycle).Count > 0), "events were forgotten");
        }

        [Fact]
        public void NamesAreAvrLibcs()
        {
            Assert.Equal("TIMER0_OVF", AvrRegisters.VectorName(16));
            Assert.Equal("INT0", AvrRegisters.VectorName(1));
            Assert.Equal("TWI", AvrRegisters.VectorName(24));
            Assert.Equal("D13", AvrRegisters.PinName(13));
            Assert.Equal("A5", AvrRegisters.PinName(19));
        }
    }
}
