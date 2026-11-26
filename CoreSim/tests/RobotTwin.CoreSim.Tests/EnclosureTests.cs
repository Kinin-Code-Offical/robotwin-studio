using System;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Pcb;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[enclosure] The routed motor driver in a box: what touches the lid, a wall or the floor, and what goes through an opening.</summary>
    public class EnclosureTests
    {
        private readonly ITestOutputHelper _out;

        public EnclosureTests(ITestOutputHelper output) => _out = output;

        private static BoardDocument Board()
        {
            var board = BoardDocument.Synchronize(DesignAndPcbTests.MotorDriverSchematic(), PartLibrary.Default);
            board.Width = 70;
            board.Height = 50;
            AutoPlacer.Place(board);
            return board;
        }

        private static (BoardComponent C, Footprint Fp) Tallest(BoardDocument b) =>
            b.Components.Select(c => (C: c, Fp: FootprintLibrary.Get(c.FootprintId)!)).Where(x => x.Fp != null).OrderByDescending(x => x.Fp.Height).First();

        [Fact]
        public void ABoxMadeAroundTheBoardFitsIt()
        {
            var b = Board();
            b.Enclosure = BoardEnclosure.Around(b);
            var found = EnclosureCheck.Check(b, b.Enclosure);
            _out.WriteLine($"{b.Enclosure.InnerLength} × {b.Enclosure.InnerWidth} × {b.Enclosure.InnerHeight} mm, standoff {b.Enclosure.Standoff}: {string.Join("; ", found.Select(f => f.Message))}");
            Assert.Empty(found);
            Assert.DoesNotContain(DesignRuleChecker.Check(b), v => v.Rule == EnclosureCheck.Rule);
        }

        [Fact]
        public void TheTallestPartHitsALowLid()
        {
            var b = Board();
            var box = BoardEnclosure.Around(b);
            var (tall, fp) = Tallest(b);
            double room = box.InnerHeight - box.Standoff - b.Stackup.Thickness;
            box.InnerHeight -= room - fp.Height + 2;   // 2 mm too low for it
            var found = EnclosureCheck.Check(b, box);
            _out.WriteLine(string.Join("\n", found.Select(f => $"{f.Severity} {f.Message}")));
            var hit = Assert.Single(found, f => f.Severity == DrcSeverity.Error && f.Message.StartsWith(tall.RefDes + " kapağa", StringComparison.Ordinal));
            Assert.Contains("kapağa " + 2.0.ToString("0.0#", System.Globalization.CultureInfo.CurrentCulture) + " mm giriyor", hit.Message);
        }

        [Fact]
        public void APartOverTheEdgeNeedsAnOpening()
        {
            var b = Board();
            var box = BoardEnclosure.Around(b);
            var hdr = b.Components.Single(c => c.FootprintId.Contains("1x03", StringComparison.OrdinalIgnoreCase) || c.RefDes.StartsWith("J", StringComparison.Ordinal));
            var fp = FootprintLibrary.Get(hdr.FootprintId)!;
            // slide the header so its body sticks 2 mm out past the right wall
            var (_, _, maxX, _) = hdr.Courtyard(fp);
            hdr.X += box.InnerLength - box.OffsetX - maxX + 2;
            var found = EnclosureCheck.Check(b, box);
            _out.WriteLine(string.Join("\n", found.Select(f => f.Message)));
            Assert.Contains(found, f => f.Severity == DrcSeverity.Error && f.Message.StartsWith(hdr.RefDes + " sağ duvara", StringComparison.Ordinal));

            var (_, minY, _, maxY) = hdr.Courtyard(fp);
            box.Openings.Add(new EnclosureOpening { Name = "Başlık", Wall = EnclosureWall.Right, Position = (minY + maxY) / 2, Width = maxY - minY + 2, Bottom = 0, Height = box.InnerHeight });
            Assert.DoesNotContain(EnclosureCheck.Check(b, box), f => f.Message.StartsWith(hdr.RefDes + " sağ", StringComparison.Ordinal));
        }

        [Fact]
        public void LeadsUnderTheBoardNeedRoomAboveTheFloor()
        {
            var b = Board();
            var box = BoardEnclosure.Around(b);
            box.Standoff = 1;   // a board lying almost on the floor: the trimmed leads (1.5 mm) touch it
            var found = EnclosureCheck.Check(b, box);
            Assert.Contains(found, f => f.Severity == DrcSeverity.Error && f.Message.StartsWith("Delikli parçaların uçları tabana", StringComparison.Ordinal));
        }

        [Fact]
        public void TheBoxIsSavedWithTheBoard()
        {
            var b = Board();
            b.Enclosure = BoardEnclosure.Around(b);
            b.Enclosure.Openings.Add(new EnclosureOpening { Name = "USB", Wall = EnclosureWall.Left, Position = 20, Width = 12, Bottom = 6, Height = 7 });
            var json = System.Text.Json.JsonSerializer.Serialize(b, RobotTwin.CoreSim.Formats.RobotWinFormats.Json);
            var back = System.Text.Json.JsonSerializer.Deserialize<BoardDocument>(json, RobotTwin.CoreSim.Formats.RobotWinFormats.Json)!;
            Assert.NotNull(back.Enclosure);
            Assert.Equal(b.Enclosure.InnerHeight, back.Enclosure!.InnerHeight);
            var usb = Assert.Single(back.Enclosure.Openings);
            Assert.Equal(EnclosureWall.Left, usb.Wall);
            Assert.Equal(7, usb.Height);
        }
    }
}
