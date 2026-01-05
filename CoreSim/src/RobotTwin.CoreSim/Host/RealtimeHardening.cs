using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace RobotTwin.CoreSim.Host
{
    internal static class RealtimeHardening
    {
        public static IDisposable? TryEnable(RealtimeHardeningOptions? options)
        {
            if (options == null || !options.Enabled)
            {
                return null;
            }

            var scope = new RealtimeHardeningScope();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (options.UseHighPriority)
                {
                    TrySetProcessPriority(ProcessPriorityClass.High);
                    TrySetThreadPriority(ThreadPriority.Highest);
                }

                if (options.AffinityMask.HasValue)
                {
                    TrySetAffinity(options.AffinityMask.Value);
                }

                if (options.UseHighResolutionTimer)
                {
                    scope.TryBeginTimerResolution(1);
                }
            }
            return scope;
        }

        private static void TrySetProcessPriority(ProcessPriorityClass priority)
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                process.PriorityClass = priority;
            }
            catch
            {
                // Best-effort only.
            }
        }

        private static void TrySetThreadPriority(ThreadPriority priority)
        {
            try
            {
                Thread.CurrentThread.Priority = priority;
            }
            catch
            {
                // Best-effort only.
            }
        }

        private static void TrySetAffinity(long affinityMask)
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                process.ProcessorAffinity = (IntPtr)affinityMask;
            }
            catch
            {
                // Best-effort only.
            }
        }

        private sealed class RealtimeHardeningScope : IDisposable
        {
            private bool _timerActive;
            private uint _timerPeriod;

            public void TryBeginTimerResolution(uint periodMs)
            {
                if (_timerActive) return;
                if (timeBeginPeriod(periodMs) == 0)
                {
                    _timerActive = true;
                    _timerPeriod = periodMs;
                }
            }

            public void Dispose()
            {
                if (!_timerActive) return;
                timeEndPeriod(_timerPeriod);
                _timerActive = false;
            }
        }

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uPeriod);
    }
}
