using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>Stackup, zones, net classes, DRC, interactive routing, outline, holes, drawings and footprint files.</summary>
    [Trait("Category", "Design")]
    public class PcbEditorTests
    {
        /// <summary>J1 (2-pin header, THT) and R1 (0805, SMD): GND J1.1–R1.2, SIG J1.2–R1.1.</summary>
        private static BoardDocument SmallBoard(double width = 40, double height = 30)
        {
            var b = new BoardDocument { Width = width, Height = height, CornerRadius = 0 };
            var j1 = new BoardComponent { RefDes = "J1", FootprintId = "HDR-1x2", X = 6, Y = 10 };
            j1.PadNets["1"] = "GND";
            j1.PadNets["2"] = "SIG";
            var r1 = new BoardComponent { RefDes = "R1", FootprintId = "R0805", X = 30, Y = 12.54 };
            r1.PadNets["1"] = "SIG";
            r1.PadNets["2"] = "GND";
            b.Components.Add(j1);
            b.Components.Add(r1);
            return b;
        }

        private static PlacedPad PadOf(BoardDocument b, string refDes, string pad) => b.Pads().Single(p => p.Component.RefDes == refDes && p.Pad.Name == pad);

        private static List<DrcViolation> Errors(BoardDocument b) => DesignRuleChecker.Check(b).Where(v => v.Severity == DrcSeverity.Error).ToList();

        // ================================================================== stackup and layers

        [Fact]
        public void StackupHasTheRightLayersAndThickness()
        {
            Assert.Equal(new[] { CopperLayer.Top, CopperLayer.Bottom }, PcbLayers.Stack(2));
            Assert.Equal(new[] { CopperLayer.Top, CopperLayer.In1, CopperLayer.In2, CopperLayer.Bottom }, PcbLayers.Stack(4));
            Assert.Equal(6, PcbLayers.Stack(6).Count);
            Assert.Equal(3, PcbLayers.StackIndex(CopperLayer.Bottom, 4));
            Assert.Equal(-1, PcbLayers.StackIndex(CopperLayer.In3, 4));
            Assert.Equal("In2.Cu", PcbLayers.KiCadName(CopperLayer.In2));
            Assert.Equal(31, PcbLayers.KiCadOrdinal(CopperLayer.Bottom));

            foreach (int n in new[] { 2, 4, 6 })
            {
                var st = new BoardStackup { CopperLayers = n, Thickness = 1.6 };
                var layers = st.Build(35);
                Assert.Equal(n, layers.Count(l => l.Type == "copper"));
                Assert.Equal(n - 1, layers.Count(l => l.Type != "copper"));
                Assert.Equal(1.6 - 2 * st.SolderMaskThickness, layers.Sum(l => l.Thickness), 6);
                if (n > 2) Assert.Equal("prepreg", layers[1].Type);
            }

            // Pads: SMD only on their (flipped) outer layer, THT on every layer.
            var b = SmallBoard();
            b.Stackup.CopperLayers = 4;
            b.Find("R1")!.Side = CopperLayer.Bottom;
            var smd = PadOf(b, "R1", "1");
            Assert.True(smd.OnLayer(CopperLayer.Bottom));
            Assert.False(smd.OnLayer(CopperLayer.In1));
            Assert.False(smd.OnLayer(CopperLayer.Top));
            Assert.True(PadOf(b, "J1", "1").OnLayer(CopperLayer.In2));
        }

        [Fact]
        public void BlindViasSpanOnlyTheirLayersAndNeedPermission()
        {
            var b = SmallBoard();
            b.Stackup.CopperLayers = 4;
            var blind = new Via { Net = "SIG", X = 20, Y = 20, StartLayer = CopperLayer.Top, EndLayer = CopperLayer.In1 };
            Assert.False(blind.IsThrough(4));
            Assert.True(blind.Spans(CopperLayer.In1, 4));
            Assert.False(blind.Spans(CopperLayer.In2, 4));
            Assert.True(new Via().IsThrough(4));
            b.Vias.Add(blind);
            Assert.Contains(DesignRuleChecker.Check(b), v => v.Rule == "Kör/gömülü via");
            b.Stackup.AllowBlindBuriedVias = true;
            Assert.DoesNotContain(DesignRuleChecker.Check(b), v => v.Rule == "Kör/gömülü via");

            var files = GerberExporter.Generate(b, "t");
            var drill = Assert.Single(files, f => f.Key == "t-F_Cu-In1_Cu.drl").Value;
            Assert.Contains("TF.FileFunction,Plated,1,2,Blind", drill);
            Assert.Contains("X20.000Y10.000", drill);
            Assert.DoesNotContain("X20.000Y10.000", files["t-PTH.drl"]);
            Assert.Contains("Copper,L2,Inr", files["t-In1_Cu.g1"]);
            Assert.Contains("Copper,L4,Bot", files["t-B_Cu.gbl"]);

            string pcb = KiCadExporter.ExportPcb(b, "blind");
            Assert.Contains("(1 \"In1.Cu\" signal)", pcb);
            Assert.Contains("(2 \"In2.Cu\" signal)", pcb);
            Assert.Contains("(via blind", pcb);
            Assert.Contains("(layers \"F.Cu\" \"In1.Cu\")", pcb);
            Assert.Contains("(stackup", pcb);
            Assert.Equal(4, pcb.Split('\n').Count(l => l.Contains("(type \"copper\")")));
        }

        [Fact]
        public void FourLayerBoardRoutesToAnInnerGroundPlaneAndPassesDrc()
        {
            var b = SmallBoard();
            b.Stackup.CopperLayers = 4;
            b.Zones.Add(new Zone { Net = "GND", Layer = CopperLayer.In1 });
            var router = new AutoRouter(b);
            Assert.DoesNotContain(CopperLayer.In1, router.RoutingLayers);
            Assert.Contains("GND", router.PlaneNets);
            var result = router.Route();
            Assert.True(result.Complete, string.Join("; ", result.Failed));
            Assert.Equal(1, result.PlaneConnections);   // only the SMD GND pad needs a via
            Assert.DoesNotContain(b.Tracks, t => t.Layer == CopperLayer.In1);
            Assert.Contains(b.Vias, v => v.Net == "GND");

            ZoneFiller.FillAll(b);
            Assert.True(b.Zones[0].FilledRegions.Count >= 1);
            var errors = Errors(b);
            Assert.True(errors.Count == 0, string.Join("\n", errors));
            Assert.Empty(b.Ratsnest());

            var files = GerberExporter.Generate(b, "four");
            Assert.Contains("four-In1_Cu.g1", files.Keys);
            Assert.Contains("four-In2_Cu.g2", files.Keys);
            Assert.Contains("G36*", files["four-In1_Cu.g1"]);   // the plane is a region
        }

        // ================================================================== zones

        [Fact]
        public void ZoneFillKeepsClearanceAndMakesThermalReliefs()
        {
            var b = SmallBoard();
            var sig1 = PadOf(b, "J1", "2");
            var sig2 = PadOf(b, "R1", "1");
            b.Tracks.Add(new Track { Net = "SIG", Layer = CopperLayer.Top, X1 = sig1.X, Y1 = sig1.Y, X2 = sig2.X, Y2 = sig2.Y, Width = 0.3 });
            var zone = new Zone { Net = "GND", Layer = CopperLayer.Top, ThermalGap = 0.5, ThermalSpokeWidth = 0.4, MinWidth = 0.25 };
            b.Zones.Add(zone);
            ZoneFiller.FillAll(b);
            Assert.True(zone.IsFilled);
            var fill = zone.FilledRegions;
            Assert.NotEmpty(fill);

            double c = b.ClearanceBetween("GND", "SIG");
            foreach (var region in fill)
            {
                foreach (var contour in region.Contours())
                {
                    foreach (var p in contour)
                    {
                        double toTrack = Geometry.PointSegmentDistance(p.X, p.Y, sig1.X, sig1.Y, sig2.X, sig2.Y) - 0.15;
                        Assert.True(toTrack >= c - 0.01, $"fill point {p} is {toTrack:0.###} mm from the SIG track");
                        Assert.True(DesignRuleChecker.PadSegmentDistance(sig2, p.X, p.Y, p.X, p.Y) >= c - 0.01);
                        Assert.True(p.X >= b.Rules.EdgeClearance - 0.01 && p.Y >= b.Rules.EdgeClearance - 0.01);
                    }
                }
            }

            // GND pads: joined by spokes, but surrounded by the thermal gap.
            var gnd = PadOf(b, "J1", "1");
            Assert.Contains(fill, r => BoardConnectivity.RegionTouchesRect(r, gnd.X, gnd.Y, gnd.Width / 2, gnd.Height / 2));
            double diag = gnd.Width / 2 + zone.ThermalGap / 2;
            Assert.False(Polygon.AnyContains(fill, gnd.X + diag, gnd.Y + diag));
            Assert.True(Polygon.AnyContains(fill, gnd.X + gnd.Width / 2 + zone.ThermalGap / 2, gnd.Y));   // spoke

            // GND is connected through the pour alone.
            Assert.DoesNotContain(BoardConnectivity.Build(b).Unrouted(), u => u.Net == "GND");
            Assert.DoesNotContain(b.Ratsnest(), r => r.Net == "GND");
            Assert.Empty(Errors(b));

            // Moving the track makes the fill outdated; refilling fixes it.
            b.Tracks[0].Y2 += 1;
            Assert.True(ZoneFiller.IsOutdated(b, zone));
            Assert.Contains(DesignRuleChecker.Check(b), v => v.Rule == "Dolgu güncel değil");
            Assert.Equal(1, ZoneFiller.RefillOutdated(b));
            Assert.False(ZoneFiller.IsOutdated(b, zone));
        }

        [Fact]
        public void IslandsWithoutTheNetAreRemovedAndPriorityWins()
        {
            var b = SmallBoard();
            // A SIG track from top to bottom edge splits the board; the right part has a GND pad (R1.2), the left part J1.1.
            b.Tracks.Add(new Track { Net = "SIG", Layer = CopperLayer.Top, X1 = 20, Y1 = 0.8, X2 = 20, Y2 = 29.2, Width = 0.5 });
            // A third, empty part cut off by another wall.
            b.Tracks.Add(new Track { Net = "SIG", Layer = CopperLayer.Top, X1 = 36, Y1 = 0.8, X2 = 36, Y2 = 29.2, Width = 0.5 });
            var zone = new Zone { Net = "GND", Layer = CopperLayer.Top };
            b.Zones.Add(zone);
            ZoneFiller.FillAll(b);
            Assert.Equal(2, zone.FilledRegions.Count);
            zone.IslandRemoval = IslandRemoval.Never;
            ZoneFiller.FillAll(b);
            Assert.Equal(3, zone.FilledRegions.Count);
            zone.IslandRemoval = IslandRemoval.BelowArea;
            zone.MinIslandArea = 200;   // the 3.5 mm strip is ~95 mm²
            ZoneFiller.FillAll(b);
            Assert.Equal(2, zone.FilledRegions.Count);

            // Overlapping zones: the higher priority one keeps its copper.
            var b2 = SmallBoard();
            var high = new Zone { Net = "SIG", Layer = CopperLayer.Bottom, Priority = 2, Outline = Shapes.Rect(10, 5, 25, 25) };
            var low = new Zone { Net = "GND", Layer = CopperLayer.Bottom, Priority = 0 };
            b2.Zones.Add(low);
            b2.Zones.Add(high);
            Assert.Equal(new[] { high, low }, ZoneFiller.FillOrder(b2));
            b2.Tracks.Add(new Track { Net = "SIG", Layer = CopperLayer.Bottom, X1 = 15, Y1 = 15, X2 = 16, Y2 = 15, Width = 0.3 });   // keeps the SIG zone
            ZoneFiller.FillAll(b2);
            Assert.True(Polygon.AnyContains(high.FilledRegions, 17, 15));
            Assert.False(Polygon.AnyContains(low.FilledRegions, 17, 15));
            Assert.True(Polygon.AnyContains(low.FilledRegions, 32, 25));
            foreach (var p in low.FilledRegions.SelectMany(r => r.Contours()).SelectMany(c => c))
            {
                Assert.False(p.X > 10 + 0.1 && p.X < 25 - 0.1 && p.Y > 5 + 0.1 && p.Y < 25 - 0.1, $"GND copper at {p} inside the SIG zone");
            }
        }

        [Fact]
        public void BoardModelRoundTripsThroughJson()
        {
            var b = SmallBoard();
            b.Stackup.CopperLayers = 6;
            b.Stackup.AllowBlindBuriedVias = true;
            b.NetClasses.Add(new NetClass { Name = "HV", Clearance = 0.6, TrackWidth = 0.5 });
            b.AssignClass("SIG", "HV");
            b.Zones.Add(new Zone { Net = "GND", Layer = CopperLayer.In3, Priority = 3, PadConnection = ZonePadConnection.Solid, Outline = Shapes.Rect(2, 2, 20, 20) });
            b.Vias.Add(new Via { Net = "SIG", X = 10, Y = 3, StartLayer = CopperLayer.In1, EndLayer = CopperLayer.In2 });
            b.Outline.AddRange(new[] { new OutlinePoint(0, 0, 2), new OutlinePoint(40, 0), new OutlinePoint(40, 30, 3), new OutlinePoint(0, 30) });
            b.MountingHoles.Add(new MountingHole { X = 37, Y = 27, Drill = 3.2 });
            b.Texts.Add(new BoardText { Text = "REV A", X = 20, Y = 25, Layer = GraphicLayer.SilkBottom });
            b.Dimensions.Add(new BoardDimension { X1 = 0, Y1 = 30, X2 = 40, Y2 = 30, Offset = 4 });
            ZoneFiller.FillAll(b);
            Assert.False(ZoneFiller.IsOutdated(b, b.Zones[0]));

            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
            string json = JsonSerializer.Serialize(b, options);
            var back = JsonSerializer.Deserialize<BoardDocument>(json, options)!;
            Assert.Equal(6, back.LayerCount);
            Assert.True(back.Stackup.AllowBlindBuriedVias);
            Assert.Equal(0.6, back.ClassFor("sig").Clearance);   // assignments stay case-insensitive
            Assert.Equal(3, back.NetClasses.Count);
            var z = Assert.Single(back.Zones);
            Assert.Equal(CopperLayer.In3, z.Layer);
            Assert.Equal(ZonePadConnection.Solid, z.PadConnection);
            Assert.Equal(b.Zones[0].FilledRegions.Sum(r => r.Area), z.FilledRegions.Sum(r => r.Area), 3);
            Assert.False(ZoneFiller.IsOutdated(back, z));
            Assert.Equal(CopperLayer.In2, back.Vias.Single().EndLayer);
            Assert.Equal(4, back.Outline.Count);
            Assert.Single(back.MountingHoles);
            Assert.True(back.Texts.Single().Mirrored);
            Assert.Equal(40, back.Dimensions.Single().Length, 9);

            // Old files: no net classes → Default/Power from the rules.
            var old = JsonSerializer.Deserialize<BoardDocument>("{\"width\":50,\"height\":40,\"rules\":{\"trackWidth\":0.25,\"powerTrackWidth\":1.2,\"clearance\":0.3},\"powerNets\":[\"VCC\"]}", options)!;
            Assert.Equal(0.25, old.ClassFor("X").TrackWidth);
            Assert.Equal(1.2, old.ClassFor("VCC").TrackWidth);
            Assert.Equal(0.3, old.ClearanceBetween("X", "VCC"));
            Assert.Equal(2, old.LayerCount);
        }

        // ================================================================== net classes

        [Fact]
        public void NetClassesDriveTheRouterAndDrc()
        {
            var b = SmallBoard();
            b.NetClasses.Add(new NetClass { Name = "HV", Clearance = 0.6, TrackWidth = 0.6, ViaDiameter = 1.0, ViaDrill = 0.5 });
            b.AssignClass("SIG", "HV");
            Assert.Equal("HV", b.ClassNameFor("SIG"));
            Assert.Equal(0.6, b.ClearanceBetween("SIG", "GND"));
            Assert.Equal(0.6, b.TrackWidthFor("SIG"));

            var result = new AutoRouter(b).Route();
            Assert.True(result.Complete, string.Join("; ", result.Failed));
            Assert.All(b.Tracks.Where(t => t.Net == "SIG"), t => Assert.Equal(0.6, t.Width));
            Assert.Empty(Errors(b));

            // A GND track 0.4 mm from a SIG track: fine for Default (0.2), a violation for HV (0.6).
            var b2 = SmallBoard();
            b2.Tracks.Add(new Track { Net = "SIG", Layer = CopperLayer.Top, X1 = 10, Y1 = 20, X2 = 20, Y2 = 20, Width = 0.3 });
            b2.Tracks.Add(new Track { Net = "GND", Layer = CopperLayer.Top, X1 = 10, Y1 = 20.7, X2 = 20, Y2 = 20.7, Width = 0.3 });
            Assert.DoesNotContain(DesignRuleChecker.Check(b2), v => v.Rule == "Yol-yol aralığı");
            b2.NetClasses.Add(new NetClass { Name = "HV", Clearance = 0.6 });
            b2.AssignClass("SIG", "HV");
            var v1 = Assert.Single(DesignRuleChecker.Check(b2), v => v.Rule == "Yol-yol aralığı");
            Assert.True(v1.Message.Contains("en az 0.6") || v1.Message.Contains("en az 0,6"), v1.Message);
            Assert.Equal(CopperLayer.Top, v1.Layer);

            // The KiCad project carries the classes and the assignment.
            using var doc = JsonDocument.Parse(KiCadExporter.ExportProject("hv", b2));
            var ns = doc.RootElement.GetProperty("net_settings");
            Assert.Contains(ns.GetProperty("classes").EnumerateArray(), c => c.GetProperty("name").GetString() == "HV" && c.GetProperty("clearance").GetDouble() == 0.6);
            Assert.Contains(ns.GetProperty("netclass_patterns").EnumerateArray(), p => p.GetProperty("pattern").GetString() == "SIG" && p.GetProperty("netclass").GetString() == "HV");
        }

        // ================================================================== DRC

        [Fact]
        public void DrcChecksRingsHolesSilkEdgesAndDanglingCopper()
        {
            var b = SmallBoard();
            b.Vias.Add(new Via { Net = "SIG", X = 15, Y = 20, Diameter = 0.6, Drill = 0.5 });
            b.Vias.Add(new Via { Net = "GND", X = 15.6, Y = 20, Diameter = 0.6, Drill = 0.3 });
            var rules = DesignRuleChecker.Check(b).Select(v => v.Rule).ToList();
            Assert.Contains("Halka genişliği", rules);
            Assert.Contains("Delik-delik aralığı", rules);
            Assert.Contains("Bağlantısız via", rules);

            // Silkscreen over pads (the tactile switch outline crosses its pads) is a warning.
            var b2 = SmallBoard();
            b2.Components.Add(new BoardComponent { RefDes = "SW1", FootprintId = "TACT-6", X = 15, Y = 15 });
            var silk = Assert.Single(DesignRuleChecker.Check(b2), v => v.Rule == "Serigrafi pedde");
            Assert.Equal(DrcSeverity.Warning, silk.Severity);
            Assert.Contains("SW1", silk.Message);

            // A dangling track end and copper outside an L-shaped outline.
            var b3 = SmallBoard();
            b3.Outline.AddRange(new[] { new OutlinePoint(0, 0), new OutlinePoint(40, 0), new OutlinePoint(40, 15), new OutlinePoint(20, 15), new OutlinePoint(20, 30), new OutlinePoint(0, 30) });
            var r1 = PadOf(b3, "R1", "1");
            b3.Tracks.Add(new Track { Net = "SIG", Layer = CopperLayer.Top, X1 = r1.X, Y1 = r1.Y, X2 = 30, Y2 = 25, Width = 0.3 });
            var found = DesignRuleChecker.Check(b3);
            Assert.Contains(found, v => v.Rule == "Boşta yol ucu" && Math.Abs(v.Y - 25) < 1e-9);
            Assert.Contains(found, v => v.Rule == "Kart kenarı" && v.Message.StartsWith("SIG yolu"));
            Assert.True(b3.EdgeDistance(30, 25) < 0);
            Assert.True(b3.EdgeDistance(10, 25) > 0);

            // Mounting hole clearance and courtyard.
            var b4 = SmallBoard();
            b4.MountingHoles.Add(new MountingHole { X = 31, Y = 12.54, Drill = 3.2 });
            var r4 = DesignRuleChecker.Check(b4);
            Assert.Contains(r4, v => v.Rule == "Gövde çakışması" && v.Message.Contains("montaj"));
        }

        // ================================================================== interactive routing and editing

        [Fact]
        public void WalkaroundGoesAroundAForeignTrackAndStopsAtObstacles()
        {
            var b = SmallBoard();
            b.Tracks.Add(new Track { Net = "GND", Layer = CopperLayer.Top, X1 = 20, Y1 = 4, X2 = 20, Y2 = 22, Width = 0.5 });
            var router = new InteractiveRouter(b, "SIG");
            Assert.Equal(b.TrackWidthFor("SIG"), router.Width);

            // Nothing in the way: the posture elbow.
            var free = router.Walkaround(CopperLayer.Top, new PcbPoint(4, 25), new PcbPoint(12, 27), diagonalFirst: true);
            Assert.Equal(3, free.Count);
            Assert.Equal(Math.Abs(free[1].X - free[0].X), Math.Abs(free[1].Y - free[0].Y), 9);   // 45° leg first
            var straight = router.Walkaround(CopperLayer.Top, new PcbPoint(4, 25), new PcbPoint(12, 27), diagonalFirst: false);
            Assert.Equal(straight[1].Y, straight[0].Y, 9);   // straight leg first

            // Through the wall: the path walks around its end.
            var start = new PcbPoint(14, 14);
            var target = new PcbPoint(26, 14);
            var path = router.Walkaround(CopperLayer.Top, start, target, true);
            Assert.Equal(start, path[0]);
            Assert.Equal(target.X, path[path.Count - 1].X, 6);
            Assert.Equal(target.Y, path[path.Count - 1].Y, 6);
            Assert.True(router.PathClear(CopperLayer.Top, path));
            Assert.Contains(path, p => p.Y < 4 || p.Y > 22);
            for (int i = 1; i < path.Count; i++)
            {
                double dx = Math.Abs(path[i].X - path[i - 1].X), dy = Math.Abs(path[i].Y - path[i - 1].Y);
                if (i > 1 && i < path.Count - 1) Assert.True(dx < 1e-6 || dy < 1e-6 || Math.Abs(dx - dy) < 1e-6, "segment not at 0/45/90°");
            }

            // Target inside the keep-out of the wall: stop short of it, still clean.
            var stop = router.Walkaround(CopperLayer.Top, start, new PcbPoint(20, 14), true);
            Assert.True(router.PathClear(CopperLayer.Top, stop));
            Assert.True(stop[stop.Count - 1].X < 20 - 0.25 - 0.15 - 0.2 + 1e-6);

            // 90° mode uses orthogonal segments only.
            var ortho = router.Walkaround(CopperLayer.Top, start, target, true, only90: true);
            for (int i = 2; i < ortho.Count - 1; i++)
            {
                Assert.True(Math.Abs(ortho[i].X - ortho[i - 1].X) < 1e-6 || Math.Abs(ortho[i].Y - ortho[i - 1].Y) < 1e-6);
            }

            // Vias are checked on every layer they cross.
            var j1 = PadOf(b, "J1", "1");
            Assert.False(router.ViaClear(j1.X + 1.2, j1.Y, CopperLayer.Top, CopperLayer.Bottom));
            Assert.True(router.ViaClear(10, 25, CopperLayer.Top, CopperLayer.Bottom));
        }

        [Fact]
        public void DraggingASegmentStretchesItsNeighboursAndSelectingFindsTheWholeTrack()
        {
            var b = SmallBoard();
            var a = PadOf(b, "J1", "2");
            var z = PadOf(b, "R1", "1");
            // J1.2 → (12, 12.54) → (12, 20) → (24, 20) → (24, 12.54) → R1.1
            var t1 = new Track { Net = "SIG", Layer = CopperLayer.Top, X1 = a.X, Y1 = a.Y, X2 = 12, Y2 = 12.54, Width = 0.3 };
            var t2 = new Track { Net = "SIG", Layer = CopperLayer.Top, X1 = 12, Y1 = 12.54, X2 = 12, Y2 = 20, Width = 0.3 };
            var t3 = new Track { Net = "SIG", Layer = CopperLayer.Top, X1 = 12, Y1 = 20, X2 = 24, Y2 = 20, Width = 0.3 };
            var t4 = new Track { Net = "SIG", Layer = CopperLayer.Top, X1 = 24, Y1 = 20, X2 = 24, Y2 = 12.54, Width = 0.3 };
            var t5 = new Track { Net = "SIG", Layer = CopperLayer.Top, X1 = 24, Y1 = 12.54, X2 = z.X, Y2 = z.Y, Width = 0.3 };
            b.Tracks.AddRange(new[] { t1, t2, t3, t4, t5 });
            Assert.DoesNotContain(BoardConnectivity.Build(b).Unrouted(), u => u.Net == "SIG");

            var (tracks, vias) = TrackEditing.ConnectedTrack(b, t3);
            Assert.Equal(5, tracks.Count);
            Assert.Empty(vias);

            // Drag the bottom segment 3 mm down (the along-track part of the move is ignored).
            var added = TrackEditing.DragSegment(b, t3, 1.0, 3.0);
            Assert.Empty(added);
            Assert.Equal(23, t3.Y1, 9);
            Assert.Equal(23, t3.Y2, 9);
            Assert.Equal(23, t2.Y2, 9);
            Assert.Equal(12, t2.X2, 9);   // neighbour keeps its direction
            Assert.Equal(23, t4.Y1, 9);
            Assert.DoesNotContain(BoardConnectivity.Build(b).Unrouted(), u => u.Net == "SIG");

            // A segment ending on a pad keeps the pad end and gets a joining segment.
            var join = TrackEditing.DragSegment(b, t1, 0, -1);
            Assert.Single(join);
            Assert.Equal(a.X, join[0].X1, 9);
            Assert.Equal(a.Y, join[0].Y1, 9);
            Assert.DoesNotContain(BoardConnectivity.Build(b).Unrouted(), u => u.Net == "SIG");
        }

        // ================================================================== outline, holes, drawings, outputs

        [Fact]
        public void PolygonOutlineWithRoundedCornersIsExportedWithArcs()
        {
            var b = SmallBoard();
            b.Outline.AddRange(new[] { new OutlinePoint(0, 0, 3), new OutlinePoint(40, 0, 3), new OutlinePoint(40, 30, 3), new OutlinePoint(0, 30, 3) });
            var elements = b.OutlineElements();
            Assert.Equal(4, elements.Count(e => e.IsArc));
            Assert.Equal(4, elements.Count(e => !e.IsArc));
            Assert.Equal(40 * 30 - (4 - Math.PI) * 9, Polygon.Area(b.OutlinePolygon(0.001)), 1);
            Assert.True(b.EdgeDistance(0.3, 0.3) < 0);   // cut corner

            var gerber = GerberExporter.Generate(b, "o");
            Assert.Equal(4, gerber["o-Edge_Cuts.gm1"].Split('\n').Count(l => l.StartsWith("G02", StringComparison.Ordinal) || l.StartsWith("G03", StringComparison.Ordinal)));
            string pcb = KiCadExporter.ExportPcb(b, "o");
            Assert.Equal(4, pcb.Split('\n').Count(l => l.TrimStart().StartsWith("(gr_arc", StringComparison.Ordinal)));
        }

        [Fact]
        public void HolesTextsDimensionsAndAssemblyFilesAreExported()
        {
            var b = SmallBoard();
            b.MountingHoles.Add(new MountingHole { X = 3.5, Y = 26.5, Drill = 3.2 });
            b.MountingHoles.Add(new MountingHole { X = 36.5, Y = 26.5, Drill = 3.2, PadDiameter = 6, Net = "GND" });
            b.Texts.Add(new BoardText { Text = "ROBOTWIN v2", X = 20, Y = 26, Size = 1.5 });
            b.Dimensions.Add(new BoardDimension { X1 = 0, Y1 = 0, X2 = 40, Y2 = 0, Offset = -3 });
            b.Find("R1")!.Side = CopperLayer.Bottom;
            b.Find("R1")!.Rotation = 90;

            var files = GerberExporter.Generate(b, "h");
            Assert.Contains("X3.500Y3.500", files["h-NPTH.drl"]);
            Assert.Contains("NonPlated", files["h-NPTH.drl"]);
            Assert.Contains("X36.500Y3.500", files["h-PTH.drl"]);
            Assert.True(files["h-F_Silkscreen.gto"].Split('\n').Count(l => l.EndsWith("D01*", StringComparison.Ordinal)) > 30);
            Assert.True(files["h-B_Silkscreen.gbo"].Split((char)10).Count(l => l.EndsWith("D01*", StringComparison.Ordinal)) > 5);   // R1 on the bottom
            Assert.Contains("G36*", files["h-B_Paste.gbp"]);   // bottom SMD (round-rect) pads
            Assert.DoesNotContain("G36*", files["h-F_Paste.gtp"]);
            Assert.StartsWith("Comment,Designator,Footprint,Quantity", files["h-BOM.csv"]);
            Assert.Contains("R1,", files["h-CPL.csv"]);
            Assert.Contains(",Bottom,270", files["h-CPL.csv"]);

            string pcb = KiCadExporter.ExportPcb(b, "h");
            Assert.Contains("np_thru_hole", pcb);
            Assert.Contains("(gr_text \"ROBOTWIN v2\"", pcb);
            Assert.Contains("(gr_text \"40.00 mm\"", pcb);
            Assert.Equal(2, pcb.Split('\n').Count(l => l.Contains("RobotWin:MountingHole_3.2mm")) / 2);

            var dim = b.Dimensions[0];
            Assert.Equal(7, dim.Geometry().Lines.Count);
            Assert.Equal("40.00 mm", dim.Label());
            Assert.Equal("1575 mil", dim.Label(mil: true));
        }

        [Fact]
        public void SilkscreenIsClippedAtPads()
        {
            var keepOut = new List<(double X, double Y, double HX, double HY)> { (5, 0, 1, 1) };
            var pieces = Silk.Clip(new Segment2(0, 0, 10, 0), keepOut).ToList();
            Assert.Equal(2, pieces.Count);
            Assert.Equal(4, pieces[0].X2, 9);
            Assert.Equal(6, pieces[1].X1, 9);
            Assert.Empty(Silk.Clip(new Segment2(4.5, 0, 5.5, 0), keepOut));
            Assert.Single(Silk.Clip(new Segment2(0, 5, 10, 5), keepOut));
            Assert.True(StrokeFont.Measure("ABC", 1.0) > 2);
        }

        [Fact]
        public void ZoneFillIsFastEnoughForInteractiveUse()
        {
            // 60 parts in a grid with routed nets and pours on both sides of a 120 x 90 mm board.
            var b = new BoardDocument { Width = 120, Height = 90 };
            for (int i = 0; i < 60; i++)
            {
                var c = new BoardComponent { RefDes = "R" + (i + 1), FootprintId = i % 3 == 0 ? "AXIAL-400" : "R0805", X = 6 + (i % 8) * 14, Y = 8 + (i / 8) * 10 };
                c.PadNets["1"] = i % 2 == 0 ? "GND" : "N" + (i / 2);
                c.PadNets["2"] = "N" + ((i + 1) / 2);
                b.Components.Add(c);
            }

            var routing = new AutoRouter(b) { Passes = 1 }.Route();
            Assert.True(routing.Routed > 40, routing.Routed + " routed");
            b.Zones.Add(new Zone { Net = "GND", Layer = CopperLayer.Top });
            b.Zones.Add(new Zone { Net = "GND", Layer = CopperLayer.Bottom });
            var watch = System.Diagnostics.Stopwatch.StartNew();
            ZoneFiller.FillAll(b);
            watch.Stop();
            Assert.True(watch.ElapsedMilliseconds < 8000, $"fill took {watch.ElapsedMilliseconds} ms");
            Assert.All(b.Zones, z => Assert.NotEmpty(z.FilledRegions));
            var bad = DesignRuleChecker.Check(b).Where(v => v.Severity == DrcSeverity.Error && v.Rule != "Bağlanmamış").ToList();
            Assert.True(bad.Count == 0, string.Join(Environment.NewLine, bad.Take(8)));
        }

        // ================================================================== footprints

        [Fact]
        public void UserFootprintRoundTripsWithGraphicsAndRegisters()
        {
            var fp = new Footprint { Id = "USER-SOIC-TEST", Description = "Test SOIC", Height = 1.5 };
            for (int i = 0; i < 4; i++)
            {
                fp.Pads.Add(new Pad { Name = (i + 1).ToString(), X = -2.7, Y = -1.905 + i * 1.27, Width = 1.55, Height = 0.6, Shape = PadShape.RoundRect, CornerRatio = 0.2 });
                fp.Pads.Add(new Pad { Name = (8 - i).ToString(), X = 2.7, Y = -1.905 + i * 1.27, Width = 1.55, Height = 0.6, Shape = PadShape.RoundRect, CornerRatio = 0.2 });
            }

            fp.Pads.Add(new Pad { Name = "MH", X = 0, Y = 0, Width = 1.0, Height = 1.0, Drill = 1.0, NonPlated = true });
            fp.Silkscreen.Add(new Segment2(-1.6, -2.6, 1.6, -2.6));
            fp.SilkCircles.Add((-2.9, -2.9, 0.2));
            fp.FabLines.Add(new Segment2(-1.95, -2.45, 1.95, -2.45));
            fp.Texts.Add(new FootprintText { Text = "%R", X = 0, Y = 3.5, Layer = GraphicLayer.FabTop });
            fp.FitCourtyard();
            Assert.Equal(-2.7 - 0.775 - 0.25, fp.CourtyardMinX, 6);

            string dir = Path.Combine(Path.GetTempPath(), "rtw-fp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string path = Path.Combine(dir, fp.Id + CustomFootprint.Extension);
                CustomFootprint.From(fp).Save(path);
                var back = CustomFootprint.Load(path).ToFootprint();
                Assert.Equal(9, back.Pads.Count);
                Assert.Equal(0.2, back.Pads[0].CornerRatio);
                Assert.True(back.Pads.Single(p => p.Name == "MH").NonPlated);
                Assert.Single(back.Silkscreen);
                Assert.Single(back.SilkCircles);
                Assert.Single(back.FabLines);
                Assert.Equal("%R", back.Texts.Single().Text);
                Assert.Equal(fp.CourtyardMaxY, back.CourtyardMaxY, 9);

                FootprintLibrary.Register(back);
                Assert.Same(back, FootprintLibrary.Get("USER-SOIC-TEST"));
                Assert.Contains("USER-SOIC-TEST", FootprintLibrary.AllIds);
                Assert.False(FootprintLibrary.IsBuiltIn("USER-SOIC-TEST"));
                Assert.True(FootprintLibrary.IsBuiltIn("R0805"));

                string mod = KiCadExporter.ExportFootprint(back);
                Assert.Contains("(roundrect_rratio 0.2)", mod);
                Assert.Contains("np_thru_hole", mod);
                Assert.Contains("(fp_text user \"%R\"", mod);

                // Old part files (no graphics) still get a courtyard silkscreen.
                var legacy = new CustomFootprint { Id = "X", CourtyardMinX = -1, CourtyardMinY = -1, CourtyardMaxX = 1, CourtyardMaxY = 1 }.ToFootprint();
                Assert.Equal(4, legacy.Silkscreen.Count);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
