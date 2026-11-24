using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox.Radio;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [radio] 2D layer over the 3D world view: expanding rings where radios transmit
    /// (BLE blue, Wi-Fi orange, Bluetooth controllers violet), link lines between associated
    /// devices coloured by RSSI with a dBm label, and IR beams to the receivers that see them.
    /// </summary>
    public sealed class RadioOverlay : FrameworkElement
    {
        /// <summary>Visual expansion speed of the rings (m/s of simulated time; real radio waves are ~10⁸× faster).</summary>
        public const double RingSpeed = 0.9;

        private RadioMedium? _medium;
        private Func<string, Point3D?>? _position;

        public RadioOverlay()
        {
            IsHitTestVisible = false;
        }

        public Func<Point3D, (Point Screen, double Depth, bool Visible)>? Project { get; set; }

        public bool ShowRings { get; set; } = true;
        public bool ShowLinks { get; set; } = true;

        /// <summary>Called every frame with the medium (null clears) and a live position lookup by node id.</summary>
        public void Update(RadioMedium? medium, Func<string, Point3D?> position)
        {
            _medium = medium;
            _position = position;
            InvalidateVisual();
        }

        public static Color TechColor(RadioTech tech) => tech switch
        {
            RadioTech.Ble => Color.FromRgb(0x3E, 0x8E, 0xF7),
            RadioTech.WiFi => Color.FromRgb(0xF2, 0xA3, 0x3B),
            RadioTech.Bluetooth => Color.FromRgb(0xA9, 0x70, 0xFF),
            _ => Color.FromRgb(0xE5, 0x48, 0x4D)
        };

        /// <summary>Link quality colour: green ≥ −60 dBm, yellow ≥ −75, orange ≥ −85, red below.</summary>
        public static Color RssiColor(double rssi) =>
            rssi >= -60 ? Color.FromRgb(0x3C, 0xD0, 0x64) : rssi >= -75 ? Color.FromRgb(0xE6, 0xD2, 0x3C) : rssi >= -85 ? Color.FromRgb(0xF2, 0x8A, 0x2C) : Color.FromRgb(0xE5, 0x48, 0x4D);

        private Point3D? Where(string nodeId, Vec3 fallback)
        {
            var p = _position?.Invoke(nodeId);
            return p ?? new Point3D(fallback.X, fallback.Y, fallback.Z);
        }

        protected override void OnRender(DrawingContext dc)
        {
            var m = _medium;
            if (m == null || Project == null) return;
            double now = m.Time;

            if (ShowRings)
            {
                foreach (var e in m.Emissions(now - 2.2))
                {
                    double age = now - e.Time;
                    if (age < 0) continue;
                    double reach = e.Tech == RadioTech.WiFi ? 1.6 : e.Tech == RadioTech.Bluetooth ? 1.0 : 1.2;
                    reach *= Math.Pow(10, (e.PowerDbm - 4) / 40.0);
                    double r = age * RingSpeed;
                    if (r > reach || r < 0.002) continue;
                    double alpha = (1 - r / reach) * 0.85;
                    var c = TechColor(e.Tech);
                    var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(alpha * 255), c.R, c.G, c.B)), 1.6);
                    var center = Where(e.NodeId, e.Position) ?? new Point3D(e.Position.X, e.Position.Y, e.Position.Z);
                    DrawRing(dc, center, r, pen);
                }
            }

            if (ShowLinks)
            {
                foreach (var link in m.Links())
                {
                    var a = Where(link.A.Id, link.A.Position());
                    var b = Where(link.B.Id, link.B.Position());
                    if (a == null || b == null) continue;
                    var pa = Project(a.Value);
                    var pb = Project(b.Value);
                    if (!pa.Visible || !pb.Visible) continue;
                    var col = RssiColor(link.RssiDbm);
                    var pen = new Pen(new SolidColorBrush(Color.FromArgb(0xE0, col.R, col.G, col.B)), 2.2);
                    if (link.Label == "eşleşiyor") pen.DashStyle = DashStyles.Dash;
                    dc.DrawLine(pen, pa.Screen, pb.Screen);
                    var mid = new Point((pa.Screen.X + pb.Screen.X) / 2, (pa.Screen.Y + pb.Screen.Y) / 2);
                    string text = string.Format(CultureInfo.InvariantCulture, "{0} {1:0} dBm", link.Label, link.RssiDbm);
                    var ft = new FormattedText(RobotWin.Studio.Shell.Lang.T(text), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, new SolidColorBrush(col), 1.0);
                    var box = new Rect(mid.X - ft.Width / 2 - 4, mid.Y - ft.Height / 2 - 1, ft.Width + 8, ft.Height + 2);
                    dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xB0, 0x0B, 0x0B, 0x0D)), null, box, 4, 4);
                    dc.DrawText(ft, new Point(box.X + 4, box.Y + 1));
                }

                // IR: a dashed red beam from an active emitter to every receiver in its line of sight.
                foreach (var e in m.Ir.Emitters)
                {
                    if (e.LastActivity < now - 0.25) continue;
                    var pe = Project(Where(e.Id, e.Position()) ?? default);
                    foreach (var r in m.Ir.Receivers)
                    {
                        bool seen = m.Ir.LineOfSight(e, r);
                        var pr = Project(Where(r.Id, r.Position()) ?? default);
                        if (!pe.Visible || !pr.Visible) continue;
                        var pen = new Pen(new SolidColorBrush(seen ? Color.FromArgb(0xE0, 0xFF, 0x40, 0x50) : Color.FromArgb(0x50, 0x90, 0x40, 0x40)), seen ? 2.0 : 1.0) { DashStyle = DashStyles.Dot };
                        dc.DrawLine(pen, pe.Screen, pr.Screen);
                    }
                }
            }
        }

        private void DrawRing(DrawingContext dc, Point3D c, double radius, Pen pen)
        {
            const int n = 40;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                bool started = false;
                for (int i = 0; i <= n; i++)
                {
                    double a = 2 * Math.PI * i / n;
                    var p = Project!(new Point3D(c.X + radius * Math.Cos(a), c.Y + radius * Math.Sin(a), c.Z));
                    if (!p.Visible)
                    {
                        started = false;
                        continue;
                    }

                    if (!started)
                    {
                        ctx.BeginFigure(p.Screen, false, false);
                        started = true;
                    }
                    else
                    {
                        ctx.LineTo(p.Screen, true, false);
                    }
                }
            }

            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
    }
}
