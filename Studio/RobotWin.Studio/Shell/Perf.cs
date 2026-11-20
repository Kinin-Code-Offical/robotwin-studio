using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// Lightweight frame profiler: real frame rate from WPF's render loop plus named sections
    /// (<c>using (Perf.Measure("fizik")) { … }</c>) averaged over a sliding window. Costs a
    /// Stopwatch read per section, so it can stay on in release builds.
    /// </summary>
    public static class Perf
    {
        private sealed class Section
        {
            public double Sum;
            public double Max;
            public int Count;
            public double Avg;
            public double Peak;
        }

        private static readonly Dictionary<string, Section> _sections = new Dictionary<string, Section>(StringComparer.Ordinal);
        private static readonly Stopwatch _clock = Stopwatch.StartNew();
        private static readonly Queue<double> _frames = new Queue<double>();
        private static double _lastFrame = -1;
        private static double _windowStart;
        private static bool _hooked;

        /// <summary>Frames per second over the last second.</summary>
        public static double Fps { get; private set; }

        /// <summary>Average and worst frame time (ms) over the last second.</summary>
        public static double FrameMs { get; private set; }
        public static double FrameMaxMs { get; private set; }

        /// <summary>Raised about twice a second after the averages were refreshed.</summary>
        public static event EventHandler? Updated;

        public static void Start()
        {
            if (_hooked) return;
            _hooked = true;
            CompositionTarget.Rendering += OnRendering;
        }

        private static void OnRendering(object? sender, EventArgs e)
        {
            double now = _clock.Elapsed.TotalMilliseconds;
            if (_lastFrame >= 0)
            {
                double dt = now - _lastFrame;
                _recFrames?.Add(dt);
                _frames.Enqueue(dt);
                while (_frames.Count > 240) _frames.Dequeue();
            }

            _lastFrame = now;
            if (now - _windowStart < 500) return;
            _windowStart = now;

            // Keep about one second of frames.
            double total = 0;
            var recent = _frames.Reverse().TakeWhile(f => (total += f) <= 1000).ToList();
            if (recent.Count > 0)
            {
                FrameMs = recent.Average();
                FrameMaxMs = recent.Max();
                Fps = 1000.0 / Math.Max(0.001, FrameMs);
            }

            foreach (var s in _sections.Values)
            {
                s.Avg = s.Count == 0 ? 0 : s.Sum / s.Count;
                s.Peak = s.Max;
                s.Sum = 0;
                s.Max = 0;
                s.Count = 0;
            }

            Updated?.Invoke(null, EventArgs.Empty);
        }

        public readonly struct Scope : IDisposable
        {
            private readonly string _name;
            private readonly long _start;

            internal Scope(string name)
            {
                _name = name;
                _start = Stopwatch.GetTimestamp();
            }

            public void Dispose() => Add(_name, (Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency);
        }

        public static Scope Measure(string name) => new Scope(name);

        // ------------------------------------------------------------------ benchmark recording

        private static List<double>? _recFrames;
        private static Dictionary<string, List<double>>? _recSections;

        /// <summary>Starts collecting every frame time and section call (for --bench).</summary>
        public static void BeginRecording()
        {
            _recFrames = new List<double>();
            _recSections = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        }

        /// <summary>Stops collecting and summarises: fps, mean / 95th percentile / worst frame, per-section mean and worst.</summary>
        public static string EndRecording()
        {
            var frames = _recFrames ?? new List<double>();
            var sections = _recSections ?? new Dictionary<string, List<double>>();
            _recFrames = null;
            _recSections = null;
            if (frames.Count < 2) return "kare yok";
            var sorted = frames.OrderBy(f => f).ToList();
            double mean = frames.Average(), p95 = sorted[(int)(0.95 * (sorted.Count - 1))], worst = sorted[^1];
            var sb = new StringBuilder($"{1000 / mean,5:0} FPS  kare ort {mean,5:0.0} ms  p95 {p95,5:0.0}  en kötü {worst,5:0.0}");
            foreach (var kv in sections.OrderByDescending(kv => kv.Value.Sum()))
            {
                sb.Append($"  | {kv.Key} {kv.Value.Average():0.00}/{kv.Value.Max():0.0} ms");
            }

            return sb.ToString();
        }

        public static void Add(string name, double ms)
        {
            if (_recSections != null)
            {
                if (!_recSections.TryGetValue(name, out var list)) _recSections[name] = list = new List<double>();
                list.Add(ms);
            }

            if (!_sections.TryGetValue(name, out var s))
            {
                s = new Section();
                _sections[name] = s;
            }

            s.Sum += ms;
            s.Count++;
            if (ms > s.Max) s.Max = ms;
        }

        /// <summary>Average (and worst) time per call of a section in the last half second, or 0.</summary>
        public static (double Avg, double Peak) Of(string name) => _sections.TryGetValue(name, out var s) ? (s.Avg, s.Peak) : (0, 0);

        public static IEnumerable<(string Name, double Avg, double Peak)> Sections =>
            _sections.Where(kv => kv.Value.Avg > 0.0005 || kv.Value.Peak > 0.0005).OrderByDescending(kv => kv.Value.Avg).Select(kv => (kv.Key, kv.Value.Avg, kv.Value.Peak));

        public static string Report()
        {
            var sb = new StringBuilder();
            sb.Append($"{Fps:0} FPS · kare {FrameMs:0.0} ms (en kötü {FrameMaxMs:0.0})");
            foreach (var (name, avg, peak) in Sections) sb.Append($" · {name} {avg:0.00}/{peak:0.0} ms");
            return sb.ToString();
        }
    }

    /// <summary>Small overlay with FPS, frame time and the costliest sections (Ctrl+Shift+F).</summary>
    public sealed class PerfOverlay : Border
    {
        private readonly TextBlock _text = new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11, Foreground = Brushes.White, LineHeight = 15 };

        public PerfOverlay()
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x14, 0x14, 0x16));
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x32, 0x32, 0x37));
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(6);
            Padding = new Thickness(10, 6, 10, 7);
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Bottom;
            Margin = new Thickness(0, 0, 14, 38);
            IsHitTestVisible = false;
            Child = _text;
            Visibility = Visibility.Collapsed;
            Perf.Updated += (_, _) =>
            {
                if (Visibility != Visibility.Visible) return;
                var sb = new StringBuilder();
                var color = Perf.Fps >= 55 ? "iyi" : Perf.Fps >= 30 ? "orta" : "düşük";
                sb.AppendLine($"{Perf.Fps,5:0} FPS  ({color})");
                sb.AppendLine($"kare  {Perf.FrameMs,6:0.0} ms   en kötü {Perf.FrameMaxMs,5:0.0}");
                foreach (var (name, avg, peak) in Perf.Sections.Take(8)) sb.AppendLine($"{name,-10}{avg,6:0.00} ms   en kötü {peak,5:0.0}");
                _text.Text = sb.ToString().TrimEnd();
                _text.Foreground = Perf.Fps >= 55 ? new SolidColorBrush(Color.FromRgb(0xC8, 0xF0, 0xD8)) : Perf.Fps >= 30 ? new SolidColorBrush(Color.FromRgb(0xF0, 0xDC, 0xA8)) : new SolidColorBrush(Color.FromRgb(0xF4, 0xB8, 0xB8));
            };
        }
    }
}
