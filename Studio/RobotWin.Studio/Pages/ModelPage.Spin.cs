using System;
using RobotTwin.CoreSim.Mechanics;
using RobotWin.Studio.Modeling;

namespace RobotWin.Studio.Pages
{
    /// <summary>[spin] Blender's Spin in edit mode: the steps, the angle and the centre asked, then swept (Modeling/ModelEditor.Spin).</summary>
    public partial class ModelPage
    {
        private static readonly (string Name, Vec3 Axis)[] SpinAxes = { ("Z", Vec3.UnitZ), ("X", Vec3.UnitX), ("Y", Vec3.UnitY) };

        private void SpinDialog(string axisName, Vec3 axis)
        {
            var r = ParamDialog.Show(Owner, "Döndürerek çoğalt (" + axisName + " ekseni)",
                "Seçili yüzler ya da kenarlar nesnenin " + axisName + " ekseni çevresinde, merkez noktasından geçen eksende adım adım döndürülerek çoğaltılır. 360° tam tur kendine kapanır; eksen üstündeki noktalar tek kalır.",
                ("Adım sayısı", 12), ("Açı (°)", 360), ("Merkez X (mm)", 0), ("Merkez Y (mm)", 0), ("Merkez Z (mm)", 0));
            if (r == null) return;
            _ed.Spin(axis, new Vec3(r[2], r[3], r[4]), r[1], Math.Max(1, Math.Min(512, (int)Math.Round(r[0]))));
            _vp.RefreshEditMesh();
        }
    }
}
