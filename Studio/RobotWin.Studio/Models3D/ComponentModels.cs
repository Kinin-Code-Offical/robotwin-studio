using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Pcb;

namespace RobotWin.Studio.Models3D
{
    /// <summary>
    /// Parametric 3D models of electronic components, boards, modules and electromechanical
    /// parts, sized from datasheet dimensions in millimetres.
    /// <para>
    /// Model frame: millimetres, right-handed, +Z up. The origin is the part's footprint
    /// origin on the board surface (z = 0); a footprint pad at layout position (x, y) — layout
    /// Y points down the board — sits at (x, −y, 0), and through-hole leads go down through a
    /// 1.6 mm board to z ≈ −2.9. Off-board devices built standalone (servo, DC motor, battery,
    /// lamp, supply) are the device itself with the centre of its base at the origin.
    /// </para>
    /// Geometry is cached per model key and frozen; every call returns a fresh root whose
    /// moving parts and live materials belong to that instance.
    /// </summary>
    public static class ComponentModels
    {
        /// <summary>Board thickness the through-hole leads are sized for.</summary>
        public const double BoardThickness = 1.6;

        /// <summary>Z of through-hole lead ends below the board surface.</summary>
        public const double LeadTail = -2.9;

        private static readonly Dictionary<string, ModelTemplate> Cache = new Dictionary<string, ModelTemplate>();
        private static readonly object Gate = new object();

        // ================================================================== public API

        /// <summary>
        /// Standalone model of a part (for free placement in a 3D world): millimetres,
        /// origin at the footprint origin on z = 0, +Z up. Off-board devices (servo, motor,
        /// battery …) are the device itself. Rotating parts are child groups named with
        /// <see cref="PartNameProperty"/> ("horn", "shaft", "knob") — see <see cref="FindMoving"/>.
        /// </summary>
        public static Model3DGroup Build(PartDefinition def, IReadOnlyDictionary<string, string> parameters) =>
            Create(def, parameters).Root;

        /// <summary>Standalone instance with its live hooks (heat glow, LED light, display, moving parts).</summary>
        public static ComponentInstance Create(PartDefinition def, IReadOnlyDictionary<string, string> parameters, bool standalone = true)
        {
            var fpId = def.ResolveFootprint(parameters);
            var req = new ModelRequest(def, parameters, FootprintLibrary.Get(fpId), fpId, ValueOf(def, parameters), def.RefPrefix + "?", standalone);
            return Template(req).Instantiate();
        }

        /// <summary>
        /// Model of a part as mounted on a board footprint (the footprint decides lead
        /// positions; off-board devices show their wires or plug instead of the device).
        /// </summary>
        public static ComponentInstance CreateOnBoard(Footprint footprint, PartDefinition? def, IReadOnlyDictionary<string, string>? parameters, string value, string refDes)
        {
            var p = parameters ?? new Dictionary<string, string>();
            var req = new ModelRequest(def, p, footprint, footprint.Id, value, refDes, false);
            return Template(req).Instantiate();
        }

        /// <summary>
        /// Where a cable attaches to each electrical pin of the standalone model (same frame as
        /// <see cref="Build"/>): header pin tips, servo connector contacts, motor tabs, battery
        /// lead ends, terminal wire entries … Direction points away from the part.
        /// </summary>
        public static IReadOnlyList<(string Pin, Point3D Position, Vector3D Direction)> PinAnchors(PartDefinition def, IReadOnlyDictionary<string, string> parameters)
        {
            var fpId = def.ResolveFootprint(parameters);
            var req = new ModelRequest(def, parameters, FootprintLibrary.Get(fpId), fpId, ValueOf(def, parameters), def.RefPrefix + "?", true);
            return MapPins(def, parameters, Template(req).Anchors);
        }

        /// <summary>Pin anchors of an on-board instance (pin names via the part's pad mapping).</summary>
        public static IReadOnlyList<(string Pin, Point3D Position, Vector3D Direction)> MapPins(PartDefinition? def, IReadOnlyDictionary<string, string>? parameters,
            IReadOnlyList<(string Pad, Point3D Position, Vector3D Direction)> padAnchors)
        {
            var result = new List<(string, Point3D, Vector3D)>();
            if (def == null)
            {
                foreach (var a in padAnchors) result.Add((a.Pad, a.Position, a.Direction));
                return result;
            }

            foreach (var pin in def.Pins)
            {
                string pad = def.ResolvePad(pin, parameters);
                foreach (var a in padAnchors)
                {
                    if (!string.Equals(a.Pad, pad, StringComparison.OrdinalIgnoreCase)) continue;
                    result.Add((pin.Name, a.Position, a.Direction));
                    break;
                }
            }

            return result;
        }

