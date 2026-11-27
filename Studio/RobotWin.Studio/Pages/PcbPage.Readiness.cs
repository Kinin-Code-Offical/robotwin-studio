using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [fab-ready] The manufacturing readiness screen (D3): every check before the files go to the fab in one window, each
    /// with its outcome (done, a warning, a problem, or not checked here, said as such), run on a copy of the board in the
    /// background; "Üretim dosyalarını kaydet" goes on to the export when nothing failed.
    /// </summary>
    public partial class PcbPage
    {
        private Window? _readinessWindow;

        private void OnReadiness(object sender, RoutedEventArgs e) => ShowReadiness();

        private async void ShowReadiness()
        {
            if (_readinessWindow != null)
            {
                _readinessWindow.Activate();
                return;
            }

            var list = new StackPanel();
            var summary = new TextBlock { Style = (Style)FindResource("Text.Body"), Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
            var again = new Button { Content = Lang.T("Yeniden denetle"), Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(0, 0, 8, 0) };
            var export = new Button { Content = Lang.T("Üretim dosyalarını kaydet…"), Style = (Style)FindResource("Btn.Primary"), Padding = new Thickness(14, 4, 14, 4) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(again);
            buttons.Children.Add(export);
            var root = new DockPanel { Margin = new Thickness(18, 14, 18, 16) };
            var title = new TextBlock { Text = Lang.T("Üretime hazırlık"), Style = (Style)FindResource("Text.Title"), FontSize = 18, Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(title, Dock.Top);
            DockPanel.SetDock(summary, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(title);
            root.Children.Add(summary);
            root.Children.Add(buttons);
            root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            _readinessWindow = new Window
            {
                Title = Lang.T("Üretime hazırlık"), Owner = Window.GetWindow(this), Width = 620, Height = 640, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow,
                Content = root, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowActivated = !Unattended.Active
            };
            _readinessWindow.SetResourceReference(BackgroundProperty, "B.Surface");
            _readinessWindow.SetResourceReference(ForegroundProperty, "B.Text");
            _readinessWindow.Closed += (_, _) => _readinessWindow = null;
            export.Click += (_, _) =>
            {
                _readinessWindow?.Close();
                OnExport(this, new RoutedEventArgs());
            };

            async Task Run()
            {
                list.Children.Clear();
                summary.Text = Lang.T("Denetleniyor: kurallar, bağlantılar, dolgular, DFM, delikler, Gerber dosyaları okunup kartla karşılaştırılıyor…");
                again.IsEnabled = false;
                var copy = Board.Clone();   // the checks read a copy: the board stays editable meanwhile
                string name = ProjectFileName().Replace(' ', '_');
                List<ReadinessItem> items;
                try
                {
                    items = await Task.Run(() => FabReadiness.Check(copy, name));
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
                {
                    summary.Text = Lang.T("Denetim yapılamadı: ") + ex.Message;
                    again.IsEnabled = true;
                    return;
                }

                bool ready = FabReadiness.Ready(items);
                summary.Text = Lang.T(ready ? "Kart üretime hazır görünüyor" : "Üretime göndermeden önce düzeltilmesi gerekenler var") + " · " + Lang.T(FabReadiness.Summary(items));
                summary.SetResourceReference(TextBlock.ForegroundProperty, ready ? "B.Success" : "B.Danger");
                foreach (var item in items) list.Children.Add(ReadinessRow(item));
                again.IsEnabled = true;
                export.Content = Lang.T(ready ? "Üretim dosyalarını kaydet…" : "Yine de kaydet…");
                Lang.Translate(list);
                if (Unattended.Active && _readinessWindow != null)
                {
                    _readinessWindow.UpdateLayout();
                    App.SnapshotTarget = VisualTreeHelper.GetChildrenCount(_readinessWindow) > 0 ? VisualTreeHelper.GetChild(_readinessWindow, 0) as FrameworkElement : root;
                }
            }

            again.Click += async (_, _) => await Run();
            Lang.Translate(root);
            _readinessWindow.Show();
            await Run();
        }

        private FrameworkElement ReadinessRow(ReadinessItem item)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            string brush = item.State switch
            {
                ReadinessState.Pass => "B.Success",
                ReadinessState.Warning => "B.Warning",
                ReadinessState.Fail => "B.Danger",
                _ => "B.TextMuted"
            };
            var mark = new TextBlock
            {
                Text = item.State switch { ReadinessState.Pass => "✓", ReadinessState.Warning => "!", ReadinessState.Fail => "✗", _ => "–" },
                FontWeight = FontWeights.Bold, FontSize = 14, VerticalAlignment = VerticalAlignment.Top
            };
            mark.SetResourceReference(TextBlock.ForegroundProperty, brush);
            row.Children.Add(mark);
            var text = new StackPanel();
            var name = new TextBlock { Text = Lang.T(item.Name), FontWeight = FontWeights.SemiBold, FontSize = 13 };
            text.Children.Add(name);
            var detail = new TextBlock { Text = Lang.T(item.Detail), FontSize = 12, TextWrapping = TextWrapping.Wrap };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "B.TextSecondary");
            text.Children.Add(detail);
            if (item.State == ReadinessState.NotChecked)
            {
                var note = new TextBlock { Text = Lang.T("Bu ekranda denetlenmedi"), FontSize = 11.5, FontStyle = FontStyles.Italic };
                note.SetResourceReference(TextBlock.ForegroundProperty, "B.TextMuted");
                text.Children.Add(note);
            }

            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            return row;
        }
    }
}
