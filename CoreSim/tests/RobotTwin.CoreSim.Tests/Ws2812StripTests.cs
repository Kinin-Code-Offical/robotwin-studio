using RobotTwin.CoreSim.Firmware.Devices;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[neopixel] The WS2812B chain's protocol: GRB words land LED by LED, the rest passes on, current follows colour.</summary>
    public class Ws2812StripTests
    {
        [Fact]
        public void EachLedTakesItsWordInGrbOrder()
        {
            var strip = new Ws2812Strip(3);
            byte[] rest = strip.Show(new byte[] { 0x10, 0xFF, 0x00, 0x00, 0x00, 0x80 });   // LED0 red-ish, LED1 blue
            Assert.Empty(rest);
            Assert.Equal(((byte)0xFF, (byte)0x10, (byte)0x00), strip.Colors[0]);
            Assert.Equal(((byte)0x00, (byte)0x00, (byte)0x80), strip.Colors[1]);
            Assert.Equal(((byte)0, (byte)0, (byte)0), strip.Colors[2]);           // not reached: keeps its colour
            Assert.Equal(1, strip.Frames);
        }

        [Fact]
        public void WordsPastTheChainLeaveThroughDout()
        {
            var strip = new Ws2812Strip(1);
            byte[] rest = strip.Show(new byte[] { 1, 2, 3, 4, 5, 6, 7 });
            Assert.Equal(((byte)2, (byte)1, (byte)3), strip.Colors[0]);
            Assert.Equal(new byte[] { 4, 5, 6, 7 }, rest);
        }

        [Fact]
        public void WiringFindsTheStripOnItsGpio()
        {
            var lib = RobotTwin.CoreSim.Design.PartLibrary.Default;
            var def = lib.Get(RobotTwin.CoreSim.Firmware.LedStripWiring.PartId);
            var load = new Ws2812Load("D1", 1, 0, 7, 8);
            // GPIO5 is node 6, wired to node 7 (the strip's DIN); GPIO4 is node 9, unconnected.
            var found = RobotTwin.CoreSim.Firmware.LedStripWiring.Attach(
                new[] { ("D1", def) },
                (r, pin) => pin == "DIN" ? 7 : -1,
                g => g == 5 ? 6 : g == 4 ? 9 : -1,
                n => n == 6 ? new System.Collections.Generic.HashSet<int> { 6, 7 } : new System.Collections.Generic.HashSet<int> { n },
                r => r == "D1" ? load : null);
            Assert.Single(found);
            Assert.Equal(5, found[0].Gpio);
            Assert.Same(load, found[0].Strip);
        }

        [Fact]
        public void ChainedStripsPassTheRestOfTheFrameOn()
        {
            // D1 (2 LEDs) on GPIO5; its DOUT (node 8) is wired to D2's DIN (node 9 through a wire); D2 has 1 LED.
            var lib = RobotTwin.CoreSim.Design.PartLibrary.Default;
            var def = lib.Get(RobotTwin.CoreSim.Firmware.LedStripWiring.PartId);
            var d1 = new Ws2812Load("D1", 1, 0, 7, 2) { SupplyVoltage = 5 };
            var d2 = new Ws2812Load("D2", 1, 0, 9, 1) { SupplyVoltage = 5 };
            var found = RobotTwin.CoreSim.Firmware.LedStripWiring.Attach(
                new[] { ("D1", def), ("D2", def) },
                (r, pin) => (r, pin) switch { ("D1", "DIN") => 7, ("D1", "DOUT") => 8, ("D2", "DIN") => 9, _ => -1 },
                g => g == 5 ? 7 : -1,
                n => n == 8 || n == 9 ? new System.Collections.Generic.HashSet<int> { 8, 9 } : new System.Collections.Generic.HashSet<int> { n },
                r => r == "D1" ? d1 : r == "D2" ? d2 : null);
            Assert.Single(found);                      // only D1 hangs on the GPIO
            Assert.Same(d2, d1.Next);
            d1.Receive(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            Assert.Equal(((byte)5, (byte)4, (byte)6), d1.Strip.Colors[1]);
            Assert.Equal(((byte)8, (byte)7, (byte)9), d2.Strip.Colors[0]);   // the third word went through D1's DOUT

            d2.SupplyVoltage = 0;                      // an unpowered strip takes nothing
            d1.Receive(new byte[] { 0, 0, 0, 0, 0, 0, 10, 20, 30 });
            Assert.Equal(((byte)8, (byte)7, (byte)9), d2.Strip.Colors[0]);
            d1.SupplyVoltage = 0;                      // nor passes anything on
            d2.SupplyVoltage = 5;
            d1.Receive(new byte[] { 0, 0, 0, 0, 0, 0, 10, 20, 30 });
            Assert.Equal(((byte)8, (byte)7, (byte)9), d2.Strip.Colors[0]);
        }

        /// <summary>[neopixel-vih] An ESP32's 3.3 V data into a 5 V strip: out of the datasheet's VIH (said once) but read; 1.8 V is not.</summary>
        [Fact]
        public void TheDataLevelIsHeldAgainstTheStripsInputHigh()
        {
            var a = new Ws2812Load("D1", 1, 0, 2, 2) { SupplyVoltage = 5.0 };
            var b = new Ws2812Load("D2", 3, 0, 4, 1) { SupplyVoltage = 5.0 };
            a.Next = b;
            a.Receive(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, 3.3);
            Assert.Equal(((byte)2, (byte)1, (byte)3), a.Strip.Colors[0]);   // read: 3.3 V is above half of 5 V
            Assert.True(a.DataOutOfSpec);   // but below VIH = 3.5 V
            Assert.Contains("VIH", a.TakeWarning());
            Assert.Null(a.TakeWarning());   // said once
            Assert.False(b.DataOutOfSpec);   // the next strip gets its data again from D1's 5 V
            Assert.Equal(((byte)8, (byte)7, (byte)9), b.Strip.Colors[0]);

            a.Receive(new byte[] { 0, 0, 0 }, 1.8);   // a 1.8 V output: the strip does not see it
            Assert.Equal(((byte)2, (byte)1, (byte)3), a.Strip.Colors[0]);
            Assert.Contains("okuyamaz", a.TakeWarning());

            var fine = new Ws2812Load("D3", 1, 0, 2, 1) { SupplyVoltage = 3.7 };   // a strip on a Li-ion cell: 3.3 V ≥ 0.7·3.7
            fine.Receive(new byte[] { 9, 9, 9 }, 3.3);
            Assert.False(fine.DataOutOfSpec);
            Assert.Null(fine.TakeWarning());
        }

        [Fact]
        public void CurrentFollowsTheColours()
        {
            var strip = new Ws2812Strip(2);
            Assert.Equal(2e-3, strip.SupplyCurrent, 9);                          // dark: about 1 mA each
            strip.Show(new byte[] { 255, 255, 255 });                              // LED0 full white
            Assert.Equal(2e-3 + 60e-3, strip.SupplyCurrent, 9);
        }
    }
}
