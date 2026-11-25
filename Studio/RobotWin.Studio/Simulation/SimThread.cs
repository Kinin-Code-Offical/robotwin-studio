using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Input;
using System.Windows.Threading;

namespace RobotWin.Studio.Simulation
{
    /// <summary>A running simulation that the simulation thread advances (a world, a circuit).</summary>
    internal interface ISimThreadWork
    {
        /// <summary>
        /// Called on the simulation thread with <see cref="SimThread.Gate"/> held: catch up with the wall clock for at most
        /// <paramref name="maxWall"/> seconds of computing, stopping early when <see cref="SimThread.UiWaiting"/>. True while
        /// it is still behind and wants the next turn at once.
        /// </summary>
        bool AdvanceOnSimThread(double maxWall);

        /// <summary>An error the simulation thread caught in <see cref="AdvanceOnSimThread"/>: stop and say so (UI thread).</summary>
        void FailedOnSimThread(Exception error);
    }

    /// <summary>
    /// [perf] The live simulations (a world, a circuit) advance on their own thread instead of inside the UI thread's frames,
    /// so a heavy one takes a core of its own and the window keeps its frame rate. One lock, <see cref="Gate"/>, keeps the
    /// two threads apart. The simulation thread holds it while it advances, in pieces of at most a couple of milliseconds.
    /// The UI thread holds it whenever it handles something: input being routed, a timer, posted work, a binding — all of
    /// it, so the pages, panels and windows read and change a running simulation exactly as before, without knowing there
    /// is a thread. Without it the UI thread only waits for the next message or draws: WPF's layout and render pass, whose
    /// frame handlers and controls that show a simulation take <see cref="Hold"/> around what they read. So the simulation
    /// runs beside the drawing, and in the idle time between frames.
    /// </summary>
    internal static class SimThread
    {
        /// <summary>Held by whichever thread is working on a running simulation.</summary>
        public static readonly object Gate = new object();

        /// <summary>Longest piece the simulation thread advances without letting the UI thread in (seconds of computing).</summary>
        public const double MaxPiece = 0.002;

        private static readonly List<ISimThreadWork> Work = new List<ISimThreadWork>();   // under the Gate
        private static readonly AutoResetEvent WakeUp = new AutoResetEvent(false);
        private static Thread? _thread;
        private static volatile bool _uiWaiting;
        private static Dispatcher? _ui;
        private static int _operations;   // UI thread: posted work in progress (not drawing)
        private static int _inputs;       // UI thread: input events being routed

        [ThreadStatic] private static bool _isSimThread;

        /// <summary>True on the simulation thread (code shared with the UI thread queues what only the UI may do).</summary>
        public static bool OnSimThread => _isSimThread;

        /// <summary>The UI thread is waiting for the gate: the simulation thread ends its piece at the next step.</summary>
        public static bool UiWaiting => _uiWaiting;

        private static volatile bool _behind;

        /// <summary>
        /// A running simulation could not keep up with the clock in its last turn: drawing that reads it should leave it
        /// more of the time (see <see cref="FrameDue"/>).
        /// </summary>
        public static bool Behind => _behind;

        /// <summary>Share of the time that frames reading a simulation may take while it is behind.</summary>
        public const double DrawShare = 0.25;

        /// <summary>
        /// Whether a frame that reads the simulation should be built now: always, unless the simulation is behind and the
        /// last such frame, which took <paramref name="lastCost"/> seconds, began less than <c>lastCost / DrawShare</c> ago.
        /// In between, a view redraws what it has (the camera still moves).
        /// </summary>
        public static bool FrameDue(long lastStart, double lastCost) =>
            !_behind || (Stopwatch.GetTimestamp() - lastStart) / (double)Stopwatch.Frequency >= lastCost / DrawShare;

        /// <summary>
        /// Runs simulations on the thread (Ayarlar → Simülasyon; off: inside the UI thread's frames, as before). Only in the
        /// app, whose start-up installs the UI thread's hold on the gate: tests and tools drive the sessions themselves.
        /// </summary>
        public static bool Enabled => _ui != null && Shell.AppSettings.Current.SimulationThread;

