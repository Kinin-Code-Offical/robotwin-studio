using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [cforce] Unity's Constant Force card: a push (N) and a twist (N·m) on every physics step, each along the world's
    /// axes or the part's own (Unity's Relative Force / Relative Torque). The physics is in
    /// CoreSim Sandbox/WorldPhysics.ConstantForce.cs.
    /// </summary>
    public partial class WorldPage
    {
        private void ConstantForceSection(WorldObject o)
        {
            const string key = WorldPhysics.ConstantForceKey;
            if (!WorldComponents.Has(o, key)) return;   // else under "Bileşen ekle"
            void Remove()
            {
                foreach (var k in WorldComponents.KeysOf(key)) o.Parameters.Remove(k);
            }

            if (OffCard(o, key, "Sabit kuvvet", "Sabit kuvvet kaldırıldı", Remove)) return;
            Inspector.Children.Add(FeatureHeader("Sabit kuvvet", "Sabit kuvvet kaldırıldı", Remove, toggle: key));
            VectorParam(o, key, "Kuvvet (N) X · Y · Z", "Kuvvet değişti");
            Inspector.Children.Add(LocalAxes(o, WorldPhysics.ConstantForceLocalKey, "Kuvvet parçanın kendi eksenlerinde", "İşaretliyse kuvvet parçanın eksenlerindedir ve parça dönünce onunla döner (Unity'deki Relative Force)."));
            VectorParam(o, WorldPhysics.ConstantTorqueKey, "Tork (N·m) X · Y · Z", "Tork değişti");
            Inspector.Children.Add(LocalAxes(o, WorldPhysics.ConstantTorqueLocalKey, "Tork parçanın kendi eksenlerinde", "İşaretliyse tork parçanın eksenlerindedir (Unity'deki Relative Torque)."));
            Inspector.Children.Add(Note("Her fizik adımında kütle merkezine etkir: F = m·a ile hızlanır, yerçekimi ve sürükleme de işler. Z yukarıdır; kendi eksenlerinde bir roketin itkisi gibi burnuyla döner."));
        }

        private void VectorParam(WorldObject o, string key, string label, string undo)
        {
            WorldPhysics.TryVector(o.Parameters.TryGetValue(key, out var s) ? s : null, out var v);
            Inspector.Children.Add(Vector3Row(label, v.X, v.Y, v.Z, (x, y, z) => Edit(undo, () =>
            {
                var n = new Vec3(x, y, z);
                if (key == WorldPhysics.ConstantForceKey || n.Length > 0) o.Parameters[key] = WorldPhysics.VectorText(n);   // the push is also the switch
                else o.Parameters.Remove(key);
            }), "0.####"));
        }

        private CheckBox LocalAxes(WorldObject o, string key, string text, string tip)
        {
            var box = new CheckBox { Content = text, IsChecked = o.Parameters.ContainsKey(key), Margin = new Thickness(0, 0, 0, 8), ToolTip = tip };
            box.Click += (_, _) => Edit(text, () =>
            {
                if (box.IsChecked == true) o.Parameters[key] = "1";
                else o.Parameters.Remove(key);
            });
            return box;
        }
    }
}
