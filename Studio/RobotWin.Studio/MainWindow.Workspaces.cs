using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio
{
    /// <summary>
    /// [workspaces] Task-oriented layouts as Blender's and Fusion's workspaces, switched from the title bar, the Pencere menu
    /// and the command palette: the built-in ones set the page, its bottom row and its tool strips (as Fusion's change the
    /// toolbar), a saved one brings every panel and strip back where it was; "Varsayılan düzene dön" resets the panels as
    /// VS Code's Reset View Locations does. A project can keep its own layout (as KiCad's .kicad_prl, Visual Studio's .suo):
    /// it comes back when the project opens.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Opens a workspace (built-in or saved) by name.</summary>
        public void ApplyWorkspace(string name)
        {
            if (WorkspaceProfiles.FindBuiltin(name) is { } b)
            {
                Navigate(b.Page);
                DockOf(b.Page)?.ShowBottom(b.Bottom, b.Tab);
                if (b.Strips != null && StripsOf(b.Page) is { } host)
                {
                    foreach (var (id, shown, folded) in WorkspaceStrips.Parse(b.Strips)) host.SetState(id, shown, folded);
                    host.Apply();
                }

                name = b.Name;
            }
            else if (WorkspaceProfiles.Find(name) is { } p)
            {
                ApplyProfile(p);
                name = p.Name;
            }
            else
            {
                return;
            }

            WorkspaceProfiles.Active = name;
            UpdateWorkspaceButton();
            W.Status = "Çalışma alanı: " + name;
        }

        /// <summary>
        /// [dock-min] Full view (Blender's Ctrl+Space): the page's panels and bottom row out of the way, the canvas
        /// the whole window; again brings them back as they were.
        /// </summary>
        private void ToggleFullView()
        {
            if (DockOf(_current) is not { } d) return;
            d.ToggleFullView();
            W.Status = d.Collapsed ? "Tam görünüm: paneller gizlendi (Ctrl+Space ile geri gelir)" : "Paneller geri geldi";
        }

        /// <summary>A saved layout back on screen: every page's panels and tool strips, then its page.</summary>
        private void ApplyProfile(WorkspaceProfile p)
        {
            foreach (var kv in p.Layouts) DockOf(kv.Key)?.Restore(kv.Value);
            foreach (var kv in p.Strips)
                if (StripsOf(kv.Key) is { } host && host.LoadLayout(kv.Value)) host.Apply();
            Navigate(p.Page);
        }

        /// <summary>A page's tool strips over its canvas (the Kod page has none).</summary>
        private OverlayHost? StripsOf(string page) => page switch
        {
            "schematic" => _schematic.ToolOverlays,
            "world" => _world.ToolOverlays,
            "pcb" => _pcb.ToolOverlays,
            "model" => _model.ToolOverlays,
            _ => null
        };

        private DockManager? DockOf(string page) => page switch
        {
            "schematic" => _schematic.Panels,
            "world" => _world.Panels,
            "pcb" => _pcb.Panels,
            "model" => _model.Panels,
            "code" => _code.Panels,
            _ => null
        };

        /// <summary>The layout on screen as a workspace: the page in front and every page's panels.</summary>
        private WorkspaceProfile CaptureWorkspace(string name)
        {
            var p = new WorkspaceProfile { Name = name, Page = _current };
            if (_schematic.Panels is { } s) p.Layouts["schematic"] = s.Snapshot();
            if (_world.Panels is { } w) p.Layouts["world"] = w.Snapshot();
            if (_pcb.Panels is { } pc) p.Layouts["pcb"] = pc.Snapshot();
            if (_model.Panels is { } md) p.Layouts["model"] = md.Snapshot();
            if (_code.Panels is { } cd) p.Layouts["code"] = cd.Snapshot();
            foreach (var page in new[] { "schematic", "world", "pcb", "model" })
                if (StripsOf(page) is { } host) p.Strips[page] = host.SaveLayout();
            return p;
        }

        // ------------------------------------------------------------------ [workspaces] the open file's own layout

        // A project keeps it in its package's settings.json, a world or a Montaj in its .rwworld (WorldDocument.Settings).

        private string? DocSettings
        {
            get => W.ActiveWorld is { } w ? w.Settings : W.Project.Settings;
            set
            {
                if (W.ActiveWorld is { } w) w.Settings = value;
                else W.Project.Settings = value;
                W.MarkProjectChanged();
            }
        }

        /// <summary>The words for the open file: (save to it, update it, remove it, its layout's tooltip, saved, opened, removed).</summary>
        private (string Save, string Update, string Remove, string Tip, string Saved, string Opened, string Removed) DocWords =>
            W.ActiveWorld is { } w
                ? w.IsAssembly
                    ? ("Ekrandaki düzeni Montaja kaydet", "Montajın düzenini ekrandakiyle güncelle", "Montajın düzenini kaldır", "Bu Montajla kaydedilen düzen",
                       "Düzen Montaja eklendi: kaydedince Montajla birlikte saklanır, Montaj açılınca geri gelir", "Montajın düzeni açıldı", "Montajın düzeni kaldırıldı (ekrandaki düzen olduğu gibi kalır)")
                    : ("Ekrandaki düzeni dünyaya kaydet", "Dünyanın düzenini ekrandakiyle güncelle", "Dünyanın düzenini kaldır", "Bu dünyayla kaydedilen düzen",
                       "Düzen dünyaya eklendi: kaydedince dünyayla birlikte saklanır, dünya açılınca geri gelir", "Dünyanın düzeni açıldı", "Dünyanın düzeni kaldırıldı (ekrandaki düzen olduğu gibi kalır)")
                : ("Ekrandaki düzeni projeye kaydet", "Projenin düzenini ekrandakiyle güncelle", "Projenin düzenini kaldır", "Bu projeyle kaydedilen düzen",
                   "Düzen projeye eklendi: kaydedince projeyle birlikte saklanır, proje açılınca geri gelir", "Projenin düzeni açıldı", "Projenin düzeni kaldırıldı (ekrandaki düzen olduğu gibi kalır)");

        private bool ProjectHasLayout => WorkspaceProfiles.FromProject(DocSettings) != null;

        /// <summary>The layout on screen into the open file: it comes back whenever the file opens (saved with it).</summary>
        private void SaveLayoutToProject()
        {
            DocSettings = WorkspaceProfiles.WithProjectLayout(DocSettings, CaptureWorkspace(WorkspaceProfiles.ProjectLayoutName));
            WorkspaceProfiles.Active = WorkspaceProfiles.ProjectLayoutName;
            UpdateWorkspaceButton();
            W.Status = DocWords.Saved;
        }

        private void ApplyProjectLayout()
        {
            if (WorkspaceProfiles.FromProject(DocSettings) is not { } p) return;
            ApplyProfile(p);
            WorkspaceProfiles.Active = WorkspaceProfiles.ProjectLayoutName;
            UpdateWorkspaceButton();
            W.Status = DocWords.Opened;
        }

        private void RemoveProjectLayout()
        {
            if (!ProjectHasLayout) return;
            DocSettings = WorkspaceProfiles.WithProjectLayout(DocSettings, null);
            if (WorkspaceProfiles.Active == WorkspaceProfiles.ProjectLayoutName) WorkspaceProfiles.Active = null;
            UpdateWorkspaceButton();
            W.Status = DocWords.Removed;
        }

        /// <summary>A project, world or Montaj opened from its file: its own layout, if it keeps one, once the page it opened on is up.</summary>
        private void OnProjectOpened()
        {
            if (!ProjectHasLayout)
            {
                if (WorkspaceProfiles.Active == WorkspaceProfiles.ProjectLayoutName)
                {
                    WorkspaceProfiles.Active = null;   // the one before kept a layout of its own; this one does not
                    UpdateWorkspaceButton();
                }

                return;
            }

            Dispatcher.BeginInvoke(new Action(ApplyProjectLayout), DispatcherPriority.Background);
        }

        private void SaveWorkspaceAs()
        {
            string? name = Dialog.Prompt(this, "Çalışma alanını kaydet", "Ekrandaki düzen (açık sayfa, panellerin yeri ve boyutları) bu adla kaydedilir.", WorkspaceProfiles.FreeName("Benim düzenim"));
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (WorkspaceProfiles.FindBuiltin(name) != null)
            {
                W.Status = "Bu ad hazır bir çalışma alanının; başka bir ad seçin";
                return;
            }

            WorkspaceProfiles.Save(CaptureWorkspace(name));
            UpdateWorkspaceButton();
            W.Status = "Çalışma alanı kaydedildi: " + name;
        }

        private void UpdateActiveWorkspace()
        {
            if (WorkspaceProfiles.Active is not { } name || WorkspaceProfiles.Find(name) == null) return;
            WorkspaceProfiles.Save(CaptureWorkspace(name));
            W.Status = "Çalışma alanı güncellendi: " + name;
        }

        private void RenameActiveWorkspace()
        {
            if (WorkspaceProfiles.Active is not { } name || WorkspaceProfiles.Find(name) == null) return;
            string? to = Dialog.Prompt(this, "Çalışma alanını yeniden adlandır", "Yeni ad:", name);
            if (string.IsNullOrWhiteSpace(to) || to.Trim() == name) return;
            if (!WorkspaceProfiles.Rename(name, to)) W.Status = "Bu ad kullanılıyor; başka bir ad seçin";
            UpdateWorkspaceButton();
        }

        private void DeleteActiveWorkspace()
        {
            if (WorkspaceProfiles.Active is not { } name || WorkspaceProfiles.Find(name) == null) return;
            if (Dialog.Show(this, "Çalışma alanını sil", $"'{name}' çalışma alanı silinsin mi? Ekrandaki düzen olduğu gibi kalır.", "Sil", "Vazgeç") != 0) return;
            WorkspaceProfiles.Delete(name);
            UpdateWorkspaceButton();
        }

        /// <summary>Every page's panels back to where they start (as VS Code's Reset View Locations).</summary>
        private void ResetLayouts()
        {
            _schematic.Panels?.Reset();
            _world.Panels?.Reset();
            _pcb.Panels?.Reset();
            _model.Panels?.Reset();
            _code.Panels?.Reset();
            WorkspaceProfiles.Active = null;
            UpdateWorkspaceButton();
            W.Status = "Paneller varsayılan yerlerine döndü";
        }

        /// <summary>The workspace list and its actions (the title bar's menu and Pencere → Çalışma alanı).</summary>
        private void FillWorkspaceMenu(ItemsControl m)
        {
            m.Items.Clear();
            string? active = WorkspaceProfiles.Active;
            foreach (var b in WorkspaceProfiles.Builtins)
            {
                string name = b.Name;
                var it = Check(b.Name, null, string.Equals(active, b.Name, StringComparison.CurrentCultureIgnoreCase), _ => ApplyWorkspace(name));
                it.ToolTip = b.Description;
                m.Items.Add(it);
            }

            if (WorkspaceProfiles.Saved.Count > 0)
            {
                m.Items.Add(new Separator());
                foreach (var p in WorkspaceProfiles.Saved)
                {
                    string name = p.Name;
                    m.Items.Add(Check(p.Name, null, string.Equals(active, p.Name, StringComparison.CurrentCultureIgnoreCase), _ => ApplyWorkspace(name)));
                }
            }

            if (ProjectHasLayout)
            {
                m.Items.Add(new Separator());
                var mine = Check(DocLayoutName, null, active == WorkspaceProfiles.ProjectLayoutName, _ => ApplyProjectLayout());
                mine.ToolTip = Lang.T(DocWords.Tip);
                m.Items.Add(mine);
            }

            bool own = active != null && WorkspaceProfiles.Find(active) != null;
            m.Items.Add(new Separator());
            m.Items.Add(Item("Ekrandaki düzeni çalışma alanı olarak kaydet…", null, SaveWorkspaceAs));
            m.Items.Add(Item("Bu çalışma alanını ekrandaki düzenle güncelle", null, UpdateActiveWorkspace, own));
            m.Items.Add(Item("Yeniden adlandır…", null, RenameActiveWorkspace, own));
            m.Items.Add(Item("Sil", null, DeleteActiveWorkspace, own));
            m.Items.Add(new Separator());
            m.Items.Add(Item(ProjectHasLayout ? DocWords.Update : DocWords.Save, null, SaveLayoutToProject));
            if (ProjectHasLayout) m.Items.Add(Item(DocWords.Remove, null, RemoveProjectLayout));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Varsayılan düzene dön", null, ResetLayouts));
        }

        private void OnWorkspaceMenu(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = WorkspaceButton, Placement = PlacementMode.Bottom };
            FillWorkspaceMenu(menu);
            menu.IsOpen = true;
        }

        private void UpdateWorkspaceButton() =>
            WorkspaceLabel.Text = Lang.T(WorkspaceProfiles.Active == WorkspaceProfiles.ProjectLayoutName ? DocLayoutName : WorkspaceProfiles.Active ?? "Çalışma alanı");

        /// <summary>[workspaces] The open file's own layout, as it is called (a project's, a world's, a Montaj's).</summary>
        private string DocLayoutName => W.ActiveWorld is { } w ? (w.IsAssembly ? "Montajın düzeni" : "Dünyanın düzeni") : WorkspaceProfiles.ProjectLayoutName;

        /// <summary>The palette's entries: every workspace, and saving the layout as one.</summary>
        private IEnumerable<PaletteCommand> WorkspaceCommands()
        {
            const string group = "Çalışma alanı";
            foreach (var b in WorkspaceProfiles.Builtins)
            {
                string name = b.Name;
                yield return new PaletteCommand(group, name, () => ApplyWorkspace(name), null, b.Description) { Keywords = "workspace düzen yerleşim layout" };
            }

            foreach (var p in WorkspaceProfiles.Saved)
            {
                string name = p.Name;
                yield return new PaletteCommand(group, name, () => ApplyWorkspace(name)) { Keywords = "workspace düzen yerleşim layout" };
            }

            yield return new PaletteCommand(group, "Ekrandaki düzeni çalışma alanı olarak kaydet…", SaveWorkspaceAs) { Keywords = "workspace save layout kaydet" };
            yield return new PaletteCommand(group, "Ekrandaki düzeni açık dosyaya kaydet (proje, dünya ya da Montaj)", SaveLayoutToProject) { Keywords = "workspace project world layout proje dünya montaj düzen kaydet" };
            yield return new PaletteCommand(group, "Varsayılan düzene dön", ResetLayouts) { Keywords = "reset layout panel sıfırla" };
        }
    }
}
