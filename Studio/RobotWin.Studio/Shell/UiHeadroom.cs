using System;
using System.Diagnostics;
using System.Windows.Threading;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// Keeps room on the UI thread while a simulation runs. A world or circuit that needs more than a frame's time
    /// would otherwise fill every frame, and WPF runs nothing below render priority while frames are due: timers
    /// (code save, autosave, the rail, sensor views, the guide, the API host, the benchmark), panel-layout saves and
    /// background work would wait for as long as the simulation runs. A beat at background priority notes when such
    /// work last got its turn; once that is longer ago than <see cref="MaxWait"/>, the next frame simulates only a
    /// sliver, the frame ends early and the waiting work runs. A heavy simulation loses roughly a tenth of its speed;
    /// a light one, which leaves time in every frame anyway, loses nothing.
    /// </summary>
    internal static class UiHeadroom
    {
        /// <summary>Longest wait for work below render priority while a simulation fills the frames (seconds).</summary>
        public const double MaxWait = 0.2;

        /// <summary>Simulation time per frame when that wait is over (seconds of wall clock).</summary>
        public const double Sliver = 0.002;

        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static DispatcherTimer? _beat;
        private static double _lastBeat;

        /// <summary>How long the simulation may run in this frame: <paramref name="full"/>, or a sliver when other work is waiting.</summary>
        public static double SimulationBudget(double full)
        {
            if (_beat == null)
            {
                // Created on the UI thread by the first frame; the interval keeps the beat cheap when nothing runs.
                _beat = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
                _beat.Tick += (_, _) => _lastBeat = Clock.Elapsed.TotalSeconds;
                _beat.Start();
                _lastBeat = Clock.Elapsed.TotalSeconds;
            }

            return Clock.Elapsed.TotalSeconds - _lastBeat > MaxWait ? Math.Min(full, Sliver) : full;
        }
    }
}
