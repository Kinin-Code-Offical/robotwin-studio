using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace RobotTwin.CoreSim.Sandbox.Sensing
{
    /// <summary>
    /// [sensorlog] Every reading the world's sensors publish while it runs, one CSV table per sensor: what the sensor
    /// measured, beside the truth where the simulation knows it (an IMU's real specific force and rate, a GPS fix's real
    /// error), and a label column with the fault scenario active on that sensor at that moment ("normal" or the fault's
    /// id) — a labelled data set for sensor fusion and fault detection, made from the same run.
    /// </summary>
    public sealed class SensorLog
    {
        private readonly WorldDocument _world;
        private readonly Dictionary<string, Table> _tables = new Dictionary<string, Table>(StringComparer.Ordinal);
        private WorldSensors? _source;

        private sealed class Table
        {
            public string Name = "";
            public string Kind = "";
            public readonly StringBuilder Text = new StringBuilder();
            public int Rows;
        }

        public SensorLog(WorldDocument world) => _world = world ?? throw new ArgumentNullException(nameof(world));

        /// <summary>Starts taking what <paramref name="sensors"/> publish.</summary>
        public void Attach(WorldSensors sensors)
        {
            Detach();
            _source = sensors;
            sensors.Published += OnPublished;
        }

        public void Detach()
        {
            if (_source != null) _source.Published -= OnPublished;
            _source = null;
        }

        /// <summary>Rows taken so far, over every sensor.</summary>
        public int Rows => _tables.Values.Sum(t => t.Rows);

        /// <summary>The sensors logged (file name stem, kind, rows).</summary>
        public IEnumerable<(string Name, string Kind, int Rows)> Sensors => _tables.Values.Select(t => (t.Name, t.Kind, t.Rows));

        /// <summary>A value to 9 significant digits (more than any sensor resolves); NaN (no value) as an empty cell.</summary>
        private static string N(double v) => double.IsNaN(v) ? "" : v.ToString("G9", CultureInfo.InvariantCulture);

        /// <summary>A time to the nanosecond, without the binary fraction's tail (0.02, not 0.020000000000000004).</summary>
        private static string T(double v) => v.ToString("0.#########", CultureInfo.InvariantCulture);

        private string Label(string objectId, double time)
        {
            var o = _world.Find(objectId);
            return o != null && MachineFaults.Of(o) is { } f && time >= f.At ? f.Fault.Id : "normal";
        }

        private void OnPublished(SensorReading r)
        {
            string? header = null, row = null;
            string t = T(r.Time), label = Label(r.ObjectId, r.Time);
            switch (r)
            {
                case ImuReading i:
                    header = "time_s,ax,ay,az,gx,gy,gz,true_ax,true_ay,true_az,true_gx,true_gy,true_gz,chip_temp_c,label";
                    row = string.Join(",", t, N(i.Accel.X), N(i.Accel.Y), N(i.Accel.Z), N(i.Gyro.X), N(i.Gyro.Y), N(i.Gyro.Z),
                        N(i.TrueAccel.X), N(i.TrueAccel.Y), N(i.TrueAccel.Z), N(i.TrueGyro.X), N(i.TrueGyro.Y), N(i.TrueGyro.Z), N(i.TemperatureC), label);
                    break;
                case GpsReading g:
                    var f = g.Fix;
                    header = "time_s,valid,latitude,longitude,altitude_m,satellites,hdop,speed_mps,course_deg,true_error_east_m,true_error_north_m,label";
                    row = string.Join(",", t, f.Valid ? "1" : "0", N(f.Latitude), N(f.Longitude), N(f.Altitude), f.Satellites.ToString(CultureInfo.InvariantCulture),
                        N(f.Hdop), N(f.SpeedMps), N(f.CourseDeg), N(f.ErrorEast), N(f.ErrorNorth), label);
                    break;
                case CompassReading c:
                    header = "time_s,bx_gauss,by_gauss,bz_gauss,heading_deg,label";
                    row = string.Join(",", t, N(c.Field.X), N(c.Field.Y), N(c.Field.Z), N(c.HeadingDeg), label);
                    break;
                case EncoderReading e:
                    header = "time_s,count,counts_per_rev,angle_rad,rpm,label";
                    row = string.Join(",", t, e.Count.ToString(CultureInfo.InvariantCulture), N(e.CountsPerRevolution), N(e.OutputAngleRad), N(e.OutputRpm), label);
                    break;
                case LineSensorReading l:
                {
                    var cols = new List<string> { "time_s", "powered" };
                    var vals = new List<string> { t, l.Powered ? "1" : "0" };
                    for (int k = 0; k < l.Channels.Length; k++)
                    {
                        string ch = (k + 1).ToString(CultureInfo.InvariantCulture);
                        cols.Add("ch" + ch + "_photocurrent_a");
                        cols.Add("ch" + ch + "_volts");
                        cols.Add("ch" + ch + "_true_reflectance");
                        vals.Add(N(l.Channels[k].Photocurrent));
                        vals.Add(N(l.Channels[k].Voltage));
                        vals.Add(N(l.Channels[k].Reflectance));
                    }

                    cols.Add("label");
                    vals.Add(label);
                    header = string.Join(",", cols);
                    row = string.Join(",", vals);
                    break;
                }

                case LidarReading d when d.Powered && d.Scan.Ranges.Length > 0:
                    // A scan a row: its bearings once in the header's comment line would not fit every scan rate, so each
                    // row carries "angle:range" pairs (no return: an empty range).
                    header = "time_s,scan_rate_hz,points,angle_deg:range_m,label";
                    row = string.Join(",", t, N(d.Scan.ScanRate), d.Scan.Ranges.Length.ToString(CultureInfo.InvariantCulture),
                        string.Join(";", d.Scan.Ranges.Select((v, k) => (k < d.Scan.AnglesDeg.Length ? d.Scan.AnglesDeg[k].ToString("0.##", CultureInfo.InvariantCulture) : "") + ":" +
                                                                       (float.IsNaN(v) ? "" : v.ToString("0.####", CultureInfo.InvariantCulture)))), label);
                    break;
            }

            if (header == null || row == null) return;
            if (!_tables.TryGetValue(r.ObjectId, out var table))
            {
                table = new Table { Name = FileStem(r.Name, r.ObjectId), Kind = r.Kind };
                table.Text.Append(header).Append('\n');
                _tables[r.ObjectId] = table;
            }

            table.Text.Append(row).Append('\n');
            table.Rows++;
        }

        private string FileStem(string name, string id)
        {
            var sb = new StringBuilder();
            foreach (char c in string.IsNullOrWhiteSpace(name) ? id : name)
            {
                char k = char.IsLetterOrDigit(c) || c == '-' ? c : '_';
                if (k != '_' || sb.Length == 0 || sb[sb.Length - 1] != '_') sb.Append(k);   // "Modülü (GY-NEO6MV2)": one '_'
            }
            string stem = sb.ToString().Trim('_');
            if (stem.Length == 0) stem = id;
            // Two sensors of the same name keep apart.
            string unique = stem;
            for (int k = 2; _tables.Values.Any(t => string.Equals(t.Name, unique, StringComparison.OrdinalIgnoreCase)); k++) unique = stem + "_" + k.ToString(CultureInfo.InvariantCulture);
            return unique;
        }

        /// <summary>A sensor's table as CSV text (null when it published nothing).</summary>
        public string? Csv(string objectId) => _tables.TryGetValue(objectId, out var t) ? t.Text.ToString() : null;

        /// <summary>
        /// Writes every table to <paramref name="folder"/> as "name.csv", and a README of what is in them; returns the files.
        /// <paramref name="extra"/>: tables made beside the log from the same run (a fusion experiment's), written with them.
        /// </summary>
        public List<string> Save(string folder, params (string Name, string Kind, string Csv)[] extra)
        {
            Directory.CreateDirectory(folder);
            var files = new List<string>();
            var readme = new StringBuilder();
            readme.Append("RobotWin Studio sensor log: ").Append(_world.Title).Append('\n');
            readme.Append("Times in seconds of simulated time; SI units (m/s², rad/s, gauss, m, A, V).\n");
            readme.Append("true_* columns: the value the simulation knows exactly, beside what the sensor measured.\n");
            readme.Append("label: the fault scenario active on that sensor at that time, or 'normal'.\n\n");
            foreach (var t in _tables.Values)
            {
                string path = Path.Combine(folder, t.Name + ".csv");
                File.WriteAllText(path, t.Text.ToString(), new UTF8Encoding(false));
                files.Add(path);
                readme.Append(t.Name).Append(".csv: ").Append(t.Kind).Append(", ").Append(t.Rows.ToString(CultureInfo.InvariantCulture)).Append(" rows\n");
            }

            foreach (var (name, kind, csv) in extra)
            {
                string path = Path.Combine(folder, name + ".csv");
                File.WriteAllText(path, csv, new UTF8Encoding(false));
                files.Add(path);
                int rows = Math.Max(0, csv.Count(ch => ch == '\n') - 1);
                readme.Append(name).Append(".csv: ").Append(kind).Append(", ").Append(rows.ToString(CultureInfo.InvariantCulture)).Append(" rows\n");
            }

            string read = Path.Combine(folder, "README.txt");
            File.WriteAllText(read, readme.ToString(), new UTF8Encoding(false));
            files.Add(read);
            return files;
        }
    }
}
