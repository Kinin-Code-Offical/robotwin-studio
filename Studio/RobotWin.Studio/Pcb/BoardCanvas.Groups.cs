using System;
using System.Linq;
using RobotTwin.CoreSim.Pcb;

namespace RobotWin.Studio.Pcb
{
    /// <summary>
    /// [group] KiCad's groups on the board: Ctrl+G groups the selection, Ctrl+Shift+G takes its groups apart, and a click
    /// on any grouped item picks the whole group, so it moves, turns and deletes as one.
    /// </summary>
    public partial class BoardCanvas
    {
        /// <summary>A grouped item's group joins (or, Ctrl+clicked off, leaves) the selection with it.</summary>
        private void WholeGroup(object hit, bool add)
        {
            var board = Board;
            if (board == null || BoardGroups.GroupOf(hit) is not { } name) return;
            bool taking = _sel.Contains(hit);
            foreach (var m in BoardGroups.Members(board, name))
            {
                if (ReferenceEquals(m, hit)) continue;
                if (taking && !_sel.Contains(m)) _sel.Add(m);
                else if (!taking && add) _sel.Remove(m);
            }
        }

        public void GroupSelection()
        {
            var board = Board;
            if (board == null || _sel.Count < 2)
            {
                Say("Gruplamak için en az iki öğe seçin");
                return;
            }

            BeforeEdit?.Invoke(this, EventArgs.Empty);
            string name = BoardGroups.Group(board, _sel.ToList());
            Edited($"{_sel.Count} öğe gruplandı: {name}");
        }

        public void UngroupSelection()
        {
            var board = Board;
            if (board == null || !_sel.Any(o => BoardGroups.GroupOf(o) != null))
            {
                Say("Seçimde grup yok");
                return;
            }

            BeforeEdit?.Invoke(this, EventArgs.Empty);
            int n = BoardGroups.Ungroup(board, _sel.ToList());
            Edited($"Grup çözüldü ({n} öğe)");
        }
    }
}
