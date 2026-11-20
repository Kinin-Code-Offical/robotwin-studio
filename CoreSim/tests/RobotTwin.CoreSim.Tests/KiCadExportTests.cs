using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    [Trait("Category", "Design")]
    public class KiCadExportTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;

        // ================================================================== s-expression reader

        private sealed class SExpr
        {
            public string? Atom;
            public bool Quoted;
            public readonly List<SExpr> Items = new List<SExpr>();

            public bool IsList => Atom == null;
            public string Head => IsList && Items.Count > 0 && !Items[0].IsList ? Items[0].Atom! : string.Empty;
            public IEnumerable<SExpr> All(string head) => Items.Where(i => i.IsList && i.Head == head);
            public SExpr? One(string head) => All(head).FirstOrDefault();
            public SExpr Req(string head) => One(head) ?? throw new InvalidOperationException($"({Head} ...) has no ({head} ...)");
            public string A(int index) => Items[index].Atom ?? throw new InvalidOperationException($"item {index} of ({Head}) is a list");
            public double D(int index) => double.Parse(A(index), NumberStyles.Float, CultureInfo.InvariantCulture);
            public int Count => Items.Count;

            public IEnumerable<SExpr> Descendants(string head)
            {
                foreach (var item in Items.Where(i => i.IsList))
                {
                    if (item.Head == head) yield return item;
                    foreach (var d in item.Descendants(head)) yield return d;
                }
            }
        }

        /// <summary>Strict reader: one root list, balanced parentheses, KiCad string escapes.</summary>
        private static SExpr Parse(string text)
        {
            var stack = new Stack<SExpr>();
            SExpr? root = null;
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c))
                {
                    i++;
                }
                else if (c == '(')
                {
                    var list = new SExpr();
                    if (stack.Count > 0) stack.Peek().Items.Add(list);
                    else if (root != null) throw new FormatException($"second top-level list at {i}");
                    else root = list;
                    stack.Push(list);
                    i++;
                }
                else if (c == ')')
                {
                    if (stack.Count == 0) throw new FormatException($"unbalanced ')' at {i}");
                    if (stack.Peek().Items.Count == 0) throw new FormatException($"empty list at {i}");
                    stack.Pop();
                    i++;
                }
                else
                {
                    if (stack.Count == 0) throw new FormatException($"atom outside a list at {i}");
                    var atom = new SExpr();
                    if (c == '"')
                    {
                        var sb = new StringBuilder();
                        i++;
                        while (true)
                        {
                            if (i >= text.Length) throw new FormatException("unterminated string");
                            char s = text[i++];
                            if (s == '"') break;
                            if (s == '\\')
                            {
                                char e = text[i++];
                                sb.Append(e == 'n' ? '\n' : e == 'r' ? '\r' : e == 't' ? '\t' : e);
                            }
                            else
                            {
                                if (s == '\n') throw new FormatException("raw newline in string");
                                sb.Append(s);
                            }
                        }

                        atom.Atom = sb.ToString();
                        atom.Quoted = true;
                    }
                    else
                    {
                        int start = i;
                        while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '(' && text[i] != ')')
                        {
                            if (text[i] == '"') throw new FormatException($"quote inside bare atom at {i}");
                            i++;
                        }

                        atom.Atom = text.Substring(start, i - start);
                    }

                    stack.Peek().Items.Add(atom);
                }
            }

            if (stack.Count != 0) throw new FormatException($"{stack.Count} unclosed '('");
            return root ?? throw new FormatException("no list");
        }

        // ================================================================== fixtures

        private static void Label(Schematic sch, SchematicPart part, string pin, string net)
        {
            var p = Lib.Get(part.PartId).FindPin(pin) ?? throw new ArgumentException(part.PartId + " has no pin " + pin);
            sch.Labels.Add(new NetLabel { Name = net, Position = part.Transform(p.X, p.Y) });
        }

        private static void Lock(BoardDocument board, string refDes, double x, double y, int rotation, CopperLayer side)
        {
            var c = board.Find(refDes) ?? throw new ArgumentException(refDes);
            c.X = x;
            c.Y = y;
            c.Rotation = rotation;
            c.Side = side;
            c.Locked = true;
        }

        private static void Finish(BoardDocument board)
        {
            AutoPlacer.Place(board);
            new AutoRouter(board) { Passes = 2 }.Route();
            board.Zones.Add(new Zone { Net = "GND", Layer = CopperLayer.Bottom });
            // GND stitching vias for the pour.
            board.Vias.Add(new Via { Net = "GND", X = 3, Y = 3, Diameter = board.Rules.ViaDiameter, Drill = board.Rules.ViaDrill });
            board.Vias.Add(new Via { Net = "GND", X = board.Width - 3, Y = board.Height - 3, Diameter = board.Rules.ViaDiameter, Drill = board.Rules.ViaDrill });
        }

        /// <summary>The motor driver of DesignAndPcbTests plus an SMD gate pull-down; parts at 90/180/270, two on the bottom.</summary>
        private static (Schematic Sch, BoardDocument Board) BuildMotorBoard()
        {
            var sch = new Schematic { Title = "Motor sürücü" };
            var bat = sch.AddPart(Lib, "bat-3s-lipo", new GridPoint(0, 0));        // BT1
            var reg = sch.AddPart(Lib, "lm7805", new GridPoint(10, -3));          // U1
            var c1 = sch.AddPart(Lib, "ecap-100u", new GridPoint(20, 3), 90);     // C1
            var m = sch.AddPart(Lib, "motor-25ga370", new GridPoint(30, -10), 90); // M1
            var d = sch.AddPart(Lib, "1n5819", new GridPoint(36, -10), 270);      // D1
            var q = sch.AddPart(Lib, "irlz44n", new GridPoint(30, 0));            // Q1
            var rg = sch.AddPart(Lib, "res-100", new GridPoint(22, 10));          // R1
            var hdr = sch.AddPart(Lib, "hdr-1x3", new GridPoint(10, 12));         // J1
            var rl = sch.AddPart(Lib, "res-1000", new GridPoint(40, 8));          // R2
            var led = sch.AddPart(Lib, "led5-green", new GridPoint(48, 8));       // D2
            sch.AddPart(Lib, "gnd", new GridPoint(0, 20));
            var rpd = sch.AddPart(Lib, "res-10k-0805", new GridPoint(22, 16), 180); // R3

            Label(sch, bat, "+", "VBAT"); Label(sch, bat, "-", "GND");
            Label(sch, reg, "IN", "VBAT"); Label(sch, reg, "GND", "GND"); Label(sch, reg, "OUT", "5V");
            Label(sch, c1, "+", "5V"); Label(sch, c1, "-", "GND");
            Label(sch, m, "A", "VBAT"); Label(sch, m, "B", "DRAIN");
            Label(sch, d, "A", "DRAIN"); Label(sch, d, "K", "VBAT");
            Label(sch, q, "D", "DRAIN"); Label(sch, q, "S", "GND"); Label(sch, q, "G", "GATE");
            Label(sch, rg, "B", "GATE"); Label(sch, rg, "A", "PWM");
            Label(sch, hdr, "1", "5V"); Label(sch, hdr, "2", "PWM"); Label(sch, hdr, "3", "GND");
            Label(sch, rl, "A", "5V"); Label(sch, rl, "B", "LEDA");
            Label(sch, led, "A", "LEDA"); Label(sch, led, "K", "GND");
            Label(sch, rpd, "A", "GATE"); Label(sch, rpd, "B", "GND");

            var board = BoardDocument.Synchronize(sch, Lib);
            board.Width = 70;
            board.Height = 50;
            Lock(board, "Q1", 40, 25, 180, CopperLayer.Top);
            Lock(board, "D1", 55, 30, 270, CopperLayer.Top);
            Lock(board, "R3", 30, 42, 90, CopperLayer.Bottom);     // SMD on the bottom
            Lock(board, "D2", 62, 10, 270, CopperLayer.Bottom);    // THT on the bottom
            Finish(board);
            return (sch, board);
        }

        /// <summary>
        /// Arduino Nano board: pinmap/pinout presets (HC-SR04, SG90, AO3400 "GSD", BC547B "CBE",
        /// LM358), SMD and THT parts on both sides, a footprint-less ammeter, real wires
        /// (including a T into the ground symbol and a power-rail symbol), rotated and mirrored
        /// schematic symbols.
        /// </summary>
        private static (Schematic Sch, BoardDocument Board) BuildSensorBoard()
        {
            var sch = new Schematic { Title = "Nano sensör kartı" };
            var nano = sch.AddPart(Lib, "arduino-nano", new GridPoint(0, 0));          // U1
            var reg = sch.AddPart(Lib, "lm7805", new GridPoint(-30, -20));             // U2
            var cin = sch.AddPart(Lib, "ecap-100u", new GridPoint(-40, -12), 270);     // C1
            var cout = sch.AddPart(Lib, "cap-100n", new GridPoint(-20, -14), 90);      // C2
            var jin = sch.AddPart(Lib, "term-2", new GridPoint(-55, -20));             // J1
            var am = sch.AddPart(Lib, "ammeter", new GridPoint(-45, -28));             // AM1 (no footprint)
            var sonar = sch.AddPart(Lib, "hc-sr04", new GridPoint(30, -12));           // U3
            var servo = sch.AddPart(Lib, "servo-sg90", new GridPoint(30, 6));          // M1
            var btn = sch.AddPart(Lib, "tact-6mm", new GridPoint(-20, 10), 90);        // SW1
            var rpu = sch.AddPart(Lib, "res-10k-0805", new GridPoint(-20, 0), 90);     // R1
            var led = sch.AddPart(Lib, "led-0805-red", new GridPoint(20, 25));         // D1
            var rled = sch.AddPart(Lib, "res-220-0805", new GridPoint(10, 25));        // R2
            var q = sch.AddPart(Lib, "ao3400", new GridPoint(44, 30));                 // Q1
            var bz = sch.AddPart(Lib, "buzzer-5v", new GridPoint(56, 20), 90);         // BZ1
            var qb = sch.AddPart(Lib, "bc547b", new GridPoint(-10, 35));               // Q2
            var rb = sch.AddPart(Lib, "res-4700", new GridPoint(-22, 35), 180);        // R3
            var op = sch.AddPart(Lib, "lm358", new GridPoint(10, 50));                 // U4
            op.Mirror = true;
            var gnd = sch.AddPart(Lib, "gnd", new GridPoint(-30, 40));
            var rail = sch.AddPart(Lib, "rail-5v", new GridPoint(-30, -40));

            Label(sch, jin, "1", "VRAW"); Label(sch, jin, "2", "GND");
            Label(sch, am, "+", "VRAW"); Label(sch, am, "-", "VIN");
            Label(sch, reg, "IN", "VIN"); Label(sch, reg, "GND", "GND"); Label(sch, reg, "OUT", "5V");
            Label(sch, cin, "+", "VIN"); Label(sch, cin, "-", "GND");
            Label(sch, cout, "A", "5V"); Label(sch, cout, "B", "GND");
            Label(sch, nano, "VIN", "VIN"); Label(sch, nano, "5V", "5V"); Label(sch, nano, "GND1", "GND");
            Label(sch, nano, "D2", "TRIG"); Label(sch, nano, "D3", "ECHO"); Label(sch, nano, "D4", "BTN");
            Label(sch, nano, "D5", "BUZ"); Label(sch, nano, "D6", "QDRV"); Label(sch, nano, "D9", "SERVO");
            Label(sch, sonar, "VCC", "5V"); Label(sch, sonar, "TRIG", "TRIG"); Label(sch, sonar, "ECHO", "ECHO"); Label(sch, sonar, "GND", "GND");
            Label(sch, servo, "V+", "5V"); Label(sch, servo, "GND", "GND"); Label(sch, servo, "SIG", "SERVO");
            Label(sch, btn, "A", "BTN"); Label(sch, btn, "B", "GND");
            Label(sch, rpu, "B", "BTN");
            Label(sch, rled, "B", "LEDA"); Label(sch, led, "A", "LEDA"); Label(sch, led, "K", "GND");
            Label(sch, q, "G", "BUZ"); Label(sch, q, "S", "GND"); Label(sch, q, "D", "BZN");
            Label(sch, bz, "+", "5V"); Label(sch, bz, "-", "BZN");
            Label(sch, rb, "A", "QDRV"); Label(sch, rb, "B", "QB");
            Label(sch, qb, "B", "QB"); Label(sch, qb, "C", "OUT2");
            Label(sch, op, "IN+", "OUT2"); Label(sch, op, "IN-", "OPFB"); Label(sch, op, "OUT", "OPFB");
            Label(sch, op, "V+", "5V"); Label(sch, op, "V-", "GND");

            // D13 -> R2.A drawn as a wire; Q2.E wired to the ground symbol; R1.A wired to the +5V rail.
            var d13 = nano.Transform(7, 13);
            sch.AddWire(d13, rled.Transform(-3, 0));
            var g = gnd.Transform(0, -1);
            var e = qb.Transform(1, 3);
            sch.AddWire(g, new GridPoint(g.X, e.Y), e);
            var r = rail.Transform(0, 1);
            var rA = rpu.Transform(-3, 0);
            sch.AddWire(r, new GridPoint(-25, r.Y), new GridPoint(-25, rA.Y), rA);

            var board = BoardDocument.Synchronize(sch, Lib);
            board.Width = 90;
            board.Height = 70;
            Lock(board, "U2", 10, 12, 90, CopperLayer.Top);
            Lock(board, "Q2", 80, 60, 270, CopperLayer.Top);
            Lock(board, "R1", 45, 64, 0, CopperLayer.Bottom);      // SMD on the bottom
            Lock(board, "C2", 62, 8, 180, CopperLayer.Bottom);     // THT on the bottom
            Lock(board, "R2", 30, 64, 270, CopperLayer.Bottom);    // SMD, rotated, bottom
            Finish(board);
            return (sch, board);
        }

        private static readonly Lazy<(Schematic Sch, BoardDocument Board)> Motor = new Lazy<(Schematic, BoardDocument)>(BuildMotorBoard);
        private static readonly Lazy<(Schematic Sch, BoardDocument Board)> Sensor = new Lazy<(Schematic, BoardDocument)>(BuildSensorBoard);

        private static (Schematic Sch, BoardDocument Board) Fixture(string name) => name == "motor" ? Motor.Value : Sensor.Value;

        public static IEnumerable<object[]> Fixtures()
        {
            yield return new object[] { "motor" };
            yield return new object[] { "sensor" };
        }

        private static bool Exported(SchematicPart part)
        {
            var def = Lib.Find(part.PartId);
            return def != null && def.Symbol != SymbolKind.Ground && def.Symbol != SymbolKind.PowerRail;
        }

        /// <summary>BuildNetlist with pins replaced by footprint pad names: net -> {"REF/pad"}.</summary>
        private static Dictionary<string, HashSet<string>> ExpectedNets(Schematic sch)
        {
            var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var net in sch.BuildNetlist(Lib).Nets.Where(n => n.Connections.Count > 0))
            {
                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (var c in net.Connections)
                {
                    var part = sch.FindPart(c.RefDes)!;
                    var def = Lib.Get(part.PartId);
                    set.Add(part.RefDes + "/" + def.ResolvePad(def.FindPin(c.Pin)!, sch.ResolveParameters(Lib, part)));
                }

                result[net.Name] = set;
            }

            return result;
        }

        private static void AssertSameNets(Dictionary<string, HashSet<string>> expected, Dictionary<string, HashSet<string>> actual)
        {
            Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.Ordinal), actual.Keys.OrderBy(k => k, StringComparer.Ordinal));
            foreach (var kv in expected)
            {
                Assert.True(kv.Value.SetEquals(actual[kv.Key]),
                    $"net {kv.Key}: expected {{{string.Join(", ", kv.Value.OrderBy(x => x))}}}, got {{{string.Join(", ", actual[kv.Key].OrderBy(x => x))}}}");
            }
        }

        // ================================================================== KiCad transforms

        /// <summary>KiCad RotatePoint (trigo.cpp): x' = y sin + x cos, y' = y cos - x sin; exact for quarter turns.</summary>
        private static (double X, double Y) KiCadRotate(double x, double y, double degrees)
        {
            double a = ((degrees % 360) + 360) % 360;
            double s, c;
            if (a == 0) { s = 0; c = 1; }
            else if (a == 90) { s = 1; c = 0; }
            else if (a == 180) { s = 0; c = -1; }
            else if (a == 270) { s = -1; c = 0; }
            else
            {
                s = Math.Sin(a * Math.PI / 180);
                c = Math.Cos(a * Math.PI / 180);
            }

            return (y * s + x * c, y * c - x * s);
        }

        /// <summary>
        /// Schematic symbol transform as KiCad's parser builds it: (at x y a) selects TRANSFORM
        /// 0:(1,0,0,-1) 90:(0,-1,-1,0) 180:(-1,0,0,1) 270:(0,1,1,0); (mirror x|y) then composes
        /// on the library side; sheet = at + (x1*lx + y1*ly, x2*lx + y2*ly).
        /// </summary>
        private static (double X, double Y) SymbolToSheet(double lx, double ly, double sx, double sy, int angle, string? mirror)
        {
            if (mirror == "y") lx = -lx;
            else if (mirror == "x") ly = -ly;
            int x1, y1, x2, y2;
            switch (angle)
            {
                case 0: x1 = 1; y1 = 0; x2 = 0; y2 = -1; break;
                case 90: x1 = 0; y1 = -1; x2 = -1; y2 = 0; break;
                case 180: x1 = -1; y1 = 0; x2 = 0; y2 = 1; break;
                case 270: x1 = 0; y1 = 1; x2 = 1; y2 = 0; break;
                default: throw new InvalidOperationException("symbol angle " + angle);
            }

            return (sx + x1 * lx + y1 * ly, sy + x2 * lx + y2 * ly);
        }

        private static double Norm(double angle) => ((angle % 360) + 360) % 360;

        // ================================================================== tests: board

        [Theory]
        [MemberData(nameof(Fixtures))]
        public void PcbReproducesFootprintsPadsNetsAndCopper(string fixture)
        {
            var (_, board) = Fixture(fixture);

            // The fixture really exercises every orientation path.
            Assert.Contains(board.Components, c => c.Side == CopperLayer.Bottom && !FootprintLibrary.Get(c.FootprintId)!.Pads[0].IsThroughHole);
            Assert.Contains(board.Components, c => c.Side == CopperLayer.Bottom && FootprintLibrary.Get(c.FootprintId)!.Pads[0].IsThroughHole);
            Assert.True(board.Components.Select(c => Norm(c.Rotation)).Distinct().Count() >= 3);
            Assert.NotEmpty(board.Tracks);

            string text = KiCadExporter.ExportPcb(board, "test");
            var root = Parse(text);
            Assert.Equal("kicad_pcb", root.Head);
            Assert.Equal("20240108", root.Req("version").A(1));
            Assert.Equal("robotwin_studio", root.Req("generator").A(1));
            Assert.Equal("1.6", root.Req("general").Req("thickness").A(1));
            Assert.Equal("A4", root.Req("paper").A(1));

            var layers = root.Req("layers").Items.Where(l => l.IsList).ToDictionary(l => l.A(1), l => int.Parse(l.Head, CultureInfo.InvariantCulture));
            Assert.Equal(0, layers["F.Cu"]);
            Assert.Equal(31, layers["B.Cu"]);
            foreach (string l in new[] { "B.Adhes", "F.Adhes", "B.Paste", "F.Paste", "B.SilkS", "F.SilkS", "B.Mask", "F.Mask", "Edge.Cuts", "Margin", "B.CrtYd", "F.CrtYd", "B.Fab", "F.Fab" })
            {
                Assert.True(layers.ContainsKey(l), l);
            }

            var setup = root.Req("setup");
            Assert.Equal(0.05, setup.Req("pad_to_mask_clearance").D(1));
            Assert.Equal(board.Rules.Clearance, setup.Req("trace_clearance").D(1));
            Assert.Equal(board.Rules.MinTrackWidth, setup.Req("trace_min").D(1));
            Assert.Equal(board.Rules.ViaDrill, setup.Req("via_drill").D(1));

            var nets = root.All("net").ToDictionary(n => int.Parse(n.A(1), CultureInfo.InvariantCulture), n => n.A(2));
            Assert.Equal(string.Empty, nets[0]);
            Assert.Equal(nets.Count, nets.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (string net in board.NetNames()) Assert.Contains(net, nets.Values, StringComparer.OrdinalIgnoreCase);

            // Footprints and pads: absolute pad positions via KiCad's rules must equal Pads().
            var footprints = root.All("footprint").ToList();
            Assert.Equal(board.Components.Count, footprints.Count);
            var expected = board.Pads().ToDictionary(p => p.Component.RefDes + "/" + p.Pad.Name);
            var seen = new HashSet<string>();
            foreach (var fp in footprints)
            {
                string refDes = fp.All("property").Single(p => p.A(1) == "Reference").A(2);
                var comp = board.Find(refDes)!;
                Assert.Equal("RobotWin:" + FootprintLibrary.Get(comp.FootprintId)!.Id, fp.A(1));
                Assert.Equal(comp.Side == CopperLayer.Bottom ? "B.Cu" : "F.Cu", fp.Req("layer").A(1));
                Assert.Equal(comp.Value, fp.All("property").Single(p => p.A(1) == "Value").A(2));
                Assert.Matches("^/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", fp.Req("path").A(1));
                if (comp.Side == CopperLayer.Bottom)
                {
                    Assert.Equal("B.SilkS", fp.All("property").Single(p => p.A(1) == "Reference").Req("layer").A(1));
                    Assert.All(fp.Items.Where(i => i.IsList && i.Head.StartsWith("fp_", StringComparison.Ordinal)),
                        g => Assert.StartsWith("B.", g.Req("layer").A(1)));
                }

                var at = fp.Req("at");
                double fx = at.D(1), fy = at.D(2), fa = at.Count > 3 ? at.D(3) : 0.0;
                foreach (var pad in fp.All("pad"))
                {
                    string key = refDes + "/" + pad.A(1);
                    Assert.True(seen.Add(key), "duplicate pad " + key);
                    var exp = expected[key];

                    var pat = pad.Req("at");
                    double lx = pat.D(1), ly = pat.D(2), pa = pat.Count > 3 ? pat.D(3) : 0.0;
                    var (rx, ry) = KiCadRotate(lx, ly, fa);
                    double ax = fx + rx, ay = fy + ry;
                    Assert.True(Math.Abs(ax - exp.X) < 1e-6 && Math.Abs(ay - exp.Y) < 1e-6,
                        $"{key} (rot {comp.Rotation}, {comp.Side}): KiCad ({ax}, {ay}) vs board ({exp.X}, {exp.Y})");
                    Assert.Equal(Norm(fa), Norm(pa));

                    var size = pad.Req("size");
                    double w = size.D(1), h = size.D(2);
                    bool swap = Norm(pa) % 180 != 0;
                    double ew = exp.Pad.Shape == PadShape.Circle ? Math.Max(exp.Width, exp.Height) : exp.Width;
                    double eh = exp.Pad.Shape == PadShape.Circle ? Math.Max(exp.Width, exp.Height) : exp.Height;
                    Assert.Equal(ew, swap ? h : w, 6);
                    Assert.Equal(eh, swap ? w : h, 6);

                    var padLayers = pad.Req("layers").Items.Skip(1).Select(x => x.Atom).ToList();
                    if (exp.Pad.IsThroughHole)
                    {
                        Assert.Equal("thru_hole", pad.A(2));
                        Assert.Equal(new[] { "*.Cu", "*.Mask" }, padLayers);
                        Assert.Equal(exp.Pad.Drill, pad.Req("drill").D(1), 6);
                    }
                    else
                    {
                        Assert.Equal("smd", pad.A(2));
                        string side = exp.OnLayer(CopperLayer.Bottom) ? "B" : "F";
                        Assert.Equal(new[] { side + ".Cu", side + ".Paste", side + ".Mask" }, padLayers);
                    }

                    string expectedShape = exp.Pad.Shape switch
                    {
                        PadShape.Circle => "circle",
                        PadShape.Rect => "rect",
                        PadShape.Oval => "oval",
                        _ => "roundrect"
                    };
                    Assert.Equal(expectedShape, pad.A(3));

                    var netNode = pad.One("net");
                    if (string.IsNullOrEmpty(exp.Net))
                    {
                        Assert.Null(netNode);
                    }
                    else
                    {
                        Assert.NotNull(netNode);
                        int code = int.Parse(netNode!.A(1), CultureInfo.InvariantCulture);
                        Assert.Equal(nets[code], netNode.A(2));   // KiCad orphans pads whose name differs from the table
                        Assert.Equal(exp.Net, netNode.A(2), ignoreCase: true);
                    }
                }
            }

            Assert.Equal(expected.Count, seen.Count);

            // Tracks, vias, zones, outline.
            var segments = root.All("segment").ToList();
            Assert.Equal(board.Tracks.Count, segments.Count);
            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                var t = board.Tracks[i];
                Assert.Equal(t.X1, s.Req("start").D(1), 6);
                Assert.Equal(t.Y1, s.Req("start").D(2), 6);
                Assert.Equal(t.X2, s.Req("end").D(1), 6);
                Assert.Equal(t.Y2, s.Req("end").D(2), 6);
                Assert.Equal(t.Width, s.Req("width").D(1), 6);
                Assert.Equal(t.Layer == CopperLayer.Top ? "F.Cu" : "B.Cu", s.Req("layer").A(1));
                Assert.Equal(t.Net, nets[int.Parse(s.Req("net").A(1), CultureInfo.InvariantCulture)], ignoreCase: true);
            }

            var vias = root.All("via").ToList();
            Assert.NotEmpty(vias);
            Assert.Equal(board.Vias.Count, vias.Count);
            for (int i = 0; i < vias.Count; i++)
            {
                Assert.Equal(board.Vias[i].X, vias[i].Req("at").D(1), 6);
                Assert.Equal(board.Vias[i].Y, vias[i].Req("at").D(2), 6);
                Assert.Equal(board.Vias[i].Diameter, vias[i].Req("size").D(1), 6);
                Assert.Equal(board.Vias[i].Drill, vias[i].Req("drill").D(1), 6);
                Assert.Equal(new[] { "F.Cu", "B.Cu" }, vias[i].Req("layers").Items.Skip(1).Select(x => x.Atom));
                Assert.Equal(board.Vias[i].Net, nets[int.Parse(vias[i].Req("net").A(1), CultureInfo.InvariantCulture)], ignoreCase: true);
            }

            var zone = Assert.Single(root.All("zone"));
            Assert.Equal("GND", zone.Req("net_name").A(1));
            Assert.Equal("GND", nets[int.Parse(zone.Req("net").A(1), CultureInfo.InvariantCulture)]);
            Assert.Equal("B.Cu", zone.Req("layer").A(1));
            Assert.Equal(board.Rules.Clearance, zone.Req("connect_pads").Req("clearance").D(1));
            Assert.Equal("yes", zone.Req("fill").A(1));
            Assert.NotNull(zone.Req("fill").One("thermal_gap"));
            Assert.NotNull(zone.Req("fill").One("thermal_bridge_width"));
            var pts = zone.Req("polygon").Req("pts").All("xy").ToList();
            Assert.True(pts.Count >= 4);
            double e = board.Rules.EdgeClearance;
            Assert.All(pts, p => Assert.InRange(p.D(1), e - 1e-6, board.Width - e + 1e-6));
            Assert.All(pts, p => Assert.InRange(p.D(2), e - 1e-6, board.Height - e + 1e-6));

            Assert.Equal(4, root.All("gr_line").Count(l => l.Req("layer").A(1) == "Edge.Cuts"));
            Assert.Equal(4, root.All("gr_arc").Count(l => l.Req("layer").A(1) == "Edge.Cuts"));

            // All UUIDs are well formed and unique.
            var uuids = root.Descendants("uuid").Select(u => u.A(1)).ToList();
            Assert.All(uuids, u => Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", u));
            Assert.Equal(uuids.Count, uuids.Distinct().Count());

            // Deterministic.
            Assert.Equal(text, KiCadExporter.ExportPcb(board, "test"));
        }

        [Fact]
        public void PcbRuleEmbeddingIsOptionalAndSharpCornersUseARect()
        {
            var board = new BoardDocument { Width = 30, Height = 20, CornerRadius = 0 };
            var root = Parse(KiCadExporter.ExportPcb(board, "empty", embedDesignRules: false));
            var setup = root.Req("setup");
            Assert.Null(setup.One("trace_clearance"));
            Assert.NotNull(setup.One("pad_to_mask_clearance"));
            var rect = Assert.Single(root.All("gr_rect"));
            Assert.Equal("Edge.Cuts", rect.Req("layer").A(1));
            Assert.Equal(30, rect.Req("end").D(1));
            Assert.Equal(20, rect.Req("end").D(2));
            Assert.Empty(root.All("gr_arc"));
            Assert.Single(root.All("net"));   // only net 0
        }

        // ================================================================== tests: netlist

        [Theory]
        [MemberData(nameof(Fixtures))]
        public void NetlistMatchesSchematicAndBoard(string fixture)
        {
            var (sch, board) = Fixture(fixture);
            var root = Parse(KiCadExporter.ExportNetlist(sch, Lib, fixture));
            Assert.Equal("export", root.Head);
            Assert.Equal("E", root.Req("version").A(1));

            var comps = root.Req("components").All("comp").ToList();
            Assert.Equal(sch.Parts.Count(Exported), comps.Count);
            foreach (var comp in comps)
            {
                string refDes = comp.Req("ref").A(1);
                var onBoard = board.Find(refDes);
                var fpNode = comp.One("footprint");
                if (onBoard != null)
                {
                    Assert.Equal("RobotWin:" + FootprintLibrary.Get(onBoard.FootprintId)!.Id, fpNode!.A(1));
                    Assert.Equal(onBoard.Value, comp.Req("value").A(1));
                }
                else
                {
                    Assert.Null(fpNode);
                    Assert.Contains(comp.All("property"), p => p.Req("name").A(1) == "exclude_from_board");
                }

                Assert.Equal("RobotWin", comp.Req("libsource").Req("lib").A(1));
                Assert.Equal("/", comp.Req("sheetpath").Req("tstamps").A(1));
            }

            // Component tstamps are the footprint paths of the board.
            var pcb = Parse(KiCadExporter.ExportPcb(board, "x"));
            var paths = pcb.All("footprint").ToDictionary(f => f.All("property").Single(p => p.A(1) == "Reference").A(2), f => f.Req("path").A(1));
            foreach (var comp in comps)
            {
                if (paths.TryGetValue(comp.Req("ref").A(1), out var path)) Assert.Equal(path, "/" + comp.Req("tstamps").A(1));
            }

            var netNodes = root.Req("nets").All("net").ToList();
            Assert.Equal(Enumerable.Range(1, netNodes.Count).Select(i => i.ToString(CultureInfo.InvariantCulture)), netNodes.Select(n => n.Req("code").A(1)));
            var actual = netNodes.ToDictionary(n => n.Req("name").A(1),
                n => new HashSet<string>(n.All("node").Select(x => x.Req("ref").A(1) + "/" + x.Req("pin").A(1)), StringComparer.Ordinal));
            AssertSameNets(ExpectedNets(sch), actual);

            // Every netlist node that lands on a board pad agrees with the board's pad net.
            foreach (var kv in actual)
            {
                foreach (string node in kv.Value)
                {
                    var parts = node.Split('/');
                    var comp = board.Find(parts[0]);
                    if (comp == null) continue;
                    Assert.Equal(kv.Key, comp.PadNets[parts[1]]);
                }
            }

            // Pin-mapped presets really use pad numbers, not pin names.
            if (fixture == "sensor")
            {
                Assert.Contains("Q1/1", actual["BUZ"]);    // AO3400 pinout GSD: G = pad 1
                Assert.Contains("Q1/3", actual["BZN"]);    // D = pad 3
                Assert.Contains("Q2/1", actual["OUT2"]);   // BC547B pinout CBE: C = pad 1
                Assert.Contains("U4/3", actual["OUT2"]);   // LM358 IN+ = pad 3
                Assert.Contains("AM1/1", actual["VRAW"]);  // footprint-less part still in the netlist
                Assert.Contains("U1/GND2", actual["GND"]); // Arduino internal GND connection
            }
        }

        // ================================================================== tests: schematic

        [Theory]
        [MemberData(nameof(Fixtures))]
        public void SchematicHasEverySymbolAndPinAndTheSameConnectivity(string fixture)
        {
            var (sch, _) = Fixture(fixture);
            string text = KiCadExporter.ExportSchematic(sch, Lib, fixture);
            var root = Parse(text);
            Assert.Equal("kicad_sch", root.Head);
            Assert.Equal("20231120", root.Req("version").A(1));
            Assert.Equal("8.0", root.Req("generator_version").A(1));
            string rootUuid = root.Req("uuid").A(1);
            Assert.Equal("/", root.Req("sheet_instances").Req("path").A(1));
            Assert.Equal("1", root.Req("sheet_instances").Req("path").Req("page").A(1));

            var libSymbols = root.Req("lib_symbols").All("symbol").ToDictionary(s => s.A(1));
            var instances = root.All("symbol").ToList();
            var exported = sch.Parts.Where(Exported).ToList();
            Assert.Equal(exported.Count, instances.Count);
            Assert.Equal(exported.Select(p => p.RefDes).OrderBy(r => r),
                instances.Select(i => i.All("property").Single(p => p.A(1) == "Reference").A(2)).OrderBy(r => r));

            // Union-find over KiCad connection points.
            var parent = new Dictionary<string, string>();
            string Find(string k)
            {
                if (!parent.TryGetValue(k, out var p)) parent[k] = p = k;
                if (p == k) return k;
                string r = Find(p);
                parent[k] = r;
                return r;
            }

            void Union(string a, string b) => parent[Find(a)] = Find(b);
            string Key(double x, double y) => Math.Round(x, 4).ToString("0.0000", CultureInfo.InvariantCulture) + "," + Math.Round(y, 4).ToString("0.0000", CultureInfo.InvariantCulture);

            var pinPoints = new Dictionary<string, (double X, double Y)>();
            foreach (var inst in instances)
            {
                string refDes = inst.All("property").Single(p => p.A(1) == "Reference").A(2);
                var part = sch.FindPart(refDes)!;
                var def = Lib.Get(part.PartId);
                var parameters = sch.ResolveParameters(Lib, part);

                var path = inst.Req("instances").Req("project").Req("path");
                Assert.Equal(fixture, inst.Req("instances").Req("project").A(1));
                Assert.Equal("/" + rootUuid, path.A(1));
                Assert.Equal(refDes, path.Req("reference").A(1));
                Assert.Equal("1", inst.Req("unit").A(1));

                var lib = libSymbols[inst.Req("lib_id").A(1)];
                Assert.StartsWith("RobotWin:", lib.A(1));
                string bare = lib.A(1).Substring("RobotWin:".Length);
                Assert.All(lib.All("symbol"), u => Assert.StartsWith(bare + "_", u.A(1)));
                var libPins = lib.All("symbol").SelectMany(u => u.All("pin")).ToList();
                Assert.Equal(def.Pins.Count, libPins.Count);

                var at = inst.Req("at");
                double sx = at.D(1), sy = at.D(2);
                int angle = int.Parse(at.A(3), CultureInfo.InvariantCulture);
                var mirror = inst.One("mirror");
                Assert.Equal(part.Mirror, mirror != null);
                Assert.Equal(Norm(part.Rotation), angle);

                foreach (var pin in def.Pins)
                {
                    var libPin = libPins.Single(p => p.Req("name").A(1) == pin.Name);
                    Assert.Equal(def.ResolvePad(pin, parameters), libPin.Req("number").A(1));
                    var lat = libPin.Req("at");
                    var (px, py) = SymbolToSheet(lat.D(1), lat.D(2), sx, sy, angle, mirror?.A(1));

                    // KiCad's pin tip is our pin tip, scaled and shifted by whole grid units.
                    var ours = part.Transform(pin.X, pin.Y);
                    double ox = px / KiCadExporter.SchematicGridMm - ours.X, oy = py / KiCadExporter.SchematicGridMm - ours.Y;
                    Assert.True(Math.Abs(ox - Math.Round(ox)) < 1e-6 && Math.Abs(oy - Math.Round(oy)) < 1e-6, $"{refDes}.{pin.Name} off grid");

                    string id = refDes + "/" + libPin.Req("number").A(1);
                    pinPoints[id] = (px, py);
                    Find(Key(px, py));
                }

                Assert.Equal(def.Pins.Select(p => def.ResolvePad(p, parameters)).Distinct().OrderBy(x => x),
                    inst.All("pin").Select(p => p.A(1)).OrderBy(x => x));
            }

            // Shifts are the same for every part (positions kept relative to each other).
            var wires = root.All("wire").Select(w => w.Req("pts").All("xy").Select(p => (X: p.D(1), Y: p.D(2))).ToList()).ToList();
            Assert.All(wires, w => Assert.Equal(2, w.Count));
            foreach (var w in wires) Union(Key(w[0].X, w[0].Y), Key(w[1].X, w[1].Y));

            Assert.Empty(root.All("label"));
            var labels = root.All("global_label").Select(l => (Name: l.A(1), X: l.Req("at").D(1), Y: l.Req("at").D(2))).ToList();
            Assert.Equal(pinPoints.Count, labels.Count);   // one label per pin
            var byName = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var l in labels)
            {
                string k = Key(l.X, l.Y);
                if (byName.TryGetValue(l.Name, out var other)) Union(k, other);
                else byName[l.Name] = k;
            }

            // Conservative KiCad rule: anything sitting on a wire's interior joins it.
            var points = pinPoints.Values.Concat(labels.Select(l => (l.X, l.Y))).Concat(wires.SelectMany(w => w)).ToList();
            foreach (var p in points)
            {
                foreach (var w in wires)
                {
                    double vx = w[1].X - w[0].X, vy = w[1].Y - w[0].Y;
                    double len2 = vx * vx + vy * vy;
                    if (len2 < 1e-12) continue;
                    double cross = vx * (p.Y - w[0].Y) - vy * (p.X - w[0].X);
                    double dot = (p.X - w[0].X) * vx + (p.Y - w[0].Y) * vy;
                    if (Math.Abs(cross) < 1e-6 && dot > 1e-9 && dot < len2 - 1e-9) Union(Key(p.X, p.Y), Key(w[0].X, w[0].Y));
                }
            }

            // Each connected group holds exactly one label name; group by it and compare.
            var namesByRoot = labels.GroupBy(l => Find(Key(l.X, l.Y))).ToDictionary(g => g.Key, g => g.Select(l => l.Name).Distinct().ToList());
            var actual = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var kv in pinPoints)
            {
                string r = Find(Key(kv.Value.X, kv.Value.Y));
                Assert.True(namesByRoot.TryGetValue(r, out var names), kv.Key + " has no label");
                Assert.True(names!.Count == 1, $"{kv.Key} shorts nets {string.Join(", ", names)}");
                if (!actual.TryGetValue(names[0], out var set)) actual[names[0]] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(kv.Key);
            }

            AssertSameNets(ExpectedNets(sch), actual);
            Assert.Equal(text, KiCadExporter.ExportSchematic(sch, Lib, fixture));
        }

        // ================================================================== tests: SPICE

        [Fact]
        public void SpiceNetlistHasALinePerSupportedPartAndGroundIsNodeZero()
        {
            var (sch, _) = Fixture("motor");
            string cir = SpiceExporter.ExportSpice(sch, Lib);
            var lines = cir.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
            string Line(string element) => lines.Single(l => l.StartsWith(element + " ", StringComparison.Ordinal));

            Assert.StartsWith("* Motor sürücü", lines[0]);
            Assert.Equal("R1 PWM GATE 100", Line("R1"));
            Assert.Equal("R2 5V LEDA 1000", Line("R2"));
            Assert.Equal("R3 GATE 0 10000", Line("R3"));
            Assert.StartsWith("VBT1 N_BT1_rs 0 DC 12.", Line("VBT1"));          // 3S LiPo, full: 3 x 4.2 V
            Assert.Equal("RBT1_RS VBAT N_BT1_rs 0.06", Line("RBT1_RS"));
            Assert.Equal("RC1_ESR 5V N_C1_esr 0.3", Line("RC1_ESR"));
            Assert.Equal("C1 N_C1_esr 0 0.0001", Line("C1"));
            Assert.Equal("RC1_LEAK N_C1_esr 0 10000000", Line("RC1_LEAK"));
            Assert.Equal("D1 DRAIN VBAT D_1N5819", Line("D1"));
            Assert.StartsWith("D2 LEDA 0 LED_green", Line("D2"));
            Assert.Equal("MQ1 DRAIN GATE 0 0 M_IRLZ44N W=1 L=1", Line("MQ1"));
            Assert.Equal("DQ1_BODY 0 DRAIN D_BODY", Line("DQ1_BODY"));
            Assert.Contains("* U1 regulator not exported", lines);
            Assert.Contains("* M1 dc-motor not exported", lines);
            Assert.Contains("* J1 header-3 not exported", lines);

            Assert.Contains(lines, l => l.StartsWith(".model D_1N5819 D(IS=3.17E-05 N=1.373 RS=0.051 BV=40 IBV=0.001 CJO=1E-10 EG=0.69)", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.StartsWith(".model M_IRLZ44N NMOS(LEVEL=1 VTO=1.5 KP=", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.StartsWith(".model LED_green D(IS=", StringComparison.Ordinal));
            Assert.Contains(".op", lines);
            Assert.Contains(lines, l => l.StartsWith(".tran ", StringComparison.Ordinal));
            Assert.Equal(".end", lines.Last(l => l.Length > 0));

            // Ground is node 0 everywhere: no element references a node called GND.
            var elements = lines.Where(l => l.Length > 0 && !l.StartsWith("*", StringComparison.Ordinal) && !l.StartsWith(".", StringComparison.Ordinal)).ToList();
            Assert.DoesNotContain(elements, l => l.Split(' ').Contains("GND"));
            Assert.Contains(elements, l => l.Split(' ').Contains("0"));
        }

        [Fact]
        public void SpiceCoversSourcesSwitchesTransistorsAndSkipsUnsupportedParts()
        {
            var (sch, _) = Fixture("sensor");
            var copy = Schematic.FromJson(sch.ToJson());
            var gen = copy.AddPart(Lib, "siggen-sine", new GridPoint(80, 0));     // V1
            Label(copy, gen, "+", "SERVO");
            Label(copy, gen, "-", "GND");
            var isrc = copy.AddPart(Lib, "isrc-10m", new GridPoint(90, 0));       // I1
            Label(copy, isrc, "+", "OPFB");
            Label(copy, isrc, "-", "GND");

            string cir = SpiceExporter.ExportSpice(copy, Lib);
            var lines = cir.Split('\n').ToList();
            string Line(string element) => lines.Single(l => l.StartsWith(element + " ", StringComparison.Ordinal));

            Assert.Equal("VAM1 VRAW VIN DC 0", Line("VAM1"));                     // ammeter = 0 V source
            Assert.Equal("RSW1 BTN 0 1E+10", Line("RSW1"));                       // open tactile switch
            Assert.Equal("MQ1 BZN BUZ 0 0 M_AO3400 W=1 L=1", Line("MQ1"));        // pinout only affects pads
            Assert.Equal("Q2 OUT2 QB 0 Q_BC547B", Line("Q2"));
            Assert.Equal("RBZ1 5V BZN 166.6666667", Line("RBZ1"));                // 5 V / 30 mA
            Assert.Equal("V1 N_V1_rs 0 SIN(0 1 1000)", Line("V1"));
            Assert.Equal("RV1_RS SERVO N_V1_rs 50", Line("RV1_RS"));
            Assert.Equal("I1 0 OPFB DC 0.01", Line("I1"));                         // pushes current out of "+"
            Assert.StartsWith("C2 5V 0 1E-07", Line("C2"));
            Assert.StartsWith("D1 LEDA 0 LED_red", Line("D1"));
            foreach (string unsupported in new[] { "* U1 arduino-nano", "* U2 regulator", "* U3 hc-sr04", "* M1 servo", "* U4 opamp", "* J1 terminal-2" })
            {
                Assert.Contains(lines, l => l == unsupported + " not exported");
            }

            Assert.DoesNotContain(lines, l => l.Contains("#GND") || l.Contains("#PWR"));
            Assert.Contains(lines, l => l.StartsWith(".tran 1E-05 0.005", StringComparison.Ordinal));   // 100 points per period, 5 periods
        }

        // ================================================================== tests: library files

        [Fact]
        public void FootprintLibraryTablesAndProjectFileAreValid()
        {
            foreach (string id in FootprintLibrary.KnownIds)
            {
                var fp = FootprintLibrary.Get(id)!;
                var root = Parse(KiCadExporter.ExportFootprint(fp));
                Assert.Equal("footprint", root.Head);
                Assert.Equal(fp.Id, root.A(1));
                Assert.Equal("20240108", root.Req("version").A(1));
                Assert.Equal("F.Cu", root.Req("layer").A(1));
                Assert.Null(root.One("at"));
                Assert.Equal("REF**", root.All("property").Single(p => p.A(1) == "Reference").A(2));
                Assert.Single(root.All("fp_rect"), r => r.Req("layer").A(1) == "F.CrtYd");
                Assert.Contains(root.Items, i => i.IsList && i.Head.StartsWith("fp_", StringComparison.Ordinal) && i.Req("layer").A(1) == "F.SilkS");
                var pads = root.All("pad").ToList();
                Assert.Equal(fp.Pads.Count, pads.Count);
                for (int i = 0; i < pads.Count; i++)
                {
                    Assert.Equal(fp.Pads[i].Name, pads[i].A(1));
                    Assert.Equal(fp.Pads[i].X, pads[i].Req("at").D(1), 6);
                    Assert.Equal(fp.Pads[i].Y, pads[i].Req("at").D(2), 6);
                    Assert.Equal(3, pads[i].Req("at").Count);
                    Assert.Null(pads[i].One("net"));
                }

                Assert.Equal(fp.Pads.Any(p => p.IsThroughHole) ? "through_hole" : "smd", root.Req("attr").A(1));
            }

            var table = Parse(KiCadExporter.FootprintLibTable());
            Assert.Equal("fp_lib_table", table.Head);
            var lib = table.Req("lib");
            Assert.Equal("RobotWin", lib.Req("name").A(1));
            Assert.Equal("KiCad", lib.Req("type").A(1));
            Assert.Equal("${KIPRJMOD}/RobotWin.pretty", lib.Req("uri").A(1));
            Assert.Equal("${KIPRJMOD}/Other.pretty", Parse(KiCadExporter.FootprintLibTable("Other")).Req("lib").Req("uri").A(1));
            Assert.Equal("${KIPRJMOD}/RobotWin.kicad_sym", Parse(KiCadExporter.SymbolLibTable()).Req("lib").Req("uri").A(1));

            var rules = new DesignRules { Clearance = 0.25, TrackWidth = 0.35, PowerTrackWidth = 1.0, ViaDiameter = 1.0, ViaDrill = 0.5, EdgeClearance = 0.6 };
            using var doc = JsonDocument.Parse(KiCadExporter.ExportProject("demo", rules, new[] { "VBAT", "GND" }));
            var r = doc.RootElement;
            Assert.Equal("demo.kicad_pro", r.GetProperty("meta").GetProperty("filename").GetString());
            Assert.Equal(1, r.GetProperty("meta").GetProperty("version").GetInt32());
            var ds = r.GetProperty("board").GetProperty("design_settings");
            Assert.Equal(0.25, ds.GetProperty("rules").GetProperty("min_clearance").GetDouble());
            Assert.Equal(0.6, ds.GetProperty("rules").GetProperty("min_copper_edge_clearance").GetDouble());
            Assert.Equal(new[] { 0.0, 0.35, 1.0 }, ds.GetProperty("track_widths").EnumerateArray().Select(x => x.GetDouble()));
            Assert.Equal(1.0, ds.GetProperty("via_dimensions")[1].GetProperty("diameter").GetDouble());
            var ns = r.GetProperty("net_settings");
            Assert.Equal(3, ns.GetProperty("meta").GetProperty("version").GetInt32());
            var def = ns.GetProperty("classes")[0];
            Assert.Equal("Default", def.GetProperty("name").GetString());
            Assert.Equal(0.25, def.GetProperty("clearance").GetDouble());
            Assert.Equal(0.35, def.GetProperty("track_width").GetDouble());
            Assert.Equal(0.5, def.GetProperty("via_drill").GetDouble());
            Assert.Equal(1.0, ns.GetProperty("classes")[1].GetProperty("track_width").GetDouble());
            Assert.Equal(new[] { "VBAT", "GND" }, ns.GetProperty("netclass_patterns").EnumerateArray().Select(p => p.GetProperty("pattern").GetString()));
        }

        // ================================================================== tests: zip

        [Fact]
        public void ProjectZipContainsEveryFile()
        {
            var (sch, board) = Fixture("sensor");
            string path = Path.Combine(Path.GetTempPath(), "rtw-kicad-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                KiCadExporter.WriteProjectZip(path, sch, Lib, board, "sensor");
                using var zip = ZipFile.OpenRead(path);
                var names = new HashSet<string>(zip.Entries.Select(e => e.FullName), StringComparer.Ordinal);

                var expected = new List<string>
                {
                    "sensor/sensor.kicad_pro", "sensor/sensor.kicad_sch", "sensor/sensor.kicad_pcb", "sensor/sensor.net",
                    "sensor/sensor.cir", "sensor/fp-lib-table", "sensor/sym-lib-table", "sensor/RobotWin.kicad_sym"
                };
                var footprints = board.Components.Select(c => FootprintLibrary.Get(c.FootprintId)!.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                Assert.Contains("ARDUINO-NANO", footprints, StringComparer.OrdinalIgnoreCase);
                expected.AddRange(footprints.Select(id => "sensor/RobotWin.pretty/" + id + ".kicad_mod"));
                expected.AddRange(GerberExporter.Generate(board, "sensor").Keys.Select(k => "sensor/gerber/" + k));
                Assert.Equal(expected.OrderBy(x => x, StringComparer.Ordinal), names.OrderBy(x => x, StringComparer.Ordinal));

                string Read(string name)
                {
                    using var reader = new StreamReader(zip.GetEntry(name)!.Open(), Encoding.UTF8);
                    return reader.ReadToEnd();
                }

                foreach (string name in names.Where(n => !n.Contains("/gerber/") && !n.EndsWith(".kicad_pro", StringComparison.Ordinal) && !n.EndsWith(".cir", StringComparison.Ordinal)))
                {
                    var exception = Record.Exception(() => Parse(Read(name)));
                    Assert.True(exception == null, name + ": " + exception?.Message);
                }

                using (JsonDocument.Parse(Read("sensor/sensor.kicad_pro")))
                {
                }

                // Inside the project the rules live in the .kicad_pro, not in the board's setup.
                var pcb = Parse(Read("sensor/sensor.kicad_pcb"));
                Assert.Null(pcb.Req("setup").One("trace_clearance"));
                Assert.Equal("Nano sensör kartı", pcb.Req("title_block").Req("title").A(1));
                Assert.Contains("* Nano sensör kartı", Read("sensor/sensor.cir"));
                Assert.Equal(KiCadExporter.ExportSchematic(sch, Lib, "sensor"), Read("sensor/sensor.kicad_sch"));
                Assert.Contains("%FSLAX46Y46*%", Read("sensor/gerber/sensor-F_Cu.gtl"));

                // The symbol library holds exactly the symbols the schematic embeds.
                var symLib = Parse(Read("sensor/RobotWin.kicad_sym"));
                var embedded = Parse(Read("sensor/sensor.kicad_sch")).Req("lib_symbols").All("symbol").Select(s => s.A(1)).ToList();
                Assert.Equal(embedded.Select(n => n.Substring("RobotWin:".Length)), symLib.All("symbol").Select(s => s.A(1)));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
