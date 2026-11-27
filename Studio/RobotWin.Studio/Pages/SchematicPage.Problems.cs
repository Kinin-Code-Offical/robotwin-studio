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
                    return ("Boştaki NC işaretini sil", () =>
                    {
                        W.BeginChange();
                        int n = W.Schematic.NoConnects.RemoveAll(nc => nc.Position.Equals(v.Location) && Schematic.SameSheet(nc.Sheet, v.Sheet));
                        if (n == 0)
                        {
                            W.CancelChange();
                            return;
                        }

                        W.CommitChange("Boştaki NC işareti silindi");
                        BuildErcPanel();
                    });
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
