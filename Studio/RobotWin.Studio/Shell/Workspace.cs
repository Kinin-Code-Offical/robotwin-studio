using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Shell
{
    public enum DocumentKind
    {
        /// <summary>A circuit project: schematic, PCB, firmware (a .rwproj, or a board embedded in a world).</summary>
        Design,

        /// <summary>An open world with boards, parts, bodies, cables and joints (.rwworld).</summary>
        World
    }

    /// <summary>One open tab.</summary>
    public sealed class OpenDocument : INotifyPropertyChanged
    {
        private string? _filePath;
        private bool _isDirty;
        private bool _isActive;

        public OpenDocument(DocumentKind kind)
        {
            Kind = kind;
        }

        public DocumentKind Kind { get; }

        // ---- design state (while the tab is not active; the workspace holds the live copy)
        public ProjectPackage Package { get; set; } = new ProjectPackage();
        internal Stack<string> Undo { get; } = new Stack<string>();
        internal Stack<string> Redo { get; } = new Stack<string>();

        /// <summary>For a board being edited out of a world: the world tab and the design id.</summary>
        public OpenDocument? OwnerWorld { get; set; }
        public string? DesignId { get; set; }

        // ---- world state
        public WorldDocument? World { get; set; }

        /// <summary>
        /// [assembly] For a project's Montaj: the project's tab. The Montaj is part of that project: it is saved inside
        /// the project file, Ctrl+S saves the project, and its changes make the project unsaved.
        /// </summary>
        public OpenDocument? OwnerProject { get; set; }

        public bool IsMontaj => Kind == DocumentKind.World && OwnerProject != null;
        internal Stack<string> WorldUndo { get; } = new Stack<string>();
        internal Stack<string> WorldRedo { get; } = new Stack<string>();

        public string? FilePath
        {
            get => _filePath;
            set
            {
                _filePath = value;
                Changed();
            }
        }

        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (_isDirty == value) return;
                _isDirty = value;
                Changed();
            }
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value) return;
                _isActive = value;
                Changed(nameof(IsActive));
            }
        }

        public bool IsEmbedded => OwnerWorld != null;

        /// <summary>
        /// The document's name: a saved file's own name (what the user named it, whatever title the file carries inside),
        /// otherwise its title; a board opened out of a world says which world.
        /// </summary>
        public string BaseTitle
        {
            get
            {
                string t = !IsEmbedded && !IsMontaj && !string.IsNullOrEmpty(FilePath) ? Path.GetFileNameWithoutExtension(FilePath)
                    : Kind == DocumentKind.World ? World?.Title ?? "Dünya" : Package.Schematic.Title;
                if (string.IsNullOrWhiteSpace(t)) t = "Adsız";
                return IsEmbedded ? t + " (" + (OwnerWorld!.World?.Title ?? "dünya") + ")" : t;
            }
        }

        public string Title => BaseTitle + (IsDirty ? " •" : string.Empty);

        private bool _isPinned;
        private string _titleSuffix = string.Empty;

        /// <summary>
        /// [tabs] A pinned tab stays at the left of the strip, is left open by "close others / to the right / all" and
        /// by a middle click, and a saved file stays pinned the next time it is opened.
        /// </summary>
        public bool IsPinned
        {
            get => _isPinned;
            set
            {
                if (_isPinned == value) return;
                _isPinned = value;
                Changed();
            }
        }

        /// <summary>[tabs] What tells two tabs of the same name apart: the folder of a saved file, a number for a new one.</summary>
        public string TitleSuffix
        {
            get => _titleSuffix;
            internal set
            {
                if (_titleSuffix == value) return;
                _titleSuffix = value;
                Changed();
            }
        }

        /// <summary>[tabs] The name on the tab (the unsaved state is a dot of its own, not part of the name).</summary>
        public string TabTitle => BaseTitle + TitleSuffix;

        /// <summary>[tabs] The tab's tooltip: name, kind, file and state.</summary>
        public string TabToolTip
        {
            get
            {
                var sb = new System.Text.StringBuilder(BaseTitle).Append(" · ").Append(KindText);
                if (IsMontaj && OwnerProject != null) sb.Append(" (").Append(OwnerProject.BaseTitle).Append(" projesinin içinde)");
                sb.Append('\n').Append(!string.IsNullOrEmpty(FilePath) ? FilePath : IsEmbedded ? "Dünyanın içinde (dünyayla kaydedilir)" : IsMontaj ? "Projesiyle birlikte kaydedilir" : "Henüz kaydedilmedi");
                if (IsDirty) sb.Append('\n').Append("Kaydedilmemiş değişiklikler var");
                if (IsPinned) sb.Append('\n').Append("Sabitlenmiş");
                return sb.ToString();
            }
        }

        public string KindText => IsMontaj ? "Montaj" : Kind == DocumentKind.World ? "Dünya" : IsEmbedded ? "Dünyadaki kart" : "Devre";

        /// <summary>True for a fresh, untouched "new circuit" tab that can be replaced by an opened file.</summary>
        internal bool IsPristine => Kind == DocumentKind.Design && !IsDirty && FilePath == null && !IsEmbedded &&
                                    Package.Schematic.Parts.Count == 0 && Package.Schematic.Wires.Count == 0;

        public event PropertyChangedEventHandler? PropertyChanged;

        internal void Changed([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TabTitle)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TabToolTip)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BaseTitle)));
        }
    }

    /// <summary>
    /// Application-wide state: the open tabs (circuit projects and worlds), the active
    /// circuit's undo history and the running simulation. Pages observe it instead of
    /// talking to each other. The circuit pages (schematic, PCB, code, assembly) always
    /// work on <see cref="Schematic"/>/<see cref="Project"/> — the active (or last active)
    /// circuit tab; the world page works on <see cref="ActiveWorld"/>.
    /// </summary>
    public sealed class Workspace : INotifyPropertyChanged
    {
        private const int MaxUndo = 200;
        private Stack<string> _undo;
        private Stack<string> _redo;
        private Schematic _schematic = new Schematic();
        private string _status = "Hazır";
        private OpenDocument _design;
        private OpenDocument _active;

        public static Workspace Current { get; } = new Workspace();

        private Workspace()
        {
            Simulation = new SimulationSession(this);
            _design = new OpenDocument(DocumentKind.Design) { Package = Project };
            _design.Package.Schematic = _schematic;
            _undo = _design.Undo;
            _redo = _design.Redo;
            _active = _design;
            _design.IsActive = true;
            Documents.Add(_design);
            Documents.CollectionChanged += OnTabsChanged;   // [tabs]
        }

        // ================================================================ [tabs] names, pins, closed tabs

        private readonly List<string> _closed = new List<string>();

        /// <summary>Files of tabs closed this session, the latest last (Ctrl+Shift+T opens them again).</summary>
        public IReadOnlyList<string> ClosedFiles => _closed;

        /// <summary>The latest closed file that still exists and is not open again (taken off the list), or null.</summary>
        public string? TakeClosedFile()
        {
            for (int i = _closed.Count - 1; i >= 0; i--)
            {
                string p = _closed[i];
                _closed.RemoveAt(i);
                if (File.Exists(p) && FindOpen(p) == null) return p;
            }

            return null;
        }

        private void OnTabsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (OpenDocument d in e.NewItems)
                {
                    d.PropertyChanged -= OnTabPropertyChanged;
                    d.PropertyChanged += OnTabPropertyChanged;
                    if (!string.IsNullOrEmpty(d.FilePath) && IsPinnedFile(d.FilePath)) d.IsPinned = true;
                }
            }

            if (e.OldItems != null)
            {
                foreach (OpenDocument d in e.OldItems)
                {
                    if (Documents.Contains(d)) continue;   // moved
                    d.PropertyChanged -= OnTabPropertyChanged;
                    if (!string.IsNullOrEmpty(d.FilePath) && !d.IsEmbedded)
                    {
                        _closed.Remove(d.FilePath);
                        _closed.Add(d.FilePath);
                    }
                }
            }

            QueueKeepPinnedFirst();
            UpdateTabTitles();
        }

        private bool _naming;

        private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_naming || sender is not OpenDocument d) return;
            if (e.PropertyName == nameof(OpenDocument.FilePath))
            {
                if (!string.IsNullOrEmpty(d.FilePath) && IsPinnedFile(d.FilePath)) d.IsPinned = true;
                UpdateTabTitles();
            }
            else if (e.PropertyName == nameof(OpenDocument.IsPinned))
            {
                RememberPin(d);
                QueueKeepPinnedFirst();
            }
        }

        /// <summary>Two tabs never read the same: a saved file gets its folder's name, a new document a number.</summary>
        internal void UpdateTabTitles()
        {
            _naming = true;
            try
            {
                foreach (var group in Documents.GroupBy(d => d.BaseTitle, StringComparer.CurrentCultureIgnoreCase))
                {
                    var list = group.ToList();
                    int n = 0;
                    foreach (var d in list)
                    {
                        if (list.Count == 1) d.TitleSuffix = string.Empty;
                        else if (!string.IsNullOrEmpty(d.FilePath) && !d.IsEmbedded) d.TitleSuffix = " — " + Path.GetFileName(Path.GetDirectoryName(d.FilePath));
                        else d.TitleSuffix = ++n == 1 ? string.Empty : " (" + n + ")";
                    }
                }
            }
            finally
            {
                _naming = false;
            }
        }

        private bool _orderQueued;

        /// <summary>Puts pinned tabs first once the current change is over (a collection cannot change while it reports one).</summary>
        private void QueueKeepPinnedFirst()
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null)
            {
                KeepPinnedFirst();
                return;
            }

            if (_orderQueued) return;
            _orderQueued = true;
            dispatcher.BeginInvoke(new Action(() =>
            {
                _orderQueued = false;
                KeepPinnedFirst();
            }));
        }

        /// <summary>Pinned tabs first, in their order; the others after them in theirs.</summary>
        private void KeepPinnedFirst()
        {
            int pinned = 0;
            for (int i = 0; i < Documents.Count; i++)
            {
                if (!Documents[i].IsPinned) continue;
                if (i != pinned) Documents.Move(i, pinned);
                pinned++;
            }
        }

        private static bool IsPinnedFile(string path) => AppSettings.Current.PinnedFiles.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));

        private static void RememberPin(OpenDocument d)
        {
            if (string.IsNullOrEmpty(d.FilePath) || d.IsEmbedded) return;
            var list = AppSettings.Current.PinnedFiles;
            int before = list.Count;
            list.RemoveAll(p => string.Equals(p, d.FilePath, StringComparison.OrdinalIgnoreCase));
            if (d.IsPinned) list.Add(d.FilePath);
            if (list.Count != before || d.IsPinned) AppSettings.Current.Save();
        }

        public PartLibrary Library => PartLibrary.Default;

        /// <summary>Everything saved with the project besides the schematic: firmware, local parts, settings.</summary>
        public ProjectPackage Project { get; private set; } = new ProjectPackage();

        /// <summary>Renders a PNG preview of the project for the file (set by the main window).</summary>
        public Func<byte[]?>? PreviewProvider { get; set; }

        /// <summary>Raised when a board's firmware source or build changes.</summary>
        public event EventHandler? FirmwareChanged;

        public SimulationSession Simulation { get; }

        // ================================================================ tabs

        public ObservableCollection<OpenDocument> Documents { get; } = new ObservableCollection<OpenDocument>();

        public OpenDocument ActiveDocument => _active;

        /// <summary>The circuit tab the circuit pages show.</summary>
        public OpenDocument DesignDocument => _design;

        /// <summary>The world of the active tab (null when a circuit tab is active).</summary>
        public WorldDocument? ActiveWorld => _active.Kind == DocumentKind.World ? _active.World : null;

        /// <summary>Raised after another tab became active.</summary>
        public event EventHandler? ActiveDocumentChanged;

        /// <summary>Raised after an edit of the active world (including undo/redo).</summary>
        public event EventHandler? WorldChanged;

        public void Activate(OpenDocument doc)
        {
            if (doc == _active || !Documents.Contains(doc)) return;
            if (doc.Kind == DocumentKind.Design && doc != _design)
            {
                StashDesign();
                RestoreDesign(doc);
            }

            _active.IsActive = false;
            _active = doc;
            doc.IsActive = true;
            OnPropertyChanged(nameof(ActiveDocument));
            OnPropertyChanged(nameof(ActiveWorld));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Closes a tab (the caller asks about unsaved changes first). The last tab is replaced by a new circuit.</summary>
        public void Close(OpenDocument doc)
        {
            if (!Documents.Contains(doc)) return;
            if (doc.Kind == DocumentKind.World)
            {
                foreach (var child in Documents.Where(d => d.OwnerWorld == doc).ToList()) Close(child);
            }

            if (doc.IsMontaj) KeepMontaj(doc);   // [assembly] its work stays in the project (saved with it)
            foreach (var montaj in Documents.Where(d => d.OwnerProject == doc).ToList())
            {
                KeepMontaj(montaj);
                montaj.OwnerProject = null;
                Close(montaj);
            }

            bool wasActive = doc == _active, wasDesign = doc == _design;
            int index = Documents.IndexOf(doc);
            if (wasDesign)
            {
                Simulation.Stop();
                StashDesign();
            }

            Documents.Remove(doc);
            doc.IsActive = false;
            if (wasDesign)
            {
                var nextDesign = Documents.LastOrDefault(d => d.Kind == DocumentKind.Design);
                if (nextDesign == null)
                {
                    nextDesign = new OpenDocument(DocumentKind.Design);
                    nextDesign.Package.Schematic.Title = "Yeni Devre";
                    Documents.Add(nextDesign);
                }

                RestoreDesign(nextDesign);
            }

            if (!wasActive) return;
            var next = Documents[Math.Min(Math.Max(0, index - 1), Documents.Count - 1)];
            if (next.Kind == DocumentKind.Design && next != _design)
            {
                StashDesign();
                RestoreDesign(next);
            }

            _active = next;
            next.IsActive = true;
            OnPropertyChanged(nameof(ActiveDocument));
            OnPropertyChanged(nameof(ActiveWorld));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>[assembly] The open Montaj of a project tab (null when it is not open).</summary>
        public OpenDocument? MontajOf(OpenDocument project) => Documents.FirstOrDefault(d => d.OwnerProject == project);

        /// <summary>[assembly] Writes a Montaj tab into its project (in memory; the project is saved with it).</summary>
        public void KeepMontaj(OpenDocument montaj)
        {
            if (montaj.OwnerProject == null || montaj.World == null) return;
            var owner = montaj.OwnerProject;
            if (owner == _design) FlushActiveDesign();
            owner.Package.Montaj = montaj.World.ToBytes();
            if (montaj.IsDirty)
            {
                owner.IsDirty = true;
                montaj.IsDirty = false;
            }

            OnPropertyChanged(nameof(IsDirty));
        }

        /// <summary>[assembly] Opens a world as a project's Montaj (after loading it from the project or building it).</summary>
        public OpenDocument OpenMontaj(OpenDocument project, WorldDocument world, bool activate = true)
        {
            world.IsAssembly = true;
            world.ProjectId = project.Package.Schematic.EnsureProjectId();
            var doc = new OpenDocument(DocumentKind.World) { World = world, OwnerProject = project };
            Documents.Insert(Math.Min(Documents.Count, Documents.IndexOf(project) + 1), doc);
            if (activate) Activate(doc);
            return doc;
        }

        /// <summary>Copies the live circuit state into its tab.</summary>
        private void StashDesign()
        {
            Project.Schematic = Schematic;
            _design.Package = Project;
            SyncEmbedded(_design);
        }

        private void RestoreDesign(OpenDocument doc)
        {
            Simulation.Stop();
            foreach (var part in doc.Package.Parts)
            {
                try
                {
                    if (Library.Find(part.Id) == null) part.Register(Library);
                }
                catch (InvalidDataException)
                {
                }
            }

            _design = doc;
            Project = doc.Package;
            _undo = doc.Undo;
            _redo = doc.Redo;
            Schematic = doc.Package.Schematic;
            OnPropertyChanged(nameof(DesignDocument));
            DocumentReplaced?.Invoke(this, EventArgs.Empty);
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            FirmwareChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>A board edited out of a world writes its changes back into the world's copy.</summary>
        private static void SyncEmbedded(OpenDocument doc)
        {
            if (doc.OwnerWorld?.World == null || doc.DesignId == null) return;
            var design = doc.OwnerWorld.World.FindDesign(doc.DesignId);
            if (design == null) return;
            design.Package = doc.Package;
            design.Name = string.IsNullOrWhiteSpace(doc.Package.Schematic.Title) ? design.Name : doc.Package.Schematic.Title;
        }

        /// <summary>Makes the live circuit state visible to its tab and, for an embedded board, to its world.</summary>
        public void FlushActiveDesign()
        {
            Project.Schematic = Schematic;
            SyncEmbedded(_design);
        }

        private OpenDocument NewDesignTab()
        {
            if (_design.IsPristine && Documents.Contains(_design))
            {
                Activate(_design);
                return _design;
            }

            var doc = new OpenDocument(DocumentKind.Design);
            Documents.Add(doc);
            StashDesign();
            _active.IsActive = false;
            RestoreDesign(doc);
            _active = doc;
            doc.IsActive = true;
            ActiveDocumentChanged?.Invoke(this, EventArgs.Empty);
            return doc;
        }

        // ================================================================ the active circuit

        public Schematic Schematic
        {
            get => _schematic;
            private set
            {
                _schematic = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Title));
            }
        }

        public string? FilePath => _active.FilePath;

        public bool IsDirty => _active.IsDirty;

        public string Title => _active.Title;

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged();
            }
        }

        public bool CanUndo => _active.Kind == DocumentKind.World ? _active.WorldUndo.Count > 0 : _undo.Count > 0;
        public bool CanRedo => _active.Kind == DocumentKind.World ? _active.WorldRedo.Count > 0 : _redo.Count > 0;

        /// <summary>Raised after any edit (including undo/redo).</summary>
        public event EventHandler? DocumentChanged;

        /// <summary>Raised when a different document is loaded.</summary>
        public event EventHandler? DocumentReplaced;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void SetDesignDirty(bool dirty)
        {
            _design.IsDirty = dirty;
            if (dirty && _design.OwnerWorld != null) _design.OwnerWorld.IsDirty = true;
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(Title));
        }

        /// <summary>Takes a snapshot for undo. Call before mutating the schematic.</summary>
        public void BeginChange()
        {
            _undo.Push(Schematic.ToJson());
            Trim(_undo);
            _redo.Clear();
        }

        private static void Trim(Stack<string> stack)
        {
            if (stack.Count <= MaxUndo) return;
            var keep = stack.ToArray();
            stack.Clear();
            for (int i = MaxUndo - 1; i >= 0; i--) stack.Push(keep[i]);
        }

        /// <summary>Notifies that the schematic changed (after BeginChange + mutation).</summary>
        public void CommitChange(string? status = null)
        {
            SetDesignDirty(true);
            FlushActiveDesign();
            if (status != null) Status = status;
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Drops the snapshot taken by the last BeginChange (nothing actually changed).</summary>
        public void CancelChange()
        {
            if (_undo.Count > 0) _undo.Pop();
        }

        public void Undo()
        {
            if (_active.Kind == DocumentKind.World)
            {
                UndoWorld();
                return;
            }

            if (_undo.Count == 0) return;
            _redo.Push(Schematic.ToJson());
            RestoreSnapshot(_undo.Pop());
            Status = "Geri alındı";
        }

        public void Redo()
        {
            if (_active.Kind == DocumentKind.World)
            {
                RedoWorld();
                return;
            }

            if (_redo.Count == 0) return;
            _undo.Push(Schematic.ToJson());
            RestoreSnapshot(_redo.Pop());
            Status = "Yinelendi";
        }

        private void RestoreSnapshot(string json)
        {
            var board = Schematic.Board;
            Schematic = Schematic.FromJson(json);
            if (Schematic.Board == null) Schematic.Board = board;
            SetDesignDirty(true);
            FlushActiveDesign();
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Opens a new circuit tab (or reuses an untouched one).</summary>
        public void NewDocument(string title = "Yeni Devre")
        {
            LoadPackage(new ProjectPackage { Schematic = new Schematic { Title = title } }, null);
            Status = "Yeni devre oluşturuldu";
        }

        public void Load(Schematic schematic, string? path) => LoadPackage(new ProjectPackage { Schematic = schematic }, path);

        /// <summary>Opens a project package in a tab: registers its local parts, then shows it.</summary>
        public void LoadPackage(ProjectPackage package, string? path)
        {
            package.Schematic.EnsureProjectId();   // [assembly]
            foreach (var part in package.Parts)
            {
                try
                {
                    part.Register(Library);
                }
                catch (InvalidDataException)
                {
                }
            }

            var doc = NewDesignTab();
            doc.Package = package;
            doc.FilePath = path;
            doc.Undo.Clear();
            doc.Redo.Clear();
            Simulation.Stop();
            RestoreDesign(doc);
            SetDesignDirty(false);
            doc.Changed(nameof(OpenDocument.Title));
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            OnPropertyChanged(nameof(FilePath));
        }

        /// <summary>A path already open in a tab (switches to it instead of opening twice).</summary>
        public OpenDocument? FindOpen(string path) =>
            Documents.FirstOrDefault(d => d.FilePath != null && string.Equals(Path.GetFullPath(d.FilePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));

        public void Open(string path)
        {
            var open = FindOpen(path);
            if (open != null)
            {
                Activate(open);
                Status = "Zaten açık: " + Path.GetFileName(path);
                return;
            }

            var package = ProjectPackage.Load(path);
            LoadPackage(package, path);
            Status = package.WasLegacy
                ? "Açıldı (eski biçim; kaydedince yeni paket biçimine dönüşür): " + Path.GetFileName(path)
                : "Açıldı: " + Path.GetFileName(path);
            RecentFiles.Add(path);
        }

        /// <summary>Saves the active tab: a circuit to .rwproj, a world to .rwworld, an embedded board back into its world.</summary>
        public void Save(string path)
        {
            if (_active.Kind == DocumentKind.World)
            {
                SaveWorld(_active, path);
                return;
            }

            FlushActiveDesign();
            try
            {
                var preview = PreviewProvider?.Invoke();
                if (preview != null) Project.Preview = preview;
            }
            catch (InvalidOperationException)
            {
            }

            RobotWin.Studio.Libraries.LibraryManager.EmbedUsedParts(Project); // [libs] projects carry their user/imported parts
            if (MontajOf(_design) is { World: { } montaj } montajTab)
            {
                Project.Montaj = montaj.ToBytes();   // [assembly] the Montaj is saved inside its project
                montajTab.IsDirty = false;
            }

            // [tabs] Saved under a new name: the project takes the file's name (its title block, the Montaj say the same).
            if (!string.Equals(_design.FilePath, path, StringComparison.OrdinalIgnoreCase)) Schematic.Title = Path.GetFileNameWithoutExtension(path);
            Project.Save(path);
            _design.FilePath = path;
            SetDesignDirty(false);
            OnPropertyChanged(nameof(FilePath));
            Status = "Kaydedildi: " + Path.GetFileName(path);
            RecentFiles.Add(path);
        }

        /// <summary>For a board opened out of a world: writes it back (the world still has to be saved to disk).</summary>
        public void SaveEmbedded()
        {
            if (!_design.IsEmbedded) return;
            FlushActiveDesign();
            _design.IsDirty = false;
            _design.OwnerWorld!.IsDirty = true;
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(Title));
            WorldChanged?.Invoke(this, EventArgs.Empty);
            Status = "Kart dünyaya işlendi: " + _design.BaseTitle;
        }

        /// <summary>Firmware for a board, created on first use.</summary>
        public FirmwareSource FirmwareFor(string boardRefDes, string target)
        {
            var fw = Project.FirmwareFor(boardRefDes);
            if (fw == null)
            {
                fw = new FirmwareSource { BoardRefDes = boardRefDes, Target = target };
                Project.Firmware.Add(fw);
            }

            return fw;
        }

        public void MarkFirmwareChanged(string? status = null)
        {
            SetDesignDirty(true);
            FlushActiveDesign();
            if (status != null) Status = status;
            FirmwareChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Marks the PCB as edited (the board lives inside the schematic document).</summary>
        public void MarkBoardChanged(string status)
        {
            SetDesignDirty(true);
            FlushActiveDesign();
            Status = status;
        }

        // ================================================================ worlds

        public OpenDocument NewWorld(WorldDocument? world = null, string? path = null)
        {
            if (world == null)
            {
                world = new WorldDocument();
                world.Environment.Gravity = AppSettings.Current.WorldGravity;
                world.Environment.AmbientC = AppSettings.Current.AmbientTempC;
            }

            var doc = new OpenDocument(DocumentKind.World) { World = world, FilePath = path };
            Documents.Add(doc);
            Activate(doc);
            Status = path == null ? "Yeni dünya oluşturuldu" : "Açıldı: " + Path.GetFileName(path);
            return doc;
        }

        public OpenDocument OpenWorld(string path)
        {
            var open = FindOpen(path);
            if (open != null)
            {
                Activate(open);
                return open;
            }

            var world = WorldDocument.Load(path);
            RecentFiles.Add(path);
            return NewWorld(world, path);
        }

        public void SaveWorld(OpenDocument doc, string path)
        {
            if (doc.World == null) return;
            foreach (var child in Documents.Where(d => d.OwnerWorld == doc))
            {
                if (child == _design) FlushActiveDesign();
                else SyncEmbedded(child);
                child.IsDirty = false;
            }

            if (!string.Equals(doc.FilePath, path, StringComparison.OrdinalIgnoreCase)) doc.World.Title = Path.GetFileNameWithoutExtension(path);   // [tabs]
            doc.World.Save(path);
            doc.FilePath = path;
            doc.IsDirty = false;
            OnPropertyChanged(nameof(FilePath));
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(Title));
            Status = "Kaydedildi: " + Path.GetFileName(path);
            RecentFiles.Add(path);
        }

        /// <summary>
        /// [assembly] Edits a world that need not be the active tab (a Montaj, from the Parça page): one undo step on that
        /// world, which becomes unsaved (and its project, for a Montaj). <paramref name="edit"/> returns false when it
        /// changed nothing (no undo step then).
        /// </summary>
        public bool EditWorld(OpenDocument doc, Func<bool> edit, string? status = null)
        {
            if (doc.World == null) return false;
            string before = doc.World.ToJson();
            if (!edit()) return false;
            doc.WorldUndo.Push(before);
            Trim(doc.WorldUndo);
            doc.WorldRedo.Clear();
            doc.IsDirty = true;
            if (doc.OwnerProject is { } owner) owner.IsDirty = true;
            if (status != null) Status = status;
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(CanUndo));
            if (doc == _active) WorldChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        /// <summary>Snapshot before editing the active world.</summary>
        public void BeginWorldChange()
        {
            if (_active.World == null) return;
            _active.WorldUndo.Push(_active.World.ToJson());
            Trim(_active.WorldUndo);
            _active.WorldRedo.Clear();
        }

        public void CommitWorldChange(string? status = null)
        {
            if (_active.World == null) return;
            _active.IsDirty = true;
            if (_active.OwnerProject is { } owner) owner.IsDirty = true;   // [assembly] the project holds its Montaj
            if (status != null) Status = status;
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            WorldChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UndoWorld()
        {
            if (_active.World == null || _active.WorldUndo.Count == 0) return;
            _active.WorldRedo.Push(_active.World.ToJson());
            RestoreWorld(_active.WorldUndo.Pop());
            Status = "Geri alındı";
        }

        private void RedoWorld()
        {
            if (_active.World == null || _active.WorldRedo.Count == 0) return;
            _active.WorldUndo.Push(_active.World.ToJson());
            RestoreWorld(_active.WorldRedo.Pop());
            Status = "Yinelendi";
        }

        private void RestoreWorld(string json)
        {
            var w = _active.World!;
            var back = WorldDocument.FromJson(json);
            w.Title = back.Title;
            w.Description = back.Description;
            w.Environment = back.Environment;
            w.Objects = back.Objects;
            w.Cables = back.Cables;
            w.Joints = back.Joints;
            w.Parts = back.Parts;
            w.AssemblySources = back.AssemblySources;   // [assembly]
            _active.IsDirty = true;
            OnPropertyChanged(nameof(CanUndo));
            OnPropertyChanged(nameof(CanRedo));
            OnPropertyChanged(nameof(Title));
            WorldChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Opens a board of a world in its own circuit tab (edits flow back into the world).</summary>
        public OpenDocument EditWorldDesign(OpenDocument worldDoc, WorldDesign design)
        {
            var existing = Documents.FirstOrDefault(d => d.OwnerWorld == worldDoc && d.DesignId == design.Id);
            if (existing != null)
            {
                Activate(existing);
                return existing;
            }

            var doc = new OpenDocument(DocumentKind.Design) { Package = design.Package, OwnerWorld = worldDoc, DesignId = design.Id };
            Documents.Insert(Documents.IndexOf(worldDoc) + 1, doc);
            Activate(doc);
            Status = $"'{design.Name}' kartı düzenleniyor; değişiklikler dünyaya işlenir";
            return doc;
        }

        /// <summary>Notifies that a world was changed from outside the world page (e.g. a board added from a tab).</summary>
        public void NotifyWorldChanged() => WorldChanged?.Invoke(this, EventArgs.Empty);

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Most-recently-used files, persisted in %APPDATA%.</summary>
    public static class RecentFiles
    {
        private static string StorePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RobotWinStudio", "recent.txt");

        public static int Limit { get; set; } = 10;

        public static IReadOnlyList<string> Load()
        {
            try
            {
                if (Unattended.Active || !File.Exists(StorePath)) return Array.Empty<string>();
                var list = new List<string>();
                foreach (var line in File.ReadAllLines(StorePath))
                {
                    if (!string.IsNullOrWhiteSpace(line) && File.Exists(line)) list.Add(line);
                }

                return list;
            }
            catch (IOException)
            {
                return Array.Empty<string>();
            }
            catch (UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        public static void Add(string path)
        {
            if (Unattended.Active) return;
            try
            {
                var list = new List<string>(Load());
                list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                list.Insert(0, path);
                if (list.Count > Limit) list.RemoveRange(Limit, list.Count - Limit);
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                File.WriteAllLines(StorePath, list);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        public static void Clear()
        {
            try
            {
                if (File.Exists(StorePath)) File.Delete(StorePath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
