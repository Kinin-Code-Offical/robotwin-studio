using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Libraries;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    // [libs] Parsers and round trips of the external-library importers and the part creator model.
    [Trait("Category", "Libraries")]
    public class ExternalLibraryTests : IDisposable
    {
        private readonly string _tmp = Path.Combine(Path.GetTempPath(), "rw-libs-" + Guid.NewGuid().ToString("N"));

        public ExternalLibraryTests() => Directory.CreateDirectory(_tmp);

        public void Dispose()
        {
            if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true);
        }

        private static string Fixtures
        {
            get
            {
                for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
                {
                    string p = Path.Combine(d.FullName, "CoreSim", "tests", "RobotTwin.CoreSim.Tests", "Fixtures", "Libs");
                    if (Directory.Exists(p)) return p;
                }

                throw new DirectoryNotFoundException("Fixtures/Libs not found above " + AppContext.BaseDirectory);
            }
        }

        private static string Fx(string name) => Path.Combine(Fixtures, name);

        private static PartLibrary NewLibrary()
        {
            var lib = new PartLibrary();
            StandardParts.Register(lib);
            return lib;
        }

        private static double Solve(AnalogCircuit c, string node)
        {
            var sim = new AnalogSimulator(c);
            sim.Initialize();
            Assert.True(sim.SolveOperatingPoint());
            return sim.NodeVoltage(node);
        }

        // ================================================================== SPICE

        [Theory]
        [InlineData("4k7", 4700)]
        [InlineData("10MEG", 1e7)]
        [InlineData("10meg", 1e7)]
        [InlineData("1m", 1e-3)]
        [InlineData("2.2nF", 2.2e-9)]
        [InlineData("1e-3", 1e-3)]
        [InlineData(".568", 0.568)]
        [InlineData("100u", 1e-4)]
        [InlineData("1kohm", 1000)]
        [InlineData("-5V", -5)]
        public void SpiceNumbers(string text, double expected)
        {
            Assert.True(SpiceSyntax.TryParseNumber(text, out double v));
            Assert.Equal(expected, v, 9);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("")]
        [InlineData("1..5")]
        public void SpiceNumbersRejectGarbage(string text) => Assert.False(SpiceSyntax.TryParseNumber(text, out _));

        [Fact]
        public void SpiceExpressions()
        {
            Assert.True(SpiceExpression.TryEvaluate("{2*R+1k}", n => n == "R" ? 500 : (double?)null, out double v, out _));
            Assert.Equal(2000, v, 9);
            Assert.True(SpiceExpression.TryEvaluate("{max(1,3)/2}", _ => null, out v, out _));
            Assert.Equal(1.5, v, 9);
            Assert.False(SpiceExpression.TryEvaluate("{nope*2}", _ => null, out _, out string err));
            Assert.Contains("nope", err);
        }

        [Fact]
        public void SpiceLibraryParsesModelsAndSubcircuits()
        {
            var lib = SpiceParser.ParseFile(Fx("parts.lib"));
            Assert.Equal(new[] { "D1N4148", "Q2N3904", "BS170X", "IRF540X", "SW1" }, lib.Models.Keys.OrderBy(k => lib.Order.IndexOf(k)).ToArray());
            Assert.Equal(6, lib.Subcircuits.Count);
            Assert.Equal(2.52e-9, lib.Models["D1N4148"].Params["IS"], 15);
            Assert.Equal("OnSemi", lib.Models["D1N4148"].Text["MFG"]);
            Assert.Equal("NMOS", lib.Models["IRF540X"].Kind);
            Assert.Equal(new[] { "IN", "OUT", "GND" }, lib.Subcircuits["DIVIDER"].Pins);
            Assert.Equal("10k", lib.Subcircuits["DIVIDER"].Parameters["RBOT"]);
            // '+' continuation joined onto the V1 line.
            var v1 = lib.Subcircuits["CLOCK"].Elements.Single();
            Assert.Contains("1m", v1.Tokens);
            Assert.Equal("10k", lib.Params["RTOP"]);
            Assert.False(lib.Log.HasErrors);
        }

        [Fact]
        public void SpiceModelCardsMapToBuiltInParameters()
        {
            var lib = SpiceParser.ParseFile(Fx("parts.lib"));
            var log = new ImportLog();
            var d = SpiceModelMap.ToPartParameters(lib.Models["D1N4148"], out string dBase, log)!;
            Assert.Equal("diode", dBase);
            Assert.Equal(2.52e-9, SiValue.Parse(d["is"], 0), 15);
            Assert.Equal(100, SiValue.Parse(d["bv"], 0), 9);
            Assert.Contains(log.Issues, i => i.Severity == IssueSeverity.Warning && i.Message.Contains("TT"));

            var q = SpiceModelMap.ToPartParameters(lib.Models["Q2N3904"], out string qBase, log)!;
            Assert.Equal("npn", qBase);
            Assert.Equal(416.4, SiValue.Parse(q["bf"], 0), 6);
            Assert.Equal(74.03, SiValue.Parse(q["va"], 0), 6);

            var m = SpiceModelMap.ToPartParameters(lib.Models["BS170X"], out string mBase, log)!;
            Assert.Equal("nmos", mBase);
            Assert.Equal(2.1, SiValue.Parse(m["vth"], 0), 6);
            // K = KP·W/L = 0.12 (default W = L): Rds(on) at Vth+5 V = 1/(0.12·5).
            Assert.Equal(1.0 / 0.6, SiValue.Parse(m["rdson"], 0), 4);

            var vd = SpiceModelMap.ToPartParameters(lib.Models["IRF540X"], out string vdBase, log)!;
            Assert.Equal("nmos", vdBase);
            Assert.Equal(1.0 / 125 + 0.018, SiValue.Parse(vd["rdson"], 0), 6);

            var bad = new ImportLog();
            Assert.Null(SpiceModelMap.ToPartParameters(lib.Models["SW1"], out _, bad));
            Assert.True(bad.HasErrors);
        }

        [Fact]
        public void SubcircuitDividerSimulatesWithParameterOverride()
        {
            var lib = SpiceParser.ParseFile(Fx("parts.lib"));
            var c = new AnalogCircuit();
            int vin = c.Node("in"), outN = c.Node("out");
            c.Add(new VoltageSource("V1", vin, 0, new DcWaveform(10)));
            var log = new ImportLog();
            int n = SpiceCircuitBuilder.Build(lib, "DIVIDER", c, "X1", new[] { vin, outN, 0 }, new Dictionary<string, string> { ["RBOT"] = "30000" }, d => c.Add(d), log);
            Assert.Equal(2, n);
            Assert.False(log.HasErrors);
            Assert.Equal(7.5, Solve(c, "out"), 6);
        }

        [Fact]
        public void SubcircuitReportsUnsupportedElementsLineByLine()
        {
            var lib = SpiceParser.ParseFile(Fx("parts.lib"));
            var check = SpiceCircuitBuilder.Check(lib, "AMP10", out int devices, out int skipped);
            Assert.Equal(3, devices);
            Assert.Equal(2, skipped);
            var b1 = check.Issues.Single(i => i.Item == "B1");
            Assert.Equal(IssueSeverity.Error, b1.Severity);
            Assert.True(b1.Line > 0);
            Assert.Contains("davranışsal", b1.Message);
            Assert.Contains(check.Issues, i => i.Item == "K1" && i.Message.Contains("kuplaj"));

            // The supported part still works: E gain 10.
            var c = new AnalogCircuit();
            int inp = c.Node("inp"), outN = c.Node("out");
            c.Add(new VoltageSource("V1", inp, 0, new DcWaveform(0.5)));
            SpiceCircuitBuilder.Build(lib, "AMP10", c, "U1", new[] { inp, 0, outN }, null, d => c.Add(d), new ImportLog());
            c.Add(new Resistor("RL", outN, 0, 1e6));
            Assert.Equal(5.0, Solve(c, "out"), 3);
        }

        [Fact]
        public void CurrentControlledSourcesFollowSpiceSigns()
        {
            var lib = SpiceParser.ParseFile(Fx("parts.lib"));
            var c = new AnalogCircuit();
            int vin = c.Node("in"), outN = c.Node("out"), mon = c.Node("mon");
            c.Add(new VoltageSource("V1", vin, 0, new DcWaveform(1)));
            c.Add(new Resistor("RL", outN, 0, 1000));
            c.Add(new Resistor("RM", mon, 0, 1e6));
            var log = new ImportLog();
            SpiceCircuitBuilder.Build(lib, "SENSE", c, "X1", new[] { vin, outN, mon }, null, d => c.Add(d), log);
            Assert.False(log.HasErrors, log.ToString());
            // 1 mA flows from IN through VS to OUT: H gives 100 Ω × 1 mA.
            Assert.Equal(0.1, Solve(c, "mon"), 4);
        }

        [Fact]
        public void NestedSubcircuitAndPulseSource()
        {
            var lib = SpiceParser.ParseFile(Fx("parts.lib"));
            var check = SpiceCircuitBuilder.Check(lib, "MIRROR", out int devices, out int skipped);
            Assert.Equal(3, devices);
            Assert.Equal(0, skipped);
            Assert.False(check.HasErrors);

            var c = new AnalogCircuit();
            var added = new List<AnalogDevice>();
            SpiceCircuitBuilder.Build(lib, "CLOCK", c, "X1", new[] { c.Node("clk"), 0 }, null, d =>
            {
                c.Add(d);
                added.Add(d);
            }, new ImportLog());
            var v = Assert.IsType<VoltageSource>(added.Single());
            var pulse = Assert.IsType<PulseWaveform>(v.Waveform);
            Assert.Equal(1e-3, pulse.Period, 12);
            Assert.Equal(5, pulse.High, 12);
        }

        [Fact]
        public void ExtractedSubcircuitIsSelfContained()
        {
            var lib = SpiceParser.ParseFile(Fx("parts.lib"));
            string text = lib.Extract("MIRROR");
            Assert.Contains(".model Q2N3904", text);
            Assert.Contains(".model D1N4148", text);
            Assert.Contains(".subckt CLAMP", text);
            Assert.DoesNotContain("BS170X", text);
            var again = SpiceParser.Parse(text, "mirror.sub");
            SpiceCircuitBuilder.Check(again, "MIRROR", out int devices, out int skipped);
            Assert.Equal(3, devices);
            Assert.Equal(0, skipped);
        }

        [Fact]
        public void SpiceParserNeverThrowsOnBadInput()
        {
            var lib = SpiceParser.Parse(".subckt BROKEN a b\nR1 a b\nQ1 a b\n.model\n+ orphan\n.ends\n.subckt OPEN x y\nR1 x y 1k\n", "bad.lib");
            Assert.True(lib.Log.HasErrors);
            Assert.True(lib.Subcircuits.ContainsKey("OPEN"));
            var check = SpiceCircuitBuilder.Check(lib, "BROKEN", out int devices, out _);
            Assert.Equal(0, devices);
            Assert.True(check.HasErrors);
            var empty = SpiceParser.Parse("this is not spice at all \u0000\u0001", "junk.cir");
            Assert.True(empty.Log.HasErrors);
        }

        [Fact]
        public void SpiceSubcircuitBecomesCompositeUserPart()
        {
            var lib = NewLibrary();
            var session = SpiceImportSession.Open(new[] { Fx("parts.lib") });
            var item = session.Items.Single(i => i.Name == "DIVIDER");
            var log = new ImportLog();
            var part = session.BuildPart(item, lib, log)!;
            Assert.NotNull(part.Definition);
            Assert.Equal(UserModelKind.Subcircuit, part.Definition!.Model.Kind);
            Assert.Equal(3, part.Definition.Pins.Count);

            // Through .rwpart and back, then simulate the registered part.
            string path = Path.Combine(_tmp, "divider.rwpart");
            part.Save(path);
            var loaded = CustomPart.Load(path);
            Assert.True(loaded.Register(lib));
            var def = lib.Get(loaded.BasePartId);
            var c = new AnalogCircuit();
            int vin = c.Node("in"), outN = c.Node("out");
            c.Add(new VoltageSource("V1", vin, 0, new DcWaveform(5)));
            var map = new Dictionary<string, int> { ["IN"] = vin, ["OUT"] = outN, ["GND"] = 0 };
            def.Build!(new PartBuildContext("X1", c, pin => map[pin], lib.ResolveParameters(def, lib.FindPreset(loaded.Id), null), new List<AnalogDevice>()));
            Assert.Equal(2.5, Solve(c, "out"), 6);
        }

        // ================================================================== user parts

        private static UserPartSpec OpAmpSpec()
        {
            var spec = new UserPartSpec { RefPrefix = "U", Symbol = UserSymbolStyle.Box, Source = "test", BreadboardSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\"/>" };
            spec.Pins.Add(new UserPin { Name = "OUTA", Pad = "1", Type = PinElectricalType.Output, Side = PinSide.Right });
            spec.Pins.Add(new UserPin { Name = "-INA", Pad = "2", Type = PinElectricalType.Input });
            spec.Pins.Add(new UserPin { Name = "+INA", Pad = "3", Type = PinElectricalType.Input });
            spec.Pins.Add(new UserPin { Name = "GND", Pad = "4", Type = PinElectricalType.PowerIn });
            spec.Pins.Add(new UserPin { Name = "NC", Pad = "5", Type = PinElectricalType.NoConnect, Side = PinSide.Right });
            spec.Pins.Add(new UserPin { Name = "VCC", Pad = "8", Type = PinElectricalType.PowerIn });
            spec.Model = new UserModel { Kind = UserModelKind.BuiltIn, BuiltInPartId = "opamp" };
            spec.Model.PinMap["OUT"] = "OUTA";
            spec.Model.PinMap["IN-"] = "-INA";
            spec.Model.PinMap["IN+"] = "+INA";
            spec.Model.PinMap["V-"] = "GND";
            spec.Model.PinMap["V+"] = "VCC";
            spec.Parameters.Add(new UserParameter { Key = "gbw", Label = "GBW", Value = "3M", Unit = "Hz", Tolerance = "20" });
            return spec;
        }

        [Fact]
        public void UserPartRoundTripsThroughRwpart()
        {
            var lib = NewLibrary();
            var fp = PackageGenerator.Generate(new PackageSpec { Family = PackageFamily.Soic, Pins = 8, Pitch = 1.27, Span = 5.4, Height = 1.75 });
            var part = new CustomPart
            {
                Id = "user-test-opamp",
                Name = "Test op-amp",
                BasePartId = "udef-user-test-opamp",
                Category = "Kullanıcı",
                Datasheet = "https://example.invalid/ds.pdf",
                Definition = OpAmpSpec(),
                Footprint = CustomFootprint.From(fp),
                Model3D = PartModelFiles.Box(4.9, 3.9, 1.75),
                Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["footprint"] = fp.Id, ["gbw"] = "3M" }
            };
            Assert.False(part.Definition!.Validate(fp, lib).HasErrors, part.Definition.Validate(fp, lib).ToString());

            string path = Path.Combine(_tmp, "opamp.rwpart");
            part.Save(path);
            var back = CustomPart.Load(path);
            Assert.Equal(part.Datasheet, back.Datasheet);
            Assert.NotNull(back.Definition);
            var spec = back.Definition!;
            Assert.Equal(6, spec.Pins.Count);
            Assert.Equal(PinElectricalType.NoConnect, spec.Pins.Single(p => p.Name == "NC").Type);
            Assert.Equal(PinSide.Right, spec.Pins.Single(p => p.Name == "OUTA").Side);
            Assert.Equal(UserModelKind.BuiltIn, spec.Model.Kind);
            Assert.Equal("VCC", spec.Model.PinMap["V+"]);
            Assert.Equal("20", spec.Parameters.Single().Tolerance);
            Assert.NotNull(spec.BreadboardSvg);
            Assert.Equal(8, back.Footprint!.Pads.Count);
            Assert.NotNull(back.Model3D);

            Assert.True(back.Register(lib));
            var def = lib.Get("udef-user-test-opamp");
            Assert.Equal(SymbolKind.Module, def.Symbol);
            Assert.Equal(6, def.Pins.Count);
            Assert.True(def.Pins.Single(p => p.Name == "OUTA").X > 0);
            Assert.True(def.Pins.Single(p => p.Name == "VCC").X < 0);
            Assert.Equal("8", def.FindPin("VCC")!.Pad);
            Assert.Contains(def.Parameters, p => p.Key == "tol.gbw" && p.DefaultValue == "20");
            Assert.NotNull(FootprintLibrary.Get(fp.Id));

            // The built-in op-amp model drives the user pins.
            var c = new AnalogCircuit();
            var devices = new List<AnalogDevice>();
            var parameters = lib.ResolveParameters(def, lib.FindPreset(back.Id), null);
            def.Build!(new PartBuildContext("U1", c, pin => c.Node("U1." + pin), parameters, devices));
            var amp = Assert.IsType<OpAmp>(devices.Single());
            Assert.Equal(3e6, amp.GainBandwidth, 3);

            // The tolerance band is drawn for user parts too.
            var drawn = ToleranceModel.Apply(def, new Dictionary<string, string>(parameters), "U1", new ToleranceSettings { Mode = ToleranceMode.Realistic, Seed = 7 }, 0, 25);
            Assert.Contains(drawn, d => d.Key == "gbw");
        }

        [Fact]
        public void LibraryFileKeepsVersionAndUserParts()
        {
            var file = new PartLibraryFile { Name = "Benim", Version = "2.3.1" };
            file.Parts.Add(new CustomPart { Id = "a", Name = "A", BasePartId = "udef-a", Definition = OpAmpSpec() });
            string path = Path.Combine(_tmp, "benim.rwlib");
            file.Save(path);
            var back = PartLibraryFile.Load(path);
            Assert.Equal("2.3.1", back.Version);
            Assert.Equal(6, back.Parts.Single().Definition!.Pins.Count);
        }

        [Fact]
        public void ValidationFindsPadMismatchesAndDuplicates()
        {
            var lib = NewLibrary();
            var fp = PackageGenerator.Generate(new PackageSpec { Family = PackageFamily.Dip, Pins = 8, Pitch = 2.54, Span = 7.62 });
            var spec = OpAmpSpec();
            spec.Pins.Single(p => p.Name == "VCC").Pad = "9";
            spec.Pins.Add(new UserPin { Name = "GND", Pad = "6" });
            spec.Model.PinMap.Remove("V-");
            var log = spec.Validate(fp, lib, "x");
            Assert.Contains(log.Issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("pad 9"));
            Assert.Contains(log.Issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("'GND'"));
            Assert.Contains(log.Issues, i => i.Severity == IssueSeverity.Warning && i.Message.Contains("'V-'"));
            Assert.Contains(log.Issues, i => i.Severity == IssueSeverity.Warning && i.Message.Contains("pad hiçbir bacağa"));
        }

        [Fact]
        public void BehavioralRegulatorPart()
        {
            var lib = NewLibrary();
            var spec = new UserPartSpec { Model = new UserModel { Kind = UserModelKind.Behavioral, Behavior = BehavioralKind.Regulator } };
            foreach (var (n, pad) in new[] { ("VIN", "1"), ("GND", "2"), ("VOUT", "3") }) spec.Pins.Add(new UserPin { Name = n, Pad = pad });
            spec.Model.PinMap["IN"] = "VIN";
            spec.Model.PinMap["GND"] = "GND";
            spec.Model.PinMap["OUT"] = "VOUT";
            spec.Parameters.Add(new UserParameter { Key = "voltage", Label = "Çıkış", Value = "3.3", Unit = "V" });
            var def = spec.ToDefinition("udef-reg", "Reg", "Güç", "", "TO92", lib);
            var c = new AnalogCircuit();
            int vin = c.Node("vin"), vout = c.Node("vout");
            c.Add(new VoltageSource("V1", vin, 0, new DcWaveform(9)));
            c.Add(new Resistor("RL", vout, 0, 330));
            var map = new Dictionary<string, int> { ["VIN"] = vin, ["GND"] = 0, ["VOUT"] = vout };
            def.Build!(new PartBuildContext("U1", c, p => map[p], lib.ResolveParameters(def, null, null), new List<AnalogDevice>()));
            Assert.Equal(3.3, Solve(c, "vout"), 1);
        }

        // ================================================================== footprints, 3D

        [Fact]
        public void PackageGeneratorFollowsKiCadNumbering()
        {
            var soic = PackageGenerator.Generate(new PackageSpec { Family = PackageFamily.Soic, Pins = 8, Pitch = 1.27, Span = 5.4 });
            Assert.Equal(8, soic.Pads.Count);
            var p1 = soic.FindPad("1")!;
            var p8 = soic.FindPad("8")!;
            Assert.True(p1.X < 0 && p1.Y < 0);
            Assert.True(p8.X > 0 && Math.Abs(p8.Y - p1.Y) < 1e-9);
            Assert.Equal(1.27, soic.FindPad("2")!.Y - p1.Y, 9);

            var qfn = PackageGenerator.Generate(new PackageSpec { Family = PackageFamily.Qfn, Pins = 16, Pitch = 0.5, Span = 3, ExposedPad = 1.7 });
            Assert.Equal(17, qfn.Pads.Count);
            Assert.True(qfn.FindPad("5")!.Y > 0); // bottom row starts at pin 5
            var hdr = PackageGenerator.Generate(new PackageSpec { Family = PackageFamily.Header, Pins = 10, Rows = 2, Pitch = 2.54 });
            Assert.Equal(2.54, hdr.FindPad("2")!.X - hdr.FindPad("1")!.X, 9);
            Assert.NotNull(PackageGenerator.Check(new PackageSpec { Family = PackageFamily.Dip, Pins = 7 }));
            Assert.Throws<ArgumentException>(() => PackageGenerator.Generate(new PackageSpec { Family = PackageFamily.Qfn, Pins = 6 }));
        }

        [Fact]
        public void VrmlCubeLoadsInKiCadUnits()
        {
            var log = new ImportLog();
            var mesh = VrmlReader.Load(Fx("cube.wrl"), VrmlUnits.KiCad, log);
            Assert.Equal(12 + 12, mesh.TriangleCount);
            var b = mesh.Bounds;
            Assert.Equal(-1 * 0.00254, b.Min.X, 9);
            Assert.Equal(1.3 * 0.00254, b.Max.X, 9);
            Assert.Equal(0, b.Min.Z, 9);
            Assert.Equal(0.5 * 0.00254, b.Max.Z, 9);
            Assert.NotNull(mesh.TriangleColors);
            Assert.Equal(26, mesh.TriangleColors![0].R);
            Assert.Equal(204, mesh.TriangleColors[mesh.TriangleCount - 1].R);

            // Auto picks KiCad units for a model of this size.
            var auto = VrmlReader.Load(Fx("cube.wrl"));
            Assert.Equal(b.Max.X, auto.Bounds.Max.X, 9);
        }

        [Fact]
        public void VrmlRejectsUnsupportedFilesCleanly()
        {
            Assert.Throws<InvalidDataException>(() => VrmlReader.Parse("#VRML V1.0 ascii\nSeparator { }"));
            Assert.Throws<InvalidDataException>(() => VrmlReader.Parse("#VRML V2.0 utf8\nGroup { children [ Shape { geometry IndexedFaceSet { coord Coordinate { point [ 0 0 0 ] } coordIndex [ 0 7 9 -1 ] } } ] }"));
            Assert.Throws<InvalidDataException>(() => VrmlReader.Parse("#VRML V2.0 utf8\nTransform { children [ Shape {"));
            var mesh = VrmlReader.Parse("#VRML V2.0 utf8\nROUTE A.b TO C.d\nShape { geometry Box { size 1 1 1 } }", VrmlUnits.Meter);
            Assert.Equal(12, mesh.TriangleCount);
        }

        [Fact]
        public void StepFilesGiveAClearMessageAndPreferWrlSibling()
        {
            string step = Path.Combine(_tmp, "part.step");
            File.WriteAllText(step, "ISO-10303-21;");
            if (PartModelFiles.CanReadCad)
            {
                // [occt] With the Open CASCADE bridge a broken STEP file says it could not be read.
                var bad = Assert.Throws<InvalidDataException>(() => PartModelFiles.LoadMesh(step));
                Assert.Contains("STEP", bad.Message);
            }
            else
            {
                var ex = Assert.Throws<NotSupportedException>(() => PartModelFiles.LoadMesh(step));
                Assert.Contains("STEP", ex.Message);
                Assert.Contains("STL", ex.Message);
            }

            string? found = PartModelFiles.ResolveKiCadModel("${KICAD8_3DMODEL_DIR}/Test.3dshapes/SOIC-8_3.9x4.9mm_P1.27mm.step", new[] { Fixtures }, out string note);
            Assert.NotNull(found);
            Assert.EndsWith(".wrl", found);
            Assert.Contains("STEP yerine", note);
            Assert.Null(PartModelFiles.ResolveKiCadModel("${NOPE_DIR}/x.3dshapes/missing.wrl", new[] { _tmp }, out note));
            Assert.Contains("bulunamadı", note);
        }

        // ================================================================== KiCad

        [Fact]
        public void KiCadSessionBuildsOwnPinPartsWithPadMapping()
        {
            var lib = NewLibrary();
            var s = KiCadImportSession.Open(new[] { Fx("Test.kicad_sym"), Fx("Test.pretty") }, lib);
            Assert.Equal(3, s.Items.Count);
            Assert.Equal(2, s.Footprints.Count);

            var chip = s.Items.Single(i => i.Symbol.Name == "TestChip");
            Assert.Equal(KiCadPartMode.OwnPins, chip.Mode);
            Assert.Equal("TestChip_10", chip.Footprint?.Id);
            var log = new ImportLog();
            var part = s.BuildPart(chip, lib, log)!;
            Assert.False(log.HasErrors, log.ToString());
            var spec = part.Definition!;
            Assert.Equal(10, spec.Pins.Count);
            Assert.Contains(spec.Pins, p => p.Name == "RESET" && p.Pad == "5" && p.Type == PinElectricalType.Input && p.Side == PinSide.Left);
            Assert.Contains(spec.Pins, p => p.Name == "TX" && p.Type == PinElectricalType.Output && p.Side == PinSide.Right);
            Assert.Equal("https://example.invalid/testchip.pdf", part.Datasheet);
            Assert.NotNull(part.Footprint);

            // A pad override that does not exist on the footprint is an error.
            chip.PadOverrides["1"] = "99";
            var bad = new ImportLog();
            s.BuildPart(chip, lib, bad);
            Assert.True(bad.HasErrors);
            Assert.Contains("99", s.BuildPart(chip, lib, new ImportLog())!.Definition!.Pins.Select(p => p.Pad));
            chip.PadOverrides.Clear();

            // The op-amp gets the SOIC footprint and the .wrl model next to the library.
            var amp = s.Items.Single(i => i.Symbol.Name == "TestOpAmp");
            Assert.Equal("SOIC-8_3.9x4.9mm_P1.27mm", amp.Footprint?.Id);
            var ampLog = new ImportLog();
            var ampPart = s.BuildPart(amp, lib, ampLog)!;
            Assert.NotNull(ampPart);
            Assert.NotNull(ampPart.Model3D);
            Assert.True(ampPart.Model3D!.Mesh != null);

            var file = s.Commit(lib, new ImportLog());
            Assert.Equal(3, file.Parts.Count);
            Assert.Equal(file.Parts.Count, file.Parts.Select(p => p.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal("20231120", file.Version);
            foreach (var p in file.Parts) p.Register(lib);
            Assert.NotNull(lib.FindPreset(part.Id));
        }

        [Fact]
        public void KiCadSessionSurvivesBrokenFiles()
        {
            string bad = Path.Combine(_tmp, "broken.kicad_sym");
            File.WriteAllText(bad, "(kicad_symbol_lib (version 20231120) (symbol \"X\" (pin input line");
            var s = KiCadImportSession.Open(new[] { bad, Path.Combine(_tmp, "missing.kicad_mod"), Path.Combine(_tmp, "nope.kicad_sym") }, NewLibrary());
            Assert.True(s.Log.HasErrors);
            // Whatever could be salvaged is offered, never as a ready, mapped part.
            Assert.All(s.Items, i => Assert.NotEqual(KiCadImportStatus.Mapped, i.AutoStatus));
            var log = new ImportLog();
            foreach (var i in s.Items) s.BuildPart(i, NewLibrary(), log);
        }

        // ================================================================== Fritzing

        [Fact]
        public void FritzingPartReadsConnectorsImagesAndPcbPads()
        {
            var fz = FritzingImporter.Read(Fx("test_sensor.fzpz"));
            Assert.False(fz.Log.HasErrors, fz.Log.ToString());
            Assert.Equal("Test Sensör Modülü", fz.Title);
            Assert.Equal(new[] { "VCC", "OUT", "GND" }, fz.Connectors.Select(c => c.Name).ToArray());
            Assert.Contains("<svg", fz.BreadboardSvg);
            Assert.NotNull(fz.Footprint);
            var pads = fz.Footprint!.Pads.OrderBy(p => p.X).ToList();
            Assert.Equal(3, pads.Count);
            // 100 viewBox units of a 600-unit, 0.6 in wide drawing = 2.54 mm.
            Assert.Equal(2.54, pads[1].X - pads[0].X, 6);
            Assert.True(pads[0].Drill > 0);

            var part = FritzingImporter.ToCustomPart(fz, new ImportLog())!;
            Assert.Equal(3, part.Definition!.Pins.Count);
            Assert.Equal(UserModelKind.None, part.Definition.Model.Kind);
            Assert.Equal("5V", part.Definition.Parameters.Single(p => p.Key == "supply").Value);
            Assert.False(part.Definition.Validate(fz.Footprint, NewLibrary()).HasErrors);
        }

        [Fact]
        public void FritzingRejectsNonParts()
        {
            string zip = Path.Combine(_tmp, "empty.fzpz");
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create)) z.CreateEntry("readme.txt");
            Assert.True(FritzingImporter.Read(zip).Log.HasErrors);
            string xml = Path.Combine(_tmp, "bad.fzp");
            File.WriteAllText(xml, "<module><connectors></connectors");
            Assert.True(FritzingImporter.Read(xml).Log.HasErrors);
        }

        // ================================================================== Arduino

        private string MakeArduinoZip(string name, string version, bool evil = false)
        {
            string zip = Path.Combine(_tmp, name + "-" + version + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".zip");
            using var z = ZipFile.Open(zip, ZipArchiveMode.Create);
            void Text(string entry, string text)
            {
                using var w = new StreamWriter(z.CreateEntry(entry).Open(), new UTF8Encoding(false));
                w.Write(text);
            }

            Text(name + "-master/library.properties", $"name={name}\nversion={version}\nauthor=Test\nsentence=Test library\narchitectures=avr\ndepends=Wire\n");
            Text(name + "-master/src/" + name + ".h", "#pragma once\n");
            Text(name + "-master/src/" + name + ".cpp", "#include \"" + name + ".h\"\n");
            Text(name + "-master/examples/Demo/Demo.ino", "void setup(){}\nvoid loop(){}\n");
            if (evil) Text(name + "-master/../../evil.txt", "x");
            return zip;
        }

        [Fact]
        public void ArduinoLibraryInstallsUpdatesAndRefusesDuplicates()
        {
            string libs = Path.Combine(_tmp, "libraries");
            var info = ArduinoLibraries.Install(MakeArduinoZip("My Sensor", "1.0.0"), libs, overwrite: false);
            Assert.False(info.Log.HasErrors, info.Log.ToString());
            Assert.Equal("My_Sensor", Path.GetFileName(info.Folder));
            Assert.True(File.Exists(Path.Combine(info.Folder, "library.properties")));
            Assert.True(File.Exists(Path.Combine(info.Folder, "src", "My Sensor.h")));
            Assert.Contains("My Sensor.h", info.Headers);
            Assert.Equal(1, info.Examples);
            Assert.Contains(info.Log.Issues, i => i.Message.Contains("Wire"));

            var again = ArduinoLibraries.Install(MakeArduinoZip("My Sensor", "1.1.0"), libs, overwrite: false);
            Assert.True(again.Log.HasErrors);
            var updated = ArduinoLibraries.Install(MakeArduinoZip("My Sensor", "1.1.0"), libs, overwrite: true);
            Assert.False(updated.Log.HasErrors);
            Assert.Equal("1.1.0", ArduinoLibraries.List(libs).Single().Version);
        }

        [Fact]
        public void ArduinoZipSlipIsBlocked()
        {
            string libs = Path.Combine(_tmp, "libraries");
            var info = ArduinoLibraries.Install(MakeArduinoZip("Evil", "1.0.0", evil: true), libs, overwrite: false);
            Assert.True(info.Log.HasErrors);
            Assert.False(File.Exists(Path.Combine(_tmp, "evil.txt")));
            Assert.False(File.Exists(Path.Combine(libs, "evil.txt")));
            Assert.Empty(ArduinoLibraries.List(libs));
        }

        [Fact]
        public void ArduinoInspectRejectsNonLibraries()
        {
            string dir = Path.Combine(_tmp, "notalib");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "readme.md"), "hi");
            Assert.True(ArduinoLibraries.Inspect(dir).Log.HasErrors);
        }

        // ================================================================== catalog

        [Fact]
        public void CatalogTracksConflictsEnableStateAndRemoval()
        {
            string folder = Path.Combine(_tmp, "Library");
            var cat = LibraryCatalog.Load(folder);
            var a = new PartLibraryFile { Name = "A", Version = "1" };
            a.Parts.Add(new CustomPart { Id = "same", Name = "Same (A)", BasePartId = "resistor" });
            var b = new PartLibraryFile { Name = "B", Version = "2" };
            b.Parts.Add(new CustomPart { Id = "same", Name = "Same (B)", BasePartId = "resistor" });
            b.Parts.Add(new CustomPart { Id = "only-b", Name = "Only B", BasePartId = "resistor" });
            var ea = cat.Install(a, LibrarySourceKind.Spice, new[] { Fx("parts.lib") });
            var eb = cat.Install(b, LibrarySourceKind.KiCad, new[] { Fx("Test.kicad_sym") });

            var conflict = cat.Conflicts().Single();
            Assert.Equal("same", conflict.PartId);
            Assert.Equal(ea.Id, conflict.Winner);
            string fileB = Path.Combine(folder, eb.File);
            Assert.True(cat.Accepts(fileB, "same"));
            cat.Resolve("same", eb.Id);
            Assert.False(cat.Accepts(Path.Combine(folder, ea.File), "same"));
            Assert.True(cat.Accepts(fileB, "same"));

            cat.SetEnabled(ea.Id, false);
            Assert.DoesNotContain(cat.LoadOrder(), f => f.EndsWith(ea.File, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(cat.Search("only"), r => r.PartId == "only-b");

            // Reload keeps the state; a corrupt list is rebuilt from the files.
            var again = LibraryCatalog.Load(folder);
            Assert.False(again.Find(ea.Id)!.Enabled);
            Assert.Equal("2", again.Find(eb.Id)!.Version);
            File.WriteAllText(Path.Combine(folder, LibraryCatalog.FileName), "{ not json");
            var rebuilt = LibraryCatalog.Load(folder);
            Assert.True(rebuilt.Log.HasErrors);
            Assert.Equal(2, rebuilt.Libraries.Count);
            Assert.Contains(rebuilt.Libraries, l => l.PartIds.Contains("only-b"));

            Assert.True(rebuilt.Remove(rebuilt.Libraries.First(l => l.PartIds.Contains("only-b")).Id));
            Assert.False(File.Exists(fileB));
            Assert.True(Directory.EnumerateFiles(Path.Combine(folder, "Kaldırılanlar"), "*.rwlib", SearchOption.AllDirectories).Any());
        }
    }
}
