using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox.Sensing
{
    /// <summary>Latest output of one world sensor (keyed by its object id).</summary>
    public abstract class SensorReading
    {
        public string ObjectId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        /// <summary>"line", "camera", "lidar", "imu", "gps", "compass", "encoder".</summary>
        public abstract string Kind { get; }

        /// <summary>Simulated time of the reading (s).</summary>
        public double Time { get; set; }

        /// <summary>False when the sensor has no supply (it outputs nothing useful).</summary>
        public bool Powered { get; set; } = true;
    }

    public struct LineChannelReading
    {
        /// <summary>Spot-averaged diffuse IR reflectance under the channel (truth, 0–1).</summary>
        public double Reflectance;

        /// <summary>Optical signal relative to a white card at the best distance (diffuse + specular + ambient).</summary>
        public double Signal;

        public double Diffuse, Specular, Ambient;

        /// <summary>Phototransistor current (A) after noise and drift.</summary>
        public double Photocurrent;

        /// <summary>Voltage at the channel's output pin (V), NaN when not available.</summary>
        public double Voltage;

        /// <summary>Comparator output (digital modules): true = LOW = "white seen".</summary>
        public bool? DigitalLow;

        /// <summary>Distance to the surface along the axis (m) and where the spot is (world).</summary>
        public double Distance;

        public Vec3 Spot;
        public double SpotRadius;

        /// <summary>Tape under the spot centre (-1 bare floor, -2 off the floor).</summary>
        public int Tape;
    }

    public sealed class LineSensorReading : SensorReading
    {
        public override string Kind => "line";
        public string Model { get; set; } = string.Empty;
        public LineChannelReading[] Channels { get; set; } = Array.Empty<LineChannelReading>();
    }

    public sealed class CameraReading : SensorReading
    {
        public override string Kind => "camera";
        public string Model { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public CameraPixelFormat Format { get; set; }
        public double HorizontalFovDeg { get; set; }

        /// <summary>Frame rate the sensor/link allows and the rate frames were actually produced (simulated time).</summary>
        public double TargetFps { get; set; }

        public double AchievedFps { get; set; }
        public double ExposureMs { get; set; }
        public double Gain { get; set; }
        public double MeanLevel { get; set; }
        public long FrameIndex { get; set; }

        /// <summary>Processed pixels, BGRA32, row-major (what the sensor delivered).</summary>
        public byte[] Bgra { get; set; } = Array.Empty<byte>();

        /// <summary>Size of the frame in its pixel format (JPEG: estimated or encoded).</summary>
        public int FrameBytes { get; set; }
    }

    public sealed class LidarReading : SensorReading
    {
        public override string Kind => "lidar";
        public string Model { get; set; } = string.Empty;
        public LidarScan Scan { get; set; } = new LidarScan();
        public int Channels { get; set; } = 1;
    }

    public sealed class ImuReading : SensorReading
    {
        public override string Kind => "imu";

        /// <summary>Measured specific force (m/s²) and angular rate (rad/s), body axes, with bias and noise.</summary>
        public Vec3 Accel { get; set; }

        public Vec3 Gyro { get; set; }

        /// <summary>Error-free values (simulation truth).</summary>
        public Vec3 TrueAccel { get; set; }

        public Vec3 TrueGyro { get; set; }
        public Quat Orientation { get; set; } = Quat.Identity;
    }

    public sealed class GpsReading : SensorReading
    {
        public override string Kind => "gps";
        public GpsFix Fix { get; set; } = new GpsFix();
    }

    public sealed class CompassReading : SensorReading
    {
        public override string Kind => "compass";

        /// <summary>Field in the module's axes (Gauss) and the heading it implies (degrees from magnetic north, clockwise).</summary>
        public Vec3 Field { get; set; }

        public double HeadingDeg { get; set; }
    }

    public sealed class EncoderReading : SensorReading
    {
        public override string Kind => "encoder";

        /// <summary>Quadrature count (4 per line) and counts per output-shaft revolution.</summary>
        public long Count { get; set; }

        public double CountsPerRevolution { get; set; }
        public double OutputAngleRad { get; set; }
        public double OutputRpm { get; set; }
        public bool A { get; set; }
        public bool B { get; set; }
    }

    /// <summary>
    /// Read-only window on every world sensor's latest output, keyed by object id — the API
    /// external bridges (Python, ROS) read. Thread-safe snapshots: readings are immutable once
    /// published (a new object replaces the old one).
    /// </summary>
    public sealed class WorldSensors
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, SensorReading> _latest = new Dictionary<string, SensorReading>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Simulated time of the last update.</summary>
        public double Time { get; internal set; }

        /// <summary>Raised on the simulation thread for every new reading.</summary>
        public event Action<SensorReading>? Published;

        public void Publish(SensorReading reading)
        {
            if (reading == null) throw new ArgumentNullException(nameof(reading));
            lock (_lock) _latest[reading.ObjectId] = reading;
            Published?.Invoke(reading);
        }

        public SensorReading? this[string objectId]
        {
            get
            {
                lock (_lock) return _latest.TryGetValue(objectId, out var r) ? r : null;
            }
        }

        public T? Get<T>(string objectId) where T : SensorReading => this[objectId] as T;

        /// <summary>Snapshot of every latest reading.</summary>
        public IReadOnlyList<SensorReading> All
        {
            get
            {
                lock (_lock) return _latest.Values.ToList();
            }
        }

        public IReadOnlyList<T> OfKind<T>() where T : SensorReading
        {
            lock (_lock) return _latest.Values.OfType<T>().ToList();
        }

        public void Clear()
        {
            lock (_lock) _latest.Clear();
        }
    }
}
