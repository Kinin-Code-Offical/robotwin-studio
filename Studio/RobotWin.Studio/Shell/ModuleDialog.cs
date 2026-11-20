using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;

namespace RobotWin.Studio.Shell
{
    /// <summary>Asks for the module name and which nets become its pins.</summary>
    public sealed class ModuleDialog : Window
    {
        private readonly TextBox _name;
        private readonly List<(CheckBox Box, string Net)> _nets = new List<(CheckBox, string)>();

        public ModuleDialog(Schematic design, PartLibrary library)
        {
            Title = "Modül olarak kaydet";
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = (Brush)Application.Current.FindResource("B.Surface");
            Foreground = (Brush)Application.Current.FindResource("B.Text");

            var root = new StackPanel { Margin = new Thickness(20) };
            root.Children.Add(new TextBlock { Text = "Modül adı", Style = (Style)Application.Current.FindResource("Text.Caption"), Margin = new Thickness(0, 0, 0, 4) });
            _name = new TextBox { Text = string.IsNullOrWhiteSpace(design.Title) ? "Modülüm" : design.Title };
            root.Children.Add(_name);
            root.Children.Add(new TextBlock
            {
                Text = "Pin olacak hatlar (karttaki konnektörlere giden hatlar ve GND önceden seçili). İç devre, başka bir projeye eklendiğinde tümüyle simüle edilir.",
                Style = (Style)Application.Current.FindResource("Text.Caption"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 14, 0, 6)
            });

            var defaults = new HashSet<string>(ModuleBuilder.Create(design, library).Ports.Select(p => p.Net), StringComparer.OrdinalIgnoreCase);
            var list = new StackPanel();
            foreach (var net in design.BuildNetlist(library).Nets.OrderBy(n => n.IsAutoNamed).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
            {
                var cb = new CheckBox
                {
                    Content = net.Name + (net.IsAutoNamed ? "  (adsız hat)" : string.Empty),
                    IsChecked = defaults.Contains(net.Name),
                    Margin = new Thickness(0, 2, 0, 2),
                    Foreground = Foreground
                };
                _nets.Add((cb, net.Name));
                list.Children.Add(cb);
            }

            root.Children.Add(new ScrollViewer { Content = list, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Button { Content = "Vazgeç", Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
            var ok = new Button { Content = "Kütüphaneye kaydet", Style = (Style)Application.Current.FindResource("Btn.Primary"), IsDefault = true, Padding = new Thickness(14, 6, 14, 6) };
            ok.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(_name.Text) || !_nets.Any(n => n.Box.IsChecked == true)) return;
                DialogResult = true;
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            root.Children.Add(buttons);
            Content = root;
        }

        public string ModuleName => _name.Text.Trim();

        public IReadOnlyList<string> SelectedPorts => _nets.Where(n => n.Box.IsChecked == true).Select(n => n.Net).ToList();
    }
}
