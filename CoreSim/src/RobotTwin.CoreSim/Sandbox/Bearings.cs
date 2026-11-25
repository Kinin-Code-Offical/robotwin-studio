using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [bearing] What a hinge turns in or a slider runs on, and its friction: Coulomb, proportional to the load the joint
    /// carries (Mechanics/PhysicsScene.cs, SceneJoint.LoadFriction). A hinge's bearing resists with μ × load × bore / 2
    /// (SKF's simplified friction moment), a slider's with μ × the load across it.
    /// </summary>
    public sealed class BearingSpec
    {
        public string Id { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        /// <summary>Friction coefficient referred to the bore (a hinge) or the rail (a slider).</summary>
        public double Mu { get; init; }

        /// <summary>For a hinge (a radial bearing, a bushing) or a slider (a linear bearing, a slide).</summary>
        public bool Linear { get; init; }

        /// <summary>The usual bore (mm) of a hinge's bearing: a 608's 8 mm.</summary>
        public double Bore { get; init; } = 8;

        public string Note { get; init; } = string.Empty;
    }

    public static class Bearings
    {
        public const string Key = "bearing";

        public static readonly IReadOnlyList<BearingSpec> All = new[]
        {
            new BearingSpec { Id = "ball", Name = "Bilyalı rulman (608)", Mu = 0.0015, Bore = 8, Note = "Sabit bilyalı rulman, yağlı: kaykay ve 3B yazıcı makarası." },
            new BearingSpec { Id = "needle", Name = "İğneli rulman", Mu = 0.0025, Bore = 8, Note = "Dar yerde yüksek radyal yük." },
            new BearingSpec { Id = "bronze", Name = "Bronz burç (yağlı)", Mu = 0.08, Bore = 8, Note = "Sinterlenmiş, yağ emdirilmiş bronz: küçük motorların, dişli kutularının yatağı." },
            new BearingSpec { Id = "plastic", Name = "Plastik burç (POM / PTFE)", Mu = 0.15, Bore = 8, Note = "Kuru çalışan plastik yatak." },
            new BearingSpec { Id = "pin", Name = "Pim, baskı parçasında (kuru)", Mu = 0.3, Bore = 3, Note = "Çelik pim ya da vida, PLA deliğinde: menteşeli baskı parçaları." },
            new BearingSpec { Id = "linear-ball", Name = "Doğrusal rulman (LM8UU)", Mu = 0.003, Linear = true, Note = "Bilyalı doğrusal yatak, 8 mm mil üzerinde." },
            new BearingSpec { Id = "rail", Name = "Bilyalı ray (MGN)", Mu = 0.004, Linear = true, Note = "Profilli doğrusal kılavuz: CNC ve yazıcı eksenleri." },
            new BearingSpec { Id = "wheel", Name = "Tekerlekli taşıyıcı (V-slot)", Mu = 0.005, Linear = true, Note = "Alüminyum profilde POM tekerlekler." },
            new BearingSpec { Id = "slide", Name = "Kayar yatak (plastik, kuru)", Mu = 0.15, Linear = true, Note = "Plastik burç ya da kızak, yağsız." }
        };

        public static BearingSpec? Find(string? id) => All.FirstOrDefault(b => b.Id == id);

        /// <summary>The bearings that fit a hinge or a slider.</summary>
        public static IEnumerable<BearingSpec> For(WorldJointKind kind) =>
            kind == WorldJointKind.Fixed ? Enumerable.Empty<BearingSpec>() : All.Where(b => b.Linear == (kind == WorldJointKind.Slider));

        /// <summary>
        /// The friction a joint gets per newton of load: μ·bore/2 (m) for a hinge, μ for a slider; 0 without a bearing.
        /// </summary>
        public static double LoadFriction(WorldJoint j)
        {
            if (Find(j.Bearing) is not { } b || j.Kind == WorldJointKind.Fixed || b.Linear != (j.Kind == WorldJointKind.Slider)) return 0;
            if (b.Linear) return b.Mu;
            double bore = j.BearingBore > 0 ? j.BearingBore : b.Bore;
            return b.Mu * bore / 2000;
        }
    }
}
