using System.Windows;
using RobotWin.Studio.World;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [gizmo] The handle buttons of the Montaj and Dünya toolbar (Unity's W / E / R / Y, Q for none) and their two
    /// settings: where the handles sit (Pivot / Merkez) and along which axes (Yerel / Dünya).
    /// </summary>
    public partial class WorldPage
    {
        private void InitGizmo()
        {
            Scene.GizmoChanged += SyncGizmo;
            SyncGizmo();
        }

        private void OnGizmo(object sender, RoutedEventArgs e)
        {
            var mode = ((FrameworkElement)sender).Tag switch
            {
                "Rotate" => GizmoMode.Rotate,
                "Scale" => GizmoMode.Scale,
                "All" => GizmoMode.All,
                "Rect" => GizmoMode.Rect,
                _ => GizmoMode.Move
            };
            // a second click on the one in use puts the handles away (Unity's Q)
            PickGizmo(Scene.Gizmo == mode ? GizmoMode.None : mode);
        }

        private void PickGizmo(GizmoMode mode)
        {
            if (Scene.Tool != WorldTool.Select) SetTool(WorldTool.Select);
            Scene.SetGizmo(mode);
            W.Status = mode switch
            {
                GizmoMode.Move => "Taşı tutamacı (W): okla bir eksende, kareyle bir düzlemde, ortasıyla ekran düzleminde",
                GizmoMode.Rotate => "Döndür tutamacı (E): halkayla bir eksen çevresinde, dış halkayla görüş ekseni çevresinde, içiyle serbest",
                GizmoMode.Scale => "Ölçekle tutamacı (R): küple bir eksende, ortasıyla üç eksende",
                GizmoMode.All => "Hepsi bir arada (Y): taşı, döndür, ölçekle",
                GizmoMode.Rect => "Dikdörtgen (T): kenar ve köşeden boyutlandır (karşı kenar yerinde kalır, köşede Shift oranı korur), içinden taşı, köşenin dışından döndür",
                _ => "Tutamaç yok (Q): yalnız seçim"
            };
        }

        private void SyncGizmo()
        {
            GizmoMove.IsChecked = Scene.Gizmo == GizmoMode.Move;
            GizmoRotate.IsChecked = Scene.Gizmo == GizmoMode.Rotate;
            GizmoScale.IsChecked = Scene.Gizmo == GizmoMode.Scale;
            GizmoAll.IsChecked = Scene.Gizmo == GizmoMode.All;
            GizmoRect.IsChecked = Scene.Gizmo == GizmoMode.Rect;   // [rect]
            // [toolbar] icons, not words: the pivot point or the shape's centre; the part's own axes or the world's
            GizmoPlace.Content = new Theme.Icon { Data = (System.Windows.Media.Geometry)FindResource(Scene.GizmoCenter ? "I.PivotCenter" : "I.PivotPoint"), Size = 16 };
            GizmoPlace.ToolTip = RobotWin.Studio.Shell.Lang.T(Scene.GizmoCenter ? "Tutamaç şeklin ortasında (Merkez); tıklayınca pivotta" : "Tutamaç parçanın pivotunda; tıklayınca şeklin ortasında (Merkez)");
            GizmoPlace.IsChecked = Scene.GizmoCenter;
            GizmoSpace.Content = new Theme.Icon { Data = (System.Windows.Media.Geometry)FindResource(Scene.GizmoLocal ? "I.SpaceLocal" : "I.SpaceWorld"), Size = 16 };
            GizmoSpace.ToolTip = RobotWin.Studio.Shell.Lang.T(Scene.GizmoLocal ? "Eksenler parçanın kendi eksenleri (Yerel); tıklayınca dünyanınki" : "Eksenler dünyanınki; tıklayınca parçanın kendi eksenleri (Yerel)");
            GizmoSpace.IsChecked = !Scene.GizmoLocal;
        }

        private void OnGizmoPlace(object sender, RoutedEventArgs e)
        {
            Scene.GizmoCenter = !Scene.GizmoCenter;
            Scene.SetGizmo(Scene.Gizmo);
            W.Status = Scene.GizmoCenter ? "Tutamaç parçanın şeklinin ortasında" : "Tutamaç parçanın pivotunda";
        }

        private void OnGizmoSpace(object sender, RoutedEventArgs e)
        {
            Scene.GizmoLocal = !Scene.GizmoLocal;
            Scene.SetGizmo(Scene.Gizmo);
            W.Status = Scene.GizmoLocal ? "Tutamaç eksenleri parçanın kendi eksenleri (ölçek her zaman yereldir)" : "Tutamaç eksenleri dünyanın eksenleri";
        }
    }
}
