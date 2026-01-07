using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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

        [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "Best-effort hardening only; call sites are OS-guarded.")]
        private static void TrySetAffinity(long affinityMask)
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                IntPtr affinityMask1 = (IntPtr)affinityMask;

#if NET5_0_OR_GREATER
                if (OperatingSystem.IsWindows())
                {
                    typeof(Process).GetProperty("ProcessorAffinity")?.SetValue(process, affinityMask1);
                }
                else if (OperatingSystem.IsLinux())
                {
                    typeof(Process).GetProperty("ProcessorAffinity")?.SetValue(process, affinityMask1);
                }
#else
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    typeof(Process).GetProperty("ProcessorAffinity")?.SetValue(process, affinityMask1);
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
