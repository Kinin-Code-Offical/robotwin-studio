using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Sandbox;
using Asm = RobotTwin.CoreSim.Sandbox.ProjectAssembly;

namespace RobotWin.Studio.Shell
{
    /// <summary>[assembly] A project to bring into a Montaj and how.</summary>
    internal sealed class MontajSourcePlan
    {
        public MontajSourcePlan(ProjectPackage package, string? path, AssemblyForm form)
        {
            Package = package;
            Path = path;
            Form = form;
        }

        public ProjectPackage Package { get; }
        public string? Path { get; }
        public AssemblyForm Form { get; set; }
    }

    /// <summary>[assembly] What goes into a Montaj: projects (each with or without its PCB) and the Parça page's parts.</summary>
    internal sealed class MontajPlan
    {
        public List<MontajSourcePlan> Projects { get; } = new List<MontajSourcePlan>();

        /// <summary>Also send the Parça page's parts (each a body of its own).</summary>
        public bool Parts { get; set; }

        /// <summary>Unattended: the current project, its PCB if it has one.</summary>
        public static MontajPlan Default(Workspace w, OpenDocument project, bool parts)
        {
            var plan = new MontajPlan { Parts = parts };
            plan.Projects.Add(new MontajSourcePlan(project == w.DesignDocument ? w.Project : project.Package, project.FilePath, AssemblyForm.Auto));
            return plan;
        }
    }

    /// <summary>
    /// [assembly] Asks what goes into a Montaj, in the user's words: which projects (the open ones, or from a file), each
    /// with its PCB (the board as made, the motors and batteries cabled to it) or without (every part separate, cabled pin
    /// to pin), and whether the parts modelled on the Parça page come too. Nothing is guessed silently.
    /// </summary>
    internal static class MontajDialog
    {
        /// <summary>--snapshot: <see cref="Ask"/> shows the window without waiting and returns nothing.</summary>
        internal static bool SnapshotPreview { get; set; }

        private sealed class Row
        {
            public CheckBox Use = null!;
            public ComboBox Form = null!;
            public ProjectPackage Package = null!;
            public string? Path;
        }

        private static readonly (AssemblyForm Form, string Text)[] Forms =
        {
            (AssemblyForm.Auto, "Otomatik: PCB'de yerleşik parça varsa PCB'li"),
            (AssemblyForm.WithPcb, "PCB'li: lehimli kart, motor ve piller ayrı, kablolu"),
            (AssemblyForm.LooseParts, "PCB'siz: her parça ayrı, pin pin kablolu")
        };

        /// <param name="into">An existing Montaj to add to (its projects are not offered again); null = a new Montaj.</param>
        public static MontajPlan? Ask(Window owner, Workspace w, OpenDocument project, int modelParts, bool partsChecked, WorldDocument? into = null)
        {
            w.FlushActiveDesign();
            var lib = w.Library;
            var rows = new List<Row>();
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = into == null
                    ? "Montaj, robotun bütün parçalarının bir araya geldiği yerdir: devrelerin kartları ve parçaları gerçek 3B parçalar olarak gelir, Parça sayfasında modellediğiniz gövde ve braketlerle birleştirilir."
                    : "Bu montaja başka projelerin devrelerini ekleyin: her proje var olanların yanına kurulur.",
                TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.FindResource("Text.Caption"),
                Margin = new Thickness(0, 0, 0, 12)
            });
            panel.Children.Add(new TextBlock { Text = "Projeler", Style = (Style)Application.Current.FindResource("Text.Overline"), Margin = new Thickness(0, 0, 0, 6) });
            var list = new StackPanel();
            panel.Children.Add(list);

