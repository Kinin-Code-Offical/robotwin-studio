namespace RobotWinPlugin.Samples;

/// <summary>
/// A wind source: a Gaussian thermal column rising from a hot spot, w(r) = W·exp(−r²/R²),
/// with a slow pulsation. It is added to the world's wind, so it lifts light bodies through
/// drag and changes propeller inflow. Use it from Ortam → Kuvvet alanları (plugin field).
/// </summary>
[Behavior("Termal yükselme", Description = "Sıcak noktadan yükselen Gauss hava sütunu (rüzgâr kaynağı).", AttachTo = "world")]
public sealed class ThermalUpdraft : IWindSource
{
    [Parameter("Merkez", Unit = "m")] public Vec3 Center { get; set; } = new Vec3(0, 0, 0);
    [Parameter("Çekirdek yarıçapı", Unit = "m", Min = 0.01)] public double Radius { get; set; } = 0.3;
    [Parameter("Yukarı hız", Unit = "m/s")] public double Speed { get; set; } = 1.5;
    [Parameter("Nabız periyodu", Unit = "s", Min = 0)] public double Period { get; set; } = 8;

    public Vec3 WindAt(Vec3 point, double time)
    {
        double dx = point.X - Center.X, dy = point.Y - Center.Y;
        double r2 = (dx * dx + dy * dy) / (Radius * Radius);
        double pulse = Period > 0 ? 0.8 + 0.2 * Math.Sin(2 * Math.PI * time / Period) : 1;
        return new Vec3(0, 0, Speed * pulse * Math.Exp(-r2));
    }
}
