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
                r.Problems.Add(new Problem(ProblemsHub.Circuit, level, ErcRuleCatalog.Find(v.Code)?.Name ?? v.Code, v.Message, () => ShowErcMarker(marker)));
            }

            return r;
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
