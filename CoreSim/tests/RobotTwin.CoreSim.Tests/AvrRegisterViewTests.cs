using System;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Firmware;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[registers] The debugger's register view: the registers as the program would read them, and looking changes nothing.</summary>
    public class AvrRegisterViewTests
    {
        private readonly ITestOutputHelper _out;

        public AvrRegisterViewTests(ITestOutputHelper output) => _out = output;

        private static Atmega328P Running(double seconds)
        {
            var mcu = new Atmega328P();
            mcu.LoadHex(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "servo-arm-debug.hex")));
            mcu.Run(seconds);
            return mcu;
        }

        [Fact]
        public void TheTableIsTheDatasheets()
        {
            var regs = AvrRegisters.Atmega328P;
            Assert.Equal(regs.Count, regs.Select(r => r.Address).Distinct().Count());
            Assert.All(regs, r => Assert.InRange(r.Address, 0x20, 0xFF));
            Assert.All(regs.Where(r => r.Bits != null), r => Assert.Equal(8, r.Bits!.Length));
            Assert.Equal(0x5F, AvrRegisters.Find("sreg")!.Address);
            Assert.Equal("TCNT1L", AvrRegisters.LowHalf(AvrRegisters.Find("TCNT1H")!)!.Name);
            Assert.Equal("SPL", AvrRegisters.LowHalf(AvrRegisters.Find("SPH")!)!.Name);
            Assert.Null(AvrRegisters.LowHalf(AvrRegisters.Find("TCCR1B")!));
            Assert.Equal("I-----Z-", AvrRegisters.Flags(0x82));
            Assert.Equal("RXC0 UDRE0", AvrRegisters.Find("UCSR0A")!.SetBits(0xA0));
            Assert.Equal("0010 0000", AvrRegister.Binary(0x20));
        }

        [Fact]
        public void TheViewShowsWhatTheProgramWouldRead()
        {
            var mcu = Running(0.05);
            byte sreg = mcu.Peek(0x5F);
            _out.WriteLine($"SREG {AvrRegisters.Flags(sreg)}, raw memory byte 0x{mcu.ReadData(0x5F):X2}");
            Assert.Equal(mcu.StatusRegister, sreg);
            Assert.True((sreg & 0x80) != 0, "the Arduino core enables interrupts before setup()");

            // A pin driven as an output reads back through PINx as the level PORTx gives it.
            byte ddrb = mcu.Peek(0x24), portb = mcu.Peek(0x25), pinb = mcu.Peek(0x23);
            Assert.Equal(portb & ddrb, pinb & ddrb);

            // Timer 0 (millis) runs: its counter moves between two looks.
            byte t0 = mcu.Peek(0x46);
            mcu.Run(100e-6);
            Assert.NotEqual(t0, mcu.Peek(0x46));
            Assert.Equal(mcu.Peek(0x5D) | (mcu.Peek(0x5E) << 8), mcu.StackPointer);
        }

        [Fact]
        public void LookingChangesNothing()
        {
            var mcu = Running(0.02);
            var before = Enumerable.Range(0, 0x100).Select(mcu.ReadData).ToArray();
            for (int pass = 0; pass < 3; pass++)
                foreach (var r in AvrRegisters.Atmega328P) mcu.Peek(r.Address);
            var after = Enumerable.Range(0, 0x100).Select(mcu.ReadData).ToArray();
            Assert.Equal(before, after);
        }
    }
}
