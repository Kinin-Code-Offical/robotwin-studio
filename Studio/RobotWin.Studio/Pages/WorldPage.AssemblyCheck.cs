using System;
using System.Collections.Generic;
using System.Globalization;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Shell;
using RobotWin.Studio.World;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [assembly-check] One assembly report in the Problems panel (the roadmap's "tek montaj doğrulama raporu"): the checks
    /// the Montaj and the World already have, run together with **Denetle**: parts that overlap (SolidWorks' interference
    /// detection, joined parts left out), holes of two parts that run into each other off-axis (the bolt that would not
    /// go through), cables too short or too tight for where their plugs are, and each board against the box its PCB is
    /// built into (the board and its parts to the walls, the floor and the lid). A row selects the part or the cable; an edit
    /// makes the result out of date until it is checked again.
    /// </summary>
    public partial class WorldPage
    {
        private WorldDocument? _checkedWorld;
        private int _checkedEdits = -1, _worldEdits;
        private List<Problem> _assemblyProblems = new List<Problem>();
        private bool _countingEdits;

        /// <summary>The report of the world on the page now; null without one.</summary>
        public AreaReport? AssemblyProblems()
        {
            if (!_countingEdits)
            {
                W.WorldChanged += (_, _) => _worldEdits++;
                _countingEdits = true;
            }

            if (World is not { } world || world.Objects.Count == 0) return null;
            var r = new AreaReport { Check = RunAssemblyCheck };
            if (!ReferenceEquals(_checkedWorld, world))
            {
                r.NotChecked = "Montaj denetimi bu dünya için çalışmadı: çakışma, delik hizası, kablo boyları ve kart kutusu";
                return r;
            }

            if (_checkedEdits != _worldEdits) r.NotChecked = "Son değişiklikten önce denetlendi";
            r.Problems.AddRange(_assemblyProblems);
            return r;
        }

        /// <summary>Runs the checks on the world as it stands and keeps their findings.</summary>
        public void RunAssemblyCheck()
        {
            if (World is not { } world) return;
            var geometry = new SceneGeometry(Scene);
            var found = new List<Problem>();
            string NameOf(string id) => world.Find(id)?.Name is { Length: > 0 } n ? n : id;
            const string area = ProblemsHub.AssemblyCheck;

            foreach (var p in Interference.Find(world, geometry, new InterferenceOptions()))
            {
                string a = p.A, msg = string.Format(CultureInfo.CurrentCulture, "{0} ile {1} iç içe: {2:0.#} mm³ ortak hacim", NameOf(p.A), NameOf(p.B), p.VolumeMm3);
                found.Add(new Problem(area, ProblemLevel.Error, "Çakışma", msg, () => Scene.Select(a)));
            }

            foreach (var h in HoleAlignment.Find(world, geometry))
            {
                string a = h.A, msg = string.Format(CultureInfo.CurrentCulture, "{0} ile {1} delikleri {2:0.##} mm kayık (Ø{3:0.#} / Ø{4:0.#} mm): cıvata geçmez", NameOf(h.A), NameOf(h.B), h.DeviationMm, h.DiameterAMm, h.DiameterBMm);
                found.Add(new Problem(area, ProblemLevel.Warning, "Delik hizası", msg, () => Scene.Select(a)));
            }

            foreach (var c in world.Cables)
            {
                var (_, _, verdict, bad) = CableFit(c);
                if (!bad) continue;
                string id = c.Id, name = c.Name is { Length: > 0 } n ? n : c.Id;
                found.Add(new Problem(area, ProblemLevel.Warning, "Kablo boyu", name + ": " + verdict, () => Scene.SelectCable(id)));
            }

            // [assembly-check] each board design once: its PCB's box (PCB → DRC → Kutu), the board and its parts against it
            var designs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in world.Objects)
            {
                if (o.Kind != WorldObjectKind.Board || o.DesignId == null || !designs.Add(o.DesignId)) continue;
                if (world.FindDesign(o.DesignId)?.Package.Schematic.Board is not { Enclosure: { } box } board) continue;
                string id = o.Id, name = NameOf(o.Id);
                foreach (var v in RobotTwin.CoreSim.Pcb.EnclosureCheck.Check(board, box))
                {
                    found.Add(new Problem(area, v.Severity == RobotTwin.CoreSim.Pcb.DrcSeverity.Error ? ProblemLevel.Error : ProblemLevel.Warning,
                        "Kart kutusu", name + ": " + v.Message, () => Scene.Select(id)));
                }
            }

            _checkedWorld = world;
            _checkedEdits = _worldEdits;
            _assemblyProblems = found;
            W.Status = found.Count == 0 ? "Montaj denetimi: sorun yok" : $"Montaj denetimi: {found.Count} bulgu";
            ProblemsHub.Notify();
        }
    }
}
