using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Editor;
using RobotWin.Studio.Firmware;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// "Kod" page: a code workspace like an editor's explorer. "Kartlar" lists every programmable board of the project and
    /// of the open world (Uno, Nano, ESP32), each with its own sketch of any number of files and folders; "Kod
    /// klasörlerim" holds folders on disk (new ones, or any folder opened from elsewhere). Any folder can be built and
    /// written to any board, saved elsewhere on disk or exported as a zip. Built with the real toolchains and run on the
    /// emulated board.
    /// </summary>
    public partial class CodePage : UserControl
    {
        private readonly List<CodeTarget> _targets = new List<CodeTarget>();
        private readonly List<CodeFolder> _folders = new List<CodeFolder>();
        private CodeFolder? _folder;
        private string? _file;
        private CodeTarget? _target;
        private bool _loading;
        private CancellationTokenSource? _build;
        private long _serialShown;
        private List<CodeDiagnostic> _diagnostics = new List<CodeDiagnostic>();
        private readonly System.Windows.Threading.DispatcherTimer _saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };

        /// <summary>Where new code folders go: Belgeler\RobotWin\Kod.</summary>
        public static string WorkspaceRoot => Path.Combine(AppSettings.Current.ProjectFolder, "Kod");

        public CodePage()
        {
            InitializeComponent();
            TemplateBox.ItemsSource = new[] { "Şablon seç…" }.Concat(SketchTemplates.All.Select(t => t.Name)).ToList();
            TemplateBox.SelectedIndex = 0;
            Editor.TextChanged += OnTextChanged;
            Editor.Box.SelectionChanged += (_, _) => CaretText.Text = $"Satır {Editor.CaretLine}, Sütun {Editor.CaretColumn}";
            W.DocumentReplaced += (_, _) => Dispatcher.BeginInvoke(new Action(Refresh));
            W.ActiveDocumentChanged += (_, _) =>
            {
                if (IsVisible) Refresh();
            };
            W.DocumentChanged += (_, _) =>
            {
                if (IsVisible) Refresh();
            };
            W.Simulation.SerialReceived += (_, _) => Dispatcher.BeginInvoke(new Action(UpdateSerial));
            W.Simulation.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SimulationSession.State)) UpdateSerial();
            };
            _saveTimer.Tick += (_, _) => FlushDisk();
            PreviewKeyDown += OnKeys;
            WireDebugger();   // [debug] Pages/CodePage.Debug.cs
            InitCompletion();   // [code] Pages/CodePage.Libraries.cs
            Libraries.LibraryManager.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(LibrariesChanged));
        }

        private static Workspace W => Workspace.Current;

        private string Fqbn => _target?.Fqbn ?? (_folder?.Board?.Fqbn) ?? "arduino:avr:uno";

        public void OnShown()
        {
            Refresh();
            UpdateSerial();
            Editor.Focus();
        }

        // ================================================================ boards and folders

        /// <summary>[guide] Raised after code was written to a board, built, and its circuit or world started.</summary>
        internal event Action<CodeTarget>? Uploaded;

        internal CodeTarget? CurrentTarget => _target;

        internal string EditorText => Editor.Text;

        internal FrameworkElement UploadButtonElement => UploadButton;

        /// <summary>[guide] "Karta yükle ve çalıştır" for the open folder.</summary>
        internal void Upload() => OnUpload(this, new RoutedEventArgs());

        /// <summary>[guide] Picks a board (by its key) and opens its own code.</summary>
        internal bool ShowBoard(string key)
        {
            Refresh();
            var t = _targets.FirstOrDefault(x => x.Key == key);
            if (t == null) return false;
            _loading = true;
            _target = t;
            TargetBox.SelectedItem = t;
            _loading = false;
            LibrariesChanged();
            var own = _folders.FirstOrDefault(f => f.Board == t);
            if (own != null) Open(own, own.MainFile, rebuild: true);
            UpdateTargetText();
            UpdateBuildState();
            return true;
        }

        /// <summary>[guide] Selects a group of the first match in the editor, scrolled into view, so typing replaces it.</summary>
        internal void SelectInEditor(System.Text.RegularExpressions.Regex pattern, int group)
        {
            var m = pattern.Match(Editor.Text);
            if (!m.Success || m.Groups.Count <= group) return;
            var g = m.Groups[group];
            Editor.Box.Select(g.Index, g.Length);
            int line = Editor.Box.GetLineIndexFromCharacterIndex(g.Index);
            if (line >= 0) Editor.Box.ScrollToLine(line);
            Editor.Focus();
        }

        /// <summary>[guide] Replaces the first match in the editor (as typing would: undoable, the build state follows).</summary>
        internal void ReplaceInEditor(System.Text.RegularExpressions.Regex pattern, string replacement)
        {
            var m = pattern.Match(Editor.Text);
            if (!m.Success) return;
            Editor.Box.Select(m.Index, m.Length);
            Editor.Box.SelectedText = replacement;
            Editor.Box.Select(m.Index + replacement.Length, 0);
            Editor.Focus();
        }

        /// <summary>Keys of the boards the page offers ("p:U1", "w:O3" …), for checks.</summary>
        internal List<string> TargetKeys() => FindTargets().Select(t => t.Key).ToList();

        /// <summary>Every programmable board: the project's circuit, then the open world (objects and board designs).</summary>
        private List<CodeTarget> FindTargets()
        {
            var list = new List<CodeTarget>();
            foreach (var p in W.Schematic.Parts)
            {
                var def = W.Library.Find(p.PartId);
                string? fqbn = def != null && def.Symbol == SymbolKind.Board ? ArduinoCompiler.FqbnFor(def.Id) : null;
                if (fqbn == null) continue;
                var part = p;
                list.Add(new CodeTarget("p:" + p.RefDes, $"{p.RefDes} · {def!.Name}", "proje", fqbn,
                    () => W.Project.FirmwareFor(part.RefDes),
                    () => W.FirmwareFor(part.RefDes, fqbn),
                    status => W.MarkFirmwareChanged(status),
                    BuiltinSource(part.Parameters)));
            }

            var worldDoc = W.ActiveDocument.Kind == DocumentKind.World ? W.ActiveDocument : W.Documents.FirstOrDefault(d => d.Kind == DocumentKind.World);
            var world = worldDoc?.World;
            if (world != null)
            {
                void Dirty(string? status)
                {
                    worldDoc!.IsDirty = true;
                    if (status != null) W.Status = status;
                }

                foreach (var o in world.Objects)
                {
                    // [assembly] A Montaj's boards are its projects' boards: their code is written in the project
                    // (listed above, or in that project's tab), and the Montaj takes it when it is opened.
                    if (world.IsAssembly && (o.ProjectRef != null || (o.Kind == WorldObjectKind.Board && world.FindDesign(o.DesignId)?.Package.Schematic.ProjectId is { Length: > 0 } bp && world.SourceOf(bp) != null))) continue;
                    if (o.PartId != null && W.Library.Find(o.PartId) is { Symbol: SymbolKind.Board } def && ArduinoCompiler.FqbnFor(def.Id) is { } fqbn)
                    {
                        var obj = o;
                        list.Add(new CodeTarget("w:" + o.Id, o.Name, world.Title, fqbn,
                            () => obj.Firmware,
                            () => obj.Firmware = new FirmwareSource { BoardRefDes = obj.Id, Target = fqbn },
                            Dirty, BuiltinSource(obj.Parameters)));
                    }
                    else if (o.Kind == WorldObjectKind.Board && world.FindDesign(o.DesignId) is { } design && world.Objects.First(x => x.DesignId == o.DesignId) == o)
                    {
                        // A board design: each programmable board in it (shared by every copy of the design).
                        foreach (var p in design.Package.Schematic.Parts)
                        {
                            var pdef = W.Library.Find(p.PartId);
                            if (pdef?.Symbol != SymbolKind.Board || ArduinoCompiler.FqbnFor(pdef.Id) is not { } dfqbn) continue;
                            var pkg = design.Package;
                            var part = p;
                            list.Add(new CodeTarget("d:" + o.Id + ":" + p.RefDes, $"{o.Name} · {p.RefDes}", world.Title, dfqbn,
                                () => pkg.FirmwareFor(part.RefDes),
                                () =>
                                {
                                    var fw = new FirmwareSource { BoardRefDes = part.RefDes, Target = dfqbn };
                                    pkg.Firmware.Add(fw);
                                    return fw;
                                },
                                Dirty, BuiltinSource(part.Parameters)));
                        }
                    }
                }
            }

            return list;
        }

        /// <summary>The source of a board's built-in program ("builtin:servo-sweep.hex" → servo-sweep.ino), if there is one.</summary>
        private static string? BuiltinSource(IDictionary<string, string> parameters)
        {
            if (!parameters.TryGetValue("firmware", out var fw) || !fw.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase)) return null;
            string ino = Path.Combine(AppContext.BaseDirectory, "Firmware", Path.ChangeExtension(fw.Substring(8), ".ino"));
            return File.Exists(ino) ? File.ReadAllText(ino) : null;
        }

        private void Refresh()
        {
            FlushDisk();
            string? keepTarget = _target?.Key;
            string? keepFolder = (_folder as BoardCode)?.Target.Key ?? (_folder as DiskCode)?.Folder;
            string? keepFile = _file;
            _targets.Clear();
            _targets.AddRange(FindTargets());
            _folders.Clear();
            _folders.AddRange(_targets.Select(t => new BoardCode(t)));
            foreach (var dir in DiskFolders()) _folders.Add(new DiskCode(dir));

            _loading = true;
            TargetBox.ItemsSource = _targets.Count > 0 ? _targets : (System.Collections.IEnumerable)new[] { "(programlanabilir kart yok: devreye ya da dünyaya Uno, Nano, ESP32 ekleyin)" };
            _target = _targets.FirstOrDefault(t => t.Key == keepTarget) ?? _targets.FirstOrDefault();
            TargetBox.SelectedItem = (object?)_target ?? TargetBox.Items[0];
            _loading = false;

            var folder = _folders.FirstOrDefault(f => (f as BoardCode)?.Target.Key == keepFolder || (f as DiskCode)?.Folder == keepFolder)
                         ?? _folders.FirstOrDefault(f => f is BoardCode b && b.Target == _target) ?? _folders.FirstOrDefault();
            _folder = folder;
            BuildTree();
            Open(folder, folder != null && keepFile != null && folder.Files.Contains(keepFile) ? keepFile : folder?.MainFile);
        }

        /// <summary>Folders shown for this run only (snapshots), never written to the settings.</summary>
        internal static readonly List<string> SessionFolders = new List<string>();

        /// <summary>The workspace's folders and the ones opened from elsewhere.</summary>
        private static IEnumerable<string> DiskFolders()
        {
            foreach (var d in SessionFolders) yield return d;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(WorkspaceRoot))
                foreach (var d in Directory.GetDirectories(WorkspaceRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                    if (seen.Add(Path.GetFullPath(d))) yield return d;
            foreach (var d in AppSettings.Current.CodeFolders)
                if (Directory.Exists(d) && seen.Add(Path.GetFullPath(d))) yield return d;
        }

        // ================================================================ explorer

        private void BuildTree()
        {
            _loading = true;
            Explorer.Items.Clear();
            var boards = Group("KARTLAR", "Projedeki ve açık dünyadaki her programlanabilir kart; her birinin kodu kendi klasöründe");
            foreach (var f in _folders.OfType<BoardCode>()) boards.Items.Add(FolderNode(f));
            if (boards.Items.Count == 0) boards.Items.Add(new TreeViewItem { Header = Muted("(kart yok)"), Focusable = false });
            var disk = Group("KOD KLASÖRLERİM", "Diskteki klasörler: " + WorkspaceRoot + " ve açtığınız diğer klasörler");
            foreach (var f in _folders.OfType<DiskCode>()) disk.Items.Add(FolderNode(f));
            if (disk.Items.Count == 0) disk.Items.Add(new TreeViewItem { Header = Muted("(Yeni klasör ya da Klasör aç)"), Focusable = false });
            Explorer.Items.Add(boards);
            Explorer.Items.Add(disk);
            _loading = false;
        }

        private TreeViewItem Group(string title, string tip) =>
            new TreeViewItem { Header = new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("B.TextMuted") }, IsExpanded = true, ToolTip = tip, Focusable = false };

        private TextBlock Muted(string text) => new TextBlock { Text = text, Foreground = (Brush)FindResource("B.TextMuted"), FontSize = 12 };

        private sealed class Node
        {
            public Node(CodeFolder folder, string? path, bool isDir)
            {
                Folder = folder;
                Path = path;
                IsDir = isDir;
            }

            public CodeFolder Folder { get; }

            /// <summary>Null for the folder itself.</summary>
            public string? Path { get; }

            public bool IsDir { get; }
        }

        private TreeViewItem FolderNode(CodeFolder f)
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new TextBlock { Text = f is BoardCode ? "▣ " : "▤ ", Foreground = (Brush)FindResource(f is BoardCode ? "B.Accent" : "B.TextSecondary") });
            head.Children.Add(new TextBlock { Text = f.Title, Foreground = (Brush)FindResource("B.Text") });
            head.Children.Add(new TextBlock { Text = "  " + (f is BoardCode b ? b.Subtitle : string.Empty), Foreground = (Brush)FindResource("B.TextMuted"), FontSize = 11.5 });
            var item = new TreeViewItem { Header = head, Tag = new Node(f, null, true), ToolTip = f.Subtitle, IsExpanded = f == _folder };
            // Files and folders as a tree.
            var dirs = new Dictionary<string, TreeViewItem>(StringComparer.Ordinal) { [string.Empty] = item };
            TreeViewItem Dir(string path)
            {
                if (dirs.TryGetValue(path, out var d)) return d;
                int slash = path.LastIndexOf('/');
                var parent = Dir(slash < 0 ? string.Empty : path.Substring(0, slash));
                d = new TreeViewItem { Header = Label("📁 ", path.Substring(slash + 1)), Tag = new Node(f, path, true), IsExpanded = true };
                parent.Items.Add(d);
                dirs[path] = d;
                return d;
            }

            IReadOnlyList<string> files;
            try
            {
                files = f.Files;
                foreach (var empty in f.EmptyFolders) Dir(empty);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                files = Array.Empty<string>();
            }

            foreach (var path in files)
            {
                int slash = path.LastIndexOf('/');
                var parent = Dir(slash < 0 ? string.Empty : path.Substring(0, slash));
                string name = path.Substring(slash + 1);
                var leaf = new TreeViewItem { Header = Label(path == f.MainFile ? "★ " : "   ", name), Tag = new Node(f, path, false), ToolTip = path == f.MainFile ? "Ana dosya (setup / loop)" : path };
                if (f == _folder && path == _file) leaf.IsSelected = true;
                parent.Items.Add(leaf);
            }

            return item;
        }

        private StackPanel Label(string mark, string text)
        {
            var s = new StackPanel { Orientation = Orientation.Horizontal };
            s.Children.Add(new TextBlock { Text = mark, Foreground = (Brush)FindResource("B.TextMuted"), FontSize = 11 });
            s.Children.Add(new TextBlock { Text = text, Foreground = (Brush)FindResource("B.Text") });
            return s;
        }

        private void OnExplorerPick(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_loading || Explorer.SelectedItem is not TreeViewItem { Tag: Node n }) return;
            if (n.IsDir)
            {
                if (n.Folder != _folder) Open(n.Folder, n.Folder.MainFile);
                return;
            }

            if (n.Folder != _folder || n.Path != _file) Open(n.Folder, n.Path);
        }

        /// <summary>Shows a file of a folder in the editor (a board's folder also picks that board to upload to).</summary>
        private void Open(CodeFolder? folder, string? file, bool rebuild = false)
        {
            FlushDisk();
            _folder = folder;
            _file = file;
            NoBoard.Visibility = folder == null ? Visibility.Visible : Visibility.Collapsed;
            if (folder?.Board is { } board && board != _target)
            {
                _loading = true;
                _target = board;
                TargetBox.SelectedItem = board;
                _loading = false;
                LibrariesChanged();
            }

            _loading = true;
            try
            {
                Editor.Text = folder != null && file != null ? folder.Read(file) : string.Empty;
                Editor.IsReadOnly = folder == null || file == null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Editor.Text = "// okunamadı: " + ex.Message;
            }
            finally
            {
                _loading = false;
            }

            ShowDiagnostics();
            FileTitle.Text = folder == null ? string.Empty : folder.Title + "  ›  " + (file ?? string.Empty) + "     " + folder.Subtitle;
            UpdateTargetText();
            UpdateBuildState();
            if (rebuild) BuildTree();
            UpdateDebug();   // [debug] this file's breakpoints, the stopped line
        }

        private void UpdateTargetText()
        {
            string fqbn = Fqbn;
            TargetText.Text = ArduinoCompiler.IsEsp32(fqbn)
                ? "ESP32 · MSVC ile yerel derleme · Arduino-ESP32 API (GPIO, ADC, LEDC, I²C, SPI, FreeRTOS, Wi-Fi ve BLE taklidi)"
                : $"{fqbn} · avr-gcc (arduino-cli) · ATmega328P @ 16 MHz emülasyonu";
        }

        private void OnTargetChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || TargetBox.SelectedItem is not CodeTarget t) return;
            _target = t;
            LibrariesChanged();
            UpdateTargetText();
            UpdateBuildState();
            // A board's own folder follows the chosen board; a disk folder stays (it will be uploaded to this board).
            if (_folder is BoardCode)
            {
                var own = _folders.FirstOrDefault(f => f.Board == t);
                Open(own, own?.MainFile, rebuild: true);
            }
        }

        private void OnTextChanged(object? sender, EventArgs e)
        {
            if (_loading || _folder == null || _file == null) return;
            if (_folder is DiskCode)
            {
                _saveTimer.Stop();
                _saveTimer.Start();   // written to disk a moment after typing stops
            }
            else
            {
                _folder.Write(_file, Editor.Text);
            }

            UpdateBuildState();
        }

        private void FlushDisk()
        {
            _saveTimer.Stop();
            if (_loading || _folder is not DiskCode || _file == null) return;
            try
            {
                _folder.Write(_file, Editor.Text);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                W.Status = "Kaydedilemedi: " + ex.Message;
            }
        }

        private void UpdateBuildState()
        {
            var fw = _folder?.Board?.Firmware;
            if (_folder is DiskCode)
            {
                BuildStatus.Text = _target != null ? $"Klasör · yüklenecek kart: {_target.Title}" : "Klasör · yüklemek için bir kart seçin";
                BuildStatus.Foreground = (Brush)FindResource("B.TextMuted");
            }
            else if (fw?.Hex == null)
            {
                BuildStatus.Text = "Henüz derlenmedi";
                BuildStatus.Foreground = (Brush)FindResource("B.TextMuted");
            }
            else if (!fw.IsBuildCurrent)
            {
                BuildStatus.Text = "Kod değişti · yeniden derleyin";
                BuildStatus.Foreground = (Brush)FindResource("B.Warning");
            }
            else
            {
                BuildStatus.Text = $"Derlendi · {fw.BuiltAt?.ToLocalTime():HH:mm:ss}";
                BuildStatus.Foreground = (Brush)FindResource("B.Success");
            }
        }

        private void OnTemplate(object sender, SelectionChangedEventArgs e)
        {
            if (TemplateBox.SelectedIndex <= 0) return;
            var (name, source) = SketchTemplates.All[TemplateBox.SelectedIndex - 1];
            TemplateBox.SelectedIndex = 0;
            if (_folder == null || _file == null) return;
            if (Editor.Text.Trim().Length > 0 && Editor.Text != source)
            {
                int answer = Dialog.Show(Window.GetWindow(this), "Şablonu yükle", $"'{name}' şablonu {_file} dosyasının yerine geçsin mi?", "Değiştir", "Vazgeç");
                if (answer != 0) return;
            }

            Editor.Text = source;
            OnTextChanged(this, EventArgs.Empty);
        }

        private void OnAddUno(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.PlacePart("arduino-uno");

        // ================================================================ files and folders

        private Window? OwnerWindow => Window.GetWindow(this);

        /// <summary>The folder the explorer's selection is in (and the sub-folder, for new files).</summary>
        private (CodeFolder? Folder, string Dir) Selection()
        {
            if (Explorer.SelectedItem is TreeViewItem { Tag: Node n })
            {
                string dir = n.IsDir ? n.Path ?? string.Empty : n.Path!.Contains('/') ? n.Path.Substring(0, n.Path.LastIndexOf('/')) : string.Empty;
                return (n.Folder, dir);
            }

            return (_folder, string.Empty);
        }

        private void OnNewFile(object sender, RoutedEventArgs e)
        {
            var (folder, dir) = Selection();
            if (folder == null)
            {
                OnNewFolder(sender, e);
                return;
            }

            string? name = Dialog.Prompt(OwnerWindow!, "Yeni dosya", "Dosya adı (.ino, .h, .cpp, .c …). Alt klasör için yol yazın: src/motor.cpp", "yeni.h");
            if (name == null) return;
            string path = CodeFolder.Normalize((dir.Length > 0 ? dir + "/" : string.Empty) + name);
            if (folder.Files.Contains(path))
            {
                W.Status = path + " zaten var";
                return;
            }

            string ext = Path.GetExtension(path).ToLowerInvariant();
            string content = ext is ".h" or ".hpp" ? "#pragma once\n\n#include <Arduino.h>\n\n" : ext is ".cpp" or ".c" ? "#include <Arduino.h>\n\n" : string.Empty;
            folder.Write(path, content);
            Open(folder, path, rebuild: true);
        }

        private void OnNewFolder(object sender, RoutedEventArgs e)
        {
            var (folder, dir) = Selection();
            // Inside a folder: a sub-folder. Elsewhere: a new code folder on disk.
            if (folder != null && Explorer.SelectedItem is TreeViewItem { Tag: Node { IsDir: true } })
            {
                string? sub = Dialog.Prompt(OwnerWindow!, "Yeni alt klasör", $"{folder.Title} içinde klasör adı (ör. src, lib, config):", "src");
                if (sub == null) return;
                folder.AddFolder(CodeFolder.Normalize((dir.Length > 0 ? dir + "/" : string.Empty) + sub));
                BuildTree();
                return;
            }

            string? name = Dialog.Prompt(OwnerWindow!, "Yeni kod klasörü", "Klasörün adı (" + WorkspaceRoot + " içinde açılır):", "yeni_cizim");
            if (name == null) return;
            string path = Path.Combine(WorkspaceRoot, string.Concat(name.Split(Path.GetInvalidFileNameChars())));
            if (Directory.Exists(path))
            {
                W.Status = "Bu adla bir klasör zaten var";
                return;
            }

            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, Path.GetFileName(path) + ".ino"), SketchTemplates.All[0].Source);
            Refresh();
            var made = _folders.FirstOrDefault(f => f is DiskCode d && string.Equals(d.Folder, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
            if (made != null) Open(made, made.MainFile, rebuild: true);
        }

        private void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Kod klasörü aç (Arduino çizim klasörü ya da herhangi bir kod klasörü)" };
            if (dlg.ShowDialog(OwnerWindow) != true) return;
            string dir = Path.GetFullPath(dlg.FolderName);
            if (!AppSettings.Current.CodeFolders.Contains(dir, StringComparer.OrdinalIgnoreCase) && !dir.StartsWith(Path.GetFullPath(WorkspaceRoot), StringComparison.OrdinalIgnoreCase))
            {
                AppSettings.Current.CodeFolders.Add(dir);
                AppSettings.Current.Save();
            }

            Refresh();
            var f = _folders.FirstOrDefault(x => x is DiskCode d && string.Equals(d.Folder, dir, StringComparison.OrdinalIgnoreCase));
            if (f != null) Open(f, f.MainFile, rebuild: true);
            W.Status = "Açıldı: " + dir;
        }

        private void ImportFiles(CodeFolder folder, string dir, IEnumerable<string> paths)
        {
            int n = 0;
            foreach (var src in paths)
            {
                if (Directory.Exists(src))
                {
                    // A whole folder: its code files under a folder of its name.
                    foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories).Where(CodeFolder.IsCode))
                    {
                        string rel = CodeFolder.Normalize((dir.Length > 0 ? dir + "/" : string.Empty) + Path.GetFileName(src) + "/" + Path.GetRelativePath(src, f));
                        folder.Write(rel, File.ReadAllText(f));
                        n++;
                    }
                }
                else if (File.Exists(src) && CodeFolder.IsCode(src))
                {
                    folder.Write(CodeFolder.Normalize((dir.Length > 0 ? dir + "/" : string.Empty) + Path.GetFileName(src)), File.ReadAllText(src));
                    n++;
                }
            }

            W.Status = $"{n} dosya içe aktarıldı: {folder.Title}";
            Open(folder, _file != null && folder == _folder ? _file : folder.MainFile, rebuild: true);
        }

        private void OnExplorerDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
            var (folder, dir) = Selection();
            if (folder == null) return;
            ImportFiles(folder, dir, paths);
        }

        private void OnMore(object sender, RoutedEventArgs e)
        {
            var m = ExplorerMenu(Explorer.SelectedItem as TreeViewItem);
            m.PlacementTarget = MoreButton;
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            m.IsOpen = true;
        }

        private void OnExplorerMenu(object sender, MouseButtonEventArgs e)
        {
            var item = e.OriginalSource is DependencyObject d ? FindItem(d) : null;
            if (item != null) item.IsSelected = true;
            ExplorerMenu(item).IsOpen = true;
            e.Handled = true;
        }

        private static TreeViewItem? FindItem(DependencyObject? d)
        {
            while (d != null && d is not TreeViewItem) d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as TreeViewItem;
        }

        private ContextMenu ExplorerMenu(TreeViewItem? item)
        {
            var m = new ContextMenu();
            void Add(string text, Action a, bool enabled = true)
            {
                var mi = new MenuItem { Header = text, IsEnabled = enabled };
                mi.Click += (_, _) => a();
                m.Items.Add(mi);
            }

            var node = item?.Tag as Node;
            var folder = node?.Folder ?? _folder;
            Add("Yeni dosya…", () => OnNewFile(this, new RoutedEventArgs()), folder != null);
            Add("Yeni klasör…", () => OnNewFolder(this, new RoutedEventArgs()));
            Add("Dosya içe aktar…", () =>
            {
                var dlg = new OpenFileDialog { Multiselect = true, Filter = "Kod dosyaları|*.ino;*.h;*.hpp;*.c;*.cpp;*.cc;*.txt;*.md;*.json|Tüm dosyalar|*.*" };
                if (dlg.ShowDialog(OwnerWindow) == true) ImportFiles(folder!, Selection().Dir, dlg.FileNames);
            }, folder != null);
            Add("Klasör içe aktar…", () =>
            {
                var dlg = new OpenFolderDialog { Title = "Bu klasöre eklenecek klasör" };
                if (dlg.ShowDialog(OwnerWindow) == true) ImportFiles(folder!, Selection().Dir, new[] { dlg.FolderName });
            }, folder != null);
            Add("Diskten klasör aç…", () => OnOpenFolder(this, new RoutedEventArgs()));
            m.Items.Add(new Separator());
            if (folder != null)
            {
                var upload = new MenuItem { Header = "Karta yükle" };
                foreach (var t in _targets)
                {
                    var target = t;
                    var mi = new MenuItem { Header = t.ToString() };
                    mi.Click += (_, _) => UploadTo(folder, target);
                    upload.Items.Add(mi);
                }

                if (upload.Items.Count == 0) upload.Items.Add(new MenuItem { Header = "(programlanabilir kart yok)", IsEnabled = false });
                m.Items.Add(upload);
                Add("Diske kaydet (Arduino çizim klasörü olarak)…", () =>
                {
                    var dlg = new OpenFolderDialog { Title = "Kaydedilecek yer (içinde '" + SafeName(folder.Title) + "' klasörü açılır)" };
                    if (dlg.ShowDialog(OwnerWindow) != true) return;
                    FlushDisk();
                    string target = Path.Combine(dlg.FolderName, SafeName(folder.Title));
                    folder.SaveTo(target);
                    W.Status = "Kaydedildi: " + target;
                });
                Add("Zip olarak dışa aktar…", () =>
                {
                    var dlg = new SaveFileDialog { Filter = "Zip arşivi (*.zip)|*.zip", FileName = SafeName(folder.Title) + ".zip" };
                    if (dlg.ShowDialog(OwnerWindow) != true) return;
                    FlushDisk();
                    folder.ZipTo(dlg.FileName, SafeName(folder.Title));
                    W.Status = "Dışa aktarıldı: " + dlg.FileName;
                });
                if (folder is DiskCode dc)
                    Add("Klasörü Explorer'da göster", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + dc.Folder + "\"") { UseShellExecute = true }));
            }

            if (node?.Path != null)
            {
                m.Items.Add(new Separator());
                Add("Yeniden adlandır…", () =>
                {
                    string old = node.Path!;
                    string? name = Dialog.Prompt(OwnerWindow!, "Yeniden adlandır", old + " için yeni ad:", Path.GetFileName(old));
                    if (name == null) return;
                    string to = CodeFolder.Normalize((old.Contains('/') ? old.Substring(0, old.LastIndexOf('/') + 1) : string.Empty) + name);
                    FlushDisk();
                    node.Folder.Rename(old, to);
                    Open(node.Folder, _file == old ? to : _file, rebuild: true);
                });
                Add("Sil", () =>
                {
                    if (Dialog.Show(OwnerWindow!, "Sil", $"{node.Path} silinsin mi?" + (node.Folder is DiskCode ? " Diskten silinir." : string.Empty), "Sil", "Vazgeç") != 0) return;
                    node.Folder.Delete(node.Path!);
                    bool gone = _file == node.Path || (_file?.StartsWith(node.Path + "/", StringComparison.Ordinal) ?? false);
                    Open(node.Folder, gone ? node.Folder.MainFile : _file, rebuild: true);
                }, node.Path != node.Folder.MainFile);
            }
            else if (folder is DiskCode disk && node != null)
            {
                m.Items.Add(new Separator());
                bool external = AppSettings.Current.CodeFolders.Contains(disk.Folder, StringComparer.OrdinalIgnoreCase);
                Add(external ? "Listeden kaldır (diskte kalır)" : "Klasörü sil…", () =>
                {
                    if (external)
                    {
                        AppSettings.Current.CodeFolders.RemoveAll(x => string.Equals(x, disk.Folder, StringComparison.OrdinalIgnoreCase));
                        AppSettings.Current.Save();
                    }
                    else
                    {
                        if (Dialog.Show(OwnerWindow!, "Klasörü sil", $"{disk.Folder} ve içindeki her şey silinsin mi?", "Sil", "Vazgeç") != 0) return;
                        Directory.Delete(disk.Folder, true);
                    }

                    if (_folder == disk) _folder = null;
                    Refresh();
                });
            }

            return m;
        }

        private static string SafeName(string s) => string.Concat(s.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_').Replace('·', '_');

        // ================================================================ build / upload

        private async System.Threading.Tasks.Task<bool> Build(CodeFolder folder, CodeTarget? target)
        {
            FlushDisk();
            string fqbn = target?.Fqbn ?? Fqbn;
            _build?.Cancel();
            _build = new CancellationTokenSource();
            var token = _build.Token;
            var fw = folder.ToFirmware(fqbn);
            string bundle = fw.Bundle();
            BuildButton.IsEnabled = UploadButton.IsEnabled = false;
            BuildStatus.Text = "Derleniyor…";
            BuildStatus.Foreground = (Brush)FindResource("B.Accent");
            BottomTabs.SelectedIndex = 0;
            BuildLog.Text = ArduinoCompiler.IsEsp32(fqbn) ? "ESP32 çizimi MSVC ile derleniyor (ilk derleme HAL'ı da kurar) …\n" : "arduino-cli compile --fqbn " + fqbn + " …\n";
            BuildResult result;
            try
            {
                string key = SafeName(folder.Title) + "-" + (target?.Key ?? "klasor").Replace(':', '_');
                result = await ArduinoCompiler.CompileAsync(bundle, fqbn, key, token);
            }
            finally
            {
                BuildButton.IsEnabled = UploadButton.IsEnabled = true;
            }

            if (token.IsCancellationRequested) return false;
            BuildLog.Text = result.Log;
            _diagnostics = result.Diagnostics.Select(d => d.File == "sketch.ino" ? new CodeDiagnostic(d.Line, d.Column, d.IsError, d.Message, folder.MainFile) : d).ToList();
            ShowDiagnostics();
            Problems.ItemsSource = _diagnostics.Count > 0 ? _diagnostics : (System.Collections.IEnumerable)new[] { result.Success ? "Hata yok." : "Derleme başarısız (günlüğe bakın)." };
            if (result.Success && result.Hex != null)
            {
                DebugSymbols.Remember(result.Hex, result.Elf);   // [debug] lines and variables of this build
                if (target != null && folder.Board == target)
                {
                    var tf = target.EnsureFirmware();
                    if (tf.Source.Length == 0) tf.Source = folder.Read(folder.MainFile);   // a board still on its built-in program's text
                    tf.Hex = result.Hex;
                    tf.BuiltAt = DateTime.UtcNow;
                    tf.BuiltFromHash = FirmwareSource.HashOf(tf.Bundle());
                    tf.Target = fqbn;
                    target.Changed($"Derlendi: {target.Title} · {result.FlashBytes} bayt flash, {result.RamBytes} bayt RAM · {result.Duration.TotalSeconds:F1} s");
                }

                BuildStatus.Text = result.FlashBytes > 0
                    ? $"Derlendi · flash {result.FlashBytes}/{result.FlashMax} B ({100.0 * result.FlashBytes / Math.Max(1, result.FlashMax):F0}%) · RAM {result.RamBytes}/{result.RamMax} B · {result.Duration.TotalSeconds:F1} s"
                    : $"Derlendi · {result.Duration.TotalSeconds:F1} s";
                BuildStatus.Foreground = (Brush)FindResource(result.RamMax > 0 && result.RamBytes > 0.85 * result.RamMax ? "B.Warning" : "B.Success");
                return true;
            }

            int errors = _diagnostics.Count(d => d.IsError);
            BuildStatus.Text = errors > 0 ? $"{errors} hata" : "Derleme başarısız";
            BuildStatus.Foreground = (Brush)FindResource("B.Danger");
            // [code] A library the sketch includes is not installed: open the library tab on it.
            if (_diagnostics.Select(d => MissingHeader(d.Message)).FirstOrDefault(h => h != null) is { } missing && !ArduinoCompiler.IsEsp32(fqbn))
            {
                BuildStatus.Text = $"{missing} kurulu değil · Kütüphaneler sekmesinden kurun";
                FindLibraryFor(missing);
                return false;
            }

            var first = _diagnostics.FirstOrDefault(d => d.IsError);
            if (first != null) GoTo(first);
            return false;
        }

        private void ShowDiagnostics() => Editor.Diagnostics = _diagnostics.Where(d => d.File == _file).ToList();

        private void GoTo(CodeDiagnostic d)
        {
            if (_folder == null) return;
            if (d.File != _file && _folder.Files.Contains(d.File)) Open(_folder, d.File, rebuild: true);
            Editor.GoTo(d.Line, Math.Max(1, d.Column));
        }

        private async void OnBuild(object sender, RoutedEventArgs e)
        {
            if (_folder != null) await Build(_folder, _folder.Board ?? _target);
        }

        private void OnUpload(object sender, RoutedEventArgs e)
        {
            if (_folder == null) return;
            if (_target == null)
            {
                W.Status = "Yüklenecek kart yok: devreye ya da dünyaya Arduino Uno, Nano ya da ESP32 ekleyin.";
                return;
            }

            UploadTo(_folder, _target);
        }

        /// <summary>
        /// Writes a folder's code to a board (copying it into the board's own folder when it is another folder), builds it
        /// and runs it: the project's circuit for a project board, the world for a board in the world.
        /// </summary>
        private async void UploadTo(CodeFolder folder, CodeTarget target)
        {
            var boardCode = _folders.OfType<BoardCode>().FirstOrDefault(b => b.Target == target) ?? new BoardCode(target);
            if (folder != boardCode)
            {
                var existing = target.Firmware;
                if (existing != null && existing.Source.Trim().Length > 0 && existing.Bundle() != folder.ToFirmware(target.Fqbn).Bundle()
                    && Dialog.Show(OwnerWindow!, "Karta yükle", $"{target} üzerindeki kod, '{folder.Title}' klasöründekiyle değiştirilsin mi?", "Değiştir ve yükle", "Vazgeç") != 0) return;
                FlushDisk();
                boardCode.ReplaceWith(folder);
            }

            if (!await Build(boardCode, target)) return;
            if (!target.InWorld)
            {
                // The project's board runs the project's code.
                var part = W.Schematic.Parts.FirstOrDefault(p => "p:" + p.RefDes == target.Key);
                if (part != null && (!part.Parameters.TryGetValue("firmware", out var current) || current != "project"))
                {
                    W.BeginChange();
                    part.Parameters["firmware"] = "project";
                    W.CommitChange($"{part.RefDes} artık projedeki kodu çalıştırıyor");
                }

                var sim = W.Simulation;
                sim.Stop();
                if (sim.Start())
                {
                    _serialShown = 0;
                    SerialView.Clear();
                    BottomTabs.SelectedItem = SerialTab;
                    W.Status = $"{target.Title}: kod yüklendi, simülasyon çalışıyor";
                }
                else
                {
                    W.Status = "Simülasyon başlatılamadı: " + sim.StatusText;
                }
            }
            else
            {
                W.Status = $"{target.Title} ({target.Where}): kod yüklendi; dünya baştan çalışıyor";
                (Window.GetWindow(this) as MainWindow)?.RunWorld();
            }

            Uploaded?.Invoke(target);   // [guide]
            Refresh();
        }

        private void OnStop(object sender, RoutedEventArgs e) => W.Simulation.Stop();

        private void OnReset(object sender, RoutedEventArgs e)
        {
            if (_target != null && !_target.InWorld) W.Simulation.ResetBoard(_target.Key.Substring(2));
        }

        private void OnProblemPick(object sender, EventArgs e)
        {
            if (Problems.SelectedItem is not CodeDiagnostic d) return;
            GoTo(d);
            if (MissingHeader(d.Message) is { } header && e is MouseButtonEventArgs) FindLibraryFor(header);
        }

        private void OnKeys(object sender, KeyEventArgs e)
        {
            if (DebugKeys(e))   // [debug] F5 / F10 / F11 / Shift+F11 while the board is stopped
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F7)
            {
                OnBuild(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.F5)
            {
                OnUpload(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && _folder is DiskCode d)
            {
                FlushDisk();
                W.Status = "Kaydedildi: " + Path.Combine(d.Folder, _file ?? string.Empty);
                e.Handled = true;
            }
        }

        // ================================================================ serial monitor

        private void UpdateSerial()
        {
            long total = W.Simulation.SerialTotal;
            if (total < _serialShown) _serialShown = 0;
            if (total == _serialShown) return;
            string text = W.Simulation.SerialText;
            long fresh = total - _serialShown;
            if (_serialShown == 0 || fresh >= text.Length) SerialView.Text = text;
            else SerialView.AppendText(text.Substring(text.Length - (int)fresh));
            _serialShown = total;
            if (SerialView.Text.Length > 60000) SerialView.Text = SerialView.Text.Substring(SerialView.Text.Length - 40000);
            SerialView.ScrollToEnd();
        }

        private void OnSerialSend(object sender, RoutedEventArgs e)
        {
            if (SerialInput.Text.Length == 0) return;
            W.Simulation.SendSerial(SerialInput.Text + "\n");
            SerialInput.Clear();
        }

        private void OnSerialKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) OnSerialSend(sender, e);
        }
    }
}