        /// <summary>Name of a rotating sub-model ("horn", "shaft", "knob") inside a component model.</summary>
        public static readonly DependencyProperty PartNameProperty =
            DependencyProperty.RegisterAttached("PartName", typeof(string), typeof(ComponentModels), new PropertyMetadata(null));

        public static void SetPartName(DependencyObject o, string value) => o.SetValue(PartNameProperty, value);
        public static string? GetPartName(DependencyObject o) => (string?)o.GetValue(PartNameProperty);

        /// <summary>Finds a named rotating sub-model; its Transform is a RotateTransform3D about the part's pivot.</summary>
        public static Model3DGroup? FindMoving(Model3D root, string name)
        {
            if (root is not Model3DGroup g) return null;
            if (string.Equals(GetPartName(g), name, StringComparison.OrdinalIgnoreCase)) return g;
            foreach (var c in g.Children)
            {
                var found = FindMoving(c, name);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>Sets the angle (degrees) of a named rotating sub-model; false if there is none.</summary>
        public static bool SetMovingAngle(Model3D root, string name, double degrees)
        {
            if (FindMoving(root, name)?.Transform is RotateTransform3D rt && rt.Rotation is AxisAngleRotation3D aa && !aa.IsFrozen)
            {
                aa.Angle = degrees;
                return true;
            }

            return false;
        }

        /// <summary>Wheel with tyre about the local Y axis, centred on the origin (mm).</summary>
        public static Model3DGroup Wheel(double diameter = 65, double width = 26, string style = "tt") =>
            Template(new ModelRequest("wheel", $"{diameter:0.##}|{width:0.##}|{style}", b => Gen.Wheel(b, diameter, width, style))).Instantiate().Root;

        /// <summary>Propeller about the local Z axis, hub centred on the origin (mm); blades in the XY plane.</summary>
        public static Model3DGroup Propeller(double diameter = 55, int blades = 2, uint rgb = 0xE5484D) =>
            Template(new ModelRequest("prop", $"{diameter:0.##}|{blades}|{rgb:X6}", b => Gen.Propeller(b, diameter, blades, rgb))).Instantiate().Root;

        /// <summary>
        /// Mounts a wheel on a motor's output shaft (inside the "shaft" moving part, so turning the
        /// shaft turns the wheel). The wheel group is named "wheel"; returns null when the model has no shaft.
        /// </summary>
        public static Model3DGroup? AttachWheel(ComponentInstance motor, double diameter = 65, double width = 26)
        {
            var shaft = motor.Find("shaft");
            if (shaft == null) return null;
            var dir = shaft.Tip - shaft.Pivot;
            if (dir.LengthSquared < 1e-9) dir = shaft.Axis;
            dir.Normalize();
            var wheel = Wheel(diameter, width);
            var up = new Vector3D(0, 1, 0);
            var axis = Vector3D.CrossProduct(up, dir);
            double dot = Math.Max(-1, Math.Min(1, Vector3D.DotProduct(up, dir)));
            var rot = axis.LengthSquared < 1e-12 ? (dot > 0 ? Matrix3D.Identity : Mx.Rz(180)) : Mx.R(axis, Math.Acos(dot) * 180 / Math.PI);
            var centre = shaft.Tip - dir * Math.Min(width / 2, 6);
            wheel.Transform = new MatrixTransform3D(rot * Mx.T(centre.X, centre.Y, centre.Z));
            SetPartName(wheel, "wheel");
            shaft.Group.Children.Add(wheel);
            return wheel;
        }

        /// <summary>Number of cached model templates (diagnostics).</summary>
        public static int CachedModels
        {
            get
            {
                lock (Gate) return Cache.Count;
            }
        }

        /// <summary>Drops every cached template (after a custom model changed).</summary>
        public static void ClearCache()
        {
            lock (Gate) Cache.Clear();
        }

        // ================================================================== cache and dispatch

        internal static ModelTemplate Template(ModelRequest req)
        {
            string key = req.Key;
            lock (Gate)
            {
                if (Cache.TryGetValue(key, out var t)) return t;
            }

            var b = new PartBuilder();
            try
            {
                if (req.Custom != null) req.Custom(b);
                else Generate(b, req);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                b = new PartBuilder();
                Gen.Fallback(b, req);
            }

            if (req.Fp != null) Gen.DefaultAnchors(b, req);
            var template = b.Build();
            lock (Gate)
            {
                if (Cache.TryGetValue(key, out var existing)) return existing;
                Cache[key] = template;
                if (Cache.Count > 4000) Cache.Clear();
            }

            return template;
        }

        internal static string ValueOf(PartDefinition def, IReadOnlyDictionary<string, string> p) =>
            !string.IsNullOrEmpty(def.ValueKey) && p.TryGetValue(def.ValueKey, out var v) ? v : def.Name;

        private static void Generate(PartBuilder b, ModelRequest r)
        {
            // A model the user attached to the part wins.
            if (CustomModels.TryBuild(b, r)) return;

            string id = r.Id;
            string fp = r.FpId;

            if (r.Standalone)
            {
                switch (id)
                {
                    case "servo": Gen.Servo(b, r); return;
                    case "dc-motor": Gen.Motor(b, r); return;
                    case "battery": Gen.Battery(b, r); return;
                    case "lamp": Gen.Lamp(b, r); return;
                    case "dc-source":
                    case "signal-generator":
                    case "current-source":
                    case "ammeter":
                    case "voltmeter":
                        Gen.Instrument(b, r);
                        return;
                    case "ground":
                    case "power-rail":
                        return;
                }

                if (r.Fp == null)
                {
                    Gen.Fallback(b, r);
                    return;
                }
            }

            if (r.Fp == null) return;

            // Boards and modules, aligned to their footprint pads.
            switch (id)
            {
                case "arduino-uno" when fp.StartsWith("ARDUINO-UNO", StringComparison.Ordinal): Gen.ArduinoUno(b, r); return;
                case "arduino-nano" when fp.StartsWith("ARDUINO-NANO", StringComparison.Ordinal): Gen.ArduinoNano(b, r); return;
                case Esp32Parts.DevKitCId when fp.StartsWith("ESP32", StringComparison.Ordinal):
                case Esp32Parts.DevKitV1Id when fp.StartsWith("ESP32", StringComparison.Ordinal):
                    Gen.Esp32(b, r);
                    return;
                case "hc-sr04" when fp.StartsWith("HDR", StringComparison.Ordinal): Gen.HcSr04(b, r); return;
                case "oled-ssd1306" when fp.StartsWith("HDR", StringComparison.Ordinal): Gen.Oled(b, r); return;
                case "lcd-i2c" when fp.StartsWith("HDR", StringComparison.Ordinal): Gen.Lcd(b, r); return;
                case "mpu6050" when fp.StartsWith("HDR", StringComparison.Ordinal): Gen.Gy521(b, r); return;
                case "motor-driver" when fp.StartsWith("HDR", StringComparison.Ordinal): Gen.MotorDriver(b, r); return;
                case "flight-controller" when fp.StartsWith("HDR", StringComparison.Ordinal): Gen.FlightController(b, r); return;
                case "sensor-module" when fp.StartsWith("HDR", StringComparison.Ordinal): Gen.SensorModule(b, r); return;
                case "servo" when fp.StartsWith("HDR", StringComparison.Ordinal): Gen.ServoPlug(b, r); return;
            }

            string family = Family(fp, id);
            switch (family)
            {
                case "AXIAL": Gen.Axial(b, r); return;
                case "DIODE": Gen.AxialDiode(b, r); return;
                case "CP": Gen.Electrolytic(b, r); return;
                case "RADIAL": Gen.Radial(b, r); return;
                case "LED": Gen.Led(b, r); return;
                case "CHIP": Gen.Chip(b, r); return;
                case "TO92": Gen.To92(b, r); return;
                case "TO220": Gen.To220(b, r); return;
                case "TO126": Gen.To126(b, r); return;
                case "SOT23": Gen.Sot23(b, r); return;
                case "SOT223": Gen.Sot223(b, r); return;
                case "DIP": Gen.Dip(b, r); return;
                case "SOIC": Gen.Soic(b, r); return;
                case "HDR": Gen.Header(b, r, female: false); return;
                case "SOCKET": Gen.Header(b, r, female: true); return;
                case "TERM": Gen.Terminal(b, r); return;
                case "TACT": Gen.Tact(b, r); return;
                case "SW": Gen.Toggle(b, r); return;
                case "POT": Gen.Pot(b, r); return;
                case "TRIM": Gen.Trimmer(b, r); return;
                case "RELAY": Gen.Relay(b, r); return;
                case "BUZZER": Gen.Buzzer(b, r); return;
                case "XTAL": Gen.Crystal(b, r); return;
                case "WIRE": Gen.Wires(b, r); return;
                case "UNO": Gen.ArduinoUno(b, r); return;
                case "NANO": Gen.ArduinoNano(b, r); return;
                case "ESP32": Gen.Esp32(b, r); return;
            }

            Gen.Fallback(b, r);
        }

        /// <summary>Package family from a footprint id (built-in ids and common KiCad names).</summary>
        internal static string Family(string fp, string partId = "")
        {
            string f = fp.ToUpperInvariant();
            bool Has(string s) => f.IndexOf(s, StringComparison.Ordinal) >= 0;
            if (f.StartsWith("AXIAL", StringComparison.Ordinal) || Has("R_AXIAL") || Has("L_AXIAL") || (Has("FUSE") && Has("AXIAL"))) return "AXIAL";
            if (f.StartsWith("DO", StringComparison.Ordinal) && (f.StartsWith("DO35", StringComparison.Ordinal) || f.StartsWith("DO41", StringComparison.Ordinal) || f.StartsWith("DO201", StringComparison.Ordinal)) || Has("DO-35") || Has("DO-41") || Has("DO-201") || Has("DO-15")) return "DIODE";
            if (f.StartsWith("CP-", StringComparison.Ordinal) || Has("CP_RADIAL") || Has("CP_ELEC") && Has("RADIAL")) return "CP";
            if (f.StartsWith("LED0805", StringComparison.Ordinal) || Has("LED_0805") || Has("LED_0603") || Has("LED_1206")) return "CHIP";
            if (f.StartsWith("LED-", StringComparison.Ordinal) || Has("LED_D5") || Has("LED_D3")) return "LED";
            if (f.StartsWith("RADIAL", StringComparison.Ordinal) || Has("C_DISC") || Has("C_RECT")) return "RADIAL";
            if (f.EndsWith("0805", StringComparison.Ordinal) || f.EndsWith("0603", StringComparison.Ordinal) || f.EndsWith("1206", StringComparison.Ordinal) ||
                Has("_0805_") || Has("_0603_") || Has("_1206_") || Has("_0402_")) return "CHIP";
            if (f.StartsWith("TO92", StringComparison.Ordinal) || Has("TO-92")) return "TO92";
            if (f.StartsWith("TO220", StringComparison.Ordinal) || Has("TO-220")) return "TO220";
            if (f.StartsWith("TO126", StringComparison.Ordinal) || Has("TO-126")) return "TO126";
            if (f.StartsWith("SOT223", StringComparison.Ordinal) || Has("SOT-223")) return "SOT223";
            if (f.StartsWith("SOT23", StringComparison.Ordinal) || Has("SOT-23")) return "SOT23";
            if (f.StartsWith("DIP", StringComparison.Ordinal) || Has("DIP-") && !Has("SW")) return "DIP";
            if (Has("SOIC") || Has("SOP-") || Has("TSSOP") || Has("SSOP") || Has("QFP")) return "SOIC";
            if (f.StartsWith("HDR", StringComparison.Ordinal) || Has("PINHEADER")) return "HDR";
            if (Has("PINSOCKET")) return "SOCKET";
            if (f.StartsWith("TERM", StringComparison.Ordinal) || Has("TERMINALBLOCK")) return "TERM";
            if (f.StartsWith("TACT", StringComparison.Ordinal) || Has("SW_PUSH") || Has("SW_TACTILE")) return "TACT";
            if (f.StartsWith("SW-", StringComparison.Ordinal) || Has("SW_TOGGLE") || Has("SW_SLIDE")) return "SW";
            if (f.StartsWith("POT", StringComparison.Ordinal) || Has("POTENTIOMETER") && !Has("3296")) return "POT";
            if (f.StartsWith("TRIM", StringComparison.Ordinal) || Has("3296")) return "TRIM";
            if (f.StartsWith("RELAY", StringComparison.Ordinal) || Has("RELAY")) return "RELAY";
            if (f.StartsWith("BUZZER", StringComparison.Ordinal) || Has("BUZZER")) return "BUZZER";
            if (f.StartsWith("HC49", StringComparison.Ordinal) || f.StartsWith("XTAL", StringComparison.Ordinal) || Has("CRYSTAL_HC49") || Has("CRYSTAL")) return "XTAL";
            if (f.StartsWith("WIRE", StringComparison.Ordinal)) return "WIRE";
            if (f.StartsWith("ARDUINO-UNO", StringComparison.Ordinal) || Has("ARDUINO_UNO")) return "UNO";
            if (f.StartsWith("ARDUINO-NANO", StringComparison.Ordinal) || Has("ARDUINO_NANO")) return "NANO";
            if (f.StartsWith("ESP32", StringComparison.Ordinal)) return "ESP32";
            return string.Empty;
        }
    }

    /// <summary>Everything a model generator needs to know about the part.</summary>
    internal sealed class ModelRequest
    {
        private static readonly IReadOnlyDictionary<string, string> NoParameters = new Dictionary<string, string>();

        public ModelRequest(PartDefinition? def, IReadOnlyDictionary<string, string> p, Footprint? fp, string fpId, string value, string refDes, bool standalone)
        {
            Def = def;
            P = p ?? NoParameters;
            Fp = fp;
            FpId = (fpId ?? string.Empty).ToUpperInvariant();
            Value = value ?? string.Empty;
            RefDes = refDes ?? string.Empty;
            Standalone = standalone;
            Key = MakeKey();
        }

        /// <summary>Request for a non-catalog model (wheel, propeller) with its own generator.</summary>
        public ModelRequest(string kind, string args, Action<PartBuilder> custom)
        {
            P = NoParameters;
            FpId = string.Empty;
            Value = string.Empty;
            RefDes = string.Empty;
            Custom = custom;
            Key = "X|" + kind + "|" + args;
        }

        public PartDefinition? Def { get; }
        public IReadOnlyDictionary<string, string> P { get; }
        public Footprint? Fp { get; }
        public string FpId { get; }
        public string Value { get; }
        public string RefDes { get; }
        public bool Standalone { get; }
        public Action<PartBuilder>? Custom { get; }
        public string Key { get; }

        public string Id => Def?.Id ?? string.Empty;
        public SymbolKind? Symbol => Def?.Symbol;

        public string Text(string key, string fallback = "") => P.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

        public double Num(string key, double fallback) => P.TryGetValue(key, out var v) && SiValue.TryParse(v, out var d) ? d : fallback;

        /// <summary>Part number / model text ("LM358", "2N2222A", "SG90").</summary>
        public string Model => Text("model", Def?.Name ?? Value);

        public bool ModelHas(string s) => Model.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0;

        public IReadOnlyList<Pad> Pads => Fp?.Pads ?? (IReadOnlyList<Pad>)Array.Empty<Pad>();

        public Pad? Pad(string name) => Fp?.FindPad(name);

        /// <summary>Pad of a named pin (honours pinmap/pinout).</summary>
        public Pad? PinPad(string pin)
        {
            var d = Def?.FindPin(pin);
            return d == null ? null : Fp?.FindPad(Def!.ResolvePad(d, P));
        }

        private string MakeKey()
        {
            var sb = new StringBuilder();
            sb.Append(Standalone ? 'S' : 'B').Append('|').Append(Def?.Id).Append('|').Append(FpId).Append('|').Append(Value).Append('|');
            foreach (var kv in P.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (kv.Key == "position" || kv.Key == "closed" || kv.Key == "soc" || kv.Key == "load") continue;
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            }

            if (Fp != null)
            {
                sb.Append('|').Append(Fp.Pads.Count).Append(',').Append(Fp.CourtyardMinX.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
                    .Append(Fp.CourtyardMaxX.ToString("0.##", CultureInfo.InvariantCulture)).Append(',').Append(Fp.CourtyardMinY.ToString("0.##", CultureInfo.InvariantCulture))
                    .Append(',').Append(Fp.CourtyardMaxY.ToString("0.##", CultureInfo.InvariantCulture));
                if (ComponentModels.Family(FpId, Id).Length == 0) sb.Append('|').Append(RefDes);
            }

            return sb.ToString();
        }
    }
}
