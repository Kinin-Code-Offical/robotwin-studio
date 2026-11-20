using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Sandbox.Sensing
{
    /// <summary>Surface finish of a floor tape (what an IR reflectance sensor actually sees).</summary>
    public enum TapeFinish
    {
        /// <summary>Matte black (cloth / matte vinyl): ≈6 % IR diffuse reflectance, almost no gloss.</summary>
        Matte,

        /// <summary>Worn, faded, dusty black tape: ≈15–40 % reflectance in patches, white floor showing through scuffs.</summary>
        Faded,

        /// <summary>Glossy black vinyl / lacquered tape: dark when seen diffusely, but a mirror for the emitter and for lamps at the right angle.</summary>
        Glossy,

        /// <summary>White tape on a dark floor (inverted track).</summary>
        White,

        /// <summary>Metallic (aluminium / silver) tape: strong specular reflection.</summary>
        Metallic,

        /// <summary>Uses <see cref="FloorTape.Reflectance"/> and <see cref="FloorTape.Gloss"/> as given.</summary>
        Custom
    }

    /// <summary>
    /// A piece of tape on the floor: a polyline of a given width. A track is made of several
    /// tapes so that different sections can have different finishes (matte, faded, glossy).
    /// </summary>
    public sealed class FloorTape
    {
        public string Name { get; set; } = "Bant";

        /// <summary>Centre line as [x, y] pairs in metres.</summary>
        public List<double[]> Points { get; set; } = new List<double[]>();

        public double Width { get; set; } = 0.019;

        public TapeFinish Finish { get; set; } = TapeFinish.Matte;

        /// <summary>Diffuse IR (≈950 nm) reflectance for <see cref="TapeFinish.Custom"/> (0–1).</summary>
        public double Reflectance { get; set; } = 0.06;

        /// <summary>Specular (mirror-like) reflectance at normal incidence for <see cref="TapeFinish.Custom"/> (0–1).</summary>
        public double Gloss { get; set; } = 0.01;

        /// <summary>Wear of a faded tape, 0 (new) … 1 (almost gone).</summary>
        public double Wear { get; set; } = 0.5;

        public int Seed { get; set; } = 1;

        /// <summary>Optical properties of the finish: base diffuse reflectance, specular reflectance, patch amplitude.</summary>
        public (double Diffuse, double Specular, double Patch) Optics() => Finish switch
        {
            TapeFinish.Matte => (0.06, 0.008, 0.01),
            TapeFinish.Faded => (0.12 + 0.26 * Math.Max(0, Math.Min(1, Wear)), 0.02, 0.08 + 0.20 * Math.Max(0, Math.Min(1, Wear))),
            TapeFinish.Glossy => (0.07, 0.10, 0.01),
            TapeFinish.White => (0.88, 0.04, 0.02),
            TapeFinish.Metallic => (0.25, 0.60, 0.02),
            _ => (Clamp01(Reflectance), Clamp01(Gloss), 0.02)
        };

        /// <summary>Display colour of the finish (flat, for the 3D floor).</summary>
        public string DisplayColor() => Finish switch
        {
            TapeFinish.Faded => "#4A4A4E",
            TapeFinish.Glossy => "#0B0B0D",
            TapeFinish.White => "#EDEDED",
            TapeFinish.Metallic => "#B8BCC4",
            TapeFinish.Custom => Reflectance > 0.5 ? "#D8D8D8" : "#2A2A2E",
            _ => "#151517"
        };

        public static string FinishName(TapeFinish f) => f switch
        {
            TapeFinish.Matte => "Mat siyah",
            TapeFinish.Faded => "Solmuş / aşınmış",
            TapeFinish.Glossy => "Parlak siyah",
            TapeFinish.White => "Beyaz",
            TapeFinish.Metallic => "Metalik",
            _ => "Özel"
        };

        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }

    /// <summary>
    /// Light, floor optics and geography of a world for its sensors: ambient light level
    /// (cameras), infrared from the sun / incandescent lamps (reflectance sensors), the
    /// floor's own IR reflectance and gloss, taped tracks, and the geodetic origin and
    /// magnetic field used by GPS and compass modules.
    /// </summary>
    public sealed class SensingEnvironment
    {
        /// <summary>Visible illuminance on the floor (lux): 50 dim room, 300–500 office, 10 000+ daylight, 100 000 direct sun.</summary>
        public double AmbientLux { get; set; } = 400;

        /// <summary>
        /// Near-IR (≈950 nm) background relative to direct sunlight (0 = LED/fluorescent lighting only,
        /// 0.05 incandescent desk lamp, 0.2–0.4 beside a sunny window, 1 = direct sun).
        /// </summary>
        public double AmbientIr { get; set; } = 0.0;

        /// <summary>Direction the strongest light comes from (degrees): elevation above the floor and azimuth from +X.</summary>
        public double LightElevationDeg { get; set; } = 60;

        public double LightAzimuthDeg { get; set; } = 135;

        /// <summary>Floor IR diffuse reflectance (white laminate ≈ 0.85, light wood 0.6, grey concrete 0.35).</summary>
        public double FloorReflectance { get; set; } = 0.85;

        /// <summary>Floor specular reflectance (matte paper 0.01, laminate 0.04, polished tile 0.08).</summary>
        public double FloorGloss { get; set; } = 0.03;

        public List<FloorTape> Tapes { get; set; } = new List<FloorTape>();

        /// <summary>Geodetic position of the world origin (WGS-84 degrees, metres above sea level); +X = east, +Y = north.</summary>
        public double OriginLatitude { get; set; } = 41.0151;

        public double OriginLongitude { get; set; } = 28.9795;
        public double OriginAltitude { get; set; } = 40;

        /// <summary>"open" open sky (≈8–12 satellites), "urban" streets (5–8, multipath), "indoor" (weak, often no fix).</summary>
        public string Sky { get; set; } = "open";

        /// <summary>Earth field: total intensity (µT), inclination (dip, degrees down) and declination (degrees east of true north).</summary>
        public double MagneticFieldMicroTesla { get; set; } = 47.5;

        public double MagneticInclinationDeg { get; set; } = 56.5;
        public double MagneticDeclinationDeg { get; set; } = 6.0;

        /// <summary>Earth magnetic field in world coordinates (µT; +X east, +Y north, +Z up).</summary>
        public (double X, double Y, double Z) MagneticField()
        {
            double inc = MagneticInclinationDeg * Math.PI / 180, dec = MagneticDeclinationDeg * Math.PI / 180;
            double h = MagneticFieldMicroTesla * Math.Cos(inc);
            return (h * Math.Sin(dec), h * Math.Cos(dec), -MagneticFieldMicroTesla * Math.Sin(inc));
        }

        public SensingEnvironment Clone()
        {
            var c = (SensingEnvironment)MemberwiseClone();
            c.Tapes = Tapes.Select(t => new FloorTape
            {
                Name = t.Name, Points = t.Points.Select(p => (double[])p.Clone()).ToList(), Width = t.Width, Finish = t.Finish,
                Reflectance = t.Reflectance, Gloss = t.Gloss, Wear = t.Wear, Seed = t.Seed
            }).ToList();
            return c;
        }
    }
}
