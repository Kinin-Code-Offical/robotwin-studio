using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [multiselect] Unity's Selection for Montaj and Dünya: one active object (<see cref="Selected"/>, the one the
    /// inspector and the handle belong to) and the others selected with it (Ctrl+click in the view or the tree,
    /// Shift+click a range in the tree). Moving, turning, copying, duplicating and deleting take all of them.
    /// </summary>
    public sealed partial class WorldScene
    {
        private readonly List<string> _alsoSelected = new List<string>();

        /// <summary>Everything selected: the active object first, then the others in the order they were picked.</summary>
        public IReadOnlyList<string> SelectedAll
        {
            get
            {
                var all = new List<string>();
                if (Selected != null) all.Add(Selected);
                all.AddRange(_alsoSelected.Where(id => !string.Equals(id, Selected, StringComparison.OrdinalIgnoreCase)));
                return all;
            }
        }

        public bool IsSelected(string? id) =>
            id != null && (string.Equals(Selected, id, StringComparison.OrdinalIgnoreCase) || _alsoSelected.Contains(id, StringComparer.OrdinalIgnoreCase));

        /// <summary>Ctrl+click: a picked object joins the selection and becomes the active one; picked again, it leaves it.</summary>
        public void ToggleSelect(string id)
        {
            if (Selected == null)
            {
                Select(id);
                return;
            }

            if (string.Equals(Selected, id, StringComparison.OrdinalIgnoreCase))
            {
                // the active one leaves: the last of the others takes its place
                string? next = _alsoSelected.LastOrDefault();
                if (next != null) _alsoSelected.Remove(next);
                SetSelection(next, _alsoSelected.ToList());
                return;
            }

            var others = _alsoSelected.ToList();
            int at = others.FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
            if (at >= 0)
            {
                others.RemoveAt(at);
                SetSelection(Selected, others);
                return;
            }

            others.Add(Selected);
            SetSelection(id, others);
        }

        /// <summary>Several objects at once (a range in the tree, a rectangle in the view): the first is the active one.</summary>
        public void SelectMany(IReadOnlyList<string> ids)
        {
            if (ids.Count == 0)
            {
                Select(null);
                return;
            }

            SetSelection(ids[0], ids.Skip(1).ToList());
        }

        private void SetSelection(string? active, List<string> others)
        {
            others.RemoveAll(x => string.Equals(x, active, StringComparison.OrdinalIgnoreCase));
            bool same = string.Equals(active, Selected, StringComparison.OrdinalIgnoreCase) && others.SequenceEqual(_alsoSelected, StringComparer.OrdinalIgnoreCase);
            if (same) return;
            _alsoSelected.Clear();
            _alsoSelected.AddRange(others.Distinct(StringComparer.OrdinalIgnoreCase));
            if (!string.Equals(active, Selected, StringComparison.OrdinalIgnoreCase))
            {
                SelectCore(active);
                return;
            }

            RebuildOverlay();
            SelectionChanged?.Invoke(Selected);
        }
    }
}
