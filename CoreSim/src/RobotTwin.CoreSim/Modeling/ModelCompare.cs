using System;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Modeling
{
    /// <summary>[compare] Two bodies side by side: what each is, what they share and what only one of them has.</summary>
    public sealed class BodyComparison
    {
        public double VolumeA { get; set; }
        public double VolumeB { get; set; }
        public double AreaA { get; set; }
        public double AreaB { get; set; }
        public Vec3 SizeA { get; set; }
        public Vec3 SizeB { get; set; }

        /// <summary>The volume both take up (mm³).</summary>
        public double Common { get; set; }

        /// <summary>The volume only A takes up, and only B (mm³).</summary>
        public double OnlyA { get; set; }

        public double OnlyB { get; set; }

        /// <summary>The share of all the volume they take up that only one of them does (0: the same body).</summary>
        public double Difference => VolumeA + VolumeB - Common <= 0 ? 0 : (OnlyA + OnlyB) / (VolumeA + VolumeB - Common);

        /// <summary>Whether they are the same body where they stand, to <paramref name="tolerance"/> of their volume.</summary>
        public bool Same(double tolerance = 1e-4) => Difference <= tolerance;
    }

    /// <summary>
    /// [compare] SolidWorks' Compare Bodies: two bodies where they stand (in one frame), their volumes, surface areas and
    /// sizes, the volume they share (their intersection) and what each has that the other does not (the differences),
    /// so a revised part can be checked against the old one.
    /// </summary>
    public static class ModelCompare
    {
        public static BodyComparison Compare(PolyMesh a, PolyMesh b)
        {
            double va = Math.Abs(a.Volume()), vb = Math.Abs(b.Volume());
            var common = Csg.Apply(a, b, BooleanOp.Intersection);
            var onlyA = Csg.Apply(a, b, BooleanOp.Difference);
            var onlyB = Csg.Apply(b, a, BooleanOp.Difference);
            return new BodyComparison
            {
                VolumeA = va,
                VolumeB = vb,
                AreaA = a.SurfaceArea(),
                AreaB = b.SurfaceArea(),
                SizeA = a.IsEmpty ? Vec3.Zero : a.Bounds.Size,
                SizeB = b.IsEmpty ? Vec3.Zero : b.Bounds.Size,
                Common = common.IsEmpty ? 0 : Math.Abs(common.Volume()),
                OnlyA = onlyA.IsEmpty ? 0 : Math.Abs(onlyA.Volume()),
                OnlyB = onlyB.IsEmpty ? 0 : Math.Abs(onlyB.Volume())
            };
        }

        /// <summary>Two objects of a model compared where they stand in it.</summary>
        public static BodyComparison Compare(ModelDocument doc, ModelObject a, ModelObject b) => Compare(doc.WorldMesh(a), doc.WorldMesh(b));
    }
}
