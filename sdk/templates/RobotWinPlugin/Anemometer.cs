using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Design;

namespace RobotWinPlugin.Samples;

/// <summary>
/// A custom circuit element (AnalogDevice subclass): a cup anemometer with a 0.4–2.0 V output
/// for 0–32.4 m/s, modelled as a Norton source with an output resistance. Stamp() writes its
/// conductance and current into the circuit matrix at every solver iteration.
/// </summary>
public sealed class AnemometerDevice : AnalogDevice
{
    public AnemometerDevice(string id, int output, int ground) : base(id)
    {
        Output = output;
        Ground = ground;
    }

    public int Output { get; }
    public int Ground { get; }

    /// <summary>Wind speed (m/s), set by <see cref="AnemometerSensor"/> every physics step.</summary>
    public double WindSpeed { get; set; }

    public double OutputResistance { get; set; } = 1000;

    public double OutputVoltage => 0.4 + 1.6 * Math.Min(1, Math.Max(0, WindSpeed) / 32.4);

    public override void Stamp(StampContext ctx)
    {
        double g = 1.0 / OutputResistance;
        ctx.AddConductance(Output, Ground, g);
        ctx.AddCurrent(Ground, Output, OutputVoltage * g);
    }
}

/// <summary>Registers the "sample-anemometer" part (pins OUT, GND) in the part library (Parçalar → Sensör).</summary>
public sealed class AnemometerParts : IPartProvider
{
    public IEnumerable<PartDefinition> Parts()
    {
        var def = new PartDefinition
        {
            Id = "sample-anemometer", Name = "Anemometre (örnek eklenti)", Category = "Sensör",
            Description = "0.4–2.0 V çıkış, 0–32.4 m/s. Nesneye 'Anemometre sensörü' davranışını ekleyin.",
            RefPrefix = "U", Symbol = SymbolKind.Module
        };
        def.Pins.Add(new PinDefinition("OUT", "1", -3, 0));
        def.Pins.Add(new PinDefinition("GND", "2", 3, 0));
        def.Build = ctx => ctx.Add(new AnemometerDevice(ctx.RefDes, ctx.Pin("OUT"), ctx.Pin("GND")));
        yield return def;
    }

    public IEnumerable<PartPreset> Presets()
    {
        yield return new PartPreset("sample-anemometer", "Anemometre (örnek eklenti)", "sample-anemometer", "Sensör",
            "Rüzgâr hızını gerilime çevirir", new Dictionary<string, string>());
    }
}

/// <summary>A custom sensor: reads the wind at the object and drives the object's anemometer device.</summary>
[Behavior("Anemometre sensörü", Description = "Nesnenin konumundaki rüzgârı ölçer ve anemometre parçasının çıkışını sürer.")]
public sealed class AnemometerSensor : WorldBehavior
{
    [Parameter("Ölçüm yüksekliği (nesne üstü)", Unit = "m")] public double Height { get; set; } = 0.05;

    public override void OnStep(IBehaviorContext ctx, double dt)
    {
        var b = ctx.Self;
        if (b == null) return;
        var w = ctx.Air.WindAt(b.LocalToWorld(new Vec3(0, 0, Height))) - b.Velocity;
        double speed = Math.Sqrt(w.X * w.X + w.Y * w.Y);
        foreach (var d in ctx.Circuit.Devices(b.Id).OfType<AnemometerDevice>()) d.WindSpeed = speed;
        ctx.Publish("rüzgâr_m_s", speed);
    }
}
