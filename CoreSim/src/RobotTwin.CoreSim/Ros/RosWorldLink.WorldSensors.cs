using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox.Sensing;

namespace RobotTwin.CoreSim.Ros
{
    /// <summary>
    /// [ros-sense] The world's own sensor parts on ROS, beside the robot description's: every LiDAR, IMU (MPU-6050 and
    /// the like), camera, GPS and compass wired in the world publishes what it measured, as it measured it (its scan
    /// pattern, noise, exposure, fix), under <c>/robotwin/&lt;part&gt;/</c>: <c>scan</c> (<c>sensor_msgs/LaserScan</c>),
    /// <c>imu</c> (<c>sensor_msgs/Imu</c>), <c>image_raw</c> (<c>sensor_msgs/Image</c>, bgra8), <c>fix</c>
    /// (<c>sensor_msgs/NavSatFix</c>) and <c>magnetic_field</c> (<c>sensor_msgs/MagneticField</c>). The frame is the part's
    /// name. A reading is sent once, when the sensor has made a new one; an unpowered sensor sends nothing.
    /// </summary>
    public sealed partial class RosWorldLink
    {
        public const string WorldSensorPrefix = "/robotwin/";

        private sealed class WorldSensorTopic
        {
            public string Topic = string.Empty, Frame = string.Empty;
            public double LastTime = double.NegativeInfinity;
            public long LastFrame = -1;
            public long Sent;
        }

        private WorldSensors? _worldSensors;
        private readonly Dictionary<string, WorldSensorTopic> _worldTopics = new Dictionary<string, WorldSensorTopic>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The world's sensor parts published: topic, kind, messages sent.</summary>
        public IEnumerable<(string Topic, long Sent)> WorldSensorTopics => _worldTopics.Values.Select(t => (t.Topic, t.Sent));

        /// <summary>The world's sensor parts to publish (the running world's <see cref="WorldSensing.Sensors"/>), or null for none.</summary>
        public void AttachSensors(WorldSensors? sensors)
        {
            _worldSensors = sensors;
            _worldTopics.Clear();
        }

        /// <summary>"Ön LiDAR 2" → "on_lidar_2": a ROS name of a part's.</summary>
        public static string RosSlug(string name)
        {
            var b = new StringBuilder();
            foreach (char c in name.Normalize(NormalizationForm.FormD))
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                char x = c switch { 'ı' => 'i', 'İ' => 'i', _ => char.ToLowerInvariant(c) };
                if (x is >= 'a' and <= 'z' or >= '0' and <= '9') b.Append(x);
                else if (b.Length > 0 && b[b.Length - 1] != '_') b.Append('_');
            }

            string s = b.ToString().Trim('_');
            if (s.Length == 0) s = "sensor";
            return char.IsDigit(s[0]) ? "s_" + s : s;
        }

        private void StepWorldSensors()
        {
            if (_worldSensors == null || !_ros.Connected) return;
            foreach (var r in _worldSensors.All)
            {
                if (!r.Powered) continue;
                var (suffix, type) = r switch
                {
                    LidarReading => ("scan", "sensor_msgs/msg/LaserScan"),
                    ImuReading => ("imu", "sensor_msgs/msg/Imu"),
                    CameraReading => ("image_raw", "sensor_msgs/msg/Image"),
                    GpsReading => ("fix", "sensor_msgs/msg/NavSatFix"),
                    CompassReading => ("magnetic_field", "sensor_msgs/msg/MagneticField"),
                    _ => (string.Empty, string.Empty)
                };
                if (suffix.Length == 0) continue;
                string key = r.ObjectId + "/" + suffix;
                if (!_worldTopics.TryGetValue(key, out var t))
                {
                    string slug = RosSlug(string.IsNullOrWhiteSpace(r.Name) ? r.ObjectId : r.Name);
                    string topic = WorldSensorPrefix + slug + "/" + suffix;
                    for (int n = 2; _worldTopics.Values.Any(x => x.Topic == topic); n++) topic = WorldSensorPrefix + slug + "_" + n + "/" + suffix;   // two parts of one name
                    t = new WorldSensorTopic { Topic = topic, Frame = topic.Substring(WorldSensorPrefix.Length, topic.Length - WorldSensorPrefix.Length - suffix.Length - 1) };
                    _worldTopics[key] = t;
                    _ros.Advertise(t.Topic, type);
                }

                if (r is CameraReading cam ? cam.FrameIndex == t.LastFrame : r.Time <= t.LastTime) continue;   // nothing new
                t.LastTime = r.Time;
                if (r is CameraReading c) t.LastFrame = c.FrameIndex;
                var header = new JsonObject { ["stamp"] = Stamp(r.Time), ["frame_id"] = t.Frame };
                JsonObject? msg = r switch
                {
                    LidarReading l => LaserScan(l, header),
                    ImuReading i => new JsonObject
                    {
                        ["header"] = header,
                        ["orientation"] = Rotation(i.Orientation),
                        ["orientation_covariance"] = Diagonal(0),
                        ["angular_velocity"] = Xyz(i.Gyro),
                        ["angular_velocity_covariance"] = Diagonal(0),
                        ["linear_acceleration"] = Xyz(i.Accel),
                        ["linear_acceleration_covariance"] = Diagonal(0)
                    },
                    CameraReading cr when cr.Bgra.Length >= cr.Width * cr.Height * 4 && cr.Width > 0 => new JsonObject
                    {
                        ["header"] = header,
                        ["height"] = cr.Height,
                        ["width"] = cr.Width,
                        ["encoding"] = "bgra8",
                        ["is_bigendian"] = 0,
                        ["step"] = cr.Width * 4,
                        ["data"] = Convert.ToBase64String(cr.Bgra, 0, cr.Width * cr.Height * 4)   // rosbridge carries uint8[] as base64
                    },
                    GpsReading g => NavSatFix(g.Fix, header),
                    CompassReading m => new JsonObject
                    {
                        ["header"] = header,
                        ["magnetic_field"] = Xyz(m.Field * 1e-4),   // gauss → tesla
                        ["magnetic_field_covariance"] = Diagonal(0)
                    },
                    _ => null
                };
                if (msg == null) continue;
                _ros.Publish(t.Topic, msg);
                t.Sent++;
            }
        }

