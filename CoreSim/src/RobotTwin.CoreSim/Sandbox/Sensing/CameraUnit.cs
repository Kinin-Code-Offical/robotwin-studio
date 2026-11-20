using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware.Esp32;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox.Sensing
{
    /// <summary>
    /// One camera in a running world: its settings (frame size, format, field of view, frame-rate
    /// limit), the image pipeline and the frame clock. The host renders the world from
    /// <see cref="WorldSensing.CameraFrame"/> at <see cref="Width"/>×<see cref="Height"/> whenever
    /// <see cref="Due"/> says so and hands the pixels to <see cref="Submit"/>.
    /// </summary>
    public sealed class CameraUnit
    {
        private readonly Queue<double> _frameTimes = new Queue<double>();
        private double _next;

        internal CameraUnit(WorldObject o, string refDes, Dictionary<string, string> p)
        {
            Object = o;
            RefDes = refDes;
            string model = p.TryGetValue("model", out var m) ? m : "OV2640";
            Spec = CameraModels.Find(model);
            Lens = SensingParts.CameraLens(model);
            Size = CameraModels.ParseFrameSize(p.TryGetValue("framesize", out var fs) ? fs : null, Spec.DefaultFrameSize);
            if (Size.Index > Spec.MaxFrameSize && Spec.MaxFrameSize < CameraModels.FrameSizes.Length) Size = CameraModels.FrameSize(Spec.MaxFrameSize);
            Format = ParseFormat(p.TryGetValue("format", out var f) ? f : null);
            double fov = SiValue.TryParse(p.TryGetValue("fov", out var fv) ? fv : "0", out double fovv) ? fovv : 0;
            HorizontalFovDeg = fov > 1 && fov < 179 ? fov : Spec.HorizontalFovDeg;
            FpsLimit = SiValue.TryParse(p.TryGetValue("fps", out var fp) ? fp : "0", out double fl) ? fl : 0;
            JpegQuality = SiValue.TryParse(p.TryGetValue("quality", out var q) ? q : "12", out double qq) ? (int)qq : 12;
            Host = p.TryGetValue("host", out var h) && !string.IsNullOrWhiteSpace(h) ? h.Trim() : null;
            Pipeline = new CameraPipeline(Spec);
            bool realism = !p.TryGetValue("noise", out var nz) || !string.Equals(nz, "false", StringComparison.OrdinalIgnoreCase);
            Pipeline.NoiseEnabled = realism;
            Pipeline.MotionEffects = realism;
        }

        public WorldObject Object { get; }
        public string RefDes { get; }
        public CameraSpec Spec { get; }

        /// <summary>Lens position in the object frame (looks along +X, +Z up).</summary>
        public Vec3 Lens { get; }

        public CameraFrameSize Size { get; private set; }
        public CameraPixelFormat Format { get; private set; }
        public double HorizontalFovDeg { get; }
        public double FpsLimit { get; }
        public int JpegQuality { get; private set; }

        /// <summary>ESP32 object that reads this camera (parameter "host"; null = the one the driver asked first).</summary>
        public string? Host { get; set; }

        public CameraPipeline Pipeline { get; }
        public bool Powered { get; private set; } = true;

        /// <summary>The ESP32 driver configured the camera (esp_camera_init).</summary>
        public bool DriverActive { get; internal set; }

        public int Width => Size.Width;
        public int Height => Size.Height;
        public double VerticalFovDeg => Spec.VerticalFovDeg(HorizontalFovDeg, Width, Height);

        public long FrameIndex { get; private set; }
        public CameraReading? Latest { get; private set; }

        /// <summary>Frame rate the sensor (and an ESP32's DVP port, when one drives it) allows.</summary>
        public double TargetFps
        {
            get
            {
                double f = DriverActive ? CameraModels.Esp32Fps(Spec, Width, Height, Format, 20e6, JpegQuality) : Spec.SensorFps(Width, Height);
                return FpsLimit > 0 ? Math.Min(f, FpsLimit) : f;
            }
        }

        /// <summary>Frames produced per simulated second over the last second.</summary>
        public double AchievedFps => _frameTimes.Count < 2 ? 0 : (_frameTimes.Count - 1) / Math.Max(1e-6, _frameTimes.Last() - _frameTimes.Peek());

        public static CameraPixelFormat ParseFormat(string? text) => (text ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "GRAYSCALE" or "GRAY" or "GRI" => CameraPixelFormat.Grayscale,
            "JPEG" or "JPG" => CameraPixelFormat.Jpeg,
            "YUV422" or "YUV" => CameraPixelFormat.Yuv422,
            "RGB888" or "RGB" => CameraPixelFormat.Rgb888,
            _ => CameraPixelFormat.Rgb565
        };

        internal void UpdatePower(double volts) => Powered = volts >= Spec.SupplyVoltage * 0.8;

        /// <summary>Whether a new frame should be captured at simulated <paramref name="time"/>.</summary>
        public bool Due(double time) => Powered && time + 1e-9 >= _next;

        /// <summary>Driver request: new frame size / format / quality (esp_camera_init, sensor->set_framesize).</summary>
        public void Configure(int frameSize, CameraPixelFormat format, int quality)
        {
            Size = CameraModels.FrameSize(Math.Min(frameSize, Spec.MaxFrameSize));
            Format = format;
            JpegQuality = Math.Max(0, Math.Min(63, quality));
        }

        /// <summary>
        /// Takes a rendered frame (BGRA32 under the reference lighting, processed in place) at
        /// simulated <paramref name="time"/> and publishes the result.
        /// </summary>
        public CameraReading Submit(byte[] bgra, int width, int height, double time, CameraMotion motion, double lux, WorldSensors? sensors)
        {
            double period = 1.0 / Math.Max(0.5, TargetFps);
            _next = Math.Max(_next + period, time + period * 0.5);
            Pipeline.Process(bgra, width, height, lux, period, motion, HorizontalFovDeg);
            FrameIndex++;
            _frameTimes.Enqueue(time);
            while (_frameTimes.Count > 2 && time - _frameTimes.Peek() > 1.0) _frameTimes.Dequeue();
            int bytes = CameraModels.FrameBytes(width, height, Format, JpegQuality, Pipeline.Detail);
            var r = new CameraReading
            {
                ObjectId = Object.Id, Name = Object.Name, Time = time, Model = Spec.Name, Width = width, Height = height, Format = Format,
                HorizontalFovDeg = HorizontalFovDeg, TargetFps = TargetFps, AchievedFps = AchievedFps, ExposureMs = Pipeline.ExposureTime * 1000,
                Gain = Pipeline.Gain, MeanLevel = Pipeline.MeanLevel, FrameIndex = FrameIndex, Bgra = bgra, FrameBytes = bytes, Powered = Powered
            };
            Latest = r;
            sensors?.Publish(r);
            return r;
        }

        /// <summary>The latest frame in the driver's pixel format (JPEG via <see cref="WorldSensing.JpegEncoder"/> when available).</summary>
        public byte[]? Encode(CameraReading r)
        {
            if (Format == CameraPixelFormat.Jpeg)
            {
                var enc = WorldSensing.JpegEncoder;
                if (enc != null)
                {
                    try
                    {
                        return enc(r.Bgra, r.Width, r.Height, JpegQuality);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is NotSupportedException)
                    {
                    }
                }

                // No encoder: a JFIF-sized buffer of grey bytes with SOI/EOI markers.
                var b = new byte[r.FrameBytes];
                b[0] = 0xFF;
                b[1] = 0xD8;
                b[^2] = 0xFF;
                b[^1] = 0xD9;
                return b;
            }

            return CameraPipeline.Convert(r.Bgra, r.Width, r.Height, Format);
        }
    }

    /// <summary>Camera requests from the ESP32 HAL (esp_camera.h), carried on the radio_request channel.</summary>
    public static class Esp32CameraOp
    {
        /// <summary>in: i32 framesize, i32 pixformat, i32 jpeg_quality, i32 fb_count, i32 xclk_hz. ret 0, -2 no camera, -3 not powered.</summary>
        public const int Init = 0x50;

        /// <summary>out: i32 width, i32 height, i32 format, i32 length, u64 timestamp_us. ret length, 0 = no new frame yet.</summary>
        public const int Grab = 0x51;

        /// <summary>in: i32 offset. out: frame bytes. ret bytes copied.</summary>
        public const int Read = 0x52;

        /// <summary>in: i32 what (1 framesize, 2 quality, 3 pixformat), i32 value.</summary>
        public const int Set = 0x53;

        public const int Deinit = 0x54;
    }

    /// <summary>
    /// Sits in front of an ESP32's radio link and answers the camera requests from the cameras of
    /// the world (the one whose "host" is this board, else the first free one); everything else
    /// passes to the radio medium.
    /// </summary>
    public sealed class Esp32CameraBridge : IEsp32Radio
    {
        private readonly IEsp32Radio? _inner;
        private readonly Func<CameraUnit?> _camera;
        private byte[] _staging = Array.Empty<byte>();
        private long _served = -1;

        public Esp32CameraBridge(IEsp32Radio? inner, Func<CameraUnit?> camera)
        {
            _inner = inner;
            _camera = camera;
        }

        /// <summary>Raised (on the sketch thread) when the sketch initialises the camera: the camera and the result code.</summary>
        public event Action<CameraUnit, int>? Initialized;

        public int Request(int op, byte[] input, int inputLength, byte[] output, int outputCapacity, double time)
        {
            if (op < Esp32CameraOp.Init || op > Esp32CameraOp.Deinit) return _inner?.Request(op, input, inputLength, output, outputCapacity, time) ?? Esp32RadioOp.NotSupported;
            var cam = _camera();
            if (cam == null) return -2;
            switch (op)
            {
                case Esp32CameraOp.Init:
                    if (!cam.Powered)
                    {
                        Initialized?.Invoke(cam, -3);
                        return -3;
                    }

                    cam.Configure(RadioPayload.I32(input, 0), (CameraPixelFormat)RadioPayload.I32(input, 4), RadioPayload.I32(input, 8));
                    cam.DriverActive = true;
                    _served = cam.FrameIndex;
                    Initialized?.Invoke(cam, 0);
                    return 0;
                case Esp32CameraOp.Set:
                {
                    int what = RadioPayload.I32(input, 0), value = RadioPayload.I32(input, 4);
                    if (what == 1) cam.Configure(value, cam.Format, cam.JpegQuality);
                    else if (what == 2) cam.Configure(cam.Size.Index, cam.Format, value);
                    else if (what == 3) cam.Configure(cam.Size.Index, (CameraPixelFormat)value, cam.JpegQuality);
                    return 0;
                }

                case Esp32CameraOp.Grab:
                {
                    var r = cam.Latest;
                    if (!cam.Powered || r == null || r.FrameIndex <= _served || r.Width != cam.Width || r.Height != cam.Height || outputCapacity < 24) return 0;
                    _served = r.FrameIndex;
                    _staging = cam.Encode(r) ?? Array.Empty<byte>();
                    RadioPayload.Put(output, 0, r.Width);
                    RadioPayload.Put(output, 4, r.Height);
                    RadioPayload.Put(output, 8, (int)cam.Format);
                    RadioPayload.Put(output, 12, _staging.Length);
                    ulong us = (ulong)(r.Time * 1e6);
                    RadioPayload.Put(output, 16, (uint)us);
                    RadioPayload.Put(output, 20, (uint)(us >> 32));
                    return _staging.Length;
                }

                case Esp32CameraOp.Read:
                {
                    int offset = RadioPayload.I32(input, 0);
                    if (offset < 0 || offset >= _staging.Length) return 0;
                    int n = Math.Min(outputCapacity, _staging.Length - offset);
                    Buffer.BlockCopy(_staging, offset, output, 0, n);
                    return n;
                }

                default:
                    cam.DriverActive = false;
                    return 0;
            }
        }
    }
}
