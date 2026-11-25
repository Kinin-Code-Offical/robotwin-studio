using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// The inspector shows what an object has, not everything it could have: a pressure vessel, a parachute, a shell,
    /// its own air settings and behaviours appear when they are on (with "Kaldır"), the others are one click away
    /// under "Özellik ekle ▾".
    /// </summary>
    public partial class WorldPage
    {
        /// <summary>Section title of a feature the object has, with a "Kaldır" link that takes it away.</summary>
        private FrameworkElement FeatureHeader(string title, string undo, Action remove, bool sdk = false)
        {
            var row = new DockPanel { Margin = new Thickness(0, 12, 0, 6), LastChildFill = true };
            var drop = new Button
            {
                Content = "Kaldır",
                Style = (Style)FindResource("Btn.Ghost"),
                Padding = new Thickness(8, 0, 8, 0),
                MinHeight = 0,
                Height = 22,
                FontSize = 12,
                ToolTip = title + " bu nesneden kaldırılır (Ctrl+Z ile geri alınır).",
                VerticalAlignment = VerticalAlignment.Center
            };
            drop.Click += (_, _) =>
            {
                if (sdk) SdkEdit(undo, remove);
                else Edit(undo, remove);
                BuildInspector();
            };
            DockPanel.SetDock(drop, Dock.Right);
            row.Children.Add(drop);
            row.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("Text.Overline"), VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        /// <summary>"Özellik ekle ▾": the features this object can be given and does not have yet.</summary>
        private void FeaturesSection(WorldDocument world, WorldObject o)
        {
            var menu = new ContextMenu();
            void Item(string header, string tip, Action add, bool sdk = false)
            {
                var mi = new MenuItem { Header = header, ToolTip = tip };
                mi.Click += (_, _) =>
                {
                    if (sdk) SdkEdit(header + " eklendi", add);
                    else Edit(header + " eklendi", add);
                    BuildInspector();
                };
                menu.Items.Add(mi);
            }

            bool body = o.Kind == WorldObjectKind.Body;
            if (body && o.Shape == "box" && !WorldPhysics.IsShell(o) && WorldPhysics.LidOf(o) == null && !WorldPhysics.IsVessel(o))
                Item("Kabuk (içi boş gövde)", "Kutu 2 mm duvarlı, üstü açık bir kabuğa döner: içine kart, pil ve servo konur, duvarlar onlarla çarpışır, kablolar deliklerden geçer.", () =>
                {
                    o.Parameters["shell"] = "true";
                    o.Parameters["wall"] = "0.002";
                });
            if (body && o.Shape is "cylinder" or "box" or "sphere" && !WorldPhysics.IsVessel(o) && !WorldPhysics.IsShell(o))
                Item("Basınçlı kap (gaz fiziği)", "İçinde gaz tutan kapalı kap: basınç, lüle, vana, su ya da katı yakıt; su roketi, katı yakıtlı motor.", () => o.Parameters["vessel"] = "true");
            if (body && !WorldPhysics.IsWing(o) && !o.Parameters.ContainsKey("parachute"))
                Item("Kurtarma paraşütü", "Tepe noktasında (ya da belli bir zamanda) atılan paraşüt; 45 cm kanopiyle başlar.", () => o.Parameters["parachute"] = "0.45");
            if (world.Sdk.AeroOf(o.Id) == null)
                Item("Hava (özel ayar)", "Sürüklemeyi kapatın, Cd ve referans alanı elle verin ya da kaldırma kuvveti (balon, köpük) ekleyin. Ayarlanmadıkça sürükleme gövdenin şeklinden hesaplanır.",
                    () => world.Sdk.AeroFor(o.Id), sdk: true);
            if (FaultMenu(o) is { } faults) menu.Items.Add(faults);   // [fault] Pages/WorldPage.Faults.cs
            if (CouplingMenu(world, o) is { } couple) menu.Items.Add(couple);   // [couple] Pages/WorldPage.Couplings.cs
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            var behaviours = new MenuItem { Header = "Davranış (ifade, hazır davranış, betik)…", ToolTip = "Bu nesneye bir ifade (F.x = …), hazır davranış, eklenti ya da betik ekleyin." };
            menu.Items.Add(behaviours);

            Inspector.Children.Add(Caption("Özellikler"));
            var add = Action("Özellik ekle ▾", () => { }, "Bu nesneye ek fizik ekleyin: kabuk, basınçlı kap, paraşüt, hava ayarı, davranış.");
            behaviours.Click += (_, _) => BehaviorMenu(world, o.Id, add);
            add.Click += (_, _) =>
            {
                menu.PlacementTarget = add;
                menu.IsOpen = true;
            };
            Inspector.Children.Add(add);
        }
    }
}
