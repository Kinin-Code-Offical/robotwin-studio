using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RobotTwin.CoreSim.Sandbox.Sensing
{
    /// <summary>Pixel format of a camera frame (esp32-camera pixformat_t order where it matters).</summary>
    public enum CameraPixelFormat
    {
        Rgb565 = 0,
        Yuv422 = 1,
        Grayscale = 2,
        Jpeg = 3,
        Rgb888 = 4
    }

    /// <summary>A frame size (esp32-camera framesize_t index, name, pixels).</summary>
    public readonly struct CameraFrameSize
    {
        public CameraFrameSize(int index, string name, int width, int height)
        {
            Index = index;
            Name = name;
            Width = width;
            Height = height;
        }

        public int Index { get; }
        public string Name { get; }
        public int Width { get; }
        public int Height { get; }
        public int Pixels => Width * Height;
        public override string ToString() => $"{Name} {Width}×{Height}";
    }

    /// <summary>A sensor readout mode: largest frame it delivers and its frame rate.</summary>
    public readonly struct CameraMode
    {
        public CameraMode(int width, int height, double fps)
        {
            Width = width;
            Height = height;
            Fps = fps;
        }

        public int Width { get; }
        public int Height { get; }
        public double Fps { get; }
    }

    /// <summary>
    /// Datasheet description of a camera: sensor resolution, pixel size, lens field of view,
    /// readout modes with their frame rates, noise and exposure limits.
    /// </summary>
    public sealed class CameraSpec
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Sensor { get; set; } = string.Empty;
        public int NativeWidth { get; set; }
        public int NativeHeight { get; set; }

        /// <summary>Pixel pitch (µm).</summary>
        public double PixelMicrons { get; set; }

        /// <summary>Horizontal field of view of the stock lens (degrees).</summary>
        public double HorizontalFovDeg { get; set; }

        /// <summary>Readout modes, smallest-first is not required; the fastest mode that covers the frame is used.</summary>
        public List<CameraMode> Modes { get; set; } = new List<CameraMode>();

        /// <summary>Full-well capacity (e⁻) and read noise (e⁻ rms) of a pixel.</summary>
        public double FullWell { get; set; } = 6000;

        public double ReadNoise { get; set; } = 8;

        /// <summary>Largest analogue × digital gain the auto-exposure uses.</summary>
        public double MaxGain { get; set; } = 16;

        /// <summary>Rolling shutter: fraction of the frame period spent reading rows out (0 = global shutter).</summary>
        public double ReadoutFraction { get; set; } = 0.9;

        /// <summary>"DVP" (parallel, ESP32), "USB", "CSI".</summary>
        public string Interface { get; set; } = "DVP";

        public double SupplyVoltage { get; set; } = 3.3;
        public double SupplyCurrent { get; set; } = 0.04;

        /// <summary>Default frame size (index into <see cref="CameraModels.FrameSizes"/>).</summary>
        public int DefaultFrameSize { get; set; } = 8;

        public int MaxFrameSize { get; set; } = 8;

        /// <summary>Frame rate the sensor delivers at a frame size (fastest mode that covers it).</summary>
        public double SensorFps(int width, int height)
        {
            double best = 0;
            foreach (var m in Modes)
            {
                if (m.Width >= width && m.Height >= height) best = Math.Max(best, m.Fps);
            }

            return best > 0 ? best : Modes.Count > 0 ? Modes.Min(m => m.Fps) : 30;
        }

        /// <summary>Vertical field of view for an aspect ratio, from the horizontal one (pinhole).</summary>
        public double VerticalFovDeg(double hfovDeg, int width, int height) =>
            2 * Math.Atan(Math.Tan(hfovDeg * Math.PI / 360) * height / Math.Max(1, width)) * 180 / Math.PI;

        /// <summary>Relative sensitivity per pixel (light gathered ∝ pixel area), 1 for a 2.2 µm pixel.</summary>
        public double PixelSensitivity => PixelMicrons * PixelMicrons / (2.2 * 2.2);
    }

    /// <summary>Camera catalogue and the frame-size / frame-rate / bandwidth arithmetic.</summary>
    public static class CameraModels
    {
        /// <summary>esp32-camera frame sizes (framesize_t order).</summary>
        public static readonly CameraFrameSize[] FrameSizes =
        {
            new CameraFrameSize(0, "96X96", 96, 96),
            new CameraFrameSize(1, "QQVGA", 160, 120),
            new CameraFrameSize(2, "QCIF", 176, 144),
            new CameraFrameSize(3, "HQVGA", 240, 176),
            new CameraFrameSize(4, "240X240", 240, 240),
            new CameraFrameSize(5, "QVGA", 320, 240),
            new CameraFrameSize(6, "CIF", 400, 296),
            new CameraFrameSize(7, "HVGA", 480, 320),
            new CameraFrameSize(8, "VGA", 640, 480),
            new CameraFrameSize(9, "SVGA", 800, 600),
            new CameraFrameSize(10, "XGA", 1024, 768),
            new CameraFrameSize(11, "HD", 1280, 720),
            new CameraFrameSize(12, "SXGA", 1280, 1024),
            new CameraFrameSize(13, "UXGA", 1600, 1200),
            new CameraFrameSize(14, "FHD", 1920, 1080),
            new CameraFrameSize(15, "QSXGA", 3280, 2464)
        };

        public static readonly IReadOnlyList<CameraSpec> All = new List<CameraSpec>
        {
            // OmniVision OV7670: 1/6", 640×480, 3.6 µm, VGA 30 fps max; typical module lens f ≈ 3.6 mm → ≈ 35° HFOV.
            new CameraSpec
            {
                Id = "OV7670", Name = "OV7670 (0.3 MP)", Sensor = "OV7670", NativeWidth = 640, NativeHeight = 480, PixelMicrons = 3.6,
                HorizontalFovDeg = 35.5, Modes = { new CameraMode(640, 480, 30) }, FullWell = 10000, ReadNoise = 10, MaxGain = 16,
                SupplyVoltage = 3.3, SupplyCurrent = 0.02, DefaultFrameSize = 5, MaxFrameSize = 8
            },
            // OmniVision OV2640: 1/4", 1600×1200, 2.2 µm; UXGA 15 fps, SVGA 30 fps, CIF 60 fps; ESP32-CAM stock lens ≈ 66° diagonal (≈ 54° H).
            new CameraSpec
            {
                Id = "OV2640", Name = "OV2640 (2 MP)", Sensor = "OV2640", NativeWidth = 1600, NativeHeight = 1200, PixelMicrons = 2.2,
                HorizontalFovDeg = 54.5, Modes = { new CameraMode(1600, 1200, 15), new CameraMode(800, 600, 30), new CameraMode(400, 296, 60) },
                FullWell = 6000, ReadNoise = 8, MaxGain = 32, SupplyVoltage = 3.3, SupplyCurrent = 0.04, DefaultFrameSize = 8, MaxFrameSize = 13
            },
            // Generic 720p USB webcam (UVC, MJPEG): 1280×720 at 30 fps, ≈ 55° diagonal FOV (≈ 48° H).
            new CameraSpec
            {
                Id = "USB720", Name = "USB web kamerası 720p", Sensor = "1/4\" CMOS", NativeWidth = 1280, NativeHeight = 720, PixelMicrons = 2.8,
                HorizontalFovDeg = 48.5, Modes = { new CameraMode(1280, 720, 30), new CameraMode(640, 480, 30) }, FullWell = 8000, ReadNoise = 6,
                MaxGain = 16, Interface = "USB", SupplyVoltage = 5.0, SupplyCurrent = 0.15, DefaultFrameSize = 8, MaxFrameSize = 11
            },
            // Sony IMX219 (Raspberry Pi camera v2): 3280×2464, 1.12 µm, f = 3.04 mm, 62.2° × 48.8°; 1080p30, 720p60, 640×480 90 fps.
            new CameraSpec
            {
                Id = "IMX219", Name = "Pi kamera v2 (IMX219, 8 MP)", Sensor = "IMX219", NativeWidth = 3280, NativeHeight = 2464, PixelMicrons = 1.12,
                HorizontalFovDeg = 62.2,
                Modes = { new CameraMode(3280, 2464, 15), new CameraMode(1920, 1080, 30), new CameraMode(1640, 1232, 40), new CameraMode(1280, 720, 60), new CameraMode(640, 480, 90) },
                FullWell = 4500, ReadNoise = 4, MaxGain = 16, Interface = "CSI", SupplyVoltage = 3.3, SupplyCurrent = 0.08, DefaultFrameSize = 8, MaxFrameSize = 15
            }
        };

        public static CameraSpec Find(string? model)
        {
            string m = (model ?? string.Empty).ToUpperInvariant();
            foreach (var s in All)
            {
                if (m.Contains(s.Id)) return s;
            }

            if (m.Contains("USB")) return All[2];
            if (m.Contains("PI") || m.Contains("219")) return All[3];
            return All[1];
        }

        public static CameraFrameSize FrameSize(int index) => FrameSizes[Math.Max(0, Math.Min(FrameSizes.Length - 1, index))];

        public static CameraFrameSize ParseFrameSize(string? text, int fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return FrameSize(fallback);
            string t = text!.Trim().ToUpperInvariant();
            foreach (var f in FrameSizes)
            {
                if (t == f.Name || t.StartsWith(f.Name + " ", StringComparison.Ordinal) || t == $"{f.Width}X{f.Height}" || t == $"{f.Width}×{f.Height}") return f;
            }

            return int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? FrameSize(i) : FrameSize(fallback);
        }

        /// <summary>Bytes a frame occupies in a pixel format (JPEG: estimated from quality and image detail).</summary>
        public static int FrameBytes(int width, int height, CameraPixelFormat format, int jpegQuality = 12, double detail = 1.0) => format switch
        {
            CameraPixelFormat.Grayscale => width * height,
            CameraPixelFormat.Rgb888 => width * height * 3,
            CameraPixelFormat.Jpeg => (int)Math.Ceiling(width * height * JpegBitsPerPixel(jpegQuality, detail) / 8.0) + 623,
            _ => width * height * 2
        };

        /// <summary>
        /// Approximate JPEG bits per pixel for esp32-camera quality 0–63 (lower = better) and image detail
        /// (≈0.3 flat walls … 1 textured room … 1.6 noisy low light). UXGA at quality 10–12 gives ≈ 150–250 kB.
        /// </summary>
        public static double JpegBitsPerPixel(int quality, double detail) =>
            (0.18 + 6.5 / (Math.Max(0, Math.Min(63, quality)) + 6.0)) * Math.Max(0.2, Math.Min(2.5, detail));

        /// <summary>
        /// Frame rate an ESP32 can actually take over its 8-bit DVP port: the sensor rate, capped by the
        /// pixel-clock bandwidth (≈ XCLK bytes/s; raw formats are 2 bytes per pixel on the bus, plus ≈ 30 % blanking).
        /// </summary>
        public static double Esp32Fps(CameraSpec spec, int width, int height, CameraPixelFormat format, double xclkHz = 20e6, int jpegQuality = 12)
        {
            double sensor = spec.SensorFps(width, height);
            if (format == CameraPixelFormat.Jpeg)
            {
                // The sensor compresses; the bus carries the JPEG stream, the readout is the limit.
                return sensor;
            }

            double busBytes = width * height * 2.0 * 1.3;
            return Math.Min(sensor, xclkHz / busBytes);
        }

        /// <summary>Pixels per radian at the image centre for a horizontal field of view.</summary>
        public static double FocalPixels(int width, double hfovDeg) => width / 2.0 / Math.Tan(hfovDeg * Math.PI / 360);
    }

    /// <summary>Motion of a camera during a frame (rad/s about its own axes, m/s), for blur and rolling shutter.</summary>
    public struct CameraMotion
    {
        /// <summary>Turn rate seen as horizontal image motion (yaw about the camera's up axis).</summary>
        public double Yaw;

        /// <summary>Turn rate seen as vertical image motion (pitch).</summary>
        public double Pitch;

        public double Roll;
    }

    /// <summary>
    /// Image pipeline of one camera: auto exposure (shutter then gain) from the scene light, photon
    /// shot noise and read noise scaled by gain, motion blur over the exposure time, rolling-shutter
    /// skew while turning, vignetting, then quantisation. Works on BGRA32 pixels rendered under the
    /// reference lighting (<see cref="ReferenceLux"/>).
    /// </summary>
    public sealed class CameraPipeline
    {
        public const double ReferenceLux = 400;
        private static readonly float[] Gauss = BuildGauss();
        private int _noiseIndex;
        private uint _rng = 2463534242;

        public CameraPipeline(CameraSpec spec)
        {
            Spec = spec;
            ExposureTime = 1.0 / 60;
        }

        public CameraSpec Spec { get; }

        /// <summary>Current shutter time (s) and gain.</summary>
        public double ExposureTime { get; private set; }

        public double Gain { get; private set; } = 1;

        /// <summary>Mean brightness of the last output frame (0–1).</summary>
        public double MeanLevel { get; private set; }

        /// <summary>Fraction of clipped pixels in the last frame.</summary>
        public double Clipped { get; private set; }

        public bool AutoExposure { get; set; } = true;

        public bool NoiseEnabled { get; set; } = true;

        public bool MotionEffects { get; set; } = true;

        /// <summary>Auto-exposure target mean level (0–1).</summary>
        public double Target { get; set; } = 0.42;

        /// <summary>Estimated image detail (for JPEG size).</summary>
        public double Detail { get; private set; } = 1;

        /// <summary>
        /// Processes a frame in place.
        /// <paramref name="bgra"/>: rendered pixels (premultiplied BGRA, opaque) at the reference lighting.
        /// <paramref name="lux"/>: scene illuminance. <paramref name="framePeriod"/>: 1/fps.
        /// </summary>
        public void Process(byte[] bgra, int width, int height, double lux, double framePeriod, CameraMotion motion, double hfovDeg)
        {
            int n = width * height;
            if (bgra.Length < n * 4 || n == 0) return;

            // ---- scene brightness at nominal exposure
            long sum = 0;
            for (int i = 0; i < n * 4; i += 16) sum += bgra[i] + bgra[i + 1] * 2 + bgra[i + 2];
            double meanIn = Math.Max(1e-3, sum / (double)(n / 4 + 1) / 4 / 255);
            double nominal = Math.Max(1e-6, lux / ReferenceLux * Spec.PixelSensitivity);

            if (AutoExposure)
            {
                // total = nominal · (T / (1/60)) · gain; shutter first (up to the frame period), then gain.
                double want = Target / meanIn / nominal;
                double tMax = Math.Min(framePeriod * 0.95, 1.0 / 15);
                double tMin = 1e-5;
                double t = Math.Max(tMin, Math.Min(tMax, want / 60.0));
                double g = Math.Max(1, Math.Min(Spec.MaxGain, want / (t * 60)));
                // AE converges over a few frames.
                ExposureTime = Math.Exp(Math.Log(ExposureTime) * 0.6 + Math.Log(t) * 0.4);
                Gain = Math.Exp(Math.Log(Gain) * 0.6 + Math.Log(g) * 0.4);
            }

            double k = nominal * ExposureTime * 60 * Gain;

            // ---- motion: blur over the exposure, skew over the readout
            double f = CameraModels.FocalPixels(width, hfovDeg);
            if (MotionEffects)
            {
                int blurX = (int)Math.Round(Math.Abs(motion.Yaw) * ExposureTime * f);
                int blurY = (int)Math.Round(Math.Abs(motion.Pitch) * ExposureTime * f);
                if (blurX > 1) BoxBlur(bgra, width, height, Math.Min(blurX, width / 4), true);
                if (blurY > 1) BoxBlur(bgra, width, height, Math.Min(blurY, height / 4), false);
                double readout = framePeriod * Spec.ReadoutFraction;
                double skew = motion.Yaw * readout * f;
                if (Math.Abs(skew) >= 1) Skew(bgra, width, height, skew);
            }

            // ---- exposure, vignetting, noise, quantisation
            double electronsPerUnit = Spec.FullWell / Math.Max(1, Gain);
            double read = Spec.ReadNoise * Gain / Spec.FullWell;
            double cx = width / 2.0, cy = height / 2.0, r2max = cx * cx + cy * cy;
            double vig = 0.25 * Math.Min(1, hfovDeg / 90.0);
            long outSum = 0;
            int clipped = 0;
            long grad = 0;
            for (int y = 0; y < height; y++)
            {
                double dy = y - cy;
                int row = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    double dx = x - cx;
                    double v = k * (1 - vig * (dx * dx + dy * dy) / r2max);
                    int p = row + x * 4;
                    for (int c = 0; c < 3; c++)
                    {
                        double s = bgra[p + c] / 255.0 * v;
                        if (NoiseEnabled)
                        {
                            double shot = Math.Sqrt(Math.Max(0, Math.Min(1, s)) / electronsPerUnit);
                            s += (shot + read) * NextGauss();
                        }

                        int q = (int)(Math.Max(0, Math.Min(1, s)) * 255 + 0.5);
                        if (q >= 255) clipped++;
                        bgra[p + c] = (byte)q;
                    }

                    bgra[p + 3] = 255;
                    outSum += bgra[p + 1];
                    if (x > 0 && (x & 3) == 0) grad += Math.Abs(bgra[p + 1] - bgra[p - 3]);
                }
            }

            MeanLevel = outSum / (double)n / 255;
            Clipped = clipped / (double)(n * 3);
            Detail = Math.Max(0.25, Math.Min(2.5, grad / (double)(n / 4 + 1) / 9.0));
        }

        private double NextGauss()
        {
            _noiseIndex = (_noiseIndex + 1) & (Gauss.Length - 1);
            if (_noiseIndex == 0)
            {
                _rng ^= _rng << 13;
                _rng ^= _rng >> 17;
                _rng ^= _rng << 5;
                _noiseIndex = (int)(_rng & (Gauss.Length - 1));
            }

            return Gauss[_noiseIndex];
        }

        private static float[] BuildGauss()
        {
            var g = new float[1 << 16];
            var r = new Random(7670);
            for (int i = 0; i < g.Length; i++)
            {
                double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
                g[i] = (float)(Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
            }

            return g;
        }

        private static void BoxBlur(byte[] px, int w, int h, int len, bool horizontal)
        {
            int lines = horizontal ? h : w, count = horizontal ? w : h;
            var acc = new int[3];
            var line = new byte[count * 3];
            for (int l = 0; l < lines; l++)
            {
                for (int i = 0; i < count; i++)
                {
                    int p = horizontal ? (l * w + i) * 4 : (i * w + l) * 4;
                    line[i * 3] = px[p];
                    line[i * 3 + 1] = px[p + 1];
                    line[i * 3 + 2] = px[p + 2];
                }

                acc[0] = acc[1] = acc[2] = 0;
                int half = len / 2;
                for (int i = -half; i < count; i++)
                {
                    int add = Math.Min(count - 1, i + half);
                    acc[0] += line[add * 3];
                    acc[1] += line[add * 3 + 1];
                    acc[2] += line[add * 3 + 2];
                    int rem = i - half - 1;
                    if (rem >= 0)
                    {
                        acc[0] -= line[rem * 3];
                        acc[1] -= line[rem * 3 + 1];
                        acc[2] -= line[rem * 3 + 2];
                    }

                    if (i < 0) continue;
                    int span = Math.Min(count - 1, i + half) - Math.Max(0, i - half) + 1;
                    int p = horizontal ? (l * w + i) * 4 : (i * w + l) * 4;
                    px[p] = (byte)(acc[0] / span);
                    px[p + 1] = (byte)(acc[1] / span);
                    px[p + 2] = (byte)(acc[2] / span);
                }
            }
        }

        /// <summary>Rolling shutter: each row is read later than the one above, so a turning camera shears the image.</summary>
        private static void Skew(byte[] px, int w, int h, double totalShift)
        {
            var row = new byte[w * 4];
            for (int y = 0; y < h; y++)
            {
                int shift = (int)Math.Round(totalShift * (y / (double)h - 0.5));
                if (shift == 0) continue;
                Buffer.BlockCopy(px, y * w * 4, row, 0, w * 4);
                for (int x = 0; x < w; x++)
                {
                    int sx = Math.Max(0, Math.Min(w - 1, x + shift));
                    Buffer.BlockCopy(row, sx * 4, px, (y * w + x) * 4, 4);
                }
            }
        }

        /// <summary>Converts processed BGRA pixels to a sensor output format (JPEG is left to the host's encoder).</summary>
        public static byte[] Convert(byte[] bgra, int width, int height, CameraPixelFormat format)
        {
            int n = width * height;
            switch (format)
            {
                case CameraPixelFormat.Grayscale:
                {
                    var g = new byte[n];
                    for (int i = 0; i < n; i++) g[i] = (byte)((bgra[i * 4 + 2] * 77 + bgra[i * 4 + 1] * 150 + bgra[i * 4] * 29) >> 8);
                    return g;
                }

                case CameraPixelFormat.Rgb888:
                {
                    var o = new byte[n * 3];
                    for (int i = 0; i < n; i++)
                    {
                        o[i * 3] = bgra[i * 4 + 2];
                        o[i * 3 + 1] = bgra[i * 4 + 1];
                        o[i * 3 + 2] = bgra[i * 4];
                    }

                    return o;
                }

                case CameraPixelFormat.Yuv422:
                {
                    var o = new byte[n * 2];
                    for (int i = 0; i + 1 < n; i += 2)
                    {
                        int r0 = bgra[i * 4 + 2], g0 = bgra[i * 4 + 1], b0 = bgra[i * 4];
                        int r1 = bgra[i * 4 + 6], g1 = bgra[i * 4 + 5], b1 = bgra[i * 4 + 4];
                        int y0 = (77 * r0 + 150 * g0 + 29 * b0) >> 8, y1 = (77 * r1 + 150 * g1 + 29 * b1) >> 8;
                        int u = (((-43 * r0 - 85 * g0 + 128 * b0) >> 8) + 128), v = (((128 * r0 - 107 * g0 - 21 * b0) >> 8) + 128);
                        o[i * 2] = (byte)y0;
                        o[i * 2 + 1] = (byte)Math.Max(0, Math.Min(255, u));
                        o[i * 2 + 2] = (byte)y1;
                        o[i * 2 + 3] = (byte)Math.Max(0, Math.Min(255, v));
                    }

                    return o;
                }

                default:
                {
                    // RGB565, big-endian like the OV sensors put it on the bus.
                    var o = new byte[n * 2];
                    for (int i = 0; i < n; i++)
                    {
                        int r = bgra[i * 4 + 2] >> 3, g = bgra[i * 4 + 1] >> 2, b = bgra[i * 4] >> 3;
                        int v = (r << 11) | (g << 5) | b;
                        o[i * 2] = (byte)(v >> 8);
                        o[i * 2 + 1] = (byte)v;
                    }

                    return o;
                }
            }
        }
    }
}
