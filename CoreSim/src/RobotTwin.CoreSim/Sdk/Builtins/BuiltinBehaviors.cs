using System;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

// Ready-made behaviours, registered through the same path as user plugins (key "builtin:<type>").
// They double as examples of the SDK: see sdk/templates for the plugin versions.
namespace RobotTwin.CoreSim.Sdk.Builtins
{
    [Behavior("Sabit kuvvet", Description = "Her adımda sabit bir kuvvet ve tork uygular (dünya veya gövde ekseninde).")]
    public sealed class ConstantForce : WorldBehavior
    {
        [Parameter("Kuvvet", Unit = "N")] public Vec3 Force { get; set; } = new Vec3(0, 0, 0.1);
        [Parameter("Tork", Unit = "N·m")] public Vec3 Torque { get; set; }
        [Parameter("Gövde ekseninde", Description = "Açıkken kuvvet nesneyle birlikte döner.")] public bool Local { get; set; }
        [Parameter("Uygulama noktası (gövde)", Unit = "m")] public Vec3 Point { get; set; }

        public override void OnStep(IBehaviorContext ctx, double dt)
        {
            var b = ctx.Self;
            if (b == null) return;
            if (Local)
            {
                b.ApplyLocalForce(Force, Point);
                b.ApplyTorque(b.Orientation.Rotate(Torque));
            }
            else
            {
                b.ApplyForceAtPoint(Force, b.LocalToWorld(Point));
                b.ApplyTorque(Torque);
            }
        }
    }

    [Behavior("Yay–sönümleyici", Description = "Nesneyi bir noktaya veya başka bir nesneye yay ve viskoz sönümle bağlar (F = −k·(L−L0) − c·v).")]
    public sealed class SpringDamper : WorldBehavior
    {
        [Parameter("Yay sabiti k", Unit = "N/m", Min = 0)] public double K { get; set; } = 20;
        [Parameter("Sönüm c", Unit = "N·s/m", Min = 0)] public double C { get; set; } = 0.5;
        [Parameter("Serbest boy L0", Unit = "m", Min = 0)] public double RestLength { get; set; }
        [Parameter("Diğer nesne", Description = "Boşsa sabit noktaya bağlanır.")] public string Other { get; set; } = string.Empty;
        [Parameter("Sabit nokta", Unit = "m")] public Vec3 Anchor { get; set; }
        [Parameter("Başlangıç konumunu sabit nokta al")] public bool AnchorAtStart { get; set; } = true;

        private ISimBody? _other;

        public override void OnStart(IBehaviorContext ctx)
        {
            if (AnchorAtStart && ctx.Self != null) Anchor = ctx.Self.Position;
            _other = string.IsNullOrWhiteSpace(Other) ? null : ctx.Body(Other);
            if (!string.IsNullOrWhiteSpace(Other) && _other == null) ctx.Warn($"'{Other}' adlı nesne bulunamadı; sabit noktaya bağlanıyor.");
        }

        public override void OnStep(IBehaviorContext ctx, double dt)
        {
            var b = ctx.Self;
            if (b == null) return;
            var p = _other?.Position ?? Anchor;
            var d = b.Position - p;
            double len = d.Length;
            var n = len > 1e-9 ? d / len : Vec3.Zero;
            var vrel = b.Velocity - (_other?.Velocity ?? Vec3.Zero);
            var f = n * (-K * (len - RestLength)) - n * (C * vrel.Dot(n));
            if (RestLength == 0) f = d * -K - vrel * C;
            b.ApplyForce(f);
            if (_other != null && !_other.IsStatic) _other.ApplyForce(-f);
            ctx.Publish("uzama", len - RestLength);
        }
    }

    [Behavior("Manyetik çekim", Description = "İki nesne arasında mıknatıs benzeri çekme/itme: F = F₁·(1 cm / r)ⁿ (dipol–dipol için n = 4), eşit ve zıt.")]
    public sealed class MagneticAttraction : WorldBehavior
    {
        [Parameter("Hedef nesne")] public string Target { get; set; } = string.Empty;
        [Parameter("1 cm'deki kuvvet", Unit = "N", Description = "Pozitif çeker, negatif iter.")] public double Strength { get; set; } = 2.0;
        [Parameter("Üs n", Min = 0, Max = 8)] public double Exponent { get; set; } = 4;
        [Parameter("En küçük mesafe", Unit = "m", Min = 1e-4)] public double MinDistance { get; set; } = 0.004;
        [Parameter("Etki mesafesi", Unit = "m", Min = 0)] public double Range { get; set; } = 0.3;

        private ISimBody? _target;

        public override void OnStart(IBehaviorContext ctx)
        {
            _target = ctx.Body(Target);
            if (_target == null) ctx.Warn($"Manyetik çekim: hedef '{Target}' bulunamadı.");
        }

