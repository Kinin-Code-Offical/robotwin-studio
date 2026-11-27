using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Modeling;

namespace RobotWin.Studio.Modeling
{
    /// <summary>
    /// [refgeom] SolidWorks' reference geometry in the modeller: a plane, an axis or a point made from what is selected
    /// (CoreSim Modeling/ModelReferences.cs), and the mirror and circular pattern that take them.
    /// </summary>
    public sealed partial class ModelEditor
    {
        /// <summary>The selected references of a kind.</summary>
        public List<ModelObject> SelectedReferences(ReferenceKind kind) =>
            SelectedObjects.Where(o => o.Reference?.Kind == kind).ToList();

        /// <summary>Points picked in edit mode, in the model's frame.</summary>
        private List<Vec3> PickedPoints()
        {
            if (Mode != EditorMode.Edit || EditMesh == null || Active == null) return new List<Vec3>();
            var m = Doc.WorldMatrix(Active);
            return SelVerts.Where(v => v >= 0 && v < EditMesh.Vertices.Count).Take(3).Select(v => m.Apply(EditMesh.Vertices[v])).ToList();
        }

        /// <summary>
        /// A reference made the way the selection asks: a plane through three picked points, offset from a plane, half way
        /// between two, square to an axis; an axis through two points, along a round part, where two planes meet; a point at
        /// a picked point or the middle of the parts. Without such a selection it is laid at <paramref name="fallback"/>
        /// (a plane level with the floor, an upright axis).
        /// </summary>
        public void AddReference(ReferenceKind kind, Vec3 fallback, double offset = 20)
        {
            var pts = PickedPoints();
            var planes = SelectedReferences(ReferenceKind.Plane);
            var axes = SelectedReferences(ReferenceKind.Axis);
            var points = SelectedReferences(ReferenceKind.Point).Select(o => ModelReferences.Frame(Doc, o).Origin).ToList();
            var parts = SelectedObjects.Where(o => !o.IsReference).ToList();
            pts.AddRange(points);
            ModelObject? made = null;
            string how = "";
            if (Mode == EditorMode.Edit) ToggleEditMode();   // a reference is an object of its own
            Do(ModelReferences.NextName(Doc, kind) + " eklendi", () =>
            {
                switch (kind)
                {
                    case ReferenceKind.Plane:
                        if (pts.Count >= 3 && (made = ModelReferences.PlaneThrough(Doc, pts[0], pts[1], pts[2])) != null) how = "üç noktadan";
                        else if (planes.Count == 2 && (made = ModelReferences.MidPlane(Doc, planes[0], planes[1])) != null) how = "iki düzlemin ortasında";
                        else if (planes.Count == 1) (made, how) = (ModelReferences.PlaneOffset(Doc, planes[0], offset), $"{planes[0].Name} düzleminden {offset:0.##} mm");
                        else if (axes.Count == 1) (made, how) = (ModelReferences.PlaneNormalTo(Doc, axes[0], pts.Count > 0 ? pts[0] : null), $"{axes[0].Name} eksenine dik");
                        break;
                    case ReferenceKind.Axis:
                        if (pts.Count >= 2 && (made = ModelReferences.AxisThrough(Doc, pts[0], pts[1])) != null) how = "iki noktadan";
                        else if (planes.Count == 2 && (made = ModelReferences.AxisWhere(Doc, planes[0], planes[1])) != null) how = "iki düzlemin kesiştiği yerde";
                        else if (parts.Count == 1 && (made = ModelReferences.AxisOf(Doc, parts[0])) != null) how = $"{parts[0].Name} ekseninde";
                        break;
                    default:
                        if (pts.Count >= 1) (made, how) = (ModelReferences.PointAt(Doc, pts[0]), "seçili noktada");
                        else if (parts.Count > 0 && (made = ModelReferences.PointAtCenter(Doc, parts)) != null) how = "seçilenlerin ortasında";
                        break;
                }

                if (made == null)
                {
                    made = kind == ReferenceKind.Point ? ModelReferences.PointAt(Doc, fallback) : ModelReferences.Add(Doc, kind, fallback, Vec3.UnitZ);
                    how = kind == ReferenceKind.Plane ? "zemine paralel" : kind == ReferenceKind.Axis ? "dik" : "görünümün ortasında";
                }

                Selected.Clear();
                Selected.Add(made.Id);
                ActiveId = made.Id;
            }, ModelChange.All);
            if (made != null) Status($"{made.Name}: {how} · G ile taşıyın, R ile döndürün");
        }

        /// <summary>Mirrored copies of the selected parts across the selected reference plane.</summary>
        public void MirrorAcross(ModelObject plane)
        {
            var objs = SelectedRoots().Where(o => !o.IsReference).ToList();
            if (objs.Count == 0) return;
            Do($"{plane.Name} düzlemine göre ayna kopyası", () =>
            {
                var copies = ModelReferences.MirrorCopy(Doc, objs, plane);
                Selected.Clear();
                Selected.AddRange(copies.Select(c => c.Id));
                ActiveId = copies.Last().Id;
            }, ModelChange.All);
        }

        /// <summary>A circular pattern of the selected parts about the selected reference axis.</summary>
        public void ArrayCircularAbout(int count, double degrees, ModelObject axis)
        {
            var objs = SelectedRoots().Where(o => !o.IsReference).ToList();
            if (objs.Count == 0 || count < 2) return;
            Do($"Dairesel dizi ({axis.Name} çevresinde): {count} adet", () =>
            {
                var copies = ModelReferences.ArrayCircular(Doc, objs, count, degrees, axis);
                Selected.AddRange(copies.Select(c => c.Id));
            }, ModelChange.All);
        }
    }
}
