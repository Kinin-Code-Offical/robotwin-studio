using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;

namespace RobotWin.Studio.Models3D
{
    /// <summary>
    /// Renders every component model into PNG contact sheets (used by
    /// <c>--gallery &lt;folder&gt;</c> for visual checks of the model library).
    /// </summary>
    public static partial class ModelGallery   // [objs] ready parts and lights in Models3D/ModelGallery.Ready.cs
    {
        private sealed class Item
        {
            public Item(string page, string title, Func<ComponentInstance> make, bool board = true)
            {
                Page = page;
                Title = title;
                Make = make;
                Board = board;
            }

            public string Page { get; }
            public string Title { get; }
            public Func<ComponentInstance> Make { get; }
            public bool Board { get; }
        }

        private static ComponentInstance Part(string partId, string? presetId = null, string overrides = "", bool standalone = true)
        {
            var lib = PartLibrary.Default;
            var def = lib.Get(partId);
            var preset = presetId != null ? lib.FindPreset(presetId) : null;
            var o = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in overrides.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = kv.IndexOf('=');
                if (eq > 0) o[kv.Substring(0, eq)] = kv.Substring(eq + 1);
            }

            var p = lib.ResolveParameters(def, preset, o);
            if (standalone) return ComponentModels.Create(def, p);
            var fp = FootprintLibrary.Get(def.ResolveFootprint(p))!;
            return ComponentModels.CreateOnBoard(fp, def, p, ComponentModels.ValueOf(def, p), def.RefPrefix + "1");
        }

