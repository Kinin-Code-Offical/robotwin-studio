using System;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>[problems] The circuit's share of the Problems panel: the ERC as it stands now, under the user's severities and exclusions.</summary>
    public partial class SchematicPage
    {
        /// <summary>The ERC now (it is quick, so it is never out of date); null on an empty sheet.</summary>
        public AreaReport? CircuitProblems()
        {
            if (W.Schematic.Parts.Count == 0) return null;
            var r = new AreaReport();
            var erc = ErcPolicy.Apply(W.Schematic, ElectricalRulesChecker.Check(W.Schematic, W.Library));
            foreach (var v in erc.Shown)
            {
                var level = v.Severity switch
                {
                    ErcSeverity.Error => ProblemLevel.Error,
                    ErcSeverity.Warning => ProblemLevel.Warning,
                    _ => ProblemLevel.Info
                };
                var marker = v;
                var (tip, fix) = ErcFix(marker);
                r.Problems.Add(new Problem(ProblemsHub.Circuit, level, ErcRuleCatalog.Find(v.Code)?.Name ?? v.Code, v.Message, () => ShowErcMarker(marker)) { Fix = fix, FixTip = tip });
            }

            return r;
        }

        /// <summary>
        /// [problems] The fix an ERC marker suggests, as KiCad's and VS Code's quick fixes: designators numbered, an unused pin
        /// marked "no connection", a stray NC mark taken away; any other accepted as an exception (it stays in the report).
        /// Each is one undo step.
        /// </summary>
        /// <summary>[problems] One undo step that removes what <paramref name="remove"/> finds (nothing: no step).</summary>
        private void RemoveAt(string status, Func<Schematic, int> remove)
        {
            W.BeginChange();
            if (remove(W.Schematic) == 0)
            {
                W.CancelChange();
                return;
            }

            W.CommitChange(status);
            BuildErcPanel();
        }

        /// <summary>
        /// [problems] The dangling end at the marker: the last segment of the wire that ends there goes (the whole wire when it
        /// is a single segment), so the part of it that joins something stays.
        /// </summary>
        internal static int TrimDangling(Schematic s, ErcViolation v)
        {
            foreach (var w in s.Wires)
            {
                if (!Schematic.SameSheet(w.Sheet, v.Sheet) || w.Points.Count < 2) continue;
                bool atStart = w.Points[0].Equals(v.Location), atEnd = w.Points[^1].Equals(v.Location);
                if (!atStart && !atEnd) continue;
                if (w.Points.Count == 2)
                {
                    s.Wires.Remove(w);
                    return 1;
                }

                w.Points.RemoveAt(atEnd ? w.Points.Count - 1 : 0);
                return 1;
            }

            return 0;
        }

        private (string Tip, Action Fix) ErcFix(ErcViolation v)
        {
            string name = ErcRuleCatalog.Find(v.Code)?.Name ?? v.Code;
            switch (v.Code)
            {
                case "not-annotated":
                case "duplicate-refdes":
                    return ("Numaralandır: numarasız ve yinelenen tanımlayıcılar sıradaki boş numarayı alır", () => AnnotateNow(new AnnotationOptions()));
                case "pin-unconnected":
                    return ("Pine 'bağlantı yok' (NC) işareti koy", () =>
                    {
                        W.BeginChange();
                        W.Schematic.NoConnects.Add(new NoConnectFlag { Position = v.Location, Sheet = v.Sheet });
                        W.CommitChange("NC işareti kondu: " + (v.RefDes ?? v.Location.ToString()));
                        BuildErcPanel();
                    });
                case "nc-floating":
                    return ("Boştaki NC işaretini sil", () => RemoveAt("Boştaki NC işareti silindi", s => s.NoConnects.RemoveAll(nc => nc.Position.Equals(v.Location) && Schematic.SameSheet(nc.Sheet, v.Sheet))));
                case "nc-connected":
                    // the pin is wired after all: the mark that says it is not goes
                    return ("NC işaretini kaldır: pin bir hatta bağlı", () => RemoveAt("NC işareti kaldırıldı", s => s.NoConnects.RemoveAll(nc => nc.Position.Equals(v.Location) && Schematic.SameSheet(nc.Sheet, v.Sheet))));
                case "wire-dangling":
                    // a stub drawn past its pin or left over after a part moved: the segment ending there goes
                    return ("Boştaki kablo parçasını sil", () => RemoveAt("Boştaki kablo parçası silindi", s => TrimDangling(s, v)));
                case "single-pin-net" or "label-single" when NetCompletions.For(W.Schematic, W.Library, v) is { } complete:
                    // [problems] a one-pin net finished: its label written as the net it was meant for, or put on the free pin of its name
                    return (complete.Text, () =>
                    {
                        W.BeginChange();
                        complete.ApplyTo(W.Schematic);
                        W.CommitChange(Lang.T($"Tek pinli hat tamamlandı: {v.Net}"));
                        BuildErcPanel();
                    });
                case "label-unconnected" when v.Net != null:
                    return ("Hiçbir şeye değmeyen etiketi sil", () => RemoveAt("Bağlanmamış etiket silindi", s => s.Labels.RemoveAll(l => l.Position.Equals(v.Location) && Schematic.SameSheet(l.Sheet, v.Sheet))));
                default:
                    return ("İstisna olarak işaretle: kabul edildi, raporda istisna olarak kalır", () => ErcPolicyEdit($"ERC istisnası: {name}", () => ErcPolicy.Exclude(W.Schematic, v, string.Empty)));
            }
        }

        /// <summary>Goes to an ERC marker as a click on it in the ERC list does: the place, its part selected, its net highlighted.</summary>
        public void ShowErcMarker(ErcViolation v)
        {
            Canvas.EnterSheet(v.Sheet);   // [sheets]
            Canvas.ZoomToPoint(v.Location, 22);
            var part = v.RefDes != null ? W.Schematic.FindPart(v.RefDes) : null;
            if (part != null) Canvas.SelectItems(new object[] { part });
            Canvas.HighlightedNet = v.Net;
            W.Status = Lang.T(v.Message);
        }
    }
}
