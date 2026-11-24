using System;
using System.Diagnostics;
using System.Threading;

namespace RobotTwin.CoreSim.Mechanics
{
    /// <summary>
    /// [perf] A team of worker threads that stays awake between the passes of a solver step. A pass over a few thousand
    /// particles takes tens of microseconds; handing each one to the thread pool woke its threads again every time,
    /// which cost about as much as the pass (a liquid substep makes a dozen of them). The workers here spin for a
    /// moment after a pass and pick up the next one at once, then sleep when the solver stops handing out work.
    /// Work is taken in chunks from a shared counter, so slower cores (efficiency cores) simply take fewer chunks.
    /// One caller at a time: a second caller (another thread, or a pass inside a pass) is told to do it another way.
    /// </summary>
    internal sealed class WorkTeam
    {
        /// <summary>The process's team: a worker for every core but the caller's own.</summary>
        public static readonly WorkTeam Shared = new WorkTeam(Environment.ProcessorCount - 1);

        /// <summary>How long a worker waits for the next pass before it sleeps.</summary>
        private static readonly long SpinTicks = Stopwatch.Frequency / 500;   // 2 ms

        /// <summary>A pass, published whole: a worker reads its number, work and size together.</summary>
        private sealed class Pass
        {
            public Pass(int number, Action<int, int> body, int count, int chunk)
            {
                Number = number;
                Body = body;
                Count = count;
                Chunk = chunk;
            }

            public readonly int Number;
            public readonly Action<int, int> Body;
            public readonly int Count, Chunk;
        }

        private readonly int _workers;
        private Thread[]? _threads;
        private int _busy;   // 1 while a caller runs a pass
        private readonly object _sleep = new object();   // sleeping workers wait on this
        private Pass? _current;

        // Chunks are taken through _ticket: the pass number in the high half, the next index in the low half, so a
        // worker still finishing an earlier pass can never take a chunk of this one.
        private long _ticket;
        private int _number;
        private int _done;
        private Exception? _error;

        private WorkTeam(int workers)
        {
            _workers = Math.Max(0, workers);
        }

        /// <summary>Workers besides the caller.</summary>
        public int Workers => _workers;

        /// <summary>
        /// Runs <paramref name="body"/>(from, to) over [0, <paramref name="count"/>) in chunks of
        /// <paramref name="chunk"/>, on the caller's thread and the team's. False (nothing run) when the team is busy
        /// (another thread, or this pass's own body asking) or there is no one to share with.
        /// </summary>
        public bool TryRun(int count, int chunk, Action<int, int> body)
        {
            if (_workers == 0 || Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return false;
            try
            {
                if (count <= 0) return true;
                EnsureThreads();
                _error = null;
                Volatile.Write(ref _done, 0);
                _number = unchecked(_number + 1);
                if (_number == 0) _number = 1;   // 0 is "no pass yet"
                var pass = new Pass(_number, body, count, Math.Max(1, chunk));
                Volatile.Write(ref _current, pass);
                lock (_sleep)
                {
                    Volatile.Write(ref _ticket, (long)pass.Number << 32);
                    Monitor.PulseAll(_sleep);
                }

                Work(pass);
                var backoff = new Backoff();
                while (Volatile.Read(ref _done) < count) backoff.Pause();
                if (_error != null) throw new AggregateException(_error);
                return true;
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
            }
        }

        /// <summary>
        /// <paramref name="body"/>(i) for every i in [0, <paramref name="count"/>), <paramref name="chunk"/> at a time,
        /// on the shared team, or on the thread pool when the team is busy.
        /// </summary>
        public static void For(int count, int chunk, Action<int> body)
        {
            if (Shared.TryRun(count, chunk, (from, to) =>
                {
                    for (int i = from; i < to; i++) body(i);
                }))
                return;
            System.Threading.Tasks.Parallel.For(0, count, body);
        }

        /// <summary>Takes chunks of <paramref name="pass"/> until there are none left (or a later pass has begun).</summary>
        private void Work(Pass pass)
        {
            while (true)
            {
                long t = Volatile.Read(ref _ticket);
                if ((int)(t >> 32) != pass.Number) return;
                int from = (int)(t & 0xFFFFFFFF);
                if (from >= pass.Count) return;
                int to = Math.Min(pass.Count, from + pass.Chunk);
                if (Interlocked.CompareExchange(ref _ticket, ((long)pass.Number << 32) | (uint)to, t) != t) continue;
                try
                {
                    pass.Body(from, to);
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref _error, ex, null);
                }
                finally
                {
                    Interlocked.Add(ref _done, to - from);
                }
            }
        }

        /// <summary>A short busy wait that grows, then gives the core to any thread that wants it (never sleeps).</summary>
        private struct Backoff
        {
#if NET5_0_OR_GREATER
            private SpinWait _spin;

            public void Pause() => _spin.SpinOnce(-1);   // .NET's own back-off, without its Sleep(1)
#else
            private int _n;

            public void Pause()
            {
                if (_n < 6) Thread.SpinWait(4 << _n++);
                else Thread.Yield();
            }
#endif
        }

        private void EnsureThreads()
        {
            if (_threads != null) return;
            _threads = new Thread[_workers];
            for (int i = 0; i < _workers; i++)
            {
                _threads[i] = new Thread(Loop) { IsBackground = true, Name = "RobotWin solver worker " + (i + 1) };
                _threads[i].Start();
            }
        }

        private void Loop()
        {
            int seen = 0;
            while (true)
            {
                // Wait for the next pass: spin a moment (the solver's next pass is usually microseconds away), then sleep.
                long start = Stopwatch.GetTimestamp();
                var backoff = new Backoff();
                int now;
                while ((now = (int)(Volatile.Read(ref _ticket) >> 32)) == seen)
                {
                    if (Stopwatch.GetTimestamp() - start < SpinTicks)
                    {
                        backoff.Pause();
                        continue;
                    }

                    lock (_sleep)
                    {
                        while ((int)(Volatile.Read(ref _ticket) >> 32) == seen) Monitor.Wait(_sleep);
                    }
                }

                seen = now;
                // The pass the ticket names (a newer one may already be published: the ticket will name it next).
                if (Volatile.Read(ref _current) is { } pass && pass.Number == now) Work(pass);
            }
        }
    }
}
