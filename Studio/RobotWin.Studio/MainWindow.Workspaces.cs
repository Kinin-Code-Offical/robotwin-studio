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

        // ------------------------------------------------------------------ [workspaces] the project's own layout

        private bool ProjectOpen => W.ActiveDocument.Kind != DocumentKind.World;

        private bool ProjectHasLayout => ProjectOpen && WorkspaceProfiles.FromProject(W.Project.Settings) != null;

        /// <summary>The layout on screen into the project: it comes back whenever the project opens (saved with it).</summary>
        private void SaveLayoutToProject()
        {
            if (!ProjectOpen) return;
            W.Project.Settings = WorkspaceProfiles.WithProjectLayout(W.Project.Settings, CaptureWorkspace(WorkspaceProfiles.ProjectLayoutName));
            W.MarkProjectChanged();
            WorkspaceProfiles.Active = WorkspaceProfiles.ProjectLayoutName;
            UpdateWorkspaceButton();
            W.Status = "Düzen projeye eklendi: kaydedince projeyle birlikte saklanır, proje açılınca geri gelir";
        }

        private void ApplyProjectLayout()
        {
            if (!ProjectOpen || WorkspaceProfiles.FromProject(W.Project.Settings) is not { } p) return;
            ApplyProfile(p);
            WorkspaceProfiles.Active = WorkspaceProfiles.ProjectLayoutName;
            UpdateWorkspaceButton();
            W.Status = "Projenin düzeni açıldı";
        }

        private void RemoveProjectLayout()
        {
            if (!ProjectHasLayout) return;
            W.Project.Settings = WorkspaceProfiles.WithProjectLayout(W.Project.Settings, null);
            W.MarkProjectChanged();
            if (WorkspaceProfiles.Active == WorkspaceProfiles.ProjectLayoutName) WorkspaceProfiles.Active = null;
            UpdateWorkspaceButton();
            W.Status = "Projenin düzeni kaldırıldı (ekrandaki düzen olduğu gibi kalır)";
        }

        /// <summary>A project opened from its file: its own layout, if it keeps one, once the page it opened on is up.</summary>
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
                var mine = Check(WorkspaceProfiles.ProjectLayoutName, null, active == WorkspaceProfiles.ProjectLayoutName, _ => ApplyProjectLayout());
                mine.ToolTip = Lang.T("Bu projeyle kaydedilen düzen");
                m.Items.Add(mine);
            }

            bool own = active != null && WorkspaceProfiles.Find(active) != null;
            m.Items.Add(new Separator());
            m.Items.Add(Item("Ekrandaki düzeni çalışma alanı olarak kaydet…", null, SaveWorkspaceAs));
            m.Items.Add(Item("Bu çalışma alanını ekrandaki düzenle güncelle", null, UpdateActiveWorkspace, own));
            m.Items.Add(Item("Yeniden adlandır…", null, RenameActiveWorkspace, own));
            m.Items.Add(Item("Sil", null, DeleteActiveWorkspace, own));
            m.Items.Add(new Separator());
            m.Items.Add(Item(ProjectHasLayout ? "Projenin düzenini ekrandakiyle güncelle" : "Ekrandaki düzeni projeye kaydet", null, SaveLayoutToProject, ProjectOpen));
            if (ProjectHasLayout) m.Items.Add(Item("Projenin düzenini kaldır", null, RemoveProjectLayout));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Varsayılan düzene dön", null, ResetLayouts));
        }

        private void OnWorkspaceMenu(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = WorkspaceButton, Placement = PlacementMode.Bottom };
            FillWorkspaceMenu(menu);
            menu.IsOpen = true;
        }

        private void UpdateWorkspaceButton() => WorkspaceLabel.Text = Lang.T(WorkspaceProfiles.Active ?? "Çalışma alanı");

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
            yield return new PaletteCommand(group, "Ekrandaki düzeni projeye kaydet", SaveLayoutToProject) { Keywords = "workspace project layout proje düzen kaydet" };
            yield return new PaletteCommand(group, "Varsayılan düzene dön", ResetLayouts) { Keywords = "reset layout panel sıfırla" };
        }
    }
}
