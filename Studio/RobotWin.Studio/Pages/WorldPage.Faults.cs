using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [fault] A part's fault scenario in the inspector: shown (with its start time, its size and "Kaldır") only when the part
    /// has one; otherwise offered under "Özellik ekle ▾" (Pages/WorldPage.Features.cs).
    /// </summary>
    public partial class WorldPage
    {
        private void FaultSection(WorldObject o)
        {
            if (MachineFaults.Of(o) is not { } f)
            {
                OffCard(o, MachineFaults.Key, "Arıza senaryosu", "Arıza senaryosu kaldırıldı", () => MachineFaults.Clear(o));   // [components] switched off
                return;
            }

            Inspector.Children.Add(FeatureHeader("Arıza: " + f.Fault.Name, "Arıza senaryosu kaldırıldı", () => MachineFaults.Clear(o), toggle: MachineFaults.Key));
            Inspector.Children.Add(Note(f.Fault.Description));
            Inspector.Children.Add(NumberField("Başlama zamanı (s)", f.At, v =>
                Edit("Arıza zamanı değişti", () => o.Parameters[MachineFaults.AtKey] = v.ToString(CultureInfo.InvariantCulture)), 0, 1e6, "0.##"));
            if (f.Fault.DefaultValue != null)
                Inspector.Children.Add(NumberField("Büyüklük (" + f.Fault.Unit + ")", f.Value, v =>
                    Edit("Arıza büyüklüğü değişti", () => o.Parameters[MachineFaults.ValueKey] = v.ToString(CultureInfo.InvariantCulture)), 0, 1e6, "0.###"));
        }

        /// <summary>The fault scenarios a part can be given, as a submenu of "Özellik ekle ▾".</summary>
        private MenuItem? FaultMenu(WorldObject o)
        {
            if (WorldComponents.Has(o, MachineFaults.Key)) return null;   // [components] on or switched off
            var faults = MachineFaults.For(o.PartId).ToList();
            if (faults.Count == 0) return null;
            var menu = new MenuItem { Header = "Arıza senaryosu", ToolTip = "Parçanın belli bir anda nasıl bozulacağını seçin: PLC programının arızaya tepkisi denenir." };
            foreach (var f in faults)
            {
                var item = new MenuItem { Header = f.Name, ToolTip = f.Description };
                item.Click += (_, _) =>
                {
                    Edit("Arıza senaryosu eklendi", () => MachineFaults.Set(o, f, 5));
                    BuildInspector();
                };
                menu.Items.Add(item);
            }

            return menu;
        }
    }
}
