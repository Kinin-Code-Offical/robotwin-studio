using System;
using System.Linq;
using System.Windows;
using RobotTwin.CoreSim.Modeling;

namespace RobotWin.Studio.Modeling
{
    /// <summary>
    /// [proportional] Blender's proportional editing (O in edit mode): while selected points are moved, turned or scaled,
    /// the points within a radius of them follow by a share that fades with distance (CoreSim Modeling/MeshProportional.cs).
    /// The wheel changes the radius during the move; the circle of influence is drawn round the pivot.
    /// </summary>
    public sealed partial class ModelViewport
    {
        public bool Proportional { get; set; } = Shell.AppSettings.Current.ModelProportional;

        /// <summary>The radius of influence (mm).</summary>
        public double ProportionalRadius { get; set; } = Shell.AppSettings.Current.ModelProportionalRadius > 0 ? Shell.AppSettings.Current.ModelProportionalRadius : 20;

        public ProportionalFalloff ProportionalFalloff { get; set; } = ProportionalFalloff.Smooth;

        public void ToggleProportional()
        {
            Proportional = !Proportional;
            Shell.AppSettings.Current.ModelProportional = Proportional;
            Shell.AppSettings.Current.Save();
            _ed.Status(Proportional
                ? $"Orantılı düzenleme açık: seçimin {ProportionalRadius:0.##} mm çevresi de yumuşakça gelir (taşırken tekerlek yarıçapı değiştirir)"
                : "Orantılı düzenleme kapalı");
            RefreshOverlays();
        }

        /// <summary>The shares for this move, from the points as they were when it began (those dropped go back).</summary>
        private void RefreshSoft(Modal m)
        {
            var mesh = _ed.EditMesh;
            if (mesh == null) return;
            foreach (var kv in m.Soft) mesh.Vertices[kv.Key] = kv.Value.P;
            m.Soft.Clear();
            if (!Proportional || m.Verts.Count == 0) return;
            var world = mesh.Vertices.Select(v => m.EW.Apply(v)).ToList();
            foreach (var kv in MeshProportional.Weights(world, m.Verts.Keys.ToList(), ProportionalRadius, ProportionalFalloff))
                m.Soft[kv.Key] = (mesh.Vertices[kv.Key], kv.Value);
            Shell.AppSettings.Current.ModelProportionalRadius = ProportionalRadius;
        }

        /// <summary>The circle of influence on screen while a proportional move is on: its centre and radius (px).</summary>
        internal (Point Centre, double Radius)? ProportionalCircle
        {
            get
            {
                if (_modal is not { Kind: ModalKind.Move or ModalKind.Rotate or ModalKind.Scale } m || !Proportional || _ed.Mode != EditorMode.Edit) return null;
                var c = Cam.Project(m.Pivot);
                if (!c.Visible) return null;
                return (c.Screen, ProportionalRadius / Math.Max(1e-9, Cam.PixelSize(m.Pivot)));
            }
        }
    }
}
