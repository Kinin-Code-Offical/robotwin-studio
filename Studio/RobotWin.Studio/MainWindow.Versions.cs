using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio
{
    /// <summary>
    /// [versions] Dosya → Önceki sürümler: the active document's autosaves and its versions before each save (the backup
    /// folder), each opened as a copy or brought back in its place. Bringing one back keeps the version it replaces, so it
    /// can be undone the same way.
    /// </summary>
    public partial class MainWindow
    {
        public void ShowVersions()
        {
            var doc = W.ActiveDocument;
            if (doc.Kind == DocumentKind.Design) W.FlushActiveDesign();
            bool own = doc.FilePath != null && !doc.IsEmbedded && !doc.IsMontaj;
            string ext = doc.Kind == DocumentKind.World ? WorldDocument.Extension : RobotWinFormats.ProjectExtension;
            string name = own ? Path.GetFileNameWithoutExtension(doc.FilePath!) : doc.BaseTitle;
            var window = new VersionsWindow(this, name, Backups.VersionsOf(name, ext), own && File.Exists(doc.FilePath));
            if (App.IsSnapshot)
            {
                window.ShowActivated = false;
                window.Show();
                window.UpdateLayout();
                App.SnapshotTarget = VisualTreeHelper.GetChildrenCount(window) > 0 ? VisualTreeHelper.GetChild(window, 0) as FrameworkElement : null;
                return;
            }

            window.ShowDialog();
        }

        /// <summary>A version in a tab of its own, unsaved (its name says when it is from), beside the document.</summary>
        internal void OpenVersionCopy(BackupVersion v)
        {
            string label = " (" + v.Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + ")";
            try
            {
                if (string.Equals(v.File.Extension, WorldDocument.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    var world = WorldDocument.Load(v.File.FullName);
                    world.Title = (string.IsNullOrWhiteSpace(world.Title) ? "Dünya" : world.Title) + label;
                    W.NewWorld(world, null);
                    Navigate("world");
                }
                else
                {
                    var package = ProjectPackage.Load(v.File.FullName);
                    package.Schematic.Title = (string.IsNullOrWhiteSpace(package.Schematic.Title) ? "Proje" : package.Schematic.Title) + label;
                    W.LoadPackage(package, null);
                    Navigate("schematic");
                }

                W.Status = "Önceki sürüm kopya olarak açıldı: " + v.File.Name;
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.Text.Json.JsonException || ex is UnauthorizedAccessException)
            {
                Dialog.Show(this, "Açılamadı", ex.Message, "Tamam");
            }
        }

        /// <summary>The active document's file goes back to this version (asked first); the version it replaces is kept.</summary>
        internal bool RestoreVersion(BackupVersion v)
        {
            var doc = W.ActiveDocument;
            string? path = doc.FilePath;
            if (path == null || doc.IsEmbedded || doc.IsMontaj) return false;
            string when = VersionsWindow.When(v.Time);
            string message = $"{Path.GetFileName(path)}, {when} hâline döner. Şu anki hâli de önceki sürüm olarak saklanır: bu adım da aynı yoldan geri alınabilir." +
                             (doc.IsDirty ? "\n\nSekmedeki kaydedilmemiş değişiklikler kaybolur." : string.Empty);
            if (Dialog.Show(this, "Bu sürüm geri yüklensin mi?", message, "Geri yükle", "Vazgeç") != 0) return false;
            try
            {
                Backups.KeepPrevious(path, always: true);
                File.Copy(v.File.FullName, path, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Dialog.Show(this, "Geri yüklenemedi", ex.Message, "Tamam");
                return false;
            }

            W.Close(doc);
            OpenFile(path);
            W.Status = $"{Path.GetFileName(path)}: {when} sürümü geri yüklendi (önceki hâli yedeklerde).";
            return true;
        }

        /// <summary>
        /// --snapshot --fault versions-demo: the open example saved into a folder of its own with a few earlier versions
        /// beside it (that folder is the backup folder for this run only), and the window on them.
        /// </summary>
        private void LoadVersionsDemo()
        {
            string dir = Path.Combine(Path.GetTempPath(), "robotwin-versions-demo");
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                AppSettings.Current.BackupFolder = dir;   // a snapshot never saves the settings
                W.FlushActiveDesign();
                var doc = W.DesignDocument;
                string name = Backups.SafeName(string.IsNullOrWhiteSpace(doc.Package.Schematic.Title) ? "Proje" : doc.Package.Schematic.Title);
                string file = Path.Combine(dir, name + RobotWinFormats.ProjectExtension);
                doc.Package.Save(file);
                doc.FilePath = file;
                var now = DateTime.Now;
                foreach (var (ago, before) in new[] { (TimeSpan.FromMinutes(25), true), (TimeSpan.FromMinutes(131), false), (TimeSpan.FromHours(26), true), (TimeSpan.FromDays(3), false) })
                {
                    var t = now - ago;
                    string stamp = t.ToString(before ? "yyyyMMdd-HHmmss" : "yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
                    File.Copy(file, Path.Combine(dir, name + "-" + (before ? "önceki-" : string.Empty) + stamp + RobotWinFormats.ProjectExtension), true);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return;
            }

            ShowVersions();
        }
    }
}
