using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Formats
{
    /// <summary>
    /// [ros] A sensor a robot description declares for the simulator (<c>&lt;gazebo reference="link"&gt;&lt;sensor …&gt;</c>, as
    /// Gazebo classic and Gazebo Sim read them): a laser scanner (<c>ray</c>, <c>gpu_ray</c>, <c>lidar</c>, <c>gpu_lidar</c>)
    /// or an IMU, with its rate, its pose on the link, its scan and range, its noise and the ROS topic it publishes on.
    /// </summary>
    public sealed class UrdfSensor
    {
        /// <summary>"lidar" or "imu".</summary>
        public string Kind { get; set; } = "lidar";

        public string Name { get; set; } = string.Empty;

        /// <summary>The link it rides on (the gazebo element's reference).</summary>
        public string Link { get; set; } = string.Empty;

        /// <summary>Where it sits on the link (the sensor's <c>&lt;pose&gt;</c>: x y z roll pitch yaw).</summary>
        public UrdfPose Pose { get; set; } = UrdfPose.Identity;

        public double Rate { get; set; } = 10;

        /// <summary>The topic (relative, e.g. "scan") and the frame the messages carry (the link unless the plugin names one).</summary>
        public string Topic { get; set; } = string.Empty;

        public string Frame { get; set; } = string.Empty;

        // Laser scanner: horizontal rays from MinAngle to MaxAngle (rad, counter-clockwise), returns MinRange…MaxRange (m).
        public int Samples { get; set; } = 360;

        public double MinAngle { get; set; } = -Math.PI;

        public double MaxAngle { get; set; } = Math.PI;

        public double MinRange { get; set; } = 0.1;

        public double MaxRange { get; set; } = 10;

        /// <summary>Gaussian noise (σ): range (m) for a scanner, angular rate (rad/s) for an IMU.</summary>
        public double Noise { get; set; }

        /// <summary>An IMU's linear acceleration noise (σ, m/s²).</summary>
        public double AccelNoise { get; set; }
    }

    public static class UrdfSensorReader
    {
        private static readonly string[] Scanners = { "ray", "gpu_ray", "lidar", "gpu_lidar" };

        /// <summary>The sensors in one <c>&lt;gazebo&gt;</c> element; <paramref name="rootLink"/> for one with no reference.</summary>
        public static IEnumerable<UrdfSensor> Read(XElement gazebo, string rootLink, List<string> notes)
        {
            string link = (string?)gazebo.Attribute("reference") ?? rootLink;
            foreach (var s in gazebo.Elements().Where(e => e.Name.LocalName == "sensor"))
            {
                string type = ((string?)s.Attribute("type") ?? string.Empty).Trim().ToLowerInvariant();
                string name = (string?)s.Attribute("name") ?? type;
                bool scanner = Scanners.Contains(type);
                if (!scanner && type != "imu")
                {
                    notes.Add($"'{name}' ({type}) algılayıcısı henüz yok; alınmadı.");
                    continue;
                }

                var sensor = new UrdfSensor
                {
                    Kind = scanner ? "lidar" : "imu",
                    Name = name,
                    Link = link,
                    Pose = SdfPose(Text(s, "pose")),
                    Rate = Positive(Number(Text(s, "update_rate")), scanner ? 10 : 100),
                    Frame = link
                };

                if (scanner)
                {
                    var ray = Child(s, "ray") ?? Child(s, "lidar");
                    var horizontal = Child(Child(ray, "scan"), "horizontal");
                    sensor.Samples = (int)Math.Max(1, Math.Min(100000, Positive(Number(Text(horizontal, "samples")), 360)));
                    sensor.MinAngle = Finite(Number(Text(horizontal, "min_angle")), -Math.PI);
                    sensor.MaxAngle = Finite(Number(Text(horizontal, "max_angle")), Math.PI);
                    var range = Child(ray, "range");
                    sensor.MinRange = Positive(Number(Text(range, "min")), 0.1);
                    sensor.MaxRange = Positive(Number(Text(range, "max")), 10);
                    sensor.Noise = Math.Max(0, Finite(Number(Text(Child(ray, "noise"), "stddev")), 0));
                    if (Child(Child(ray, "scan"), "vertical") is { } vertical && Number(Text(vertical, "samples")) > 1)
                        notes.Add($"'{name}' çok katmanlı tarayıcı: yalnız yatay tarama (/scan) yayımlanır.");
                }
                else
                {
                    var imu = Child(s, "imu");
                    sensor.Noise = AxisNoise(Child(imu, "angular_velocity"));
                    sensor.AccelNoise = AxisNoise(Child(imu, "linear_acceleration"));
                }

                // The topic: Gazebo Sim's <topic>, a ROS 2 plugin's "~/out:=scan" remapping, or a ROS 1 plugin's <topicName>.
                string? topic = Text(s, "topic");
                string? frame = Text(s, "gz_frame_id");
                foreach (var plugin in s.Elements().Where(e => e.Name.LocalName == "plugin"))
                {
                    foreach (var remap in plugin.Descendants().Where(e => e.Name.LocalName == "remapping"))
                    {
                        var parts = remap.Value.Split(new[] { ":=" }, StringSplitOptions.None);
                        if (parts.Length == 2 && parts[0].Trim() == "~/out") topic = parts[1].Trim();
                    }

                    topic = Text(plugin, "topicName") ?? topic;
                    frame = Text(plugin, "frame_name") ?? Text(plugin, "frameName") ?? frame;
                }

                sensor.Topic = string.IsNullOrWhiteSpace(topic) ? (scanner ? "scan" : "imu") : topic!.Trim();
                if (!string.IsNullOrWhiteSpace(frame)) sensor.Frame = frame!.Trim();
                yield return sensor;
            }
        }

        /// <summary>The largest per-axis σ of an IMU's <c>&lt;x&gt;&lt;noise&gt;&lt;stddev&gt;</c> (Gazebo gives one per axis).</summary>
        private static double AxisNoise(XElement? quantity)
        {
            if (quantity == null) return 0;
            double sigma = 0;
            foreach (var axis in quantity.Elements())
                sigma = Math.Max(sigma, Finite(Number(Text(Child(axis, "noise"), "stddev")), 0));
            return sigma;
        }

        private static XElement? Child(XElement? e, string name) => e?.Elements().FirstOrDefault(x => x.Name.LocalName == name);

        private static string? Text(XElement? e, string name) => Child(e, name)?.Value.Trim();

        private static double Number(string? text) =>
            text != null && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;

        private static double Positive(double v, double fallback) => v > 0 && !double.IsInfinity(v) ? v : fallback;

        private static double Finite(double v, double fallback) => double.IsNaN(v) || double.IsInfinity(v) ? fallback : v;

        /// <summary>SDF's "x y z roll pitch yaw".</summary>
        private static UrdfPose SdfPose(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return UrdfPose.Identity;
            var n = text!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(Number).ToArray();
            if (n.Length < 6 || n.Any(double.IsNaN)) return UrdfPose.Identity;
            return new UrdfPose(new Vec3(n[0], n[1], n[2]), new Vec3(n[3], n[4], n[5]));
        }
    }
}
