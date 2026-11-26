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
        public double SupplyVoltage { get; internal set; } = double.NaN;

        /// <summary>The strip has enough supply to take data and light (WS2812B: 3.5–5.3 V).</summary>
        public bool Powered => SupplyVoltage >= 3.5;

        /// <summary>The strip whose DIN this strip's DOUT drives (a chain), or null.</summary>
        public Ws2812Load? Next { get; set; }

        /// <summary>[neopixel-vih] The high level of the data the strip saw last (V), NaN before any.</summary>
        public double DataHigh { get; private set; } = double.NaN;

        /// <summary>[neopixel-vih] The datasheet's guaranteed input high level: VIH = 0.7·VDD (3.5 V on a 5 V strip).</summary>
        public double InputHigh => 0.7 * SupplyVoltage;

        /// <summary>[neopixel-vih] The data's high level is below the datasheet's VIH (it works on most strips, not on every one).</summary>
        public bool DataOutOfSpec => Powered && !double.IsNaN(DataHigh) && DataHigh < InputHigh;

        private string? _warning;
        private bool _warnedSpec, _warnedLost;

        /// <summary>[neopixel-vih] A warning about the data level not yet told (once each kind), or null.</summary>
        public string? TakeWarning()
        {
            string? w = _warning;
            _warning = null;
            return w;
        }

        /// <summary>
        /// A frame arriving on DIN: the LEDs take their words and what runs past the last LED leaves through DOUT into
        /// the next strip of the chain. A strip without its supply neither lights nor passes anything on.
        /// [neopixel-vih] <paramref name="dataHigh"/> is the high level the sender drives (an ESP32's 3.3 V): below the
        /// datasheet's VIH = 0.7·VDD it is out of spec (said once; it still works, as on most real strips), below half of
        /// VDD (where a CMOS input switches) the strip does not see the data at all. Each strip of a chain gets its data
        /// again from the one before it, at that strip's supply.
        /// </summary>
        public void Receive(byte[] frame, double dataHigh = double.NaN)
        {
            var strip = this;
            double level = dataHigh;
            for (int hops = 0; strip != null && strip.Powered && frame.Length > 0 && hops < 64; hops++)
            {
                if (!strip.Accepts(level)) return;
                frame = strip.Strip.Show(frame);
                level = strip.SupplyVoltage;   // DOUT is driven from the strip's own supply
                strip = strip.Next;
            }
        }

        private bool Accepts(double level)
        {
            if (double.IsNaN(level)) return true;
            DataHigh = level;
            if (level < 0.5 * SupplyVoltage)
            {
                if (!_warnedLost)
                {
                    _warnedLost = true;
                    _warning = $"{Id}: veri {level:0.0#} V, {SupplyVoltage:0.0#} V'luk şerit bunu okuyamaz (VIH {InputHigh:0.0#} V); seviye dönüştürücü (74AHCT125) kullanın";
                }

                return false;
            }

            if (level < InputHigh && !_warnedSpec)
            {
                _warnedSpec = true;
                _warning = $"{Id}: veri {level:0.0#} V, {SupplyVoltage:0.0#} V'luk şeridin VIH sınırının ({InputHigh:0.0#} V) altında: çoğu şeritte çalışır ama güvencesi yok; seviye dönüştürücü (74AHCT125) ya da ilk LED'i bir diyotla ~4,3 V'tan besleme";
            }

            return true;
        }

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
