namespace RobotWinPlugin.Samples;

/// <summary>
/// Wind turbine on a hinge (rotor shaft): aerodynamic torque from the local wind with a
/// C_P(λ) curve capped by the Betz limit, minus a generator load k_g·ω. Publishes rpm and
/// power so a circuit (SDK sensor part) or another behaviour can use them.
/// Attach it to the rotor object; join the rotor to its mast with a hinge whose axis points
/// into the wind.
/// </summary>
[Behavior("Rüzgâr türbini (jeneratörlü)", Description = "Rotor torku ½ρAR·C_P(λ)/λ·v² − jeneratör yükü; devir ve güç yayımlar.")]
public sealed class WindTurbine : WorldBehavior
{
    [Parameter("Rotor çapı", Unit = "m", Min = 0.01)] public double Diameter { get; set; } = 0.4;
    [Parameter("C_P en büyük", Min = 0, Max = 0.593, Description = "Betz sınırı 16/27 ≈ 0.593")] public double CpMax { get; set; } = 0.42;
    [Parameter("En iyi λ", Min = 0.5)] public double LambdaOpt { get; set; } = 6.5;
    [Parameter("Jeneratör katsayısı", Unit = "N·m·s/rad", Min = 0)] public double GeneratorK { get; set; } = 2e-4;
    [Parameter("Mafsal kimliği (boş = ilk menteşe)")] public string Joint { get; set; } = "";

    private ISimJoint? _hinge;

    public override void OnStart(IBehaviorContext ctx)
    {
        _hinge = Joint.Length > 0 ? ctx.Joint(Joint)
            : ctx.Joints.FirstOrDefault(j => j.Kind == JointKind.Hinge && (j.Child == ctx.Self || j.Parent == ctx.Self));
        if (_hinge == null) ctx.Warn("Menteşe bulunamadı: rotoru direğe bir menteşeyle bağlayın.");
    }

    public override void OnStep(IBehaviorContext ctx, double dt)
    {
        if (_hinge == null) return;
        var hub = _hinge.Child;
        var axis = _hinge.Axis;
        double v = Math.Abs((ctx.Air.WindAt(hub.Position) - hub.Velocity).Dot(axis));
        double r = Diameter / 2, area = Math.PI * r * r;
        double omega = _hinge.Speed;
        double lambda = v > 1e-3 ? Math.Abs(omega) * r / v : 0;
        double x = lambda / LambdaOpt;
        double cp = Math.Max(0, CpMax * x * (2 - x));
        // C_T = C_P / λ, finite at λ → 0 (starting torque).
        double ct = CpMax / LambdaOpt * Math.Max(0, 2 - x);
        double aero = 0.5 * ctx.Air.Density * area * r * ct * v * v;
        double load = GeneratorK * omega;
        _hinge.MotorTorque = aero - load;
        ctx.Publish("devir_dk", omega * 60 / (2 * Math.PI));
        ctx.Publish("aero_güç_W", 0.5 * ctx.Air.Density * area * cp * v * v * v);
        ctx.Publish("elektrik_güç_W", load * omega);
    }

    public override void OnStop(IBehaviorContext ctx)
    {
        if (_hinge != null) _hinge.MotorTorque = 0;
    }
}
