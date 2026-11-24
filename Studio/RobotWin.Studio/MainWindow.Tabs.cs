using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio
{
    /// <summary>
    /// Document tabs can be dragged sideways to reorder them, like in a browser. [tabs] The strip scrolls with the wheel and
    /// its arrows when the tabs do not fit, the active tab is always scrolled into view, the list button shows every open
    /// tab, and pinned tabs stay at the left.
    /// </summary>
    public partial class MainWindow
    {
        private void SetupTabStrip()
        {
            W.ActiveDocumentChanged += (_, _) => RevealActiveTab();
            W.Documents.CollectionChanged += (_, _) => RevealActiveTab();
            TabScroll.SizeChanged += (_, _) => UpdateTabArrows();
            Bind(Key.T, ModifierKeys.Control | ModifierKeys.Shift, ReopenClosedTab);
        }

        /// <summary>Scrolls the strip so the active tab is in full view (after the tabs have been laid out).</summary>
        private void RevealActiveTab() => Dispatcher.BeginInvoke(new System.Action(() =>
        {
            if (Tabs.ItemContainerGenerator.ContainerFromItem(W.ActiveDocument) is FrameworkElement c && c.IsVisible)
            {
                var left = c.TranslatePoint(new Point(0, 0), Tabs).X;
                double from = TabScroll.HorizontalOffset, to = from + TabScroll.ViewportWidth;
                if (left < from) TabScroll.ScrollToHorizontalOffset(left - 4);
                else if (left + c.ActualWidth > to) TabScroll.ScrollToHorizontalOffset(left + c.ActualWidth - TabScroll.ViewportWidth + 4);
            }

            UpdateTabArrows();
        }), System.Windows.Threading.DispatcherPriority.Loaded);

        private void UpdateTabArrows()
        {
            bool overflow = TabScroll.ScrollableWidth > 0.5;
            TabLeft.Visibility = TabRight.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
            TabLeft.IsEnabled = TabScroll.HorizontalOffset > 0.5;
            TabRight.IsEnabled = TabScroll.HorizontalOffset < TabScroll.ScrollableWidth - 0.5;
        }

        private void OnTabsScrolled(object sender, ScrollChangedEventArgs e) => UpdateTabArrows();

        private void OnTabsLeft(object sender, RoutedEventArgs e) => TabScroll.ScrollToHorizontalOffset(TabScroll.HorizontalOffset - System.Math.Max(120, TabScroll.ViewportWidth * 0.6));

        private void OnTabsRight(object sender, RoutedEventArgs e) => TabScroll.ScrollToHorizontalOffset(TabScroll.HorizontalOffset + System.Math.Max(120, TabScroll.ViewportWidth * 0.6));

        /// <summary>Every open tab in a list (the ones scrolled out of view too); a click goes to it.</summary>
        private void OnTabList(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = TabList, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var doc in W.Documents)
            {
                var d = doc;
                var item = new MenuItem
                {
                    Header = d.TabTitle + (d.IsDirty ? "  •" : string.Empty),
                    IsChecked = d == W.ActiveDocument,
                    InputGestureText = (d.IsPinned ? "sabit · " : string.Empty) + d.KindText,
                    ToolTip = d.TabToolTip,
                };
                item.Click += (_, _) => W.Activate(d);
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());
            var reopen = MenuItem("Kapatılan sekmeyi yeniden aç", "Ctrl+Shift+T", ReopenClosedTab);
            reopen.IsEnabled = W.ClosedFiles.Count > 0;
            menu.Items.Add(reopen);
            menu.IsOpen = true;
        }

        private void OnTabUnpin(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is OpenDocument doc) doc.IsPinned = false;
            e.Handled = true;
        }

        /// <summary>
        /// --snapshot --fault tabs: many tabs through the real workspace — names (a saved file by its file name, two new
        /// circuits told apart), a pinned one moved to the left and kept by "close others", the strip scrolled to the active
        /// tab with its arrows, a closed file opened again. Leaves many tabs open for the picture.
        /// </summary>
        internal async void RunTabsCheck(string logPath)
        {
            var log = new System.Text.StringBuilder();
            int failures = 0;

            void Check(bool ok, string what)
            {
                if (!ok) failures++;
                log.AppendLine((ok ? "OK   " : "FAIL ") + what);
            }

            async System.Threading.Tasks.Task Settle()
            {
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                await System.Threading.Tasks.Task.Delay(150);
            }

            try
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "robotwin-tabs-e2e");
                System.IO.Directory.CreateDirectory(dir);
                // A saved world: its tab reads the file's name; Save As gave the world that name inside too.
                var arm = Examples.WorldTemplates.Find("servo-arm")!.Build();
                NewWorld(arm);
                string saved = System.IO.Path.Combine(dir, "Masa kolu.rwworld");
                W.SaveWorld(W.ActiveDocument, saved);
                var savedDoc = W.ActiveDocument;
                Check(savedDoc.TabTitle == "Masa kolu" && arm.Title == "Masa kolu", $"kayıtlı dünya dosya adıyla: \"{savedDoc.TabTitle}\" (iç başlık \"{arm.Title}\")");

                W.NewDocument();
                W.ActiveDocument.IsDirty = true;   // worked on: a second new circuit gets a tab of its own
                W.NewDocument();
                await Settle();
                var fresh = W.Documents.Where(d => d.BaseTitle == "Yeni Devre").ToList();
                Check(fresh.Count >= 2 && fresh.Select(d => d.TabTitle).Distinct().Count() == fresh.Count, "aynı adlı yeni devreler ayrılıyor: " + string.Join(", ", fresh.Select(d => d.TabTitle)));

                foreach (var id in new[] { "car", "line-follower", "factory-cell", "sonar", "mecanum", "lights" })
                {
                    if (Examples.WorldTemplates.Find(id) is { } t) NewWorld(t.Build());
                }

                W.ActiveDocument.IsDirty = true;
                await Settle();
                Check(TabScroll.ScrollableWidth > 0, $"sekmeler sığmıyor: {W.Documents.Count} sekme, kaydırılabilir {TabScroll.ScrollableWidth:F0} px");
                Check(TabLeft.Visibility == Visibility.Visible && TabRight.Visibility == Visibility.Visible, "kaydırma okları görünüyor");
                var active = Tabs.ItemContainerGenerator.ContainerFromItem(W.ActiveDocument) as FrameworkElement;
                double left = active?.TranslatePoint(new Point(0, 0), Tabs).X ?? -1;
                Check(active != null && left >= TabScroll.HorizontalOffset - 1 && left + active.ActualWidth <= TabScroll.HorizontalOffset + TabScroll.ViewportWidth + 1,
                    "etkin (son açılan) sekme görünür alanda");

                savedDoc.IsPinned = true;
                await Settle();
                Check(W.Documents.IndexOf(savedDoc) == 0, "sabitlenen sekme en sola geçti");
                Check(Shell.AppSettings.Current.PinnedFiles.Contains(saved), "sabitlenen dosya hatırlanıyor");
                W.Activate(savedDoc);
                await Settle();
                Check(TabScroll.HorizontalOffset < 1, "en soldaki sabit sekmeye geçince şerit başa kaydı");

                // Closed and opened again: comes back pinned.
                CloseTab(savedDoc);
                await Settle();
                Check(!W.Documents.Contains(savedDoc) && W.ClosedFiles.Contains(saved), "kapatılan dosya listede");
                ReopenClosedTab();
                await Settle();
                var back = W.Documents.FirstOrDefault(d => string.Equals(d.FilePath, saved, System.StringComparison.OrdinalIgnoreCase));
                Check(back != null && back.IsPinned && W.Documents.IndexOf(back) == 0, "Ctrl+Shift+T: yeniden açıldı, yine sabit ve en solda");

                // "Close others" (the tab menu's) leaves pinned tabs; unsaved ones are not asked about here.
                foreach (var d in W.Documents) d.IsDirty = false;
                var keep = W.Documents.Last();
                CloseOthers(keep);
                await Settle();
                Check(back != null && W.Documents.Contains(back) && W.Documents.Contains(keep)
                      && W.Documents.All(d => d == keep || d.IsPinned || d.IsPristine), "Diğerlerini kapat: sabit sekme ve seçilen kaldı (" + string.Join(", ", W.Documents.Select(d => d.TabTitle)) + ")");
                foreach (var id in new[] { "two-boards", "displays" })
                {
                    if (Examples.WorldTemplates.Find(id) is { } t) NewWorld(t.Build());
                }

                W.ActiveDocument.IsDirty = true;
                await Settle();
            }
            catch (System.Exception ex)
            {
                failures++;
                log.AppendLine("FAIL beklenmeyen hata: " + ex);
            }

            log.AppendLine(failures == 0 ? "TÜMÜ GEÇTİ" : failures + " HATA");
            System.IO.File.WriteAllText(logPath, log.ToString());
        }

        /// <summary>Ctrl+Shift+T: opens the last closed file again.</summary>
        private void ReopenClosedTab()
        {
            if (W.TakeClosedFile() is { } path) OpenFile(path);
            else W.Status = "Yeniden açılacak kapatılmış bir dosya yok.";
        }

        private OpenDocument? _dragTab;
        private Point _dragStart;
        private bool _dragging;

        private void SetupTabDrag()
        {
            Tabs.PreviewMouseLeftButtonDown += (_, e) =>
            {
                _dragTab = DocumentAt(e.OriginalSource as DependencyObject);
                _dragStart = e.GetPosition(Tabs);
                _dragging = false;
            };
            Tabs.PreviewMouseMove += (_, e) =>
            {
                if (_dragTab == null || e.LeftButton != MouseButtonState.Pressed) return;
                var p = e.GetPosition(Tabs);
                if (!_dragging)
                {
                    if (System.Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance) return;
                    _dragging = true;
                    Tabs.CaptureMouse();
                    Mouse.OverrideCursor = Cursors.SizeWE;
                }

                // Swap with the neighbour the pointer has moved over (pinned tabs among the pinned, the others among the others).
                int from = W.Documents.IndexOf(_dragTab);
                if (from < 0) return;
                for (int i = 0; i < W.Documents.Count; i++)
                {
                    if (i == from || W.Documents[i].IsPinned != _dragTab.IsPinned || Tabs.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
                    var left = c.TranslatePoint(new Point(0, 0), Tabs).X;
                    double mid = left + c.ActualWidth / 2;
                    if ((i > from && p.X > mid) || (i < from && p.X < mid))
                    {
                        W.Documents.Move(from, i);
                        break;
                    }
                }
            };
            Tabs.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (_dragging)
                {
                    Tabs.ReleaseMouseCapture();
                    Mouse.OverrideCursor = null;
                    if (_dragTab != null) W.Activate(_dragTab);
                    e.Handled = true;
                }

                _dragTab = null;
                _dragging = false;
            };
            Tabs.LostMouseCapture += (_, _) =>
            {
                if (!_dragging) return;
                Mouse.OverrideCursor = null;
                _dragging = false;
                _dragTab = null;
            };
        }

        private static OpenDocument? DocumentAt(DependencyObject? d)
        {
            while (d != null)
            {
                if (d is Button) return null;   // the close button keeps working normally
                if (d is FrameworkElement fe && fe.DataContext is OpenDocument doc) return doc;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }

            return null;
        }
    }
}
