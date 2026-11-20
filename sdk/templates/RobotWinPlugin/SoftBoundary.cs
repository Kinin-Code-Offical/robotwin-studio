namespace RobotWinPlugin.Samples;

/// <summary>
/// A force field (IForceField via the ForceField base): keeps bodies inside a box with a
/// spring–damper "wall" acting beyond the boundary. Useful for drones and floating objects.
/// The box is centred on the field's position (Ortam → Kuvvet alanları → plugin field).
/// </summary>
[Behavior("Yumuşak sınır", Description = "Nesneleri bir kutunun içinde tutan yay–sönüm duvarı (kuvvet alanı).", AttachTo = "world")]
public sealed class SoftBoundary : ForceField
{
    [Parameter("Yarı boyut", Unit = "m")] public Vec3 HalfSize { get; set; } = new Vec3(1.4, 0.9, 1.0);
    [Parameter("Yay (kg başına)", Unit = "N/m", Min = 0)] public double Stiffness { get; set; } = 40;
    [Parameter("Sönüm (kg başına)", Unit = "N·s/m", Min = 0)] public double Damping { get; set; } = 4;

    private Vec3 _center;

    public override string Name => "Yumuşak sınır";

    public override void Configure(IReadOnlyDictionary<string, string> parameters, ForceFieldSpec spec) => _center = spec.Center;

    public override (Vec3 Force, Vec3 Torque) Evaluate(ISimBody body, IBehaviorContext ctx)
    {
        var p = body.Position - _center;

        double Axis(double x, double h, double v)
        {
            double over = Math.Abs(x) - h;
            return over <= 0 ? 0 : -Math.Sign(x) * Stiffness * over - Damping * v;
        }

        var a = new Vec3(Axis(p.X, HalfSize.X, body.Velocity.X), Axis(p.Y, HalfSize.Y, body.Velocity.Y), Axis(p.Z, HalfSize.Z, body.Velocity.Z));
        return (a * body.Mass, Vec3.Zero);
    }
}
