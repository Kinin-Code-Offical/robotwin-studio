using System;
using System.Diagnostics;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>
    /// [islands] The constraints in islands: bodies that move are joined by every contact, joint row, joint point and
    /// coupling between them (a body that does not move, static, kinematic or asleep, joins nothing: it takes no impulse);
    /// each island's constraints are solved on their own, side by side on the cores, in the order they have in the whole
    /// list. Islands share no moving body, so solving them apart is solving them together: the result is the same bit for
    /// bit, side by side or one after the other. Twenty-five robots on one floor are twenty-five islands.
    /// </summary>
    public sealed partial class PhysicsScene
    {
        private int[] _islandParent = Array.Empty<int>();
        private int[] _islandOfRoot = Array.Empty<int>();
        private int _islandCount;

        // each island's constraints by index, in order: island k's are [Start[k], Start[k + 1]) of Index
        private int[] _islContactStart = Array.Empty<int>(), _islContactIndex = Array.Empty<int>();
        private int[] _islRowStart = Array.Empty<int>(), _islRowIndex = Array.Empty<int>();
        private int[] _islPointStart = Array.Empty<int>(), _islPointIndex = Array.Empty<int>();
        private int[] _islCouplingStart = Array.Empty<int>(), _islCouplingIndex = Array.Empty<int>();
        private int[] _islandOrder = Array.Empty<int>();   // heaviest first, for the cores

        // side by side or one after the other, whichever the last measurement found cheaper (the result is the same)
        private const int IslandMeasureSubsteps = 64, IslandRemeasureEvery = 2048, IslandSettleSubsteps = 256;
        private long _islandSubsteps;
        private double _islandCostInTurn = double.NaN, _islandCostSideBySide = double.NaN;
        private double _islandMeasured;
        private int _islandMeasuredCount;
        private bool _islandMeasuringSideBySide;

        /// <summary>[islands] Tests: always (true) or never (false) side by side, instead of by measurement.</summary>
        internal bool? ForceIslandsSideBySide { get; set; }

        /// <summary>[islands] How many islands the last substep solved, and whether side by side.</summary>
        public int IslandCount => _islandCount;

        public bool IslandsSideBySide { get; private set; }

        private bool Moves(int slot) => slot >= 0 && _state[slot].InvMass > 0;

        private int IslandRoot(int x)
        {
            var p = _islandParent;
            while (p[x] != x)
            {
                p[x] = p[p[x]];
                x = p[x];
            }

            return x;
        }

        private void JoinIslands(int a, int b)
        {
            if (!Moves(a) || !Moves(b)) return;
            a = IslandRoot(a);
            b = IslandRoot(b);
            if (a != b) _islandParent[Math.Max(a, b)] = Math.Min(a, b);
        }

        /// <summary>The island of a constraint on these slots: that of its first moving body; -1 when none moves.</summary>
        private int IslandOf(int a, int b, int c = -1, int d = -1)
        {
            int s = Moves(a) ? a : Moves(b) ? b : Moves(c) ? c : Moves(d) ? d : -1;
            if (s < 0) return -1;
            int root = IslandRoot(s);
            if (_islandOfRoot[root] < 0) _islandOfRoot[root] = _islandCount++;
            return _islandOfRoot[root];
        }

        /// <summary>Below this many constraints a substep is solved all at once without looking for islands (nothing to share).</summary>
        private const int IslandsFrom = 48;

        /// <summary>After the contacts, joints and couplings of the substep are prepared: the islands and their constraints.</summary>
        private void BuildIslands()
        {
            if (_contactCount + _rowCount + _pointCount + _couplingCount < IslandsFrom || WorkTeam.Shared.Workers == 0)
            {
                _islandCount = 1;   // all at once (ForIslands runs island −1)
                return;
            }

            int slots = Bodies.Count;
            if (_islandParent.Length < slots)
            {
                _islandParent = new int[slots * 2];
                _islandOfRoot = new int[slots * 2];
            }

            for (int i = 0; i < slots; i++)
            {
                _islandParent[i] = i;
                _islandOfRoot[i] = -1;
            }

            for (int i = 0; i < _contactCount; i++) JoinIslands(_contacts[i].SA, _contacts[i].SB);
            for (int i = 0; i < _rowCount; i++) JoinIslands(_rows[i].P, _rows[i].C);
            for (int i = 0; i < _pointCount; i++) JoinIslands(_points[i].P, _points[i].C);
            for (int i = 0; i < _couplingCount; i++)
            {
                ref var r = ref _couplingRows[i];
                if (r.Count > 1) JoinIslands(r.B0, r.B1);
                if (r.Count > 2) JoinIslands(r.B0, r.B2);
                if (r.Count > 3) JoinIslands(r.B0, r.B3);
            }

            _islandCount = 0;
            // islands numbered as their constraints first come; a constraint on nothing that moves goes to island 0
            var contactIsland = Scratch(ref _scratchContactIsland, _contactCount);
            for (int i = 0; i < _contactCount; i++) contactIsland[i] = IslandOf(_contacts[i].SA, _contacts[i].SB);
            var rowIsland = Scratch(ref _scratchRowIsland, _rowCount);
            for (int i = 0; i < _rowCount; i++) rowIsland[i] = IslandOf(_rows[i].P, _rows[i].C);
            var pointIsland = Scratch(ref _scratchPointIsland, _pointCount);
            for (int i = 0; i < _pointCount; i++) pointIsland[i] = IslandOf(_points[i].P, _points[i].C);
            var couplingIsland = Scratch(ref _scratchCouplingIsland, _couplingCount);
            for (int i = 0; i < _couplingCount; i++)
            {
                ref var r = ref _couplingRows[i];
                couplingIsland[i] = IslandOf(r.B0, r.Count > 1 ? r.B1 : -1, r.Count > 2 ? r.B2 : -1, r.Count > 3 ? r.B3 : -1);
            }

            if (_islandCount == 0) _islandCount = 1;
            Group(contactIsland, _contactCount, ref _islContactStart, ref _islContactIndex);
            Group(rowIsland, _rowCount, ref _islRowStart, ref _islRowIndex);
            Group(pointIsland, _pointCount, ref _islPointStart, ref _islPointIndex);
            Group(couplingIsland, _couplingCount, ref _islCouplingStart, ref _islCouplingIndex);

            if (_islandOrder.Length < _islandCount) _islandOrder = new int[_islandCount * 2];
            for (int k = 0; k < _islandCount; k++) _islandOrder[k] = k;
            Array.Sort(_islandOrder, 0, _islandCount, _islandComparer ??= new Heavier(this));
        }

        private int[] _scratchContactIsland = Array.Empty<int>(), _scratchRowIsland = Array.Empty<int>(), _scratchPointIsland = Array.Empty<int>(), _scratchCouplingIsland = Array.Empty<int>();

        private static int[] Scratch(ref int[] array, int count)
        {
            if (array.Length < count) array = new int[Math.Max(16, count * 2)];
            return array;
        }

        /// <summary>Constraint indices by island (counting sort: within an island the order of the whole list).</summary>
        private void Group(int[] island, int count, ref int[] start, ref int[] index)
        {
            if (start.Length < _islandCount + 1) start = new int[_islandCount * 2 + 2];
            if (index.Length < count) index = new int[Math.Max(16, count * 2)];
            Array.Clear(start, 0, _islandCount + 1);
            for (int i = 0; i < count; i++) start[Math.Max(0, island[i]) + 1]++;
            for (int k = 0; k < _islandCount; k++) start[k + 1] += start[k];
            Span<int> fill = _islandCount <= 256 ? stackalloc int[_islandCount] : new int[_islandCount];
            for (int k = 0; k < _islandCount; k++) fill[k] = start[k];
            for (int i = 0; i < count; i++) index[fill[Math.Max(0, island[i])]++] = i;
        }

        private int IslandWeight(int k) =>
            (_islContactStart[k + 1] - _islContactStart[k]) + (_islRowStart[k + 1] - _islRowStart[k]) + 3 * (_islPointStart[k + 1] - _islPointStart[k]);

        private Heavier? _islandComparer;

        /// <summary>Islands heaviest first (then by number).</summary>
        private sealed class Heavier : System.Collections.Generic.IComparer<int>
        {
            private readonly PhysicsScene _scene;

            internal Heavier(PhysicsScene scene) => _scene = scene;

            public int Compare(int x, int y)
            {
                int c = _scene.IslandWeight(y).CompareTo(_scene.IslandWeight(x));
                return c != 0 ? c : x.CompareTo(y);
            }
        }

        /// <summary>The range of island <paramref name="k"/>'s constraints in an index list, or all of them (k = −1).</summary>
        private static (int From, int To, int[]? Index) IslandRange(int k, int total, int[] start, int[] index) =>
            k < 0 ? (0, total, null) : (start[k], start[k + 1], index);

        // the substep's solver settings, for the passes over an island
        private (double H, double CBias, double CMass, double CImp, double JBias, double JMass, double JImp) _pass;
        private Action<int>? _iterate, _relax;

        /// <summary>The velocity iterations of island <paramref name="k"/> (all: −1), after its warm start.</summary>
        private void IterateIsland(int k)
        {
            var p = _pass;
            WarmStart(k);
            for (int it = 0; it < Iterations; it++)
            {
                SolveJoints(true, p.JBias, p.JMass, p.JImp, p.H, k);
                SolveCouplings(true, p.JBias, p.JMass, p.JImp, k);
                SolveContacts(true, p.CBias, p.CMass, p.CImp, p.H, false, k);
            }
        }

        /// <summary>The relaxation passes of island <paramref name="k"/> (all: −1), after the positions moved.</summary>
        private void RelaxIsland(int k)
        {
            var p = _pass;
            for (int it = 0; it < RelaxIterations; it++)
            {
                SolveJoints(false, p.JBias, p.JMass, p.JImp, p.H, k);
                SolveCouplings(false, p.JBias, p.JMass, p.JImp, k);
                SolveContacts(false, p.CBias, p.CMass, p.CImp, p.H, true, k);
            }
        }

        /// <summary>
        /// Runs <paramref name="pass"/> for every island, side by side on the cores when that measured cheaper, else for
        /// all at once (island −1) on this thread.
        /// </summary>
        private void ForIslands(Action<int> pass)
        {
            bool sideBySide = IslandsBesideEachOther();
            long started = Stopwatch.GetTimestamp();
            if (sideBySide)
            {
                var order = _islandOrder;
                WorkTeam.Each(_islandCount, i => pass(order[i]));
            }
            else pass(-1);

            IslandsSideBySide = sideBySide;
            NoteIslandCost(sideBySide, Stopwatch.GetTimestamp() - started);
        }

        private bool IslandsBesideEachOther()
        {
            if (_islandCount < 2 || WorkTeam.Shared.Workers == 0) return false;
            if (ForceIslandsSideBySide is { } forced) return forced;
            if (_islandSubsteps < IslandSettleSubsteps || double.IsNaN(_islandCostInTurn)) return false;
            if (double.IsNaN(_islandCostSideBySide)) return true;
            bool better = _islandCostSideBySide < 0.9 * _islandCostInTurn;
            return (_islandSubsteps - IslandSettleSubsteps) % IslandRemeasureEvery < IslandMeasureSubsteps ? !better : better;
        }

        private void NoteIslandCost(bool sideBySide, long ticks)
        {
            if (_islandCount < 2) return;
            _islandSubsteps++;
            if (_islandSubsteps <= IslandSettleSubsteps) return;
            if (sideBySide != _islandMeasuringSideBySide)
            {
                _islandMeasured = 0;
                _islandMeasuredCount = 0;
                _islandMeasuringSideBySide = sideBySide;
            }

            _islandMeasured += ticks;
            if (++_islandMeasuredCount < IslandMeasureSubsteps) return;
            if (sideBySide) _islandCostSideBySide = _islandMeasured / _islandMeasuredCount;
            else _islandCostInTurn = _islandMeasured / _islandMeasuredCount;
            _islandMeasured = 0;
            _islandMeasuredCount = 0;
        }
    }
}
