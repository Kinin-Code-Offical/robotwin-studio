namespace RobotWinPlugin.Samples;

/// <summary>
/// Two permanent magnets as point dipoles on a common axis: F = 3·μ0·m1·m2 / (2π·r⁴)
/// (attractive for aligned moments). Equal and opposite forces; softened below MinDistance.
/// </summary>
[Behavior("Dipol mıknatıs", Description = "F = 3μ₀m₁m₂ / (2πr⁴) eksenel dipoller; eşit ve zıt kuvvet.")]
public sealed class DipoleMagnet : WorldBehavior
{
    private const double Mu0 = 4e-7 * Math.PI;

    [Parameter("Diğer mıknatıs")] public string Other { get; set; } = "";
    [Parameter("Moment m₁", Unit = "A·m²")] public double Moment1 { get; set; } = 0.5;
    [Parameter("Moment m₂", Unit = "A·m²", Description = "Negatif = ters kutup (iter)")] public double Moment2 { get; set; } = 0.5;
    [Parameter("En küçük mesafe", Unit = "m", Min = 1e-4)] public double MinDistance { get; set; } = 0.005;

    private ISimBody? _other;

    public override void OnStart(IBehaviorContext ctx)
    {
        _other = ctx.Body(Other);
        if (_other == null) ctx.Warn($"'{Other}' bulunamadı.");
    }

    public override void OnStep(IBehaviorContext ctx, double dt)
    {
        var a = ctx.Self;
        if (a == null || _other == null) return;
        var d = _other.Position - a.Position;
        double r = Math.Max(MinDistance, d.Length);
        double f = 3 * Mu0 * Moment1 * Moment2 / (2 * Math.PI * Math.Pow(r, 4));
        var force = d.Normalized() * f;
        a.ApplyForce(force);
        _other.ApplyForce(-force);
        ctx.Publish("kuvvet_N", f);
    }
}
