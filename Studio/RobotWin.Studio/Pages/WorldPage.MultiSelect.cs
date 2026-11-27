using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [multiselect] Unity's multi-selection on the Montaj and Dünya pages: Ctrl+click adds or takes out (view and tree),
    /// Shift+click selects a range in the tree, Ctrl+A everything. The inspector shows the active one; the handle,
    /// copy, cut, duplicate and delete take them all.
    /// </summary>
    public partial class WorldPage
    {
        /// <summary>The scene tree's objects in the order they are listed (for a Shift+click range).</summary>
        private readonly List<string> _treeOrder = new List<string>();

        /// <summary>Where a Shift+click range starts: the last row clicked without Shift.</summary>
        private string? _rangeAnchor;

        private void ClickTreeRow(string id)
        {
            var mods = Keyboard.Modifiers;
            if (mods.HasFlag(ModifierKeys.Shift) && (_rangeAnchor ?? Scene.Selected) is { } anchor)
            {
                int a = _treeOrder.FindIndex(x => string.Equals(x, anchor, StringComparison.OrdinalIgnoreCase));
                int b = _treeOrder.FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
                if (a >= 0 && b >= 0)
                {
                    var range = _treeOrder.Skip(Math.Min(a, b)).Take(Math.Abs(a - b) + 1).ToList();
                    range.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
                    range.Insert(0, id);   // the row clicked is the active one
                    if (mods.HasFlag(ModifierKeys.Control)) range.AddRange(Scene.SelectedAll);   // Ctrl+Shift adds the range
                    Scene.SelectMany(range.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
                    return;
                }
            }

            _rangeAnchor = id;
            if (mods.HasFlag(ModifierKeys.Control)) Scene.ToggleSelect(id);
            else Scene.Select(id);
        }

        /// <summary>The selected objects that exist, the active one first.</summary>
        private List<string> SelectionIds()
        {
            var world = World;
            return world == null ? new List<string>() : Scene.SelectedAll.Where(id => world.Find(id) != null).ToList();
        }

        /// <summary>Ctrl+A: every object (the switched-off ones too, as Unity).</summary>
        private void SelectAllObjects()
        {
            if (World is not { } world || world.Objects.Count == 0) return;
            var ids = world.Objects.Select(o => o.Id).ToList();
            if (Scene.Selected != null && ids.Remove(Scene.Selected)) ids.Insert(0, Scene.Selected);
            Scene.SelectMany(ids);
        }

        /// <summary>[multiselect] Unity's Select Invert (Ctrl+I): every object that was not selected, and none that was.</summary>
        private void InvertSelection()
        {
            if (World is not { } world) return;
            var was = new HashSet<string>(Scene.SelectedAll, StringComparer.OrdinalIgnoreCase);
            var ids = world.Objects.Select(o => o.Id).Where(id => !was.Contains(id)).ToList();
            if (ids.Count == 0) Scene.Select(null);
            else Scene.SelectMany(ids);
        }

        /// <summary>[multiselect] Unity's Select Children: the object and everything mounted on it, down the tree.</summary>
        private void SelectChildren(string id)
        {
            if (World is not { } world || world.Find(id) == null) return;
            var ids = new List<string> { id };
            ids.AddRange(world.MountedOn(id).Select(o => o.Id));
            Scene.SelectMany(ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }

        /// <summary>The inspector's line under the name when more than one object is selected.</summary>
        private void MultiSelectNote()
        {
            int n = Scene.SelectedAll.Count;
            if (n > 1)
                Inspector.Children.Add(Note($"+{n - 1} nesne daha seçili (Ctrl+tık ile ekleyin ya da çıkarın): tutamaçla taşıma ve döndürme, kopyala, kes, çoğalt ve sil hepsine uygulanır; ayarlar etkin olanınkidir."));
        }
    }
}
