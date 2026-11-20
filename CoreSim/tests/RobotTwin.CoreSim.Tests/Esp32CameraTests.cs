using System;
using System.IO;
using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware.Esp32;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Sensing;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[sense] An ESP32 sketch reads frames from a world camera through esp_camera.h.</summary>
    [Trait("Category", "Firmware")]
    public class Esp32CameraTests
    {
        private const string Sketch = @"
#include ""esp_camera.h""
void setup() {
  Serial.begin(115200);
  camera_config_t c = {};
  c.pin_pwdn = 32; c.pin_reset = -1; c.pin_xclk = 0; c.pin_sccb_sda = 26; c.pin_sccb_scl = 27;
  c.xclk_freq_hz = 20000000; c.pixel_format = PIXFORMAT_GRAYSCALE; c.frame_size = FRAMESIZE_QQVGA;
  c.jpeg_quality = 12; c.fb_count = 1;
  esp_err_t err = esp_camera_init(&c);
  Serial.printf(""init 0x%x\n"", err);
  sensor_t* s = esp_camera_sensor_get();
  if (s) Serial.printf(""pid 0x%x\n"", s->id.PID);
}
void loop() {
  camera_fb_t* fb = esp_camera_fb_get();
  if (!fb) { Serial.println(""no frame""); delay(100); return; }
  unsigned long sum = 0;
  for (size_t i = 0; i < fb->len; i++) sum += fb->buf[i];
  Serial.printf(""frame %dx%d len=%d mean=%lu t=%ld.%06ld\n"", (int)fb->width, (int)fb->height, (int)fb->len, sum / fb->len, fb->timestamp.tv_sec, fb->timestamp.tv_usec);
  esp_camera_fb_return(fb);
}
";

        private readonly ITestOutputHelper _out;

        public Esp32CameraTests(ITestOutputHelper output)
        {
            _out = output;
        }

        [Esp32Fact]
        public void SketchGrabsGrayscaleFramesFromTheWorldCamera()
        {
            var lib = PartLibrary.Default;
            string dir = Path.Combine(Path.GetTempPath(), "robotwin-esp32-cam-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var build = new Esp32SketchCompiler().Compile(Sketch, dir);
            Assert.True(build.Success, string.Join("\n", build.Diagnostics) + "\n" + build.Log);

            var w = new WorldDocument();
            var esp = w.AddComponent(lib, Esp32Parts.DevKitCId, new WorldPose());
            var cam = w.AddComponent(lib, "cam-ov2640", new WorldPose { X = 0.1 });
            w.Connect(new CableEnd { ObjectId = esp.Id, Pin = "3V3" }, new CableEnd { ObjectId = cam.Id, Pin = "VCC" });
            w.Connect(new CableEnd { ObjectId = esp.Id, Pin = "GND1" }, new CableEnd { ObjectId = cam.Id, Pin = "GND" });
            var wc = WorldCompiler.Compile(w, lib);
            var sim = new AnalogSimulator(wc.Compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            var sensing = WorldSensing.Build(w, wc, null);
            var unit = sensing.Cameras.Single();
            using var cosim = new Esp32CoSimulation(build.DllPath!, wc.Compiled, wc.Programmables[0].RefDes, null, new Esp32Options { BootDelay = 0.05 });
            cosim.Radio = new Esp32CameraBridge(cosim.Radio, () => unit);
            var serial = new StringBuilder();
            cosim.SerialOutput += s => serial.Append(s);
            double t = 0;
            while (t < 1.0)
            {
                cosim.Advance(100e-6, sim.Time);
                sim.Step(100e-6);
                sensing.Advance(sim.Time, 100e-6, sim);
                t = sim.Time;
                // The host "renders" a mid-grey frame whenever one is due.
                if (unit.Due(t))
                {
                    var px = new byte[unit.Width * unit.Height * 4];
                    for (int i = 0; i < px.Length; i += 4)
                    {
                        px[i] = px[i + 1] = px[i + 2] = 100;
                        px[i + 3] = 255;
                    }

                    unit.Submit(px, unit.Width, unit.Height, t, default, 400, sensing.Sensors);
                }
            }

            _out.WriteLine(serial.ToString());
            string text = serial.ToString();
            Assert.Contains("init 0x0", text);
            Assert.Contains("pid 0x26", text);
            var frames = text.Split('\n').Where(l => l.StartsWith("frame 160x120 len=19200", StringComparison.Ordinal)).ToList();
            Assert.True(frames.Count >= 10, "frames: " + frames.Count);
            // Auto exposure brings the mean towards its ≈42 % target.
            int mean = int.Parse(frames.Last().Split("mean=")[1].Split(' ')[0]);
            Assert.InRange(mean, 70, 140);
            // The driver now limits the rate to what the ESP32's DVP port carries (raw QQVGA: sensor's 60 fps).
            Assert.InRange(unit.TargetFps, 59, 61);
        }
    }
}
