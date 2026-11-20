using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Mechanics
{
    // [terrain] Stroke undo/redo: only the 32×32-vertex blocks a stroke touched are kept.

    /// <summary>
    /// Undo history of terrain strokes. A stroke copies each block of vertices the first time it
    /// writes into it (heights and layer weights); at the end the new contents of the same blocks
    /// are kept for redo. Memory grows with the touched area, not with the terrain size.
    /// </summary>
    public sealed class TerrainHistory
    {
        public const int Block = 32;

        private sealed class BlockState
        {
            public int C0, R0, C1, R1;
            public float[] Heights = null!;
            public byte[]?[] Weights = null!;
            public float[]? AfterHeights;
            public byte[]?[]? AfterWeights;
        }

        private sealed class Stroke
        {
            public string Name = string.Empty;
            public readonly Dictionary<long, BlockState> Blocks = new Dictionary<long, BlockState>();
        }

        private readonly List<Stroke> _undo = new List<Stroke>();
        private readonly List<Stroke> _redo = new List<Stroke>();
        private Stroke? _open;

        /// <summary>Most strokes kept.</summary>
        public int Limit { get; set; } = 64;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public bool InStroke => _open != null;
        public string? UndoName => _undo.Count > 0 ? _undo[_undo.Count - 1].Name : null;
        public string? RedoName => _redo.Count > 0 ? _redo[_redo.Count - 1].Name : null;

        public void BeginStroke(string name)
        {
            _open = new Stroke { Name = name };
        }

        internal void Capture(TerrainMap map, int c0, int r0, int c1, int r1)
        {
            var s = _open;
            if (s == null) return;
            c0 = Math.Max(0, c0);
            r0 = Math.Max(0, r0);
            c1 = Math.Min(map.Columns - 1, c1);
            r1 = Math.Min(map.Rows - 1, r1);
            for (int br = r0 / Block; br <= r1 / Block; br++)
            for (int bc = c0 / Block; bc <= c1 / Block; bc++)
            {
                long key = ((long)br << 32) | (uint)bc;
                if (s.Blocks.ContainsKey(key)) continue;
                int bc0 = bc * Block, br0 = br * Block;
                int bc1 = Math.Min(map.Columns - 1, bc0 + Block - 1), br1 = Math.Min(map.Rows - 1, br0 + Block - 1);
                s.Blocks[key] = new BlockState
                {
                    C0 = bc0, R0 = br0, C1 = bc1, R1 = br1,
                    Heights = map.CopyHeights(bc0, br0, bc1, br1),
                    Weights = map.CopyWeights(bc0, br0, bc1, br1)
                };
            }
        }

        /// <summary>Closes the stroke; returns false when it changed nothing.</summary>
        public bool EndStroke(TerrainMap map)
        {
            var s = _open;
            _open = null;
            if (s == null || s.Blocks.Count == 0) return false;
            foreach (var b in s.Blocks.Values)
            {
                b.AfterHeights = map.CopyHeights(b.C0, b.R0, b.C1, b.R1);
                b.AfterWeights = map.CopyWeights(b.C0, b.R0, b.C1, b.R1);
            }

            _undo.Add(s);
            if (_undo.Count > Limit) _undo.RemoveAt(0);
            _redo.Clear();
            return true;
        }

        /// <summary>Forgets everything (a new terrain).</summary>
        public void Clear()
        {
            _undo.Clear();
            _redo.Clear();
            _open = null;
        }

        public string? Undo(TerrainMap map)
        {
            if (_undo.Count == 0) return null;
            var s = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            Apply(map, s, before: true);
            _redo.Add(s);
            return s.Name;
        }

        public string? Redo(TerrainMap map)
        {
            if (_redo.Count == 0) return null;
            var s = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            Apply(map, s, before: false);
            _undo.Add(s);
            return s.Name;
        }

        private static void Apply(TerrainMap map, Stroke s, bool before)
        {
            int c0 = int.MaxValue, r0 = int.MaxValue, c1 = -1, r1 = -1;
            foreach (var b in s.Blocks.Values)
            {
                map.PasteHeights(b.C0, b.R0, b.C1, b.R1, before ? b.Heights : b.AfterHeights!);
                map.PasteWeights(b.C0, b.R0, b.C1, b.R1, before ? b.Weights : b.AfterWeights!);
                c0 = Math.Min(c0, b.C0);
                r0 = Math.Min(r0, b.R0);
                c1 = Math.Max(c1, b.C1);
                r1 = Math.Max(r1, b.R1);
            }

            map.CompactLayers();
            // Normals of the neighbours change too.
            map.NotifyChanged(c0 - 1, r0 - 1, c1 + 1, r1 + 1, true, true);
        }
    }
}
