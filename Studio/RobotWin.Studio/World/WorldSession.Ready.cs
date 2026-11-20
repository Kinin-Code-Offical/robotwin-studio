using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [objs] Positional audio (the camera is the listener, every sound comes from its object)
    /// and the light emitted by free-standing LEDs and lamps.
    /// </summary>
    public sealed partial class WorldSession
    {
        private Vec3 _earLast, _earVelocity;
        private DateTime _earTime;

        /// <summary>Called every frame with the 3D camera: where the ears are and which way they face.</summary>
        public void SetListener(Vec3 position, Vec3 forward, Vec3 up)
        {
            var now = DateTime.UtcNow;
            double dt = (now - _earTime).TotalSeconds;
            var v = dt > 1e-3 && dt < 0.5 ? (position - _earLast) / dt : Vec3.Zero;
            // Orbiting / zooming jumps are not a moving listener.
            if (v.Length > 15) v = Vec3.Zero;
            _earVelocity = _earVelocity * 0.7 + v * 0.3;
            _earLast = position;
            _earTime = now;
            _sounds.Listener = new AudioListener(position, forward, up, _earVelocity);
            _sounds.Locate ??= LocateSound;
            if (World != null && Simulator != null && (now - _hotChecked).TotalSeconds > 0.25)
            {
                _hotChecked = now;
                string? hot = null;
                double max = double.NegativeInfinity;
                foreach (var o in World.Objects)
                {
                    double t = Temperature(o);
                    if (!double.IsNaN(t) && t > max)
                    {
                        max = t;
                        hot = o.Id;
                    }
                }

                _sounds.HotSource = hot;
            }
        }

        private DateTime _hotChecked;

        /// <summary>World position and velocity of a sound's source: device "O3/M1", physics body "O3:shaft", object "O3" or a cable.</summary>
        private (Vec3 Position, Vec3 Velocity)? LocateSound(string source)
        {
            var world = World;
            if (world == null) return null;
            if (source.StartsWith("cable:", StringComparison.Ordinal))
            {
                var c = world.Cables.FirstOrDefault(x => "cable:" + x.Id == source);
                if (c == null) return null;
                var a = Where(world.Find(c.A.ObjectId));
                var b = Where(world.Find(c.B.ObjectId));
                return a != null && b != null ? ((a.Value + b.Value) * 0.5, Vec3.Zero) : null;
            }

            int cut = source.IndexOfAny(new[] { '/', ':' });
            var o = world.Find(cut > 0 ? source.Substring(0, cut) : source);
            var at = Where(o);
            if (at == null) return null;
            return (at.Value, Physics?.VelocityOf(o!.Id) ?? Vec3.Zero);
        }

        private Vec3? Where(WorldObject? o)
        {
            if (o == null) return null;
            if (Physics?.PoseOf(o.Id) is { } p) return p.Position + p.Orientation.Rotate(new Vec3(0, 0, 0.012));
            return o.Pose.Position + new Vec3(0, 0, 0.012);
        }

        // ================================================================ light output

        private sealed class LightInfo
        {
            public readonly List<(AnalogDevice Device, double R, double G, double B, double Lumens)> Emitters = new List<(AnalogDevice, double, double, double, double)>();
        }

        private readonly Dictionary<string, LightInfo?> _lights = new Dictionary<string, LightInfo?>(StringComparer.OrdinalIgnoreCase);
        private WorldCircuit? _lightsFor;

        /// <summary>True for parts that emit light into the world (LEDs, lamps, LED strips, power LEDs).</summary>
        public static bool IsLight(PartDefinition? def) =>
            def != null && (def.Symbol == SymbolKind.Led || def.Symbol == SymbolKind.Lamp || def.Id == "led-rgb" || def.Id == "led-strip" || def.Id == "led-power");

        /// <summary>
        /// Light an object emits right now: colour (linear 0..1 per channel, weighted by flux) and the luminous
        /// flux (lm) from the simulated current of each LED / lamp in it.
        /// </summary>
        public (double R, double G, double B, double Lumens) LightOutput(WorldObject o)
        {
            if (Circuit == null || o.Kind != WorldObjectKind.Component) return (0, 0, 0, 0);
            if (_lightsFor != Circuit)
            {
                _lights.Clear();
                _lightsFor = Circuit;
            }

            if (!_lights.TryGetValue(o.Id, out var info))
            {
                info = BuildLight(o);
                _lights[o.Id] = info;
            }

            if (info == null) return (0, 0, 0, 0);
            double r = 0, g = 0, b = 0, lm = 0;
            foreach (var (d, cr, cg, cb, flux) in info.Emitters)
            {
                double k = d switch
                {
                    Led led => led.IsFailed ? 0 : Math.Min(2.5, led.Brightness),
                    Lamp lamp => lamp.IsFailed ? 0 : Math.Min(2.5, lamp.Brightness),
                    _ => 0
                };
                if (k <= 0) continue;
                double f = flux * k;
                r += cr * f;
                g += cg * f;
                b += cb * f;
                lm += f;
            }

            return lm <= 0 ? (0, 0, 0, 0) : (r / lm, g / lm, b / lm, lm);
        }

        private LightInfo? BuildLight(WorldObject o)
        {
            var def = o.PartId != null ? _library.Find(o.PartId) : null;
            if (!IsLight(def) || Circuit?.ComponentRef(o.Id) is not { } r) return null;
            var preset = o.PresetId != null ? _library.FindPreset(o.PresetId) : null;
            var p = _library.ResolveParameters(def!, preset, o.Parameters);
            double Num(string key, double fallback) => p.TryGetValue(key, out var v) && SiValue.TryParse(v, out var x) ? x : fallback;
            string colour = p.TryGetValue("color", out var c) ? c : "white";
            var info = new LightInfo();
            foreach (var d in Circuit.Compiled.DevicesOf(r))
            {
                if (d is not Led && d is not Lamp) continue;
                (double R, double G, double B) rgb;
                double flux;
                if (def!.Id == "led-rgb")
                {
                    char ch = d.Id.Length > 0 ? char.ToUpperInvariant(d.Id[d.Id.Length - 1]) : 'R';
                    rgb = ch == 'G' ? (0.25, 1.0, 0.4) : ch == 'B' ? (0.3, 0.5, 1.0) : (1.0, 0.22, 0.15);
                    // Share of the white flux per die (green dominates luminous flux).
                    flux = Num("lumens", 4) * (ch == 'G' ? 0.55 : ch == 'B' ? 0.15 : 0.3);
                }
                else if (d is Lamp lamp)
                {
                    rgb = (1.0, 0.78, 0.52);
                    flux = 12.0 * lamp.RatedPower;   // small incandescent bulbs ≈ 10–14 lm/W
                }
                else
                {
                    rgb = ColourOf(colour);
                    flux = def.Id == "led-power" || def.Id == "led-strip" ? Num("lumens", 20) : IndicatorLumens(colour, def.ResolveFootprint(p));
                }

                info.Emitters.Add((d, rgb.R, rgb.G, rgb.B, flux));
            }

            return info.Emitters.Count > 0 ? info : null;
        }

        private static (double R, double G, double B) ColourOf(string colour) => (colour ?? "red").ToLowerInvariant() switch
        {
            "green" or "yeşil" => (0.27, 1.0, 0.43),
            "blue" or "mavi" => (0.31, 0.5, 1.0),
            "yellow" or "sarı" => (1.0, 0.88, 0.24),
            "orange" or "turuncu" => (1.0, 0.58, 0.16),
            "white" or "beyaz" => (1.0, 1.0, 0.98),
            "warm" => (1.0, 0.84, 0.63),
            "uv" => (0.6, 0.3, 1.0),
            "ir" => (0.3, 0.05, 0.08),
            _ => (1.0, 0.2, 0.15)
        };

        /// <summary>Typical luminous flux of an indicator LED at its rated 20 mA (lm).</summary>
        private static double IndicatorLumens(string colour, string footprint)
        {
            double lm = (colour ?? "red").ToLowerInvariant() switch
            {
                "green" or "yeşil" => 3.0,
                "blue" or "mavi" => 1.2,
                "white" or "beyaz" => 5.0,
                "uv" => 0.15,
                "ir" => 0.0,
                _ => 1.5
            };
            string f = (footprint ?? string.Empty).ToUpperInvariant();
            if (f.Contains("3MM")) lm *= 0.5;
            else if (f.Contains("0805") || f.Contains("0603")) lm *= 0.3;
            return lm;
        }
    }
}
