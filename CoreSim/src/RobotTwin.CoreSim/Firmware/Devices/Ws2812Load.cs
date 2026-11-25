using RobotTwin.CoreSim.Analog;

namespace RobotTwin.CoreSim.Firmware.Devices
{
    /// <summary>
    /// [neopixel] A WS2812B strip in the circuit: it draws its LEDs' current from VDD (resistive around the nominal 5 V, so
    /// it follows the colours <see cref="Strip"/> holds), its DIN is a high-impedance input, and it lights only while its
    /// supply is within the part's range (3.5 V and up).
    /// </summary>
    public sealed class Ws2812Load : AnalogDevice
    {
        public Ws2812Load(string id, int vdd, int ground, int din, int count) : base(id)
        {
            Vdd = vdd;
            Ground = ground;
            Din = din;
            Strip = new Ws2812Strip(count);
            Strip.Updated += _ => PendingDiscontinuity = true;
        }

        public int Vdd { get; }
        public int Ground { get; }
        public int Din { get; }

        /// <summary>The LEDs and their colours.</summary>
        public Ws2812Strip Strip { get; }

        public double NominalVoltage { get; set; } = 5.0;
        public double SupplyVoltage { get; private set; } = double.NaN;

        /// <summary>The strip has enough supply to take data and light (WS2812B: 3.5–5.3 V).</summary>
        public bool Powered => SupplyVoltage >= 3.5;

        public override void Stamp(StampContext ctx)
        {
            ctx.AddConductance(Vdd, Ground, Strip.SupplyCurrent / NominalVoltage);
            ctx.AddConductance(Din, Ground, 1e-9);
        }

        public override void Accept(AcceptContext ctx) => SupplyVoltage = ctx.V(Vdd, Ground);

        public override void Report(ISignalSink sink)
        {
            sink.Write(Id, "VDD", SupplyVoltage);
            sink.Write(Id, "I", double.IsNaN(SupplyVoltage) ? 0 : SupplyVoltage * Strip.SupplyCurrent / NominalVoltage);
        }
    }
}
