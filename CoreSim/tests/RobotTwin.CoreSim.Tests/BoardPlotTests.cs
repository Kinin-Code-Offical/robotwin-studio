using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[plot] KiCad's Plot to SVG and PDF, drawn by the same code that writes the Gerbers.</summary>
    public class BoardPlotTests
    {
        private static readonly BoardDocument Board = GerberCheckTests.RoutedBoard();

        [Fact]
        public void TheLayersComeInKiCadsOrder()
        {
            var ids = BoardPlot.Layers(Board).Select(l => l.Id).ToList();
            Assert.Equal(new[] { "F.Cu", "B.Cu", "F.Silkscreen", "B.Silkscreen", "F.Mask", "B.Mask", "F.Paste", "B.Paste", "User.Drawings", "Edge.Cuts" }, ids);
        }

        [Fact]
        public void ACopperLayerDrawsItsTracksAndPadsAndLeavesTheHolesOpen()
        {
            var top = BoardPlot.Layer(Board, "F.Cu")!;
            string svg = BoardPlot.Svg(Board, top, new PlotOptions { Label = false });
            Assert.Contains("viewBox=\"0 0 80 60\"", svg);   // 70 × 50 mm and 5 mm all round, 1:1
            int strokes = Regex.Matches(svg, "stroke=\"#C83434\"").Count;
            Assert.True(strokes >= Board.Tracks.Count(t => t.Layer == CopperLayer.Top), $"{strokes} strokes");
            Assert.True(Regex.Matches(svg, "fill=\"#C83434\"").Count >= Board.Pads().Count(p => p.OnLayer(CopperLayer.Top)));
            Assert.Equal(BoardPlot.Holes(Board).Count, Regex.Matches(svg, "fill=\"#FFFFFF\" stroke=\"none\"").Count);
            Assert.Contains("stroke=\"#000000\"", svg);   // the edge on it too
        }

        [Fact]
        public void BlackAndWhiteUsesBlackInkOnly()
        {
            string svg = BoardPlot.Svg(Board, BoardPlot.Layer(Board, "B.Cu")!, new PlotOptions { Color = false });
            var inks = Regex.Matches(svg, "(?:fill|stroke)=\"(#[0-9A-F]{6})\"").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.All(inks, c => Assert.True(c == "#000000" || c == "#FFFFFF", c));
        }

        [Fact]
        public void AMirroredPlotIsTheMirrorImage()
        {
            var silk = BoardPlot.Layer(Board, "F.Silkscreen")!;
            var plain = Starts(BoardPlot.Svg(Board, silk, new PlotOptions { Label = false }));
            var mirrored = Starts(BoardPlot.Svg(Board, silk, new PlotOptions { Label = false, Mirror = true }));
            Assert.Equal(plain.Count, mirrored.Count);
            Assert.True(plain.Count > 10);
            for (int i = 0; i < plain.Count; i++)
            {
                Assert.Equal(80 - plain[i].X, mirrored[i].X, 3);
                Assert.Equal(plain[i].Y, mirrored[i].Y, 3);
            }
        }

        private static System.Collections.Generic.List<(double X, double Y)> Starts(string svg) =>
            Regex.Matches(svg, "d=\"M([-0-9.]+) ([-0-9.]+)").Cast<Match>()
                 .Select(m => (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))).ToList();

        [Fact]
        public void LayersInOneSvgAreGroupsStackedAsOnTheBoard()
        {
            var layers = new[] { BoardPlot.Layer(Board, "F.Cu")!, BoardPlot.Layer(Board, "B.Cu")!, BoardPlot.Layer(Board, "F.Silkscreen")!, BoardPlot.Layer(Board, "Edge.Cuts")! };
            string svg = BoardPlot.Svg(Board, layers);
            int At(string id) => svg.IndexOf("<g id=\"" + id + "\"", StringComparison.Ordinal);
            Assert.True(At("B.Cu") > 0 && At("F.Cu") > At("B.Cu") && At("F.Silkscreen") > At("F.Cu") && At("Edge.Cuts") > At("F.Silkscreen"), "drawn from below up");
            Assert.Contains("<g id=\"label\"", svg);
        }

        [Fact]
        public void APdfHasAPageALayerAndATrueCrossReference()
        {
            var layers = new[] { BoardPlot.Layer(Board, "F.Cu")!, BoardPlot.Layer(Board, "B.Cu")!, BoardPlot.Layer(Board, "Edge.Cuts")! };
            var pdf = BoardPlot.Pdf(Board, layers, new PlotOptions { Title = "Motor sürücü" });
            string text = Encoding.GetEncoding("ISO-8859-1").GetString(pdf);
            Assert.StartsWith("%PDF-1.4", text);
            Assert.Contains("/Count 3", text);
            Assert.Contains("/MediaBox [0 0 226.772 181.417]", text);   // (70+10) × (50+10+4) mm in points
            int startxref = int.Parse(Regex.Match(text, "startxref\n(\\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.StartsWith("xref", text.Substring(startxref));
            var offsets = Regex.Matches(text.Substring(startxref), "(\\d{10}) 00000 n").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
            Assert.Equal(9, offsets.Count);   // catalog, pages, 3 × (page, contents), info
            for (int i = 0; i < offsets.Count; i++) Assert.StartsWith($"{i + 1} 0 obj", text.Substring(offsets[i]));

            // the first page's drawing: copper ink, white holes, strokes and fills
            int s = text.IndexOf("stream\n", StringComparison.Ordinal) + 7, e = text.IndexOf("\nendstream", s, StringComparison.Ordinal);
            var z = pdf.Skip(s).Take(e - s).ToArray();
            string page = Inflate(z);
            Assert.Contains("0.784 0.204 0.204 rg", page);   // #C83434
            Assert.Contains("1 1 1 rg", page);
            Assert.Contains("\nf\n", page);
            Assert.Contains(" w\n", page);
        }

        private static string Inflate(byte[] zlib)
        {
            Assert.Equal(0x78, zlib[0]);
            using var inflate = new DeflateStream(new MemoryStream(zlib, 2, zlib.Length - 6), CompressionMode.Decompress);
            using var outp = new MemoryStream();
            inflate.CopyTo(outp);
            var data = outp.ToArray();
            uint a = 1, b = 0;
            foreach (byte x in data)
            {
                a = (a + x) % 65521;
                b = (b + a) % 65521;
            }

            uint adler = (uint)(zlib[zlib.Length - 4] << 24 | zlib[zlib.Length - 3] << 16 | zlib[zlib.Length - 2] << 8 | zlib[zlib.Length - 1]);
            Assert.Equal((b << 16) | a, adler);
            return Encoding.ASCII.GetString(data);
        }

        [Fact]
        public void ZlibCarriesTheAdlerChecksum()
        {
            var z = PdfFile.Zlib(Encoding.ASCII.GetBytes("Wikipedia"));
            Assert.Equal(new byte[] { 0x11, 0xE6, 0x03, 0x98 }, z.Skip(z.Length - 4).ToArray());   // Adler-32 of "Wikipedia"
            Assert.Equal("Wikipedia", Inflate(z));
        }
    }
}
