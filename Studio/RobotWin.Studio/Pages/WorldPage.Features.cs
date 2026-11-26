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
        /// <summary>[components] How a component comes off again (Unity's Remove Component).</summary>
        private sealed class ComponentRemoval
        {
            public ComponentRemoval(string undo, Action remove, bool sdk)
            {
                Undo = undo;
                Remove = remove;
                Sdk = sdk;
            }

            public string Undo { get; }
            public Action Remove { get; }
            public bool Sdk { get; }
        }

        /// <summary>[components] Marks a joint's own feature header (spring, bearing, coupling) by its source title.</summary>
        private sealed class SubFeature
        {
            public SubFeature(string title) => Title = title;
            public string Title { get; }
        }

        /// <summary>
        /// [components] The title of a component the object has: the inspector makes it a card of its own (Pages/WorldPage.Components.cs)
        /// with "Bileşeni kaldır" in its header and ⋮ menu, as Unity's component headers.
        /// </summary>
        private TextBlock FeatureHeader(string title, string undo, Action remove, bool sdk = false)
        {
            var caption = Caption(title);
            caption.Tag = new ComponentRemoval(undo, remove, sdk);
            return caption;
        }

        private void RemoveComponent(ComponentRemoval r)
        {
            if (r.Sdk) SdkEdit(r.Undo, r.Remove);
            else Edit(r.Undo, r.Remove);
            BuildInspector();
        }

        /// <summary>A feature of one joint inside "Mekanik bağlantılar" (spring, bearing, coupling): its title with a "Kaldır" link.</summary>
        private FrameworkElement SubFeatureHeader(string title, string undo, Action remove, bool sdk = false)
        {
            var row = new DockPanel { Margin = new Thickness(0, 12, 0, 6), LastChildFill = true, Tag = new SubFeature(title), Background = Brushes.Transparent };
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
            if (MovingJoints(world, o).FirstOrDefault(j => !j.Sprung) is { } bare)   // [spring] Pages/WorldPage.Couplings.cs
                Item("Yay ve sönüm", "Eklemin kendisine yay ekleyin: menteşede burulma yayı, kızakta helezon yay; isterseniz amortisör gibi sönüm. Motor olsa da olmasa da eklemi serbest konumuna çeker.",
                    () => AddSpring(bare));
            if (MovingJoints(world, o).FirstOrDefault(j => j.Bearing == null) is { } loose)   // [bearing] Pages/WorldPage.Couplings.cs
                Item("Rulman / yatak", "Eklemin döndüğü ya da kaydığı yatağı seçin: bilyalı rulman, burç, doğrusal rulman, kayar yatak. Sürtünmesi taşıdığı yükle artar.",
                    () => loose.Bearing = loose.Kind == WorldJointKind.Hinge ? "ball" : "linear-ball");
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            var behaviours = new MenuItem { Header = "Davranış (ifade, hazır davranış, betik)…", ToolTip = "Bu nesneye bir ifade (F.x = …), hazır davranış, eklenti ya da betik ekleyin." };
            menu.Items.Add(behaviours);

            // [components] Unity's Add Component: a searchable list by category over the same features
            var add = Action("Bileşen ekle", () => { }, "Bu nesneye bileşen ekleyin: kabuk, basınçlı kap, paraşüt, hava ayarı, arıza, eşleme, yay, rulman, davranış. Aramak için yazın.");
            add.HorizontalAlignment = HorizontalAlignment.Stretch;
            add.HorizontalContentAlignment = HorizontalAlignment.Center;
            add.Margin = new Thickness(0, 10, 0, 6);
            behaviours.Click += (_, _) => BehaviorMenu(world, o.Id, add);
            add.Click += (_, _) => ShowComponentPicker(add, menu);
            Inspector.Children.Add(add);
        }
    }
}
