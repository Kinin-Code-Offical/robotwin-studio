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
        public void CurrentFollowsTheColours()
        {
            var strip = new Ws2812Strip(2);
            Assert.Equal(2e-3, strip.SupplyCurrent, 9);                          // dark: about 1 mA each
            strip.Show(new byte[] { 255, 255, 255 });                              // LED0 full white
            Assert.Equal(2e-3 + 60e-3, strip.SupplyCurrent, 9);
        }
    }
}
