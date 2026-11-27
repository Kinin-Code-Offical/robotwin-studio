using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Modeling;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [compare] SolidWorks' Compare Bodies for the two selected objects, where they stand: each one's volume, surface
    /// area and size, the volume they share and what only one of them has (CoreSim Modeling/ModelCompare.cs).
    /// </summary>
    public partial class ModelPage
    {
        private void CompareBodies()
        {
            var two = _ed.SelectedObjects.Where(o => !o.IsReference).ToList();
            if (two.Count != 2)
            {
                _ed.Status("Karşılaştırmak için iki gövde seçin");
                return;
            }

            var (a, b) = (two[0], two[1]);
            var c = ModelCompare.Compare(_ed.Doc, a, b);
            string F(double v) => v.ToString("0.##", CultureInfo.CurrentCulture);
            string Size(RobotTwin.CoreSim.Mechanics.Vec3 s) => $"{F(s.X)} × {F(s.Y)} × {F(s.Z)} mm";
            string verdict = c.Same()
                ? Shell.Lang.T("Aynı gövde (durdukları yerde).")
                : string.Format(Shell.Lang.T("Toplam hacmin %{0}'i yalnız birinde."), F(c.Difference * 100));
            string text =
                $"{a.Name}: {F(c.VolumeA)} mm³ · {F(c.AreaA)} mm² · {Size(c.SizeA)}\n" +
                $"{b.Name}: {F(c.VolumeB)} mm³ · {F(c.AreaB)} mm² · {Size(c.SizeB)}\n\n" +
                $"{Shell.Lang.T("Ortak hacim")}: {F(c.Common)} mm³\n" +
                $"{Shell.Lang.T("Yalnız")} {a.Name}: {F(c.OnlyA)} mm³\n" +
                $"{Shell.Lang.T("Yalnız")} {b.Name}: {F(c.OnlyB)} mm³\n\n" + verdict;
            Shell.Dialog.Show(Owner!, Shell.Lang.T("Gövdeleri karşılaştır"), text, Shell.Lang.T("Tamam"));
            _ed.Status(verdict);
        }
    }
}
