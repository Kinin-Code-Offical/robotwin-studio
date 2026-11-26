using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware;
using RobotTwin.CoreSim.Firmware.Devices;
using RobotTwin.CoreSim.Firmware.Esp32;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Sensing;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [sense] World sensors in a running world: <see cref="WorldSensing"/> samples line sensors,
    /// LiDARs, GPS, IMUs, compasses and encoders after every step; GPS NMEA goes to the UART of the
    /// board its TX is wired to; ESP32 boards get their camera through the esp_camera bridge.
    /// </summary>
    public sealed partial class WorldSession
    {
        private readonly List<(string FullRef, Hmc5883l Device, string? HardIron)> _compassDevices = new List<(string, Hmc5883l, string?)>();
        private readonly HashSet<string> _gpsWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _senseNotes = new System.Collections.Concurrent.ConcurrentQueue<string>();

        /// <summary>Sensor sampling of the running world (null when stopped). <c>Sensing.Sensors</c> is the read API for bridges.</summary>
        public WorldSensing? Sensing { get; private set; }

        /// <summary>Raised when sensing (re)starts with a new world.</summary>
        public event Action<WorldSensing>? SensingStarted;

        private void StartSense()
        {
            Sensing = null;
            _gpsWarned.Clear();
            if (World == null || Circuit == null) return;
            try
            {
                Sensing = WorldSensing.Build(World, Circuit, Physics);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is NullReferenceException)
            {
                AddLog(new SimLogEntry(0, "sensör", "Sensörler kurulamadı: " + ex.Message, AnalogEventKind.Warning));
                return;
            }

            // IMUs and compasses that a board talks to over I²C get the real motion and field of their object.
            var bound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _i2c)
            {
                string objectId = kv.Key.Split('/')[0];
                if (kv.Value is Mpu6050 mpu && bound.Add(objectId + ":imu")) Sensing.BindMpu6050(objectId, mpu);
            }

            foreach (var (fullRef, dev, hi) in _compassDevices)
            {
                string objectId = fullRef.Split('/')[0];
                if (bound.Add(objectId + ":mag")) Sensing.BindCompass(objectId, dev, hi);
            }

            // Stand-alone modules nobody reads still report their values (sensor panel, external bridges).
            foreach (var o in World.Objects.Where(o => o.Kind == WorldObjectKind.Component))
            {
                if (o.PartId == "mpu6050" && bound.Add(o.Id + ":imu")) Sensing.BindMpu6050(o.Id, new Mpu6050());
                if (o.PartId == SensingParts.CompassId && bound.Add(o.Id + ":mag")) Sensing.BindCompass(o.Id, new Hmc5883l(), o.Parameters.TryGetValue("hardiron", out var h) ? h : null);
            }

            Sensing.GpsSerial += RouteGps;
            int n = Sensing.Cameras.Count;
            if (n > 0) AddLog(new SimLogEntry(0, "kamera", $"{n} kamera: dünya kendi çözünürlük ve kare hızlarıyla çiziliyor", AnalogEventKind.StateChange));
            _rosLink?.AttachSensors(Sensing?.Sensors);   // [ros-sense]
            SensingStarted?.Invoke(Sensing);
        }

        private void AdvanceSense(double dt)
        {
            if (Sensing != null && Simulator != null) Sensing.Advance(Simulator.Time, dt, Simulator);
            while (_senseNotes.TryDequeue(out var note)) AddLog(new SimLogEntry(Time, "kamera", note, AnalogEventKind.StateChange));
        }

        private void StopSense()
        {
            if (Sensing != null) Sensing.GpsSerial -= RouteGps;
            Sensing = null;
            _compassDevices.Clear();
        }

        /// <summary>I²C protocol models of the sensing parts (called from AttachI2c for parts it does not know).</summary>
        private II2cDevice? SenseI2c(string partId, IReadOnlyDictionary<string, string> parameters, byte address, string fullRef)
        {
            if (partId != SensingParts.CompassId) return null;
            var dev = new Hmc5883l(address == 0 ? (byte)0x1E : address);
            _compassDevices.Add((fullRef, dev, parameters.TryGetValue("hardiron", out var h) ? h : null));
            return dev;
        }

        /// <summary>ESP32 camera requests (esp_camera.h) go to the camera whose "host" is this board, or the first free one.</summary>
        private void AttachCamera(Esp32CoSimulation cosim, WorldProgrammable prog)
        {
            string board = prog.ObjectId;
            var bridge = new Esp32CameraBridge(cosim.Radio, () =>
            {
                var cams = Sensing?.Cameras;
                if (cams == null) return null;
                var mine = cams.FirstOrDefault(c => string.Equals(c.Host, board, StringComparison.OrdinalIgnoreCase));
                if (mine != null) return mine;
                var free = cams.FirstOrDefault(c => c.Host == null && c.Spec.Interface == "DVP") ?? cams.FirstOrDefault(c => c.Host == null);
                if (free != null) free.Host = board;
                return free;
            });
            // Called on the sketch thread: the message is logged on the next step.
            bridge.Initialized += (cam, rc) => _senseNotes.Enqueue(rc == 0
                ? $"{cam.Object.Name}: esp_camera_init tamam ({cam.Width}×{cam.Height} {cam.Format}, en çok {cam.TargetFps:0.#} fps)"
                : $"{cam.Object.Name}: esp_camera_init başarısız (kamerada besleme yok)");
            cosim.Radio = bridge;
        }

        /// <summary>NMEA from a GPS module's TX to the UART receive pin it is cabled to.</summary>
        private void RouteGps(string gpsId, string text)
        {
            if (World == null) return;
            foreach (var c in World.Cables)
            {
                CableEnd? other = c.A.ObjectId == gpsId && c.A.Pin == "TX" ? c.B : c.B.ObjectId == gpsId && c.B.Pin == "TX" ? c.A : null;
                if (other == null) continue;
                string pin = other.Pin.Contains('.') ? other.Pin.Substring(other.Pin.IndexOf('.') + 1) : other.Pin;
                pin = pin.ToUpperInvariant();
                bool delivered = false;
                foreach (var a in _avr)
                {
                    if (_boardObject.TryGetValue(a, out var id) && id == other.ObjectId && (pin == "D0" || pin == "RX" || pin == "RX0"))
                    {
                        a.Mcu.WriteSerial(text);
                        delivered = true;
                    }
                }

                foreach (var e in _esp)
                {
                    if (!_boardObject.TryGetValue(e, out var id) || id != other.ObjectId) continue;
                    int port = pin == "RX" || pin == "IO3" ? 0 : pin == "IO16" ? 2 : pin == "IO9" ? 1 : -1;
                    if (port < 0) continue;
                    e.SendSerial(text, port);
                    delivered = true;
                }

                if (!delivered && _gpsWarned.Add(gpsId + other))
                {
                    AddLog(new SimLogEntry(Time, "GPS", $"GPS TX → {other.Pin}: bu pin bir donanım UART RX'i değil (Uno: D0, ESP32: RX/IO3 veya IO16 = Serial2); NMEA ulaşmıyor.", AnalogEventKind.Warning));
                }
            }
        }
    }
}