            void AddRow(ProjectPackage pkg, string? path, bool check)
            {
                var sch = pkg.Schematic;
                string pid = sch.EnsureProjectId();
                if (into?.SourceOf(pid) != null || rows.Any(r => r.Package.Schematic.ProjectId == pid)) return;
                bool pcb = Asm.HasPcb(sch);
                int physical = Asm.PhysicalParts(sch, lib).Count;
                var row = new Row { Package = pkg, Path = path };
                row.Use = new CheckBox { Content = Asm.TitleOf(sch), IsChecked = check, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0, 0, 0) };
                row.Form = new ComboBox { Width = 330, Margin = new Thickness(24, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                foreach (var (form, text) in Forms)
                {
                    bool ok = form != AssemblyForm.WithPcb || pcb;
                    row.Form.Items.Add(new ComboBoxItem { Content = ok ? text : text + " (PCB'de yerleşik parça yok)", Tag = form, IsEnabled = ok });
                }

                row.Form.SelectedIndex = 0;
                string about = $"{physical} parça · " + (pcb ? $"PCB'de {sch.Board!.Components.Count} parça yerleşik" : "PCB'de yerleşik parça yok");
                if (sch.Assembly is { Kind: RobotTwin.CoreSim.Mechanics.VehicleKind.Rover }) about += " · araç: şase, tekerlek ve sarhoş tekeriyle kurulur";
                if (path != null) about += " · " + System.IO.Path.GetFileName(path);
                var box = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                box.Children.Add(row.Use);
                box.Children.Add(new TextBlock { Text = about, Style = (Style)Application.Current.FindResource("Text.Caption"), Margin = new Thickness(24, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
                box.Children.Add(row.Form);
                row.Use.Checked += (_, _) => row.Form.IsEnabled = true;
                row.Use.Unchecked += (_, _) => row.Form.IsEnabled = false;
                row.Form.IsEnabled = check;
                list.Children.Add(box);
                rows.Add(row);
            }

            // The current project first, then the other open projects.
            AddRow(project == w.DesignDocument ? w.Project : project.Package, project.FilePath, into == null);
            foreach (var d in w.Documents.Where(d => d.Kind == DocumentKind.Design && !d.IsEmbedded && d != project))
                AddRow(d == w.DesignDocument ? w.Project : d.Package, d.FilePath, false);

            var fromFile = new Button { Content = "Dosyadan proje ekle (.rwproj)…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0), Padding = new Thickness(10, 4, 10, 4) };
            fromFile.Click += (_, _) =>
            {
                var dlg = new OpenFileDialog { Filter = RobotWin.Studio.Shell.Lang.Filter("RobotWin projesi (*.rwproj)|*.rwproj"), Multiselect = true, Title = RobotWin.Studio.Shell.Lang.T("Montaja proje ekle") };
                if (dlg.ShowDialog(owner) != true) return;
                foreach (var f in dlg.FileNames)
                {
                    try
                    {
                        AddRow(ProjectPackage.Load(f), f, true);
                    }
                    catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.Text.Json.JsonException || ex is UnauthorizedAccessException)
                    {
                        Dialog.Show(owner, "Proje okunamadı", System.IO.Path.GetFileName(f) + ": " + ex.Message, "Tamam");
                    }
                }
            };
            panel.Children.Add(fromFile);

            CheckBox? parts = null;
            if (modelParts > 0)
            {
                panel.Children.Add(new TextBlock { Text = "Parça sayfası", Style = (Style)Application.Current.FindResource("Text.Overline"), Margin = new Thickness(0, 14, 0, 6) });
                parts = new CheckBox { Content = $"Modellediğiniz {modelParts} parça (her biri ayrı bir gövde olarak)", IsChecked = partsChecked, Padding = new Thickness(6, 0, 0, 0) };
                panel.Children.Add(parts);
            }

            var window = Pcb.PcbForms.MakeDialog(owner, into == null ? "Montajı kur" : "Montaja proje ekle", 520, panel, out var okButton, into == null ? "Montajı kur" : "Ekle");
            okButton.Click += (_, _) => window.DialogResult = true;
            if (SnapshotPreview)
            {
                // --snapshot: shown without waiting, rendered instead of the main window.
                window.ShowActivated = false;
                window.Show();
                window.UpdateLayout();
                App.SnapshotTarget = System.Windows.Media.VisualTreeHelper.GetChildrenCount(window) > 0 ? System.Windows.Media.VisualTreeHelper.GetChild(window, 0) as FrameworkElement : null;
                return null;
            }

            if (window.ShowDialog() != true) return null;

            var plan = new MontajPlan { Parts = parts?.IsChecked == true };
            foreach (var r in rows.Where(r => r.Use.IsChecked == true))
                plan.Projects.Add(new MontajSourcePlan(r.Package, r.Path, r.Form.SelectedItem is ComboBoxItem { Tag: AssemblyForm f } ? f : AssemblyForm.Auto));
            return plan;
        }
    }
}
