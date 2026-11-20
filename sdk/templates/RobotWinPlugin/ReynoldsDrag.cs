namespace RobotWinPlugin.Samples;

/// <summary>
/// Replaces the built-in constant-Cd drag of a sphere with a Reynolds-number dependent law
/// (Schiller–Naumann + Clift–Gauvin, valid to Re ≈ 2·10⁵):
/// Cd = 24/Re·(1 + 0.15·Re^0.687) + 0.42/(1 + 42500·Re^−1.16).
/// Uses the air density and the viscosity at the world temperature.
/// </summary>
[Behavior("Reynolds sayısına bağlı sürükleme (küre)", Description = "Cd(Re) Schiller–Naumann + Clift–Gauvin; yerleşik sürüklemeyi kapatır.")]
public sealed class ReynoldsDrag : WorldBehavior
{
    [Parameter("Çap", Unit = "m", Min = 1e-5)] public double Diameter { get; set; } = 0.04;

    public override void OnStart(IBehaviorContext ctx)
    {
        if (ctx.Self != null) ctx.Self.DragArea = Vec3.Zero;   // switch the built-in law off for this body
    }

    public override void OnStep(IBehaviorContext ctx, double dt)
    {
        var b = ctx.Self;
        if (b == null) return;
        var rel = b.Velocity - ctx.Air.WindAt(b.Position);
        double v = rel.Length;
        if (v < 1e-9) return;
        double rho = ctx.Air.Density;
        double t = ctx.Air.TemperatureC + 273.15;
        double mu = 1.716e-5 * Math.Pow(t / 273.15, 1.5) * (273.15 + 110.4) / (t + 110.4);
        double re = rho * v * Diameter / mu;
        double cd = 24 / re * (1 + 0.15 * Math.Pow(re, 0.687)) + 0.42 / (1 + 42500 * Math.Pow(re, -1.16));
        double area = Math.PI * Diameter * Diameter / 4;
        double k = 0.5 * rho * cd * area * v;
        k = Math.Min(k, b.Mass / dt);   // explicit force over a fixed step: never reverse the velocity
        b.ApplyForce(rel * -k);
        ctx.Publish("Re", re);
        ctx.Publish("Cd", cd);
    }
}
