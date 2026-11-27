using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Shell;
using static RobotWin.Studio.Pcb.PcbForms;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [drc-policy] The DRC list as KiCad's DRC dialog (studied in place): errors / warnings / exclusions shown or hidden
    /// with their counts, previous / next marker (F8 / Shift+F8 as VS Code's problems), a marker accepted as an exclusion
    /// with a note, a severity per rule (error, warning, not checked) and a text report. Severities and exclusions are
    /// saved with the board and undo like any edit.
    /// </summary>
    public partial class PcbPage
    {
        private List<DrcViolation> _raw = new List<DrcViolation>();
        private DrcResult _drc = new DrcResult();

        /// <summary>[problems] Whether the DRC ran on the board as it is (an edit makes the result out of date).</summary>
        private bool _drcCurrent;

        /// <summary>Every marker the checkers gave, before the user's severities and exclusions.</summary>
        private void SetDrc(List<DrcViolation> raw)
        {
            _raw = raw;
            _drcCurrent = true;
            ApplyDrcPolicy();
            ProblemsHub.Notify();   // [problems]
        }

        private void ClearDrc()
        {
            _raw = new List<DrcViolation>();
            _drc = new DrcResult();
            _violations = new List<DrcViolation>();
            Canvas.Violations = _violations;
            Canvas.FocusedViolation = null;
            if (_drcCurrent)
            {
                _drcCurrent = false;
                ProblemsHub.Notify();   // [problems] the PCB's result is out of date now
            }
        }

        /// <summary>[problems] The PCB's share of the Problems panel: the last DRC, or that it has not run on the board as it is; null without a board.</summary>
        public AreaReport? BoardProblems()
        {
            if (W.Schematic.Board == null) return null;
            var r = new AreaReport { Check = RunDrc };
            if (!_drcCurrent) r.NotChecked = "DRC son değişiklikten beri çalıştırılmadı";
            foreach (var v in _drc.Shown)
            {
                var marker = v;
                // [problems] the suggested fix: accept it as an exception (it stays in the report, with its reason asked)
                r.Problems.Add(new Problem(ProblemsHub.Pcb, v.Severity == DrcSeverity.Error ? ProblemLevel.Error : ProblemLevel.Warning, v.Rule, v.Message, () => ShowMarker(marker))
                {
                    Fix = () => Exclude(marker), FixTip = "İstisna olarak işaretle…: kabul edildi, raporda nedeniyle istisna olarak kalır"
                });
            }

            return r;
        }

        /// <summary>Goes to a DRC marker as a click on its row does (the row selected when the filters show it).</summary>
        public void ShowMarker(DrcViolation v)
        {
            ShowPanel(3);
            var row = DrcList.Items.OfType<DrcItem>().FirstOrDefault(i => ReferenceEquals(i.Violation, v));
            if (row != null)
            {
                DrcList.SelectedItem = row;
                DrcList.ScrollIntoView(row);
                return;
            }

            Show3D(false);
            if (v.Layer.HasValue && Board.HasLayer(v.Layer.Value)) Canvas.ActiveLayer = v.Layer.Value;
            Canvas.FocusedViolation = v;
            Canvas.ZoomTo(v.X, v.Y);
        }

        /// <summary>The last run again under the current severities, exclusions and filters (the checks are not re-run).</summary>
        private void ApplyDrcPolicy()
        {
            _drc = DrcPolicy.Apply(Board, _raw);
            _violations = _drc.Shown.Where(v => v.Severity == DrcSeverity.Error ? ShowErrors.IsChecked == true : ShowWarnings.IsChecked == true).ToList();
            Canvas.Violations = _violations;
            if (Canvas.FocusedViolation is { } f && !_violations.Contains(f)) Canvas.FocusedViolation = null;
        }

        /// <summary>The rows of the list: the shown markers by severity and rule, then the exclusions when asked for.</summary>
        private List<DrcItem> DrcItems()
        {
            var items = _violations.Select(v => new DrcItem(v)).OrderBy(i => i.CategoryOrder).ThenByDescending(i => i.Violation.Severity).ThenBy(i => i.Violation.Rule, StringComparer.Ordinal).ToList();
            if (ShowExcluded.IsChecked == true)
                items.AddRange(_drc.Excluded.OrderBy(x => x.Violation.Rule, StringComparer.Ordinal).Select(x => new DrcItem(x.Violation, x.Exclusion)));
            return items;
        }

        private void UpdateDrcFilters()
        {
            ShowErrors.Content = Lang.T($"Hatalar · {_drc.Errors}");
            ShowWarnings.Content = Lang.T($"Uyarılar · {_drc.Warnings}");
            ShowExcluded.Content = Lang.T($"İstisnalar · {_drc.Excluded.Count}");
            DrcFilters.Visibility = _raw.Count > 0 || _drc.Excluded.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnDrcFilter(object sender, RoutedEventArgs e)
        {
            ApplyDrcPolicy();
            UpdateDrcPanel(Board);
            Canvas.InvalidateVisual();
        }

        // ---------------------------------------------------------------- previous / next marker

        private void OnDrcPrevious(object sender, RoutedEventArgs e) => StepMarker(-1);

        private void OnDrcNext(object sender, RoutedEventArgs e) => StepMarker(1);

        /// <summary>The next (or previous) row of the list, wrapping round; it goes there as a click on the row does.</summary>
        private void StepMarker(int step)
        {
            int n = DrcList.Items.Count;
            if (n == 0)
            {
                W.Status = _raw.Count == 0 ? "DRC işareti yok; önce DRC'yi çalıştırın" : "Gösterilen DRC işareti yok (süzgeçlere bakın)";
                return;
            }

            ShowPanel(3);
            int i = DrcList.SelectedIndex;
            DrcList.SelectedIndex = i < 0 ? (step > 0 ? 0 : n - 1) : ((i + step) % n + n) % n;
            DrcList.ScrollIntoView(DrcList.SelectedItem);
        }

        private bool OnDrcKey(KeyEventArgs e)
        {
            if (e.Key != Key.F8 || (Keyboard.Modifiers & ~ModifierKeys.Shift) != 0) return false;
            StepMarker((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1);
            return true;
        }

        // ---------------------------------------------------------------- exclusions and severities

        /// <summary>A change to the board's DRC settings: undoable and saved, and the list follows at once.</summary>
        private void PolicyEdit(string message, Action change)
        {
            W.BeginChange();
            change();
            W.CommitChange(message);
            ApplyDrcPolicy();
            UpdateDrcPanel(Board);
            Canvas.InvalidateVisual();
            ProblemsHub.Notify();   // [problems] a severity or an exclusion changes the counts
        }

        private void Exclude(DrcViolation v)
        {
            string? note = Dialog.Prompt(Window.GetWindow(this)!, "İstisna olarak işaretle", $"{v.Rule}: {v.Message}\n\nNeden kabul edildiği (isteğe bağlı, raporda görünür):", string.Empty, allowEmpty: true);
            if (note == null) return;
            PolicyEdit($"DRC istisnası: {v.Rule}", () => DrcPolicy.Exclude(Board, v, note));
            W.Status = "İşaret istisna olarak işaretlendi; 'İstisnalar' ile görünür";
        }

        private void Include(DrcViolation v) => PolicyEdit($"DRC istisnası kaldırıldı: {v.Rule}", () => DrcPolicy.Include(Board, v));

        private void SetRuleSeverity(string rule, DrcRuleSeverity severity) =>
            PolicyEdit($"DRC kuralı '{rule}': {SeverityName(rule, severity)}", () => DrcPolicy.SetSeverity(Board, rule, severity));

        private static string SeverityName(string rule, DrcRuleSeverity s) => s switch
        {
            DrcRuleSeverity.Error => "Hata",
            DrcRuleSeverity.Warning => "Uyarı",
            DrcRuleSeverity.Ignore => "Denetleme",
            _ => DrcRuleCatalog.Find(rule)?.Default switch
            {
                DrcSeverity.Error => "Varsayılan (hata)",
                DrcSeverity.Warning => "Varsayılan (uyarı)",
                _ => "Varsayılan (duruma göre)"
            }
        };

        /// <summary>The list's right-click menu for the row under the mouse (a right click selects it).</summary>
        private void OnDrcMenu(object sender, ContextMenuEventArgs e)
        {
            if (DrcList.SelectedItem is not DrcItem item)
            {
                e.Handled = true;
                return;
            }

            var v = item.Violation;
            var m = new ContextMenu();
            MenuItem Add(string header, Action action, string? gesture = null)
            {
                var it = new MenuItem { Header = header, InputGestureText = gesture ?? string.Empty };
                it.Click += (_, _) => action();
                m.Items.Add(it);
                return it;
            }

            if (item.Exclusion != null) Add("İstisnayı kaldır", () => Include(v));
            else Add("İstisna olarak işaretle…", () => Exclude(v));
            var sev = new MenuItem { Header = "Bu kuralın önemi" };
            var current = DrcPolicy.SeverityOf(Board, v.Rule);
            foreach (var s in new[] { DrcRuleSeverity.Default, DrcRuleSeverity.Error, DrcRuleSeverity.Warning, DrcRuleSeverity.Ignore })
            {
                var choice = s;
                var it = new MenuItem { Header = SeverityName(v.Rule, s), IsCheckable = true, IsChecked = s == current };
                it.Click += (_, _) => SetRuleSeverity(v.Rule, choice);
                sev.Items.Add(it);
            }

            m.Items.Add(sev);
            m.Items.Add(new Separator());
            Add("Sonraki işaret", () => StepMarker(1), "F8");
            Add("Önceki işaret", () => StepMarker(-1), "Shift+F8");
            Add("Kopyala", () => Clipboard.SetText($"[{v.Rule}] {v.Message} @ ({Fmt(v.X)}, {Fmt(v.Y)}) mm"));
            DrcList.ContextMenu = m;
        }

        /// <summary>Every rule with its severity, by group (KiCad's Board Setup → Violation Severity).</summary>
        private void OnDrcRules(object sender, RoutedEventArgs e)
        {
            bool show = DrcRulesToggle.IsChecked == true;
            DrcRulesScroll.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            DrcList.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
            if (show) FillDrcRules();
        }

        private void FillDrcRules()
        {
            var panel = DrcRulesPanel;
            panel.Children.Clear();
            panel.Children.Add(Caption("Bir kural hata, uyarı ya da hiç denetlenmeyen olabilir; ayar kartla birlikte kaydedilir. Son çalıştırmadaki işaret sayısı adın yanında.", 0));
            var counts = _raw.GroupBy(v => v.Rule).ToDictionary(g => g.Key, g => g.Count());
            foreach (var group in DrcRuleCatalog.Groups)
            {
                panel.Children.Add(Overline(group, 12));
                foreach (var info in DrcRuleCatalog.Rules.Where(r => r.Group == group))
                {
                    string rule = info.Rule;
                    string label = counts.TryGetValue(rule, out int n) ? $"{rule} ({n})" : rule;
                    var box = Combo(new[] { DrcRuleSeverity.Default, DrcRuleSeverity.Error, DrcRuleSeverity.Warning, DrcRuleSeverity.Ignore },
                        DrcPolicy.SeverityOf(Board, rule), s => SetRuleSeverity(rule, s), s => SeverityName(rule, s));
                    panel.Children.Add(Row(label, box, 150, info.Description));
                }
            }

            panel.Children.Add(Button("Hepsini varsayılana döndür", (_, _) =>
            {
                PolicyEdit("DRC kural önemleri varsayılana döndü", () => Board.Rules.Severities.Clear());
                FillDrcRules();
            }));
        }

        /// <summary>The last run as a text file (KiCad's DRC report).</summary>
        private void OnDrcReport(object sender, RoutedEventArgs e)
        {
            if (_raw.Count == 0 && _drc.Excluded.Count == 0)
            {
                W.Status = "Rapor için önce DRC'yi çalıştırın";
                return;
            }

            string name = ProjectFileName();
            var dlg = new SaveFileDialog
            {
                Title = Lang.T("DRC raporunu kaydet"),
                Filter = Lang.Filter("Metin dosyası (*.txt)|*.txt|Rapor (*.rpt)|*.rpt"),
                FileName = name + "-drc.txt"
            };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;
            try
            {
                File.WriteAllText(dlg.FileName, DrcPolicy.Report(Board, _drc, name, DateTime.Now, Lang.T));
                W.Status = "DRC raporu kaydedildi: " + Path.GetFileName(dlg.FileName);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                W.Status = "DRC raporu yazılamadı: " + ex.Message;
            }
        }
    }
}
