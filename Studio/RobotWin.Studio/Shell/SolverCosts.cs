using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotWin.Studio.Shell
{
    /// <summary>A running simulation's cost by solver: the wall-clock seconds each took so far, and the simulated time covered.</summary>
    public interface ISolverCosts
    {
        /// <summary>Simulated seconds covered so far.</summary>
        double SimulatedSeconds { get; }

        /// <summary>Adds each solver's wall-clock seconds so far under its Turkish name (one of <see cref="SolverCosts.Names"/>).</summary>
        void Costs(List<(string Name, double Seconds)> into);

        /// <summary>One line about the run (its steps and sizes) in the interface's language, or null.</summary>
        string? Detail { get; }
    }

    /// <summary>
    /// [perf] Which solver holds a simulation back: two readings of its costs, apart in time, give each solver's
    /// milliseconds of computing per simulated second and its share of the whole, and how fast the simulation runs
    /// against the clock. A simulation runs in real time while the milliseconds add up to less than a thousand.
    /// </summary>
    public sealed class SolverCosts
    {
        /// <summary>Every name a session reports a solver under (Turkish source text; the language packs have each).</summary>
        public static readonly string[] Names =
        {
            "devre", "Arduino yazılımı", "ESP32 yazılımı", "radyo", "sensörler",
            "katı cisimler", "kablolar", "sıvılar", "ısı", "motorlar ve sürücüler", "hava ve gaz", "betikler", "mesafe sensörleri"
        };

        /// <summary>The labels of the indicator's solver table and of the sessions' detail lines.</summary>
        public static readonly string[] Labels =
        {
            "simülasyon hızı", "çözücü", "ms / simülasyon sn", "pay", "duraklatıldı", "bellek", "yönetilen",
            "fizik adımı", "parçacık", "alt adım", "uykuda", "kare", "en kötü", "iyi", "orta", "düşük"
        };

        public readonly record struct Row(string Name, double MsPerSimSecond, double Share);

        private ISolverCosts? _source;
        private readonly Dictionary<string, double> _last = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly List<(string Name, double Seconds)> _now = new List<(string Name, double Seconds)>();
        private double _lastSim, _lastWall;

        /// <summary>
        /// Reads <paramref name="source"/> at wall-clock time <paramref name="wall"/> (s). Returns the solvers since the
        /// last reading, costliest first (none on the first reading of a source, or while its time stands still), and the
        /// simulated seconds per wall-clock second.
        /// </summary>
        public (List<Row> Rows, double RealTime) Read(ISolverCosts? source, double wall)
        {
            var rows = new List<Row>();
            if (source == null)
            {
                _source = null;
                return (rows, 0);
            }

            _now.Clear();
            source.Costs(_now);
            double sim = source.SimulatedSeconds;
            bool fresh = !ReferenceEquals(source, _source) || sim < _lastSim;
            double dSim = sim - _lastSim, dWall = wall - _lastWall;
            if (!fresh && dSim > 0)
            {
                var spent = _now.GroupBy(c => c.Name).Select(g => (Name: g.Key, Seconds: g.Sum(c => c.Seconds) - (_last.TryGetValue(g.Key, out double s) ? s : 0))).ToList();
                double total = spent.Sum(c => Math.Max(0, c.Seconds));
                foreach (var (name, seconds) in spent.OrderByDescending(c => c.Seconds))
                    if (seconds > 0)
                        rows.Add(new Row(name, seconds * 1000 / dSim, total > 0 ? seconds / total : 0));
            }

            _source = source;
            _last.Clear();
            foreach (var (name, seconds) in _now) _last[name] = (_last.TryGetValue(name, out double s) ? s : 0) + seconds;
            _lastSim = sim;
            _lastWall = wall;
            return (rows, !fresh && dWall > 0 ? Math.Max(0, dSim) / dWall : 0);
        }
    }
}