        /// <summary>
        /// A LiDAR's sweep as a LaserScan: its samples (clockwise from the front, in degrees) put counter-clockwise in
        /// radians into evenly spaced bins over the turn, as ROS wants them; a bin with no return reads past range_max. Of a
        /// many-channel LiDAR, the channel nearest the horizon.
        /// </summary>
        private static JsonObject LaserScan(LidarReading reading, JsonObject header)
        {
            var s = reading.Scan;
            int count = s.Ranges.Length;
            var take = Enumerable.Range(0, count).ToList();
            if (reading.Channels > 1 && s.ElevationsDeg.Length == count && count > 0)
            {
                float flat = s.ElevationsDeg.OrderBy(e => Math.Abs(e)).First();
                take = take.Where(i => s.ElevationsDeg[i] == flat).ToList();
            }

            int n = Math.Max(1, take.Count);
            double inc = 2 * Math.PI / n, maxRange = 0;
            var bins = Enumerable.Repeat(double.NaN, n).ToArray();
            foreach (int i in take)
            {
                double r = s.Ranges[i];
                if (double.IsNaN(r)) continue;
                maxRange = Math.Max(maxRange, r);
                double a = -s.AnglesDeg[i] * Math.PI / 180;   // ROS: counter-clockwise
                a = Math.IEEERemainder(a, 2 * Math.PI);   // −π … π
                int k = (int)Math.Round((a + Math.PI) / inc) % n;
                if (double.IsNaN(bins[k]) || r < bins[k]) bins[k] = r;
            }

            double rangeMax = Math.Max(1, Math.Ceiling(maxRange));
            var ranges = new JsonArray();
            foreach (double r in bins) ranges.Add(double.IsNaN(r) ? rangeMax + 1 : Math.Round(r, 4));   // no return: past range_max
            return new JsonObject
            {
                ["header"] = header,
                ["angle_min"] = -Math.PI,
                ["angle_max"] = -Math.PI + inc * (n - 1),
                ["angle_increment"] = inc,
                ["time_increment"] = s.ScanRate > 0 ? 1 / s.ScanRate / n : 0.0,
                ["scan_time"] = s.ScanRate > 0 ? 1 / s.ScanRate : 0.0,
                ["range_min"] = 0.0,
                ["range_max"] = rangeMax,
                ["ranges"] = ranges,
                ["intensities"] = new JsonArray()
            };
        }

        private static JsonObject NavSatFix(GpsFix fix, JsonObject header)
        {
            // A position's covariance from the receiver's dilution: σ ≈ HDOP × 2.5 m (a typical receiver's UERE), vertical twice that.
            double h = fix.Hdop > 0 ? Math.Pow(fix.Hdop * 2.5, 2) : 0;
            return new JsonObject
            {
                ["header"] = header,
                ["status"] = new JsonObject { ["status"] = fix.Valid ? 0 : -1, ["service"] = 1 },   // STATUS_FIX or NO_FIX; GPS
                ["latitude"] = fix.Latitude,
                ["longitude"] = fix.Longitude,
                ["altitude"] = fix.Altitude,
                ["position_covariance"] = new JsonArray(h, 0.0, 0.0, 0.0, h, 0.0, 0.0, 0.0, 4 * h),
                ["position_covariance_type"] = fix.Hdop > 0 ? 1 : 0   // APPROXIMATED or UNKNOWN
            };
        }
    }
}
