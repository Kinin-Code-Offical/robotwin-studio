using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;
using RobotWin.Studio.Firmware;
using RobotWin.Studio.Updates;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [integrity] What the open project or world needs that this computer lacks (<see cref="ProjectCheck"/>), each with
    /// what mends it: the installer's Arduino compiler component, an Arduino library installed here, Visual Studio's C++
    /// tools, the ESP32 toolchain, a part's definition. Dosya → Projeyi denetle, and the notice after opening one.
    /// </summary>
    internal sealed class ProjectCheckWindow : Window
    {
        private readonly MainWindow _main;
        private readonly string _what;
        private readonly Func<Task<List<ProjectIssue>>> _check;
        private readonly StackPanel _list = new StackPanel();
        private readonly TextBlock _summary = new TextBlock();
        private readonly TextBlock _status = new TextBlock();
        private bool _busy;

        public ProjectCheckWindow(MainWindow main, string what, Func<Task<List<ProjectIssue>>> check, List<ProjectIssue>? issues)
        {
            _main = main;
            _what = what;
            _check = check;
            Owner = main;
            Title = "Proje denetimi";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontFamily = R<FontFamily>("F.Ui");
            SetResourceReference(ForegroundProperty, "B.Text");

            var panel = new StackPanel { Margin = new Thickness(26, 22, 26, 20), Width = 600 };
            panel.Children.Add(new TextBlock { Text = "Proje denetimi", Style = R<Style>("Text.Title"), FontSize = 18 });
            _summary.Style = R<Style>("Text.Caption");
            _summary.TextWrapping = TextWrapping.Wrap;
            _summary.Margin = new Thickness(0, 6, 0, 14);
            panel.Children.Add(_summary);
            panel.Children.Add(new ScrollViewer { Content = _list, MaxHeight = 440, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 6, 0) });
            _status.Style = R<Style>("Text.Caption");
            _status.TextWrapping = TextWrapping.Wrap;
            _status.Margin = new Thickness(0, 8, 0, 12);
            panel.Children.Add(_status);

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var again = new Button { Content = "Yeniden denetle", Style = R<Style>("Btn.Ghost"), MinWidth = 110 };
            again.Click += async (_, _) => await Recheck();
            row.Children.Add(again);
            var close = new Button { Content = "Kapat", MinWidth = 96, Margin = new Thickness(8, 0, 0, 0), IsCancel = true, IsDefault = true };
            close.Click += (_, _) => Close();
            row.Children.Add(close);
            panel.Children.Add(row);

            var card = new Border
            {
                Margin = new Thickness(20),
                Background = R<Brush>("B.Overlay"),
                BorderBrush = R<Brush>("B.BorderStrong"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Effect = R<System.Windows.Media.Effects.Effect>("E.Popup"),
                Child = panel
            };
            card.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            Content = card;
            SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "B.Text");
            Loaded += async (_, _) =>
            {
                if (issues != null) Show(issues);
                else await Recheck();
            };
        }

        private static T R<T>(string key) => (T)Application.Current.FindResource(key);

        private async Task Recheck()
        {
            if (_busy) return;
            _busy = true;
            _summary.Text = "Denetleniyor…";
            try
            {
                Show(await _check());
            }
            finally
            {
                _busy = false;
            }
        }

        private void Show(List<ProjectIssue> issues)
        {
            _list.Children.Clear();
            // [integrity] what this computer lacks, and on a line of its own what is out of step between schematic, PCB and Montaj
            var lacks = issues.Where(i => i.Area != ProjectCheck.ConsistencyArea).ToList();
            int blocking = ProjectChecking.Blocking(lacks);
            int behind = issues.Count - lacks.Count;
            string head = lacks.Count == 0 ? $"{_what}: bu bilgisayarda eksik bir şey yok."
                : blocking == 0 ? $"{_what}: bu bilgisayarda çalışıyor; kodu değiştirip derlemek için gerekenler aşağıda."
                : $"{_what}: bu bilgisayarda {blocking} eksik var. Her birinin yanında onu gideren adım duruyor.";
            _summary.Text = behind == 0 ? head : head + "\n" + MainWindow.OutOfStep(behind);
            foreach (var issue in issues) _list.Children.Add(Row(issue));
        }

        private FrameworkElement Row(ProjectIssue issue)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            string brush = issue.Level == IssueLevel.Error ? "B.Danger" : issue.Level == IssueLevel.Warning ? "B.Warning" : "B.TextMuted";
            var bar = new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Margin = new Thickness(0, 2, 12, 2), Background = R<Brush>(brush) };
            grid.Children.Add(bar);

            var text = new StackPanel();
            var head = new TextBlock { TextWrapping = TextWrapping.Wrap };
            head.Inlines.Add(new System.Windows.Documents.Run(issue.Subject) { FontWeight = FontWeights.SemiBold });
            string level = issue.Level == IssueLevel.Error ? "Eksik" : issue.Level == IssueLevel.Warning ? "Uyarı" : "Not";
            head.Inlines.Add(new System.Windows.Documents.Run("   "));
            head.Inlines.Add(new System.Windows.Documents.Run(level + " · " + issue.Area) { Foreground = R<Brush>(brush) });
            text.Children.Add(head);
            text.Children.Add(new TextBlock { Text = issue.Text, TextWrapping = TextWrapping.Wrap, Style = R<Style>("Text.Body"), Margin = new Thickness(0, 3, 0, 0) });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            if (FixButton(issue) is { } fix)
            {
                fix.Margin = new Thickness(12, 0, 0, 0);
                fix.VerticalAlignment = VerticalAlignment.Top;
                Grid.SetColumn(fix, 2);
                grid.Children.Add(fix);
            }

            return grid;
        }

        private Button? FixButton(ProjectIssue issue)
        {
            switch (issue.Fix)
            {
                case IssueFix.ArduinoCompiler:
                {
                    var info = UpdateManager.Current.Install;
                    if (InstallEnvironment.CanModify(info))
                        return Action("Derleyiciyi ekle…", "Kurulum programını açar; Arduino derleyicisi bileşeni seçili gelir. Uygulama değişiklik sırasında kapanıp yeniden açılır.", () => Run(() => InstallEnvironment.StartModify(info, "arduino")));
                    return Action("Derleyici ayarları", "Kendi arduino-cli'nizin yolunu Ayarlar → Derleyiciler'de seçin.", () =>
                    {
                        Close();
                        _main.ShowSettings("Derleyiciler");
                    });
                }

                case IssueFix.Msvc:
                    return Action("Nasıl kurulur?", "Visual Studio Build Tools'un sayfasını açar: \"C++ ile masaüstü geliştirme\" iş yüküyle kurun, sonra uygulama onu kendisi bulur.",
                        () => Run(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://visualstudio.microsoft.com/visual-cpp-build-tools/") { UseShellExecute = true })));

                case IssueFix.Esp32Toolchain:
                    return Action("Araç zincirini kur…", "Arduino-ESP32'yi RobotWin'in kendi klasörüne indirip kurar (yaklaşık 1,3 GB indirme; önce sorulur).", () =>
                    {
                        Close();
                        _main.InstallEsp32Toolchain();
                    });

                case IssueFix.Library when issue.Argument != null:
                    return Action("Kur", $"arduino-cli ile '{issue.Argument}' kütüphanesini indirip kurar.", async () => await InstallLibrary(issue.Argument));

                case IssueFix.OpenPcb:
                    return Action("PCB sayfası", "PCB sayfasını açar: yalnız ekleyen değişiklikler kendiliğinden gelir; yerleşimden bir şey götürecekse sayfa uygulamadan önce sorar.", () =>
                    {
                        Close();
                        _main.Navigate("pcb");
                    });

                case IssueFix.SyncMontaj:
                    return Action("Montajı güncelle", "Montajı projesine göre günceller: eklenen parçalar gelir, silinenler kalkar; yerleştirdikleriniz, bağlantılarınız ve kendi kablolarınız korunur.", () =>
                    {
                        Close();
                        _main.UpdateMontajFromCheck();
                    });

                case IssueFix.Part:
                    return Action("Kütüphane sayfası", "Parçanın .rwpart ya da .rwlib dosyasını Kütüphane sayfasından içe aktarın.", () =>
                    {
                        Close();
                        _main.Navigate("library");
                    });

                default:
                    return null;
            }
        }

        private Button Action(string text, string tip, Action click)
        {
            var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(10, 3, 10, 3) };
            b.Click += (_, _) =>
            {
                if (!_busy) click();
            };
            return b;
        }

        private void Run(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex) when (ex is UpdateException || ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
            {
                _status.Text = ex.Message;
            }
        }

        private async Task InstallLibrary(string name)
        {
            if (ArduinoCompiler.FindCli() == null)
            {
                _status.Text = "arduino-cli bulunamadı: önce Arduino derleyicisi gerekli.";
                return;
            }

            _busy = true;
            _status.Text = $"{name} kuruluyor…";
            bool ok;
            string log;
            try
            {
                (ok, log) = await ArduinoCompiler.RunCliAsync("lib", "install", name);
            }
            finally
            {
                _busy = false;
            }

            string first = log.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? string.Empty;
            _status.Text = ok ? $"{name} kuruldu." : $"{name} kurulamadı: {first}";
            if (!ok) return;
            ArduinoCompiler.RaiseLibrariesChanged();
            await Recheck();
        }
    }
}