        /// <summary>
        /// Watches the UI thread's work from the start (called once, on the UI thread, by the app's start-up): from then on
        /// it holds the gate while it handles anything but drawing.
        /// </summary>
        public static void Install(Dispatcher ui)
        {
            if (_ui != null) return;
            _ui = ui;
            ui.Hooks.OperationStarted += (_, e) =>
            {
                if (Handles(e.Operation)) Busy(ref _operations, +1);
            };
            ui.Hooks.OperationCompleted += (_, e) =>
            {
                if (Handles(e.Operation)) Busy(ref _operations, -1);
            };
            InputManager.Current.PreNotifyInput += (_, _) => Busy(ref _inputs, +1);
            InputManager.Current.PostNotifyInput += (_, _) => Busy(ref _inputs, -1);
        }

        /// <summary>WPF draws at render priority (the frame handlers, layout, rendering): that is what may run beside a simulation.</summary>
        private static bool Handles(DispatcherOperation op) => op.Priority != DispatcherPriority.Render;

        private static void Busy(ref int counter, int delta)
        {
            // Work that was already running when the hooks were installed ends without having started: ignore it.
            if (delta < 0 && counter == 0) return;
            bool before = _operations + _inputs > 0;
            counter += delta;
            bool now = _operations + _inputs > 0;
            if (!before && now) Take();
            else if (before && !now) Monitor.Exit(Gate);
        }

        private static void Take()
        {
            if (Monitor.IsEntered(Gate))
            {
                Monitor.Enter(Gate);
                return;
            }

            _uiWaiting = true;
            try
            {
                Monitor.Enter(Gate);
            }
            finally
            {
                _uiWaiting = false;
            }
        }

        /// <summary>
        /// An input handler threw and the app carried on (App.OnUnhandled): the input it was routing never finishes, so its
        /// hold on the gate is given back here.
        /// </summary>
        public static void AfterUnhandledException()
        {
            if (_ui == null || !_ui.CheckAccess() || _inputs == 0) return;
            bool before = _operations + _inputs > 0;
            _inputs = 0;
            if (before && _operations == 0) Monitor.Exit(Gate);
        }

        /// <summary>
        /// Keeps the simulation thread out until disposed: around what drawing code reads from a running simulation (frame
        /// handlers, OnRender), and around starting and stopping one. Re-entrant; free on the simulation thread itself.
        /// </summary>
        public static Holding Hold()
        {
            if (_isSimThread) return default;
            Take();
            return new Holding(true);
        }

        public readonly struct Holding : IDisposable
        {
            private readonly bool _held;

            public Holding(bool held) => _held = held;

            public void Dispose()
            {
                if (_held) Monitor.Exit(Gate);
            }
        }

        /// <summary>Starts advancing a simulation (with the gate held by the caller or taken here).</summary>
        public static void Add(ISimThreadWork work)
        {
            using (Hold())
            {
                if (!Work.Contains(work)) Work.Add(work);
                if (_thread == null)
                {
                    // Normal priority, like the UI thread: on a computer with few cores a busy simulation must not starve it.
                    _thread = new Thread(Loop) { IsBackground = true, Name = "RobotWin simülasyon" };
                    _thread.Start();
                }
            }

            WakeUp.Set();
        }

        public static void Remove(ISimThreadWork work)
        {
            using (Hold()) Work.Remove(work);
        }

        /// <summary>A frame was drawn, a run resumed: catch up with the clock now rather than at the next timeout.</summary>
        public static void Kick() => WakeUp.Set();

        private static void Loop()
        {
            _isSimThread = true;
            var errors = new List<(ISimThreadWork Work, Exception Error)>();
            while (true)
            {
                // The lock is not fair: a UI thread asking for it goes first.
                while (_uiWaiting) Thread.Yield();
                bool behind = false, any;
                lock (Gate)
                {
                    any = Work.Count > 0;
                    for (int i = 0; i < Work.Count; i++)
                    {
                        try
                        {
                            behind |= Work[i].AdvanceOnSimThread(MaxPiece / Math.Max(1, Work.Count));
                        }
                        catch (Exception ex) when (!(ex is OutOfMemoryException || ex is StackOverflowException || ex is ThreadAbortException))
                        {
                            // A bug in a solver: that run stops and the UI reports it like any other error.
                            errors.Add((Work[i], ex));
                        }
                    }

                    foreach (var (work, _) in errors) Work.Remove(work);
                }

                foreach (var (work, error) in errors) _ui?.BeginInvoke(new Action(() => work.FailedOnSimThread(error)));
                errors.Clear();
                _behind = behind;
                // Ahead of the clock: until the next frame asks (or 15 ms, for a minimised window that draws none).
                if (!behind) WakeUp.WaitOne(any ? 15 : Timeout.Infinite);
            }
        }
    }
}
