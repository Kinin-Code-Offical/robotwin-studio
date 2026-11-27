using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio
{
    /// <summary>
    /// [problems] VS Code's Problems panel for the whole project: the status bar counts errors and warnings of every
    /// area (circuit ERC, PCB DRC, the last build, the Montaj, the project check) and says when a check is out of date; a click opens the list by area,
    /// a row goes to the place in its editor, an out-of-date area offers to run its check.
    /// </summary>
    public partial class MainWindow
    {
        private readonly DispatcherTimer _problemsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        private Window? _problemsWindow;
        private StackPanel? _problemsList;

        private void SetupProblems()
        {
            AreaReport? On(string page, Func<AreaReport?> source)
            {
                var r = source();
                if (r == null) return null;
                // every jump opens its editor first
                var routed = new AreaReport { NotChecked = r.NotChecked, Check = r.Check == null ? null : () => { Navigate(page); r.Check(); } };
                foreach (var p in r.Problems)
                {
                    var go = p.Go;
                    routed.Problems.Add(new Problem(p.Area, p.Level, p.Rule, p.Message, () =>
                    {
                        Navigate(page);
                        Dispatcher.BeginInvoke(new Action(() => go?.Invoke()), DispatcherPriority.Loaded);
                    }) { Fix = p.Fix, FixTip = p.FixTip });
                }

                return routed;
            }

            ProblemsHub.Register(ProblemsHub.Circuit, () => On("schematic", _schematic.CircuitProblems));
            ProblemsHub.Register(ProblemsHub.Pcb, () => On("pcb", _pcb.BoardProblems));
            ProblemsHub.Register(ProblemsHub.Code, () => On("code", _code.CodeProblems));
            ProblemsHub.Register(ProblemsHub.Assembly, () => CheckArea(montaj: true));   // [problems] the project check's two areas (MainWindow.Check.cs)
            ProblemsHub.Register(ProblemsHub.Project, () => CheckArea(montaj: false));
            ProblemsHub.Register(ProblemsHub.ModelLimits, ModelLimitsArea);   // [limits]
            ProblemsHub.Changed += ScheduleProblems;
            W.DocumentChanged += (_, _) =>
            {
                if (ReferenceEquals(_checkedDoc, W.ActiveDocument))
                {
                    _checkStale = true;   // the check's result is from before this change: checked again once editing settles
                    ScheduleAutoCheck(4);
                }
                ScheduleProblems();
            };
            W.DocumentReplaced += (_, _) => ScheduleProblems();
            W.ActiveDocumentChanged += (_, _) => ScheduleProblems();   // another tab: its own areas (the check's result belongs to one document)
            _problemsTimer.Tick += (_, _) =>
            {
                _problemsTimer.Stop();
                RefreshProblems();
            };
            ScheduleProblems();
        }

        /// <summary>
        /// [limits] The Problems panel's "Model sınırları" area: for each part of the circuit whose model leaves something
        /// out (no thermal noise, no core loss, the radio a model …), what it is, as worth knowing; a row shows the part.
        /// None when no part has a noted limit.
        /// </summary>
        private AreaReport? ModelLimitsArea()
        {
            var doc = W.ActiveDocument;
            if (doc.Kind == DocumentKind.World) return null;
            var r = new AreaReport();
            foreach (var group in W.Schematic.Parts.GroupBy(p => p.PartId, StringComparer.OrdinalIgnoreCase))
            {
                var limits = RobotTwin.CoreSim.Design.ModelLimits.Of(group.Key);
                if (limits.Count == 0) continue;
                var parts = group.Select(p => p.RefDes).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                string name = W.Library.Find(group.Key)?.Name ?? group.Key;
                foreach (var limit in limits)
                    r.Problems.Add(new Problem(ProblemsHub.ModelLimits, ProblemLevel.Info, $"{string.Join(", ", parts.Take(4))}{(parts.Count > 4 ? " …" : string.Empty)} ({name})", limit, () =>
                    {
                        Navigate("schematic");
                        Dispatcher.BeginInvoke(new Action(() => _schematic.Canvas.HighlightParts(parts)), DispatcherPriority.Loaded);
                    }));
            }

            return r.Problems.Count == 0 ? null : r;
        }

        private void ScheduleProblems()
        {
            _problemsTimer.Stop();
            _problemsTimer.Start();
        }

        /// <summary>The status bar's counts and, when it is open, the list.</summary>
        private void RefreshProblems()
        {
            var reports = ProblemsHub.Collect();
            var (errors, warnings, unverified) = ProblemsHub.Counts(reports);
            ProblemErrors.Text = errors.ToString(System.Globalization.CultureInfo.CurrentCulture);
            ProblemWarnings.Text = warnings.ToString(System.Globalization.CultureInfo.CurrentCulture);
            ProblemsStale.Visibility = unverified > 0 ? Visibility.Visible : Visibility.Collapsed;
            ProblemsButton.ToolTip = Lang.T($"Sorunlar: {errors} hata, {warnings} uyarı") + (unverified > 0 ? "\n" + Lang.T($"{unverified} alanın denetimi eski") : string.Empty)
                                     + "\n" + string.Join("\n", reports.Select(r => Lang.T(ProblemsHub.Summary(r.Area, r.Report))));
            if (_problemsList != null) FillProblems(reports);
        }

        private void OnProblems(object sender, RoutedEventArgs e)
        {
            if (_problemsWindow != null)
            {
                _problemsWindow.Activate();
                return;
            }

            _problemsList = new StackPanel();
            var root = new DockPanel { Margin = new Thickness(18, 14, 18, 16) };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var again = new Button { Content = Lang.T("Yenile"), Style = (Style)FindResource("Btn.Ghost") };
            again.Click += (_, _) => RefreshProblems();
            DockPanel.SetDock(again, Dock.Right);
            head.Children.Add(again);
            head.Children.Add(new TextBlock { Text = Lang.T("Sorunlar"), Style = (Style)FindResource("Text.Title"), FontSize = Shell.TypeScale.Heading });
            DockPanel.SetDock(head, Dock.Top);
            root.Children.Add(head);
            root.Children.Add(new ScrollViewer { Content = _problemsList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            _problemsWindow = new Window
            {
                Title = Lang.T("Sorunlar"), Owner = this, Width = 560, Height = 520, ShowInTaskbar = false, WindowStyle = WindowStyle.ToolWindow, Content = root,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowActivated = !Unattended.Active
            };
            _problemsWindow.SetResourceReference(BackgroundProperty, "B.Surface");
            _problemsWindow.SetResourceReference(ForegroundProperty, "B.Text");
            _problemsWindow.Closed += (_, _) =>
            {
                _problemsWindow = null;
                _problemsList = null;
            };
            FillProblems(ProblemsHub.Collect());
            _problemsWindow.Show();
        }

        /// <summary>[problems] Snapshot: the counts now and the list's window as the picture.</summary>
        /// <summary>[problems] The areas whose "bilgi" fold is open.</summary>
        private readonly HashSet<string> _openNotes = new HashSet<string>(StringComparer.Ordinal);

        private void SnapshotProblems(bool openNotes = false)
        {
            if (openNotes) foreach (var a in new[] { ProblemsHub.Circuit, ProblemsHub.Pcb, ProblemsHub.Code, ProblemsHub.Assembly, ProblemsHub.Project, ProblemsHub.ModelLimits }) _openNotes.Add(a);
            RefreshProblems();
            OnProblems(this, new RoutedEventArgs());
            if (_problemsWindow == null) return;
            _problemsWindow.UpdateLayout();
            App.SnapshotTarget = VisualTreeHelper.GetChildrenCount(_problemsWindow) > 0 ? VisualTreeHelper.GetChild(_problemsWindow, 0) as FrameworkElement : _problemsWindow.Content as FrameworkElement;
        }

        private void FillProblems(List<(string Area, AreaReport Report)> reports)
        {
            if (_problemsList == null) return;
            _problemsList.Children.Clear();
            if (reports.Count == 0)
            {
                _problemsList.Children.Add(new TextBlock { Text = Lang.T("Projede denetlenecek bir şey yok."), Style = (Style)FindResource("Text.Body") });
                return;
            }

            // [problems] each area's state at the top (Devre tamam, PCB 2 uyarı …): a click goes to its list
            var states = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            _problemsList.Children.Add(states);
            foreach (var (area, report) in reports)
            {
                var title = new DockPanel { Margin = new Thickness(0, 10, 0, 4) };
                if (report.NotChecked != null && report.Check != null)
                {
                    var run = new Button { Content = Lang.T("Denetle"), Style = (Style)FindResource("Btn.Ghost"), Padding = new Thickness(8, 1, 8, 1) };
                    run.Click += (_, _) => report.Check();
                    DockPanel.SetDock(run, Dock.Right);
                    title.Children.Add(run);
                }

                string brush = report.Errors > 0 ? "B.Danger" : report.Warnings > 0 ? "B.Warning" : report.NotChecked != null ? "B.TextSecondary" : "B.Success";
                var summary = new TextBlock { Text = Lang.T(ProblemsHub.Summary(area, report)), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                summary.SetResourceReference(TextBlock.ForegroundProperty, brush);
                title.Children.Add(summary);
                _problemsList.Children.Add(title);

                var chipText = new TextBlock { Text = Lang.T(ProblemsHub.Summary(area, report)), FontSize = 12 };
                chipText.SetResourceReference(TextBlock.ForegroundProperty, brush);
                var chip = new Button { Content = chipText, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 6), ToolTip = Lang.T("Bu alanın listesine git") };
                chip.SetResourceReference(StyleProperty, "Btn.Ghost");
                var target = title;
                chip.Click += (_, _) => target.BringIntoView(new Rect(0, 0, 1, 400));
                states.Children.Add(chip);

                if (report.NotChecked != null)
                    _problemsList.Children.Add(new TextBlock { Text = Lang.T(report.NotChecked), Style = (Style)FindResource("Text.Caption"), Margin = new Thickness(0, 0, 0, 4) });

                // errors and warnings in full; what is only worth knowing folds under one line (it opens with a click)
                foreach (var p in report.Problems.Where(p => p.Level is ProblemLevel.Error or ProblemLevel.Warning).OrderBy(p => p.Level)) _problemsList.Children.Add(ProblemRow(p));
                var notes = report.Problems.Where(p => p.Level is ProblemLevel.Info or ProblemLevel.Unverified).ToList();
                if (notes.Count == 0) continue;
                // [problems] an opened fold stays open when the list is drawn again (after a fix, a check)
                string foldKey = area;
                bool opened = _openNotes.Contains(foldKey);
                var fold = new StackPanel { Visibility = opened ? Visibility.Visible : Visibility.Collapsed };
                foreach (var p in notes) fold.Children.Add(ProblemRow(p));
                var label = new TextBlock { Text = (opened ? "▾ " : "▸ ") + Lang.T($"{notes.Count} bilgi"), FontSize = 12 };
                label.SetResourceReference(TextBlock.ForegroundProperty, "B.TextSecondary");
                var toggle = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 0, 2), ToolTip = Lang.T("Bilgi düzeyindekileri göster / gizle") };
                toggle.SetResourceReference(StyleProperty, "Btn.Ghost");
                toggle.Click += (_, _) =>
                {
                    bool open = fold.Visibility != Visibility.Visible;
                    fold.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                    label.Text = (open ? "▾ " : "▸ ") + Lang.T($"{notes.Count} bilgi");
                    if (open) _openNotes.Add(foldKey);
                    else _openNotes.Remove(foldKey);
                };
                _problemsList.Children.Add(toggle);
                _problemsList.Children.Add(fold);
            }
        }

        /// <summary>One problem: its level's dot, the rule and the message; a click goes to its place.</summary>
        private FrameworkElement ProblemRow(Problem p)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, p.Level switch { ProblemLevel.Error => "B.Danger", ProblemLevel.Warning => "B.Warning", _ => "B.TextMuted" });
            row.Children.Add(dot);
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = Lang.T(p.Rule), FontSize = 12 });
            var msg = new TextBlock { Text = Lang.T(p.Message), FontSize = 12, TextWrapping = TextWrapping.Wrap };
            msg.SetResourceReference(TextBlock.ForegroundProperty, "B.TextSecondary");
            text.Children.Add(msg);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var b = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 0, 2), IsEnabled = p.Go != null };
            b.SetResourceReference(StyleProperty, "Btn.Ghost");
            var go = p.Go;
            b.Click += (_, _) => go?.Invoke();
            if (p.Fix is not { } fix) return b;

            // [problems] the suggested fix beside the row (VS Code's quick fix): one click, then the areas check again
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(b);
            var mend = new Button
            {
                Content = new Theme.Icon { Data = TryFindResource("I.Fix") as Geometry, Size = 15 }, ToolTip = Lang.T(p.FixTip ?? "Düzelt"),
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 2, 0, 0), Padding = new Thickness(5)
            };
            mend.SetResourceReference(StyleProperty, "Btn.Ghost");
            mend.Click += (_, _) =>
            {
                fix();
                ProblemsHub.Notify();
            };
            Grid.SetColumn(mend, 1);
            line.Children.Add(mend);
            return line;
        }
    }
}