        private static IEnumerable<Item> Items()
        {
            const string A = "1-passives", B = "2-semis", C = "3-electromech", D = "4-boards", E = "5-modules", F = "6-motion", G = "7-power";
            yield return new Item(A, "220 Ω 5 %", () => Part("resistor", null, "resistance=220"));
            yield return new Item(A, "4.7 kΩ 1 % metal film", () => Part("resistor", null, "resistance=4.7k;tolerance=1"));
            yield return new Item(A, "10 Ω 2 W", () => Part("resistor", "res-10R-2W"));
            yield return new Item(A, "1 MΩ (AXIAL-300)", () => Part("resistor", null, "resistance=1M;footprint=AXIAL-300"));
            yield return new Item(A, "220 Ω 0805", () => Part("resistor", "res-220-0805"));
            yield return new Item(A, "100 nF ceramic", () => Part("capacitor", null, "capacitance=100n"));
            yield return new Item(A, "22 pF disc", () => Part("capacitor", null, "capacitance=22p"));
            yield return new Item(A, "100 µF 16 V", () => Part("electrolytic", "ecap-100u"));
            yield return new Item(A, "1000 µF 16 V", () => Part("electrolytic", "ecap-1000u"));
            yield return new Item(A, "10 µF (Ø5)", () => Part("electrolytic", "ecap-10u"));
            yield return new Item(A, "Inductor 100 µH", () => Part("inductor", null, "inductance=100u"));
            yield return new Item(A, "Fuse 500 mA", () => Part("fuse", "fuse-500mA"));
            yield return new Item(A, "Fuse slow 2 A", () => Part("fuse", "fuse-2A-slow"));
            yield return new Item(A, "Polyfuse", () => Part("fuse", "polyfuse-500mA"));
            yield return new Item(A, "NTC 10k", () => Part("ntc", "ntc-10k"));
            yield return new Item(A, "LDR GL5528", () => Part("ldr", "ldr-gl5528"));

            yield return new Item(B, "1N4148 (DO-35)", () => Part("diode", "1n4148"));
            yield return new Item(B, "1N4007 (DO-41)", () => Part("diode", "1n4007"));
            yield return new Item(B, "1N5408 (DO-201)", () => Part("diode", "1n5408"));
            yield return new Item(B, "LED 5 mm red", () => Part("led", null, "color=red"));
            yield return new Item(B, "LED 5 mm green, lit", () =>
            {
                var i = Part("led", null, "color=green");
                i.SetBrightness(1);
                return i;
            });
            yield return new Item(B, "LED 3 mm blue / white", () => Part("led", null, "color=blue;footprint=LED-3MM"));
            yield return new Item(B, "LED 5 mm white", () => Part("led", null, "color=white"));
            yield return new Item(B, "LED 0805", () => Part("led", "led-0805-red"));
            yield return new Item(B, "2N2222A (TO-92)", () => Part("npn", "2n2222a"));
            yield return new Item(B, "BD139 (TO-126)", () => Part("npn", "bd139"));
            yield return new Item(B, "IRLZ44N (TO-220)", () => Part("nmos", "irlz44n"));
            yield return new Item(B, "LM7805 (TO-220)", () => Part("regulator", "lm7805"));
            yield return new Item(B, "AO3400 (SOT-23)", () => Part("nmos", "ao3400"));
            yield return new Item(B, "AMS1117 (SOT-223)", () => Part("regulator", "ams1117-3v3"));
            yield return new Item(B, "LM358 (DIP-8)", () => Part("opamp", "lm358"));
            yield return new Item(B, "74HC00 (DIP-14)", () => Part("gate2", "74hc00"));

            yield return new Item(C, "Pin header 1×4", () => Part("header-4"));
            yield return new Item(C, "Screw terminal 3", () => Part("terminal-3"));
            yield return new Item(C, "Tact switch 6×6", () => Part("button"));
            yield return new Item(C, "Toggle switch", () => Part("switch"));
            yield return new Item(C, "Reed switch", () => Part("switch", "reed-switch"));
            yield return new Item(C, "Potentiometer RV09", () => Part("potentiometer", "pot-10k"));
            yield return new Item(C, "Trimpot 3296W", () => Part("potentiometer", "trimpot-10k"));
            yield return new Item(C, "Relay SRD-05VDC", () => Part("relay", "relay-srd-05"));
            yield return new Item(C, "Buzzer 12 mm", () => Part("buzzer"));
            yield return new Item(C, "Servo plug (on board)", () => Part("servo", "servo-sg90", "", false));
            yield return new Item(C, "Motor leads (on board)", () => Part("dc-motor", "motor-tt", "", false));
            yield return new Item(C, "LM35 (TO-92)", () => Part("sensor-module", "lm35"));
            yield return new Item(C, "Unknown footprint (fallback)", () =>
            {
                var fp = new Footprint { Id = "GALLERY-UNKNOWN-QFN", Height = 1.2, CourtyardMinX = -4, CourtyardMinY = -4, CourtyardMaxX = 4, CourtyardMaxY = 4 };
                for (int i = 0; i < 8; i++)
                {
                    fp.Pads.Add(new Pad { Name = (i + 1).ToString(CultureInfo.InvariantCulture), X = -3.4, Y = -2.6 + i * 0.75, Width = 0.8, Height = 0.4, Shape = PadShape.RoundRect });
                    fp.Pads.Add(new Pad { Name = (16 - i).ToString(CultureInfo.InvariantCulture), X = 3.4, Y = -2.6 + i * 0.75, Width = 0.8, Height = 0.4, Shape = PadShape.RoundRect });
                }

                return ComponentModels.CreateOnBoard(fp, null, null, "XYZ123", "U7");
            });
            yield return new Item(C, "KiCad R_Axial_DIN0207 P10.16", () =>
            {
                var fp = new Footprint { Id = "R_Axial_DIN0207_L6.3mm_D2.5mm_P10.16mm_Horizontal", Height = 2.5, CourtyardMinX = -1, CourtyardMinY = -1.5, CourtyardMaxX = 11.2, CourtyardMaxY = 1.5 };
                fp.Pads.Add(new Pad { Name = "1", X = 0, Y = 0, Width = 1.6, Height = 1.6, Drill = 0.8, Shape = PadShape.Rect });
                fp.Pads.Add(new Pad { Name = "2", X = 10.16, Y = 0, Width = 1.6, Height = 1.6, Drill = 0.8 });
                var lib = PartLibrary.Default;
                var def = lib.Get("resistor");
                var p = lib.ResolveParameters(def, null, new Dictionary<string, string> { ["resistance"] = "47k" });
                return ComponentModels.CreateOnBoard(fp, def, p, "47k", "R9");
            });
            yield return new Item(C, "KiCad SOIC-8 (LM358)", () =>
            {
                var fp = new Footprint { Id = "SOIC-8_3.9x4.9mm_P1.27mm", Height = 1.75, CourtyardMinX = -3.7, CourtyardMinY = -2.7, CourtyardMaxX = 3.7, CourtyardMaxY = 2.7 };
                for (int i = 0; i < 4; i++)
                {
                    fp.Pads.Add(new Pad { Name = (i + 1).ToString(CultureInfo.InvariantCulture), X = -2.7, Y = -1.905 + i * 1.27, Width = 1.5, Height = 0.6, Shape = PadShape.RoundRect });
                    fp.Pads.Add(new Pad { Name = (8 - i).ToString(CultureInfo.InvariantCulture), X = 2.7, Y = -1.905 + i * 1.27, Width = 1.5, Height = 0.6, Shape = PadShape.RoundRect });
                }

                var lib = PartLibrary.Default;
                var def = lib.Get("opamp");
                var p = lib.ResolveParameters(def, lib.FindPreset("lm358"), null);
                return ComponentModels.CreateOnBoard(fp, def, p, "LM358", "U3");
            });
            yield return new Item(C, "User mesh (custom model)", () =>
            {
                // A user-assigned mesh replaces the generated model (here a 6 × 10 mm cylinder from MeshModel).
                var mesh = RobotTwin.CoreSim.Mechanics.MeshModel.CreateCylinder(0.003, 0.010, 24);
                var b = mesh.Bounds;
                mesh.Translate(new RobotTwin.CoreSim.Mechanics.Vec3(0, 0, -b.Min.Z));
                PartModels3D.Register("gallery-custom", new PartModel3D { Mesh = mesh.ToEmbedded(), Name = "cylinder.stl", Color = "#D04A2A", OffsetX = 3.81 });
                return Part("resistor", null, "model3d=gallery-custom");
            });

            yield return new Item(D, "Arduino Uno R3", () => Part("arduino-uno"));
            yield return new Item(D, "Arduino Nano", () => Part("arduino-nano"));
            yield return new Item(D, "ESP32 DevKitC V4", () => Part(Esp32Parts.DevKitCId));
            yield return new Item(D, "ESP32 DevKit V1", () => Part(Esp32Parts.DevKitV1Id));

            yield return new Item(E, "HC-SR04", () => Part("hc-sr04"));
            yield return new Item(E, "OLED 0.96\" 128×64", () => Part("oled-ssd1306", "oled-128x64"));
            yield return new Item(E, "OLED 0.91\" 128×32", () => Part("oled-ssd1306", "oled-128x32"));
            yield return new Item(E, "LCD 1602 + I²C", () => Part("lcd-i2c", "lcd1602-i2c"));
            yield return new Item(E, "LCD 2004 + I²C", () => Part("lcd-i2c", "lcd2004-i2c"));
            yield return new Item(E, "GY-521 MPU-6050", () => Part("mpu6050", "gy-521"));
            yield return new Item(E, "TB6612FNG", () => Part("motor-driver", "tb6612-ch"));
            yield return new Item(E, "L298N", () => Part("motor-driver", "l298n-ch"));
            yield return new Item(E, "Flight controller", () => Part("flight-controller", "fc-brushed"));
            yield return new Item(E, "TCRT5000 module", () => Part("sensor-module", "tcrt5000"));
            yield return new Item(E, "IR obstacle FC-51", () => Part("sensor-module", "ir-obstacle"));
            yield return new Item(E, "HC-SR501 PIR", () => Part("sensor-module", "pir-hcsr501"));
            yield return new Item(E, "Soil moisture", () => Part("sensor-module", "soil-moisture"));
            yield return new Item(E, "Joystick", () => Part("sensor-module", "joystick-axis"));
            yield return new Item(E, "Sound sensor", () => Part("sensor-module", "sound-sensor"));
            yield return new Item(E, "Generic sensor", () => Part("sensor-module"));

            yield return new Item(F, "SG90 (horn 30°)", () =>
            {
                var i = Part("servo", "servo-sg90");
                if (i.Find("horn") is { } h) h.Angle = 30;
                return i;
            }, false);
            yield return new Item(F, "MG90S", () => Part("servo", "servo-mg90s"), false);
            yield return new Item(F, "MG996R", () => Part("servo", "servo-mg996r"), false);
            yield return new Item(F, "TT gear motor", () => Part("dc-motor", "motor-tt"), false);
            yield return new Item(F, "25GA-370", () => Part("dc-motor", "motor-25ga370"), false);
            yield return new Item(F, "JGA25-370 + encoder", () => Part("dc-motor", "motor-jga25-12v"), false);
            yield return new Item(F, "N20", () => Part("dc-motor", "motor-n20"), false);
            yield return new Item(F, "8520 coreless", () => Part("dc-motor", "motor-8520"), false);
            yield return new Item(F, "130 motor", () => Part("dc-motor", "motor-130"), false);
            yield return new Item(F, "TT motor + wheel (shaft 40°)", () =>
            {
                var i = Part("dc-motor", "motor-tt");
                ComponentModels.AttachWheel(i);
                if (i.Find("shaft") is { } s) s.Angle = 40;
                return Wrap(i.Root);
            }, false);
            yield return new Item(F, "Wheel 65 mm", () => Wrap(ComponentModels.Wheel(65, 26)), false);
            yield return new Item(F, "Propeller 55 mm", () => Wrap(ComponentModels.Propeller(55, 2, 0xE5484D)), false);
            yield return new Item(F, "Propeller 4-blade", () => Wrap(ComponentModels.Propeller(65, 4, 0x2B2B30)), false);

            yield return new Item(G, "AA cell", () => Part("battery", "bat-aa"), false);
            yield return new Item(G, "4×AA holder", () => Part("battery", "bat-4aa"), false);
            yield return new Item(G, "4×AA NiMH", () => Part("battery", "bat-4nimh"), false);
            yield return new Item(G, "9 V", () => Part("battery", "bat-9v"), false);
            yield return new Item(G, "18650", () => Part("battery", "bat-18650"), false);
            yield return new Item(G, "18650 USB-C", () => Part("battery", "bat-18650-usbc"), false);   // [usbc]
            yield return new Item(G, "2S 18650 USB-C", () => Part("battery", "bat-2s-usbc"), false);
            yield return new Item(G, "AA Li-ion USB-C", () => Part("battery", "bat-aa-usbc"), false);
            yield return new Item(G, "4×AA Li-ion USB-C", () => Part("battery", "bat-4aa-usbc"), false);
            yield return new Item(G, "LiPo 2S 2200", () => Part("battery", "bat-2s-lipo"), false);
            yield return new Item(G, "LiPo 1S 300", () => Part("battery", "bat-1s-lipo-300"), false);
            yield return new Item(G, "CR2032", () => Part("battery", "bat-cr2032"), false);
            yield return new Item(G, "12 V SLA", () => Part("battery", "bat-12v-sla"), false);
            yield return new Item(G, "LiFePO4 4S", () => Part("battery", "bat-lifepo4-4s"), false);
            yield return new Item(G, "Lamp (lit)", () =>
            {
                var i = Part("lamp");
                i.SetBrightness(1);
                return i;
            }, false);
            yield return new Item(G, "Bench supply", () => Part("dc-source"), false);
            foreach (var ready in ReadyItems()) yield return ready;   // [objs]
        }

