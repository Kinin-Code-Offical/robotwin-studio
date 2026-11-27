using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;

namespace RobotWin.Studio.Editor
{
    /// <summary>
    /// [group] KiCad's groups on the sheet: Ctrl+G groups the selection, Ctrl+Shift+G takes its groups apart, and a click on
    /// any grouped item picks the whole group, so a sub-circuit moves, turns, copies and deletes as one.
    /// </summary>
    public partial class SchematicCanvas
    {
        /// <summary>A grouped item's group joins (or, Ctrl+clicked off, leaves) the selection with it.</summary>
        private void WholeGroup(object hit, bool toggled)
        {
            if (SchematicGroups.GroupOf(hit) is not { } name) return;
            bool taking = _selection.Contains(hit);
            foreach (var m in SchematicGroups.Members(Doc, name))
            {
                if (ReferenceEquals(m, hit)) continue;
                if (taking) _selection.Add(m);
                else if (toggled) _selection.Remove(m);
            }
        }

        /// <summary>A selected group's frame (dashed, a grid step out) with its name over the corner, as KiCad marks it.</summary>
        private void DrawGroups(DrawingContext dc)
        {
            if (_selection.Count == 0) return;
            var pen = new Pen(SymbolRenderer.Brush(Accent), 1) { DashStyle = new DashStyle(new double[] { 5, 3 }, 0) };
            pen.Freeze();
            foreach (var name in _selection.Select(SchematicGroups.GroupOf).Where(g => g != null).Distinct())
            {
                if (SchematicEditing.Bounds(SchematicGroups.Members(Doc, name!), Lib) is not { } b) continue;
                var p0 = ToScreen(new GridPoint(b.MinX - 1, b.MinY - 1));
                var p1 = ToScreen(new GridPoint(b.MaxX + 1, b.MaxY + 1));
                var r = new Rect(p0, p1);
                dc.DrawRectangle(null, pen, r);
                var ft = Text(name!, 10.5, Accent, false);
                dc.DrawText(ft, new Point(r.Left + 2, r.Top - ft.Height - 1));
            }
        }

        public void GroupSelection()
        {
            if (!GuardEditing()) return;
            if (_selection.Count < 2)
            {
                W.Status = "Gruplamak için en az iki öğe seçin";
                return;
            }

            W.BeginChange();
            string name = SchematicGroups.Group(Doc, _selection.ToList());
            W.CommitChange($"{_selection.Count} öğe gruplandı: {name}");
            InvalidateVisual();
        }

        public void UngroupSelection()
        {
            if (!GuardEditing()) return;
            if (!_selection.Any(o => SchematicGroups.GroupOf(o) != null))
            {
                W.Status = "Seçimde grup yok";
                return;
            }

            W.BeginChange();
            int n = SchematicGroups.Ungroup(Doc, _selection.ToList());
            W.CommitChange($"Grup çözüldü ({n} öğe)");
            InvalidateVisual();
        }
    }
}
