using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Design;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    [Trait("Category", "Design")]
    public class SchematicEditorTests
    {
        private static readonly PartLibrary Lib = PartLibrary.Default;

        private static GridPoint P(double x, double y) => new GridPoint(x, y);

        private static GridPoint Pin(SchematicPart part, string pin)
        {
            var def = Lib.Get(part.PartId).FindPin(pin)!;
            return part.Transform(def.X, def.Y);
        }

        /// <summary>Battery + resistor + LED loop with wires between the pins.</summary>
        private static Schematic LedLoop()
        {
            var sch = new Schematic();
            var bat = sch.AddPart(Lib, "bat-9v", P(0, 0));
            var r = sch.AddPart(Lib, "res-220", P(10, -6));
            var led = sch.AddPart(Lib, "led5-red", P(20, 0), 270);
            sch.AddWire(Pin(bat, "+"), P(Pin(bat, "+").X, -6), Pin(r, "A"));
            sch.AddWire(Pin(r, "B"), P(Pin(led, "A").X, -6), Pin(led, "A"));
            sch.AddWire(Pin(led, "K"), P(Pin(led, "K").X, 6), P(Pin(bat, "-").X, 6), Pin(bat, "-"));
            return sch;
        }

        private static List<string> Connectivity(Schematic sch) =>
            sch.BuildNetlist(Lib).Nets.Select(n => string.Join(",", n.Connections.Select(c => c.ToString()).OrderBy(s => s))).OrderBy(s => s).ToList();

        // ------------------------------------------------------------- geometry & selection

        [Fact]
        public void GridRectContainsIntersectsAndClipsSegments()
        {
            var r = new GridRect(0, 0, 10, 5);
            Assert.True(r.Contains(P(10, 5)));
            Assert.True(r.Contains(new GridRect(1, 1, 2, 2)));
            Assert.False(r.Contains(new GridRect(9, 1, 11, 2)));
            Assert.True(r.Intersects(new GridRect(9, 1, 11, 2)));
            Assert.True(r.IntersectsSegment(P(-5, 2), P(15, 2)));
            Assert.False(r.IntersectsSegment(P(-5, 8), P(15, 8)));
            Assert.True(r.IntersectsSegment(P(-1, -1), P(11, 6)));
        }

        [Fact]
        public void WindowSelectionTakesEnclosedItemsCrossingTakesTouchedOnes()
        {
            var sch = LedLoop();
            var r = sch.Parts[1];
            var b = SchematicEditing.PartBounds(r, Lib.Get(r.PartId));
            var window = b.Inflate(0.5);
            var enclosed = SchematicEditing.ItemsInRect(sch, Lib, window, crossing: false);
            Assert.Contains(r, enclosed);
            Assert.DoesNotContain(sch.Wires[0], enclosed);

            var crossing = SchematicEditing.ItemsInRect(sch, Lib, window, crossing: true);
            Assert.Contains(r, crossing);
            Assert.Contains(sch.Wires[0], crossing);
            Assert.Contains(sch.Wires[1], crossing);
            Assert.DoesNotContain(sch.Parts[0], crossing);
        }

        // ------------------------------------------------------------- rotate / mirror

        [Fact]
        public void RotatingASelectionKeepsItsConnectivity()
        {
            var sch = LedLoop();
            var before = Connectivity(sch);
            var items = SchematicEditing.AllItems(sch).ToList();
            var pivot = SchematicEditing.PivotOf(items, Lib);
            SchematicEditing.Rotate(items, pivot);
            Assert.Equal(before, Connectivity(sch));
            Assert.Equal(90, sch.Parts[0].Rotation);
            Assert.Equal(0, sch.Parts[2].Rotation);

            // Four turns are the identity.
            var json = sch.ToJson();
            for (int i = 0; i < 4; i++) SchematicEditing.Rotate(items, pivot);
            Assert.Equal(json, sch.ToJson());
            SchematicEditing.Rotate(items, pivot, clockwise: true);
            SchematicEditing.Rotate(items, pivot);
            Assert.Equal(json, sch.ToJson());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MirroringASelectionKeepsConnectivityAndIsAnInvolution(bool vertical)
        {
            var sch = LedLoop();
            sch.Parts[1].Rotation = 90; // pins no longer on the wires; re-wire to the rotated pins
            sch.Wires.Clear();
            var bat = sch.Parts[0];
            var r = sch.Parts[1];
            var led = sch.Parts[2];
            sch.AddWire(Pin(bat, "+"), P(Pin(bat, "+").X, Pin(r, "A").Y), Pin(r, "A"));
            sch.AddWire(Pin(r, "B"), P(Pin(led, "A").X, Pin(r, "B").Y), Pin(led, "A"));
            var before = Connectivity(sch);
            var items = SchematicEditing.AllItems(sch).ToList();
            var pivot = SchematicEditing.PivotOf(items, Lib);
            var json = sch.ToJson();
            SchematicEditing.Mirror(items, pivot, vertical);
            Assert.Equal(before, Connectivity(sch));
            Assert.True(sch.Parts.All(p => p.Rotation % 90 == 0));
            SchematicEditing.Mirror(items, pivot, vertical);
            Assert.Equal(json, sch.ToJson());
        }

        [Fact]
        public void PivotOfSinglePartIsItsAnchorAndOfGroupIsSnappedCentre()
        {
            var sch = LedLoop();
            Assert.Equal(sch.Parts[1].Position, SchematicEditing.PivotOf(new object[] { sch.Parts[1] }, Lib));
            var pivot = SchematicEditing.PivotOf(SchematicEditing.AllItems(sch).ToList(), Lib);
            Assert.Equal(Math.Round(pivot.X), pivot.X);
            Assert.Equal(Math.Round(pivot.Y), pivot.Y);
        }

        // ------------------------------------------------------------- move / drag

        [Fact]
        public void DragOfAPartRubberBandsAttachedWiresAndStaysOrthogonal()
        {
            var sch = LedLoop();
            string original = sch.ToJson();
            var before = Connectivity(sch);
            var r = sch.Parts[1];
            var drag = new SchematicDrag(sch, Lib, new object[] { r }, rubberBand: true);
            drag.Apply(P(0, -3));
            Assert.Equal(P(10, -9), r.Position);
            Assert.Equal(before, Connectivity(sch));
            foreach (var w in sch.Wires)
            {
                for (int i = 0; i + 1 < w.Points.Count; i++)
                {
                    var a = w.Points[i];
                    var b = w.Points[i + 1];
                    Assert.True(Math.Abs(a.X - b.X) < 1e-9 || Math.Abs(a.Y - b.Y) < 1e-9, $"diagonal segment {a}-{b}");
                }
            }

            drag.Cancel();
            Assert.Equal(original, sch.ToJson());   // exactly as it was, part identities too
        }

        [Fact]
        public void MoveWithoutRubberBandLeavesWiresBehind()
        {
            var sch = LedLoop();
            var wiresBefore = sch.Wires.Select(w => w.Points.ToList()).ToList();
            var drag = new SchematicDrag(sch, Lib, new object[] { sch.Parts[1] }, rubberBand: false);
            drag.Apply(P(0, -3));
            for (int i = 0; i < sch.Wires.Count; i++) Assert.Equal(wiresBefore[i], sch.Wires[i].Points);
            Assert.NotEqual(Connectivity(LedLoop()), Connectivity(sch));
        }

        [Fact]
        public void DraggingAWholeSelectionMovesWiresBetweenSelectedPartsRigidly()
        {
            var sch = LedLoop();
            var before = Connectivity(sch);
            var drag = new SchematicDrag(sch, Lib, sch.Parts.Cast<object>().ToList(), rubberBand: true);
            drag.Apply(P(5, 5));
            Assert.Equal(before, Connectivity(sch));
            Assert.Equal(P(5, 5), sch.Parts[0].Position);
            Assert.Equal(LedLoop().Wires[0].Points.Count, sch.Wires[0].Points.Count);
        }

        [Fact]
        public void SegmentDragMovesPerpendicularAndKeepsPinEndsAttached()
        {
            var sch = new Schematic();
            var r1 = sch.AddPart(Lib, "res-220", P(0, 0));
            var r2 = sch.AddPart(Lib, "res-220", P(20, 0));
            var wire = sch.AddWire(Pin(r1, "B"), Pin(r2, "A"));
            var before = Connectivity(sch);
            var drag = new SegmentDrag(sch, Lib, wire, 0);
            Assert.Equal(P(0, 4), drag.Constrain(P(3, 4)));
            drag.Apply(P(3, 4));
            Assert.Equal(4, wire.Points.Count);
            Assert.Equal(Pin(r1, "B"), wire.Points[0]);
            Assert.Equal(Pin(r2, "A"), wire.Points[^1]);
            Assert.Equal(before, Connectivity(sch));
            drag.Cancel();
            Assert.Equal(2, wire.Points.Count);
        }

        // ------------------------------------------------------------- wires

        [Fact]
        public void RoutePosturesFor90And45AndFree()
        {
            Assert.Equal(new[] { P(5, 0), P(5, 3) }, SchematicEditing.Route(P(0, 0), P(5, 3), WireMode.Orthogonal, false));
            Assert.Equal(new[] { P(0, 3), P(5, 3) }, SchematicEditing.Route(P(0, 0), P(5, 3), WireMode.Orthogonal, true));
            Assert.Equal(new[] { P(3, 3), P(5, 3) }, SchematicEditing.Route(P(0, 0), P(5, 3), WireMode.Diagonal45, false));
            Assert.Equal(new[] { P(2, 0), P(5, 3) }, SchematicEditing.Route(P(0, 0), P(5, 3), WireMode.Diagonal45, true));
            Assert.Equal(new[] { P(5, 3) }, SchematicEditing.Route(P(0, 0), P(5, 3), WireMode.Free, false));
            Assert.Equal(new[] { P(4, 4) }, SchematicEditing.Route(P(0, 0), P(4, 4), WireMode.Diagonal45, false));
            Assert.Equal(new[] { P(0, 7) }, SchematicEditing.Route(P(0, 0), P(0, 7), WireMode.Orthogonal, false));
        }

        [Fact]
        public void CleanPolylineDropsDuplicateAndCollinearPointsButNotFoldBacks()
        {
            var clean = SchematicEditing.CleanPolyline(new[] { P(0, 0), P(0, 0), P(2, 0), P(5, 0), P(5, 3), P(5, 3), P(8, 6), P(9, 7) });
            Assert.Equal(new[] { P(0, 0), P(5, 0), P(5, 3), P(9, 7) }, clean);
            var fold = SchematicEditing.CleanPolyline(new[] { P(0, 0), P(5, 0), P(2, 0) });
            Assert.Equal(3, fold.Count);
        }

        [Fact]
        public void BreakWireSplitsAtAPointAndKeepsConnectivity()
        {
            var sch = LedLoop();
            var before = Connectivity(sch);
            var wire = sch.Wires[2];
            int count = sch.Wires.Count;
            var mid = P((wire.Points[1].X + wire.Points[2].X) / 2, wire.Points[1].Y);
            var tail = SchematicEditing.BreakWire(sch, wire, mid);
            Assert.NotNull(tail);
            Assert.Equal(count + 1, sch.Wires.Count);
            Assert.Equal(mid, wire.Points[^1]);
            Assert.Equal(mid, tail!.Points[0]);
            Assert.Equal(before, Connectivity(sch));
            Assert.Null(SchematicEditing.BreakWire(sch, wire, wire.Points[0]));
        }

        [Fact]
        public void DeleteSegmentLeavesTheRemainingPieces()
        {
            var sch = new Schematic();
            var w = sch.AddWire(P(0, 0), P(5, 0), P(5, 5), P(10, 5));
            Assert.True(SchematicEditing.DeleteSegment(sch, w, 1));
            Assert.Equal(2, sch.Wires.Count);
            Assert.Equal(new[] { P(0, 0), P(5, 0) }, sch.Wires[0].Points);
            Assert.Equal(new[] { P(5, 5), P(10, 5) }, sch.Wires[1].Points);
        }

        [Fact]
        public void DanglingEndsAreFoundAndTJunctionsAreNotDangling()
        {
            var sch = LedLoop();
            Assert.Empty(SchematicEditing.DanglingEnds(sch, Lib));
            var w0 = sch.Wires[0];
            var t = P(w0.Points[1].X, (w0.Points[0].Y + w0.Points[1].Y) / 2);
            sch.AddWire(t, P(t.X - 4, t.Y));
            var dangling = SchematicEditing.DanglingEnds(sch, Lib);
            Assert.Single(dangling);
            Assert.Equal(P(t.X - 4, t.Y), dangling[0].End);
        }

        // ------------------------------------------------------------- clipboard

        [Fact]
        public void FragmentRoundTripsThroughJsonAndPastesWithFreshDesignators()
        {
            var sch = LedLoop();
            sch.Labels.Add(new NetLabel { Name = "VBAT", Position = sch.Wires[0].Points[1], Global = true });
            var items = SchematicEditing.AllItems(sch).ToList();
            var fragment = SchematicFragment.FromItems(items, Lib, sch.Title);
            string json = fragment.ToJson();
            Assert.Contains(SchematicFragment.FormatId, json);
            var back = SchematicFragment.FromJson(json);
            Assert.NotNull(back);
            Assert.Equal(fragment.Count, back!.Count);
            Assert.True(back.Labels[0].Global);

            var before = Connectivity(sch).Count;
            var result = back.PasteInto(sch, Lib, back.Anchor + P(40, 0));
            Assert.Equal(3, result.Parts.Count);
            Assert.Equal(3, result.Renamed.Count);
            Assert.Equal(6, sch.Parts.Count);
            Assert.Empty(Annotator.Duplicates(sch));
            Assert.Contains(sch.Parts, p => p.RefDes == "R2");
            // The copy is a separate circuit except for the shared label name.
            Assert.True(Connectivity(sch).Count >= before);
            Assert.Equal(sch.Parts[3].Position, sch.Parts[0].Position + P(40, 0));
        }

        [Fact]
        public void PasteIntoAnotherSheetKeepsUniqueDesignators()
        {
            var src = LedLoop();
            var fragment = SchematicFragment.FromItems(new object[] { src.Parts[1] }, Lib);
            var other = new Schematic();
            other.AddPart(Lib, "res-220", P(0, 0)); // R1 exists there
            var result = fragment.PasteInto(other, Lib, P(10, 10));
            Assert.Equal("R2", result.Parts[0].RefDes);
            var empty = new Schematic();
            var kept = fragment.PasteInto(empty, Lib, P(0, 0));
            Assert.Equal("R1", kept.Parts[0].RefDes);
            Assert.Equal(P(0, 0), kept.Parts[0].Position);
        }

        [Fact]
        public void ForeignClipboardTextIsRejectedAndUnknownPartsAreSkipped()
        {
            Assert.Null(SchematicFragment.FromJson("hello"));
            Assert.Null(SchematicFragment.FromJson("{\"format\":\"something\"}"));
            Assert.Null(SchematicFragment.FromJson("{\"format\":\"" + SchematicFragment.FormatId + "\", broken"));
            var f = new SchematicFragment();
            f.Parts.Add(new SchematicPart { RefDes = "X1", PartId = "no-such-part" });
            var r = f.PasteInto(new Schematic(), Lib, P(0, 0));
            Assert.Empty(r.Parts);
            Assert.Single(r.SkippedParts);
        }

        [Fact]
        public void FragmentRotateAndMirrorTurnAboutTheAnchor()
        {
            var sch = LedLoop();
            var f = SchematicFragment.FromItems(SchematicEditing.AllItems(sch), Lib);
            var anchor = f.Anchor;
            var json = f.ToJson();
            for (int i = 0; i < 4; i++) f.Rotate();
            Assert.Equal(json, f.ToJson());
            f.Mirror(false);
            Assert.Equal(anchor, f.Anchor);
            f.Mirror(false);
            Assert.Equal(json, f.ToJson());
        }

        // ------------------------------------------------------------- annotation

        [Fact]
        public void AnnotateNumbersByPositionAndKeepsValidDesignators()
        {
            var sch = new Schematic();
            var a = sch.AddPart(Lib, "res-220", P(20, 0));
            var b = sch.AddPart(Lib, "res-220", P(0, 0));
            var c = sch.AddPart(Lib, "res-220", P(0, 10));
            a.RefDes = "R?";
            b.RefDes = "R7";
            c.RefDes = "R7";
            var changes = Annotator.Annotate(sch, Lib, new AnnotationOptions { ResetExisting = false });
            Assert.Equal("R7", b.RefDes);
            Assert.Equal("R1", a.RefDes);
            Assert.Equal("R2", c.RefDes);
            Assert.Equal(2, changes.Count);

            Annotator.Annotate(sch, Lib, new AnnotationOptions { ResetExisting = true, Order = AnnotationOrder.Rows });
            Assert.Equal(new[] { "R1", "R2", "R3" }, new[] { b.RefDes, a.RefDes, c.RefDes });
            Annotator.Annotate(sch, Lib, new AnnotationOptions { ResetExisting = true, Order = AnnotationOrder.Columns });
            Assert.Equal(new[] { "R1", "R2", "R3" }, new[] { b.RefDes, c.RefDes, a.RefDes });
        }

        [Fact]
        public void AnnotationRenamesFollowIntoBoardAndHandlesSwaps()
        {
            var sch = LedLoop();
            sch.Board = RobotTwin.CoreSim.Pcb.BoardDocument.Synchronize(sch, Lib);
            var r = sch.Parts[1];
            r.RefDes = "R5";
            sch.Board.Components.First(x => x.RefDes == "R1").RefDes = "R5";
            var changes = Annotator.Annotate(sch, Lib, new AnnotationOptions { ResetExisting = true });
            Annotator.ApplyToLinkedData(sch, changes);
            Assert.Equal("R1", r.RefDes);
            Assert.Contains(sch.Board.Components, x => x.RefDes == "R1");
            Assert.DoesNotContain(sch.Board.Components, x => x.RefDes == "R5");
        }

        [Theory]
        [InlineData("R12", "R", 12)]
        [InlineData("#PWR3", "#PWR", 3)]
        [InlineData("R?", "R", null)]
        [InlineData("U", "U", null)]
        public void DesignatorParsing(string refDes, string prefix, int? number)
        {
            Assert.Equal(prefix, Annotator.Prefix(refDes));
            Assert.Equal(number, Annotator.Number(refDes));
        }

        [Fact]
        public void NaturalOrderPutsR2BeforeR10()
        {
            var list = new List<string> { "R10", "C1", "R2", "R1" };
            list.Sort(Annotator.NaturalComparer);
            Assert.Equal(new[] { "C1", "R1", "R2", "R10" }, list);
        }

        // ------------------------------------------------------------- ERC

        [Fact]
        public void ErcCleanLoopHasNoErrorsOrWarnings()
        {
            var v = ElectricalRulesChecker.Check(LedLoop(), Lib);
            Assert.DoesNotContain(v, x => x.Severity != ErcSeverity.Info);
        }

        [Fact]
        public void ErcFindsUnconnectedPinsDuplicatesAndUnannotatedParts()
        {
            var sch = LedLoop();
            var extra = sch.AddPart(Lib, "res-220", P(40, 20));
            extra.RefDes = "R1";
            var q = sch.AddPart(Lib, "res-220", P(40, 30));
            q.RefDes = "R?";
            var v = ElectricalRulesChecker.Check(sch, Lib);
            Assert.Contains(v, x => x.Code == "duplicate-refdes" && x.RefDes == "R1");
            Assert.Contains(v, x => x.Code == "not-annotated");
            Assert.Contains(v, x => x.Code == "pin-unconnected" && x.Severity == ErcSeverity.Warning);

            // A no-connect flag silences the pin.
            var tip = Pin(extra, "A");
            int before = v.Count(x => x.Code == "pin-unconnected");
            sch.NoConnects.Add(new NoConnectFlag { Position = tip });
            var after = ElectricalRulesChecker.Check(sch, Lib);
            Assert.Equal(before - 1, after.Count(x => x.Code == "pin-unconnected"));
            sch.NoConnects.Add(new NoConnectFlag { Position = P(99, 99) });
            Assert.Contains(ElectricalRulesChecker.Check(sch, Lib), x => x.Code == "nc-floating");
        }

        [Fact]
        public void ErcFlagsShortedOutputsAndUndrivenPower()
        {
            var sch = new Schematic();
            var u1 = sch.AddPart(Lib, "lm358", P(0, 0));
            var u2 = sch.AddPart(Lib, "lm358", P(0, 20));
            sch.AddWire(Pin(u1, "OUT"), P(Pin(u1, "OUT").X + 4, Pin(u1, "OUT").Y), P(Pin(u1, "OUT").X + 4, Pin(u2, "OUT").Y), Pin(u2, "OUT"));
            sch.Labels.Add(new NetLabel { Name = "VCC", Position = Pin(u1, "V+") });
            sch.Labels.Add(new NetLabel { Name = "VCC", Position = Pin(u2, "V+") });
            var v = ElectricalRulesChecker.Check(sch, Lib);
            Assert.Contains(v, x => x.Code == "output-conflict" && x.Severity == ErcSeverity.Error);
            Assert.Contains(v, x => x.Code == "power-not-driven" && x.Net == "VCC");

            // A supply on the net fixes the power error (also through a switch).
            var bat = sch.AddPart(Lib, "bat-9v", P(-20, 0));
            var sw = sch.AddPart(Lib, "toggle-spst", P(-20, -12));
            sch.Labels.Add(new NetLabel { Name = "VCC", Position = Pin(sw, Lib.Get(sw.PartId).Pins[1].Name) });
            sch.AddWire(Pin(bat, "+"), P(Pin(bat, "+").X, Pin(sw, Lib.Get(sw.PartId).Pins[0].Name).Y), Pin(sw, Lib.Get(sw.PartId).Pins[0].Name));
            Assert.DoesNotContain(ElectricalRulesChecker.Check(sch, Lib), x => x.Code == "power-not-driven");
        }

        [Fact]
        public void ErcTakesAnEsp32DevKitsVinAsTheUsbSupply()
        {
            // [neopixel] An ESP32 DevKit's VIN carries USB 5 V through its diode: a 5 V strip fed from it is powered.
            var sch = new Schematic();
            var esp = sch.AddPart(Lib, "esp32-devkit-v1", P(0, 0));
            var strip = sch.AddPart(Lib, "ws2812-8", P(30, 0));
            sch.Labels.Add(new NetLabel { Name = "5V", Position = Pin(esp, "VIN") });
            sch.Labels.Add(new NetLabel { Name = "5V", Position = Pin(strip, "VDD") });
            var v = ElectricalRulesChecker.Check(sch, Lib);
            Assert.DoesNotContain(v, x => x.Code == "power-not-driven");

            // A battery on VIN is the board's designed input: no conflict between two supplies.
            var bat = sch.AddPart(Lib, "bat-9v", P(-20, 0));
            sch.Labels.Add(new NetLabel { Name = "5V", Position = Pin(bat, "+") });
            Assert.DoesNotContain(ElectricalRulesChecker.Check(sch, Lib), x => x.Code == "supply-conflict" || x.Code == "power-not-driven");
        }

        [Fact]
        public void ErcOutputTiedToGroundIsAnError()
        {
            var sch = new Schematic();
            var u1 = sch.AddPart(Lib, "lm358", P(0, 0));
            sch.Labels.Add(new NetLabel { Name = "GND", Position = Pin(u1, "OUT") });
            Assert.Contains(ElectricalRulesChecker.Check(sch, Lib), x => x.Code == "output-to-power");
        }

        [Fact]
        public void ErcReportsDanglingWiresAndLonelyLabels()
        {
            var sch = LedLoop();
            sch.AddWire(P(50, 50), P(55, 50));
            sch.Labels.Add(new NetLabel { Name = "BOŞ", Position = P(70, 70) });
            var v = ElectricalRulesChecker.Check(sch, Lib);
            Assert.Equal(2, v.Count(x => x.Code == "wire-dangling"));
            Assert.Contains(v, x => x.Code == "label-unconnected");
        }

        // ------------------------------------------------------------- BOM

        [Fact]
        public void BomGroupsIdenticalPartsAndSkipsPowerSymbols()
        {
            var sch = LedLoop();
            sch.AddPart(Lib, "res-220", P(40, 0));
            sch.AddPart(Lib, "gnd", P(0, 20));
            sch.AddPart(Lib, "rail-5v", P(0, -20));
            var bom = BomBuilder.Build(sch, Lib);
            var res = bom.Single(l => l.PartId == "resistor");
            Assert.Equal(2, res.Quantity);
            Assert.Equal("R1, R2", res.ReferenceText);
            Assert.DoesNotContain(bom, l => l.References.Any(r => r.StartsWith("#", StringComparison.Ordinal)));
            string csv = BomBuilder.ToCsv(bom);
            Assert.StartsWith("#,Referans,Adet", csv);
            Assert.Contains("\"R1, R2\",2", csv);
            Assert.Equal(bom.Count + 1, csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Length);
            // [lang] In the interface's language when the Studio sets one.
            var keep = Localization.Translate;
            try
            {
                Localization.Translate = s => s switch { "Referans" => "Reference", "Adet" => "Qty", _ => s };
                Assert.StartsWith("#,Reference,Qty", BomBuilder.ToCsv(bom));
            }
            finally
            {
                Localization.Translate = keep;
            }
        }

        // ------------------------------------------------------------- find & replace

        [Fact]
        public void FindMatchesDesignatorsValuesAndNets()
        {
            var sch = LedLoop();
            sch.Labels.Add(new NetLabel { Name = "VBAT", Position = sch.Wires[0].Points[1] });
            var hits = SchematicSearch.Find(sch, Lib, "R1");
            Assert.Contains(hits, h => h.Kind == SearchHitKind.Part && h.RefDes == "R1");
            Assert.Contains(SchematicSearch.Find(sch, Lib, "220"), h => h.Kind == SearchHitKind.Value && h.RefDes == "R1");
            Assert.Contains(SchematicSearch.Find(sch, Lib, "vbat"), h => h.Kind == SearchHitKind.Net && h.Net == "VBAT");
            Assert.Contains(SchematicSearch.Find(sch, Lib, "D*"), h => h.RefDes == "D1");
            Assert.Empty(SchematicSearch.Find(sch, Lib, "R1", new SearchOptions { RefDes = false, Values = false, Nets = false }));
        }

        [Fact]
        public void ReplaceChangesValuesLabelsAndSkipsClashingDesignators()
        {
            var sch = LedLoop();
            sch.AddPart(Lib, "res-220", P(40, 0));
            sch.Labels.Add(new NetLabel { Name = "VBAT", Position = sch.Wires[0].Points[1] });
            int n = SchematicSearch.Replace(sch, Lib, "220", "330", new SearchOptions { RefDes = false, Nets = false });
            Assert.Equal(2, n);
            Assert.All(sch.Parts.Where(p => p.PartId == "resistor"), p => Assert.Equal("330", p.Parameters["resistance"]));
            Assert.Equal(1, SchematicSearch.Replace(sch, Lib, "VBAT", "V9", new SearchOptions { RefDes = false, Values = false }));
            Assert.Equal("V9", sch.Labels[0].Name);
            Assert.Equal(0, SchematicSearch.Replace(sch, Lib, "R2", "R1", new SearchOptions { Values = false, Nets = false, WholeWord = true }));
            Assert.Equal(1, SchematicSearch.Replace(sch, Lib, "R2", "R9", new SearchOptions { Values = false, Nets = false, WholeWord = true }));
        }

        // ------------------------------------------------------------- model

        [Fact]
        public void NoConnectsAndGlobalLabelsSurviveJsonAndOldFilesLoad()
        {
            var sch = LedLoop();
            sch.NoConnects.Add(new NoConnectFlag { Position = P(3, 4) });
            sch.Labels.Add(new NetLabel { Name = "A", Position = P(1, 1), Global = true });
            sch.Labels.Add(new NetLabel { Name = "B", Position = P(2, 1) });
            var json = sch.ToJson();
            Assert.DoesNotContain("\"global\": false", json);
            var back = Schematic.FromJson(json);
            Assert.Single(back.NoConnects);
            Assert.True(back.Labels[0].Global);
            Assert.False(back.Labels[1].Global);
            var old = Schematic.FromJson("{\"version\":1,\"title\":\"x\",\"parts\":[],\"wires\":[],\"labels\":[]}");
            Assert.NotNull(old.NoConnects);
        }
    }
}
