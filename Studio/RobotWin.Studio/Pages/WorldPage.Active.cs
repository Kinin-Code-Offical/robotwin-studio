using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [active] Unity's active GameObject switch for Montaj and Dünya: the checkbox before the name in the inspector,
    /// Alt+Shift+A and the hierarchy menu. A switched-off object and what is mounted on it leave the view and the run
    /// (WorldDocument.WithoutInactive: physics, circuit and sensors alike); the file keeps them, the tree shows them grey.
    /// </summary>
    public partial class WorldPage
    {
        private FrameworkElement ActiveHeader(WorldObject o, TextBox name)
        {
            var on = new CheckBox
            {
                IsChecked = WorldBuilding.IsActive(o),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                ToolTip = "Etkin (Alt+Shift+A): kapalıyken nesne ve üstüne takılanlar sahnede görünmez ve çalıştırmada yok sayılır (fizik, devre, sensörler); dosyada kalır"
            };
            on.Click += (_, _) => ToggleActive(o);
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(on);
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
            return grid;
        }

        /// <summary>Switches an object on or off (Unity's SetActive), as one undo step.</summary>
        private void ToggleActive(WorldObject? o)
        {
            if (o == null || World == null) return;
            bool turnOn = !WorldBuilding.IsActive(o);
            if (_session.State != SimState.Stopped) return;   // the run was built without or with it: switch when stopped
            Edit(turnOn ? $"{o.Name} etkinleştirildi" : $"{o.Name} devre dışı bırakıldı", () => WorldBuilding.SetActive(o, turnOn));
        }
    }
}