        public override void OnStep(IBehaviorContext ctx, double dt)
        {
            var a = ctx.Self;
            var b = _target;
            if (a == null || b == null) return;
            var d = b.Position - a.Position;
            double r = d.Length;
            if (r > Range || r < 1e-9) return;
            double f = Strength * Math.Pow(0.01 / Math.Max(MinDistance, r), Exponent);
            var force = d / r * f;
            a.ApplyForce(force);
            if (!b.IsStatic) b.ApplyForce(-force);
            ctx.Publish("kuvvet", f);
        }
    }

    [Behavior("Rüzgâr türbini torku", Description = "Rüzgârdan rotor torku: T = ½ρA·R·C_T(λ)·v², C_P(λ) = C_Pmax·(λ/λ*)(2 − λ/λ*). Bir menteşeye (rotor miline) uygulanır.")]
    public sealed class WindTurbineTorque : WorldBehavior
    {
        [Parameter("Rotor çapı", Unit = "m", Min = 0.01)] public double Diameter { get; set; } = 0.3;
        [Parameter("En büyük güç katsayısı C_Pmax", Min = 0, Max = 0.593)] public double CpMax { get; set; } = 0.4;
        [Parameter("En iyi uç hız oranı λ*", Min = 0.5)] public double OptimalTipSpeedRatio { get; set; } = 6;
        [Parameter("Mafsal (boşsa nesneye bağlı ilk menteşe)")] public string Joint { get; set; } = string.Empty;
        [Parameter("Dönüş yönü (+1 / −1)")] public double Direction { get; set; } = 1;

        private ISimJoint? _joint;

        public override void OnStart(IBehaviorContext ctx)
        {
            _joint = !string.IsNullOrWhiteSpace(Joint) ? ctx.Joint(Joint)
                : ctx.Joints.FirstOrDefault(j => j.Kind == JointKind.Hinge && (j.Child == ctx.Self || j.Parent == ctx.Self));
            if (_joint == null) ctx.Warn("Rüzgâr türbini: menteşe bulunamadı; tork doğrudan nesneye uygulanacak.");
        }

        public override void OnStep(IBehaviorContext ctx, double dt)
        {
            var hub = _joint?.Child ?? ctx.Self;
            if (hub == null) return;
            var axis = _joint?.Axis ?? hub.Orientation.Rotate(Vec3.UnitZ);
            var wind = ctx.Air.WindAt(hub.Position) - hub.Velocity;
            double v = Math.Abs(wind.Dot(axis));
            double r = Diameter / 2, area = Math.PI * r * r;
            double omega = Math.Abs(_joint?.Speed ?? hub.AngularVelocity.Dot(axis));
            double lambda = v > 1e-3 ? omega * r / v : 0;
            double x = lambda / OptimalTipSpeedRatio;
            double ct = CpMax / OptimalTipSpeedRatio * Math.Max(0, 2 - x);   // C_T = C_P/λ
            double torque = 0.5 * ctx.Air.Density * area * r * ct * v * v * Math.Sign(Direction == 0 ? 1 : Direction);
            if (_joint != null) _joint.MotorTorque = torque;
            else hub.ApplyTorque(axis * torque);
            ctx.Publish("devir_dk", omega * 60 / (2 * Math.PI));
            ctx.Publish("güç_W", Math.Abs(torque) * omega);
            ctx.Publish("rüzgâr_m_s", v);
        }

        public override void OnStop(IBehaviorContext ctx)
        {
            if (_joint != null) _joint.MotorTorque = 0;
        }
    }

    [Behavior("Özel sürükleme yasası", Description = "Yerleşik sürüklemenin yerine F = −(b + c·|v|)·v (v = havaya göre hız). Doğrusal (Stokes) + karesel terim.")]
    public sealed class CustomDrag : WorldBehavior
    {
        [Parameter("Doğrusal katsayı b", Unit = "N·s/m", Min = 0)] public double Linear { get; set; } = 0.01;
        [Parameter("Karesel katsayı c", Unit = "N·s²/m²", Min = 0)] public double Quadratic { get; set; } = 0.002;
        [Parameter("Yerleşik sürüklemeyi kapat")] public bool ReplaceBuiltIn { get; set; } = true;

        public override void OnStart(IBehaviorContext ctx)
        {
            if (ReplaceBuiltIn && ctx.Self != null) ctx.Self.DragArea = Vec3.Zero;
        }

        public override void OnStep(IBehaviorContext ctx, double dt)
        {
            var b = ctx.Self;
            if (b == null) return;
            var rel = b.Velocity - ctx.Air.WindAt(b.Position);
            double k = Linear + Quadratic * rel.Length;
            // Never reverse the relative velocity within one step (explicit force, fixed step).
            k = Math.Min(k, b.Mass / Math.Max(1e-6, dt));
            b.ApplyForce(rel * -k);
        }
    }
}
