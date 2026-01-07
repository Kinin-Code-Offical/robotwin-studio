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
                IntPtr affinityMask1 = (IntPtr)affinityMask;

#if NET5_0_OR_GREATER
                if (OperatingSystem.IsWindows())
                {
#pragma warning disable CA1416 // Platform compatibility
                    process.ProcessorAffinity = affinityMask1;
#pragma warning restore CA1416
                }
                else if (OperatingSystem.IsLinux())
                {
#pragma warning disable CA1416 // Platform compatibility
                    process.ProcessorAffinity = affinityMask1;
#pragma warning restore CA1416
                }
#else
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
#pragma warning disable CA1416 // Platform compatibility
                    process.ProcessorAffinity = affinityMask1;
#pragma warning restore CA1416
                }
#endif
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
#pragma warning disable CA1416 // Guarded by OS checks; best-effort hardening only.
                if (timeBeginPeriod(periodMs) == 0)
                {
                    _timerActive = true;
                    _timerPeriod = periodMs;
                }
#pragma warning restore CA1416
            }

            public void Dispose()
            {
                if (!_timerActive) return;
#pragma warning disable CA1416 // Guarded by OS checks; best-effort hardening only.
                timeEndPeriod(_timerPeriod);
#pragma warning restore CA1416
                _timerActive = false;
            }
        }

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uPeriod);
    }
}
