using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using RobotTwin.CoreSim.Pcb;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [gerbv] The exported Gerber files drawn by gerbv, a third-party viewer fabs and designers use, instead of the
    /// app's own reader, and laid over the board as <see cref="GerberCheck"/> lays its own reading: a second, outside
    /// opinion that the files say what the board is. Skipped unless RW_GERBV names gerbv.exe (gerbv 2.13 or later).
    /// </summary>
    public class GerbvCrossCheckTests
    {
        private readonly ITestOutputHelper _out;

        public GerbvCrossCheckTests(ITestOutputHelper output) => _out = output;

        public sealed class GerbvFactAttribute : FactAttribute
        {
            public GerbvFactAttribute()
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RW_GERBV"))) Skip = "RW_GERBV verilmedi (gerbv.exe yolu).";
            }
        }

        [GerbvFact]
        public void GerbvSeesTheBoardTheFilesDescribe()
        {
            string exe = Environment.GetEnvironmentVariable("RW_GERBV")!;
            var board = GerberCheckTests.RoutedBoard();
            var files = GerberExporter.Generate(board, "motor");
            string dir = Path.Combine(Path.GetTempPath(), "rw-gerbv-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                // gerbv draws at most 2000 pixels a side: the finest whole dpi that fits the board and the check's 2 mm margins
                double dpi = Math.Floor(1998 * 25.4 / (Math.Max(board.Width, board.Height) + 4));
                _out.WriteLine($"{dpi} dpi, {25.4 / dpi * 1000:0.#} µm pixels");
                var report = GerberCheck.Check(board, files, (name, text, frame) => Render(exe, dir, name, text, frame), 25.4 / dpi);
                foreach (var l in report.Layers)
                    _out.WriteLine($"{l.File} [{l.Function}] board {l.ExpectedArea:0.00} / gerbv {l.FileArea:0.00} mm² {(l.Ok ? "ok" : string.Join("; ", l.Differences.Take(3).Concat<object>(l.Warnings.Take(3))))}");
                Assert.Equal(9, report.Layers.Count);
                Assert.All(report.Layers, l => Assert.True(l.Ok, l.File + ": " + string.Join("; ", l.Differences.Take(3).Concat<object>(l.Warnings.Take(3)))));
                Assert.All(report.Layers.Where(l => l.Function.StartsWith("Copper", StringComparison.Ordinal)), l => Assert.True(l.FileArea > 100, l.File));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [GerbvFact]
        public void GerbvCatchesAFileThatLostATrack()
        {
            // the cross-check has teeth: a copper file missing one stroke is told apart, where it is
            string exe = Environment.GetEnvironmentVariable("RW_GERBV")!;
            var board = GerberCheckTests.RoutedBoard();
            var files = GerberExporter.Generate(board, "motor");
            string key = files.Keys.Single(k => k.EndsWith("F_Cu.gtl", StringComparison.Ordinal));
            var lines = files[key].Split('\n').ToList();
            int stroke = lines.FindIndex(l => l.EndsWith("D01*", StringComparison.Ordinal));
            Assert.True(stroke > 0);
            lines.RemoveAt(stroke);
            files[key] = string.Join("\n", lines);
            string dir = Path.Combine(Path.GetTempPath(), "rw-gerbv-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                double dpi = Math.Floor(1998 * 25.4 / (Math.Max(board.Width, board.Height) + 4));
                var report = GerberCheck.Check(board, new Dictionary<string, string> { [key] = files[key] }, (name, text, frame) => Render(exe, dir, name, text, frame), 25.4 / dpi);
                var layer = Assert.Single(report.Layers);
                _out.WriteLine(string.Join("; ", layer.Differences.Take(3)));
                Assert.Contains(layer.Differences, d => d.Missing);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        /// <summary>One file through gerbv to a PNG framed exactly as <paramref name="frame"/> (mm, 1:1 pixels), then into its bits.</summary>
        private static string? Render(string exe, string dir, string name, string text, GerberRaster frame)
        {
            string src = Path.Combine(dir, name), png = src + ".png";
            File.WriteAllText(src, text);
            string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
            };
            foreach (var a in new[]
                     {
                         "--export=png", "--output=" + png, "--dpi=" + F(25.4 / frame.Pixel), "--units=mm",
                         $"--origin={F(frame.X0)};{F(frame.Y0)}", $"--window={frame.W}x{frame.H}", "--border=0",
                         "--background=#000000", "--foreground=#FFFFFFFF", "--quiet", src,
                     })
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi)!;
            string err = p.StandardError.ReadToEnd();
            p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(120_000))
            {
                p.Kill();
                return "gerbv yanıt vermedi";
            }

            if (p.ExitCode != 0 || !File.Exists(png)) return $"gerbv çıkış kodu {p.ExitCode}: {err.Trim()}";
            var (w, h, gray) = Png.Gray(File.ReadAllBytes(png));
            if (w != frame.W || h != frame.H) return $"gerbv resmi {w}×{h}, beklenen {frame.W}×{frame.H}";
            for (int r = 0; r < h; r++)
                for (int c = 0; c < w; c++)
                    frame.Bits[r * w + c] = gray[(h - 1 - r) * w + c] > 127;   // PNG rows run top down, the frame's bottom up
            return null;
        }

        /// <summary>Just enough PNG (8-bit, not interlaced) to read gerbv's output: each pixel's brightness.</summary>
        private static class Png
        {
            public static (int W, int H, byte[] Gray) Gray(byte[] file)
            {
                int pos = 8, w = 0, h = 0, depth = 0, type = 0;
                byte[]? palette = null;
                var data = new MemoryStream();
                while (pos + 8 <= file.Length)
                {
                    int len = (file[pos] << 24) | (file[pos + 1] << 16) | (file[pos + 2] << 8) | file[pos + 3];
                    string kind = System.Text.Encoding.ASCII.GetString(file, pos + 4, 4);
                    int body = pos + 8;
                    if (kind == "IHDR")
                    {
                        w = (file[body] << 24) | (file[body + 1] << 16) | (file[body + 2] << 8) | file[body + 3];
                        h = (file[body + 4] << 24) | (file[body + 5] << 16) | (file[body + 6] << 8) | file[body + 7];
                        depth = file[body + 8];
                        type = file[body + 9];
                        if (depth != 8 || file[body + 12] != 0) throw new NotSupportedException($"PNG: derinlik {depth}, geçmeli {file[body + 12]}");
                    }
                    else if (kind == "PLTE") palette = file.Skip(body).Take(len).ToArray();
                    else if (kind == "IDAT") data.Write(file, body, len);
                    else if (kind == "IEND") break;
                    pos = body + len + 4;
                }

                int channels = type switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new NotSupportedException("PNG türü " + type) };
                int stride = w * channels;
                data.Position = 0;
                using var z = new ZLibStream(data, CompressionMode.Decompress);
                var raw = new byte[(stride + 1) * h];
                int got = 0, n;
                while (got < raw.Length && (n = z.Read(raw, got, raw.Length - got)) > 0) got += n;
                var cur = new byte[stride];
                var prev = new byte[stride];
                var gray = new byte[w * h];
                for (int r = 0; r < h; r++)
                {
                    int filter = raw[r * (stride + 1)];
                    Array.Copy(raw, r * (stride + 1) + 1, cur, 0, stride);
                    for (int i = 0; i < stride; i++)
                    {
                        int a = i >= channels ? cur[i - channels] : 0, b = prev[i], c = i >= channels ? prev[i - channels] : 0;
                        int add = filter switch
                        {
                            0 => 0,
                            1 => a,
                            2 => b,
                            3 => (a + b) / 2,
                            4 => Paeth(a, b, c),
                            _ => throw new NotSupportedException("PNG süzgeci " + filter),
                        };
                        cur[i] = (byte)(cur[i] + add);
                    }

                    for (int c = 0; c < w; c++)
                    {
                        int o = c * channels;
                        gray[r * w + c] = type switch
                        {
                            0 or 4 => cur[o],
                            3 => (byte)((palette![cur[o] * 3] + palette[cur[o] * 3 + 1] + palette[cur[o] * 3 + 2]) / 3),
                            _ => (byte)((cur[o] + cur[o + 1] + cur[o + 2]) / 3),
                        };
                    }

                    (prev, cur) = (cur, prev);
                }

                return (w, h, gray);
            }

            private static int Paeth(int a, int b, int c)
            {
                int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
            }
        }
    }
}