        private static ComponentInstance Wrap(Model3DGroup g) =>
            new ComponentInstance(g, null, null, null, null, Colors.Black, null, string.Empty, string.Empty, Array.Empty<MovingPart>(), g.Bounds, new Point3D(), Array.Empty<(string, Point3D, Vector3D)>());

        /// <summary>Writes gallery-*.png sheets into a folder; returns a short text report.</summary>
        public static string Export(string folder, string? filter = null, int columns = 4, int tileW = 460, int tileH = 340)
        {
            Directory.CreateDirectory(folder);
            var report = new StringBuilder();
            foreach (var page in Items().Where(i => filter == null || i.Page.Contains(filter, StringComparison.OrdinalIgnoreCase) || i.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)).GroupBy(i => i.Page))
            {
                var items = page.ToList();
                int rows = (items.Count + columns - 1) / columns;
                var grid = new Grid { Width = columns * tileW, Height = rows * tileH, Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x17)) };
                for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
                for (int rr = 0; rr < rows; rr++) grid.RowDefinitions.Add(new RowDefinition());
                for (int k = 0; k < items.Count; k++)
                {
                    var it = items[k];
                    FrameworkElement tile;
                    try
                    {
                        var inst = it.Make();
                        int tris = CountTriangles(inst.Root);
                        report.AppendLine($"{page.Key,-14} {it.Title,-28} {inst.Bounds.SizeX,6:0.0} x {inst.Bounds.SizeY,6:0.0} x {inst.Bounds.SizeZ,6:0.0} mm  tris={tris}  anchors={inst.PadAnchors.Count}");
                        tile = Tile(inst, it.Title, it.Board, tileW, tileH);
                    }
                    catch (Exception ex)
                    {
                        report.AppendLine($"{page.Key,-14} {it.Title,-28} ERROR {ex.GetType().Name}: {ex.Message}");
                        tile = new TextBlock { Text = it.Title + "\n" + ex.Message, Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10) };
                    }

                    Grid.SetColumn(tile, k % columns);
                    Grid.SetRow(tile, k / columns);
                    grid.Children.Add(tile);
                }

                grid.Measure(new Size(grid.Width, grid.Height));
                grid.Arrange(new Rect(0, 0, grid.Width, grid.Height));
                grid.UpdateLayout();
                var bmp = new RenderTargetBitmap((int)grid.Width, (int)grid.Height, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(grid);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var fs = File.Create(Path.Combine(folder, "gallery-" + page.Key + ".png"));
                enc.Save(fs);
            }

            if (filter == null || "anchors".Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                // Cable attachment points of a few standalone parts (model frame, mm).
                var lib = PartLibrary.Default;
                foreach (var (part, preset) in new[] { ("servo", "servo-sg90"), ("dc-motor", "motor-tt"), ("battery", "bat-9v"), ("arduino-uno", (string?)null), ("hc-sr04", "hc-sr04"), ("terminal-2", null) })
                {
                    var def = lib.Get(part);
                    var p = lib.ResolveParameters(def, preset != null ? lib.FindPreset(preset) : null, null);
                    var anchors = ComponentModels.PinAnchors(def, p);
                    report.AppendLine($"anchors        {part,-14} " + string.Join("  ", anchors.Take(5).Select(a => FormattableString.Invariant($"{a.Pin}@({a.Position.X:0.#},{a.Position.Y:0.#},{a.Position.Z:0.#})→({a.Direction.X:0.#},{a.Direction.Y:0.#},{a.Direction.Z:0.#})"))) + (anchors.Count > 5 ? $"  … {anchors.Count} pins" : string.Empty));
                }
            }

            if (filter == null || "8-vehicles".Contains(filter, StringComparison.OrdinalIgnoreCase)) ExportVehicles(folder, report);
            report.AppendLine($"templates cached: {ComponentModels.CachedModels}");
            File.WriteAllText(Path.Combine(folder, "gallery.txt"), report.ToString());
            return report.ToString();
        }

        /// <summary>Close-ups of the example vehicles (assembly scene) and a populated example board.</summary>
        private static void ExportVehicles(string folder, StringBuilder report)
        {
            var lib = PartLibrary.Default;
            var shots = new List<(string Title, Model3DGroup Root, Point3D Target, Vector3D Dir, double Dist)>();
            foreach (var (id, dir, dist) in new[] { ("line-follower", new Vector3D(0.9, 0.8, 0.75), 0.33), ("obstacle-avoider", new Vector3D(1.0, -0.7, 0.8), 0.36), ("quadcopter", new Vector3D(0.8, -0.9, 0.6), 0.2) })
            {
                var ex = Examples.ExampleProjects.All.FirstOrDefault(x => x.Id == id);
                if (ex == null) continue;
                var sch = ex.Build();
                var asm = sch.Assembly ?? RobotTwin.CoreSim.Mechanics.AssemblyDocument.AutoConfigure(sch, lib);
                if (asm.Kind == RobotTwin.CoreSim.Mechanics.VehicleKind.None) continue;
                var scene = new Assembly3D.AssemblyScene(asm, sch, lib);
                var d = dir;
                d.Normalize();
                shots.Add((id, scene.Root, scene.BodyPosition, d, dist));
                report.AppendLine($"8-vehicles     {id,-28} tris={CountTriangles(scene.Vehicle)}");
            }

            foreach (var id in new[] { "led-button", "arduino-displays" })
            {
                var ex = Examples.ExampleProjects.All.FirstOrDefault(x => x.Id == id);
                if (ex == null) continue;
                var sch = ex.Build();
                var board = BoardModels.BoardOf(sch, lib);
                var scene = BoardModels.BuildScene(board, sch, lib);
                var g = new Model3DGroup();
                g.Children.Add(new AmbientLight(Color.FromRgb(0x5C, 0x5C, 0x62)));
                g.Children.Add(new DirectionalLight(Color.FromRgb(0xE4, 0xE2, 0xDC), new Vector3D(-0.5, 0.7, -1)));
                g.Children.Add(new DirectionalLight(Color.FromRgb(0x58, 0x5C, 0x68), new Vector3D(0.6, -0.4, 0.4)));
                g.Children.Add(scene.Root);
                var d = new Vector3D(-0.5, -1.0, 1.1);
                d.Normalize();
                shots.Add((id + " (PCB)", g, new Point3D(0, 0, 4), d, Math.Max(board.Width, board.Height) * 1.5));
                report.AppendLine($"8-vehicles     {id + " (PCB)",-28} parts={scene.Parts.Count} tris={CountTriangles(scene.Root)}");
            }

            // Stress board: 100 parts of mixed kinds (cached templates; build time is reported).
            {
                var sch = new Schematic { Title = "100 parts" };
                string[] presets = { "res-220", "ecap-100u", "cap-100n", "1n4148", "2n2222a", "lm358", "tact-6mm", "hdr-1x4", "lm7805", "relay-srd-05", "trimpot-10k", "1n4007" };
                for (int i = 0; i < 100; i++)
                {
                    string id = presets[i % presets.Length];
                    if (lib.FindPreset(id) == null) id = "res-1M";
                    sch.AddPart(lib, id, new GridPoint(i % 10 * 12, i / 10 * 10));
                }

                sch.Parts.Add(new SchematicPart { RefDes = "D99", PartId = "led", Parameters = new Dictionary<string, string> { ["color"] = "green" } });
                var board = BoardDocument.Synchronize(sch, lib);
                board.Width = 160;
                board.Height = 120;
                AutoPlacer.Place(board);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var scene = BoardModels.BuildScene(board, sch, lib);
                double cold = sw.Elapsed.TotalMilliseconds;
                sw.Restart();
                var again = BoardModels.BuildScene(board, sch, lib);
                double warm = sw.Elapsed.TotalMilliseconds;
                var anchors = BoardModels.ConnectorAnchors(board, sch, lib);
                report.AppendLine($"8-vehicles     100-part board               parts={scene.Parts.Count} tris={CountTriangles(scene.Root)} build={cold:0} ms, rebuild={warm:0} ms, connector anchors={anchors.Count}");
                var g = new Model3DGroup();
                g.Children.Add(new AmbientLight(Color.FromRgb(0x5C, 0x5C, 0x62)));
                g.Children.Add(new DirectionalLight(Color.FromRgb(0xE4, 0xE2, 0xDC), new Vector3D(-0.5, 0.7, -1)));
                g.Children.Add(new DirectionalLight(Color.FromRgb(0x58, 0x5C, 0x68), new Vector3D(0.6, -0.4, 0.4)));
                g.Children.Add(again.Root);
                var d = new Vector3D(-0.4, -1.0, 1.3);
                d.Normalize();
                shots.Add(("100-part board", g, new Point3D(0, 0, 3), d, Math.Max(board.Width, board.Height) * 1.25));
            }

            // Rotation and bottom-side placement: the same parts at 0/90/180/270° on top, and mirrored underneath.
            {
                var sch = new Schematic { Title = "sides" };
                var ids = new[] { "lm358", "ecap-100u", "res-220", "led5-red", "2n2222a", "lm7805" };
                foreach (var id in ids.Where(x => lib.FindPreset(x) != null))
                {
                    for (int k = 0; k < 2; k++) sch.AddPart(lib, id, new GridPoint(0, 0));
                }

                var board = BoardDocument.Synchronize(sch, lib);
                board.Width = 90;
                board.Height = 50;
                int n = 0;
                foreach (var c in board.Components)
                {
                    int col = n / 2;
                    bool bottom = n % 2 == 1;
                    c.X = 10 + col * 14;
                    c.Y = bottom ? 34 : 12;
                    c.Rotation = (col % 4) * 90;
                    c.Side = bottom ? CopperLayer.Bottom : CopperLayer.Top;
                    n++;
                }

                var scene = BoardModels.BuildScene(board, sch, lib);
                var g = new Model3DGroup();
                g.Children.Add(new AmbientLight(Color.FromRgb(0x6C, 0x6C, 0x72)));
                g.Children.Add(new DirectionalLight(Color.FromRgb(0xE4, 0xE2, 0xDC), new Vector3D(-0.5, 0.7, -1)));
                g.Children.Add(new DirectionalLight(Color.FromRgb(0xA0, 0xA4, 0xB0), new Vector3D(0.3, -0.4, 1)));
                g.Children.Add(scene.Root);
                var d = new Vector3D(-0.3, -1.0, -0.55);
                d.Normalize();
                shots.Add(("top 0/90/180/270° (back row) · bottom side (front row), seen from below", g, new Point3D(0, 0, 0), d, 105));
            }

            int cols = 2, tw = 920, th = 640, rows = (shots.Count + cols - 1) / cols;
            var grid = new Grid { Width = cols * tw, Height = rows * th, Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x17)) };
            for (int c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
            for (int r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition());
            for (int k = 0; k < shots.Count; k++)
            {
                var (title, root, target, dir, dist) = shots[k];
                var cam = new PerspectiveCamera(target + dir * dist, -dir, new Vector3D(0, 0, 1), 40) { NearPlaneDistance = dist * 0.02, FarPlaneDistance = dist * 200 };
                var vp = new Viewport3D { Camera = cam, Width = tw, Height = th - 28 };
                vp.Children.Add(new ModelVisual3D { Content = root });
                var panel = new DockPanel { Width = tw, Height = th, Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1F)), Margin = new Thickness(1) };
                var label = new TextBlock { Text = title, Foreground = Brushes.Gainsboro, FontSize = 14, Margin = new Thickness(8, 4, 8, 4) };
                DockPanel.SetDock(label, Dock.Top);
                panel.Children.Add(label);
                panel.Children.Add(vp);
                Grid.SetColumn(panel, k % cols);
                Grid.SetRow(panel, k / cols);
                grid.Children.Add(panel);
            }

            grid.Measure(new Size(grid.Width, grid.Height));
            grid.Arrange(new Rect(0, 0, grid.Width, grid.Height));
            grid.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)grid.Width, (int)grid.Height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(grid);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(Path.Combine(folder, "gallery-8-vehicles.png"));
            enc.Save(fs);
        }

        internal static int CountTriangles(Model3D m)
        {
            if (m is GeometryModel3D g && g.Geometry is MeshGeometry3D mg) return mg.TriangleIndices.Count / 3;
            if (m is Model3DGroup grp) return grp.Children.Sum(CountTriangles);
            return 0;
        }

        private static FrameworkElement Tile(ComponentInstance inst, string title, bool board, int w, int h)
        {
            var root = new Model3DGroup();
            root.Children.Add(new AmbientLight(Color.FromRgb(0x5C, 0x5C, 0x62)));
            root.Children.Add(new DirectionalLight(Color.FromRgb(0xE4, 0xE2, 0xDC), new Vector3D(-0.5, 0.7, -1)));
            root.Children.Add(new DirectionalLight(Color.FromRgb(0x58, 0x5C, 0x68), new Vector3D(0.6, -0.4, 0.4)));
            root.Children.Add(new DirectionalLight(Color.FromRgb(0x30, 0x30, 0x36), new Vector3D(0.2, 0.3, 1)));
            var bb = inst.Bounds;
            var c = new Point3D(bb.X + bb.SizeX / 2, bb.Y + bb.SizeY / 2, bb.Z + bb.SizeZ / 2);
            double radius = Math.Max(2, new Vector3D(bb.SizeX, bb.SizeY, bb.SizeZ).Length / 2);
            if (board)
            {
                // A piece of translucent board so the leads through it stay visible.
                var plate = new MeshBuilder(new Frame());
                double px = Math.Max(bb.SizeX, 6) + 6, py = Math.Max(bb.SizeY, 6) + 6;
                plate.Box(c.X, c.Y, -0.8, px, py, 1.6);
                root.Children.Add(new GeometryModel3D(plate.ToGeometry(), Mat.Clear(Color.FromArgb(150, 0x1E, 0x56, 0x31), 0.2)));
            }

            root.Children.Insert(4, inst.Root);
            var dir = new Vector3D(-0.55, -1.0, 0.85);
            dir.Normalize();
            double fov = 34;
            double dist = radius / Math.Sin(fov * Math.PI / 360) * 1.05;
            var cam = new PerspectiveCamera(c + dir * dist, -dir, new Vector3D(0, 0, 1), fov) { NearPlaneDistance = dist * 0.05, FarPlaneDistance = dist * 4 };
            var vp = new Viewport3D { Camera = cam, Width = w, Height = h - 28 };
            vp.Children.Add(new ModelVisual3D { Content = root });
            var panel = new DockPanel { Width = w, Height = h, Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1F)), Margin = new Thickness(1) };
            var label = new TextBlock
            {
                Text = $"{title}   ·   {bb.SizeX:0.#}×{bb.SizeY:0.#}×{bb.SizeZ:0.#} mm",
                Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xDC)),
                FontSize = 14,
                Margin = new Thickness(8, 4, 8, 4)
            };
            DockPanel.SetDock(label, Dock.Top);
            panel.Children.Add(label);
            panel.Children.Add(vp);
            return panel;
        }
    }
}
