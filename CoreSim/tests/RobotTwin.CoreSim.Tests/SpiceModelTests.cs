using System;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[spice] Manufacturers' .model cards into our parts: the numbers, the suffixes, and the device behaving by them.</summary>
    public class SpiceModelTests
    {
        private readonly ITestOutputHelper _out;

        public SpiceModelTests(ITestOutputHelper output) => _out = output;

        private const string Cards = @"
* A small-signal diode and a general-purpose NPN, as vendors' libraries write them
.model D1N4148 D(IS=2.52n RS=.568 N=1.752 CJO=4p M=.4 TT=20n
+ BV=100 IBV=100u)   ; continuation line
.model Q2N2222A NPN(IS=14.34f XTI=3 EG=1.11 VAF=74.03 BF=255.9 NE=1.307 ISE=14.34f IKF=.2847
+ XTB=1.5 BR=6.092 NC=2 ISC=0 IKR=0 RC=1 CJC=7.306p MJC=.3416 VJC=.75 FC=.5 CJE=22.01p MJE=.377
+ VJE=.75 TR=46.91n TF=411.1p ITF=.6 VTF=1.7 XTF=3 RB=10 Vceo=40 Icrating=800m)
.MODEL MBIG NMOS (VTO=2.1 KP=20u LAMBDA=0.01)
";

        [Fact]
        public void CardsAreReadWithTheirContinuationsCommentsAndSuffixes()
        {
            var models = SpiceModels.Parse(Cards);
            Assert.Equal(new[] { "D1N4148", "Q2N2222A", "MBIG" }, models.Select(m => m.Name));
            Assert.Equal(new[] { "D", "NPN", "NMOS" }, models.Select(m => m.Type));
            Assert.Equal(2.52e-9, models[0].Parameters["IS"], 15);
            Assert.Equal(100e-6, models[0].Parameters["IBV"], 12);
            Assert.Equal(7.306, models[1].Parameters["CJC"] * 1e12, 9);
            Assert.Equal(0.8, models[1].Parameters["ICRATING"], 12);   // "800m": milli, not mega
            Assert.True(SpiceModels.TryNumber("1.5MEG", out double meg) && meg == 1.5e6);
            Assert.True(SpiceModels.TryNumber("2mil", out double mil) && Math.Abs(mil - 50.8e-6) < 1e-12);

            var (diode, unusedD) = SpiceModels.ToPart(models[0]);
            Assert.Equal("D1N4148", diode["model"]);
            Assert.Equal("4E-12", diode["cj"]);
            Assert.Contains("TT", unusedD);   // transit time: our diode has none, and it says so
            var (npn, _) = SpiceModels.ToPart(models[1]);
            Assert.Equal(("255.9", "74.03", "6.092"), (npn["bf"], npn["va"], npn["br"]));
            Assert.True(SpiceModels.Fits(models[1], "npn") && !SpiceModels.Fits(models[1], "pnp"));
        }

        /// <summary>A diode part with the vendor's card drops what Shockley's law with its IS, N and RS says.</summary>
        [Fact]
        public void ADiodeWithAVendorsCardDropsWhatItsModelSays()
        {
            var lib = PartLibrary.Default;
            var sch = new Schematic();
            var model = SpiceModels.Parse(Cards)[0];
            void Pin(SchematicPart part, string pin, string net)
            {
                var pd = lib.Get(part.PartId).FindPin(pin)!;
                sch.Labels.Add(new NetLabel { Name = net, Position = part.Transform(pd.X, pd.Y) });
            }

            var d = sch.AddPart(lib, "diode", new GridPoint(0, 0));
            foreach (var kv in SpiceModels.ToPart(model).Parameters) d.Parameters[kv.Key] = kv.Value;
            var src = sch.AddPart(lib, "bench-psu-5v-100ma", new GridPoint(-20, 0));
            var r = sch.AddPart(lib, "res-470", new GridPoint(-10, 0));
            Pin(src, "+", "V");
            Pin(src, "-", "GND");
            Pin(r, "A", "V");
            Pin(r, "B", "A");
            Pin(d, "A", "A");
            Pin(d, "K", "GND");
            var compiled = CircuitCompiler.Compile(sch, lib);
            var sim = new AnalogSimulator(compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            var diode = compiled.Circuit.Devices.OfType<Diode>().Single();
            double i = diode.Current, v = diode.Voltage;
            double vt = 0.025693;   // kT/q at 25 °C
            double shockley = 1.752 * vt * Math.Log(i / 2.52e-9 + 1) + i * 0.568;
            _out.WriteLine($"{i * 1000:0.000} mA'de {v * 1000:0.0} mV (Shockley {shockley * 1000:0.0} mV)");
            Assert.InRange(i, 0.007, 0.0095);
            Assert.InRange(v, shockley - 0.02, shockley + 0.02);   // its 27 °C IS scaled to the room by EG
        }
    }
}
