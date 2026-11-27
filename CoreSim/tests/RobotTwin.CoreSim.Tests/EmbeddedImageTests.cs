using System;
using System.Linq;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[image] Pictures on the sheet and on the board (KiCad's image): sized from the PNG, kept upright, saved and exchanged.</summary>
    public class EmbeddedImageTests
    {
        /// <summary>A PNG's signature and header (enough for its size), base64.</summary>
        private static string Png(int width, int height)
        {
            var bytes = new byte[33];
            new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(bytes, 0);
            bytes[16] = (byte)(width >> 24); bytes[17] = (byte)(width >> 16); bytes[18] = (byte)(width >> 8); bytes[19] = (byte)width;
            bytes[20] = (byte)(height >> 24); bytes[21] = (byte)(height >> 16); bytes[22] = (byte)(height >> 8); bytes[23] = (byte)height;
            bytes[24] = 8;
            bytes[25] = 6;
            return Convert.ToBase64String(bytes);
        }

        [Fact]
        public void ThePicturesSizeComesFromItsHeader()
        {
            Assert.True(EmbeddedImage.TrySize(Png(600, 300), out int w, out int h));
            Assert.Equal((600, 300), (w, h));
            var (mmW, mmH) = EmbeddedImage.SizeMm(Png(600, 300));
            Assert.Equal(50.8, mmW, 9);   // 600 px at 300 ppi
            Assert.Equal(25.4, mmH, 9);
            Assert.False(EmbeddedImage.TrySize("bm90IGEgcGljdHVyZSBhdCBhbGwsIGp1c3QgdGV4dA==", out _, out _));
        }

        [Fact]
        public void ASheetPictureStaysUprightIsCopiedAndGoesToKiCadAndBack()
        {
            var sch = DesignAndPcbTests.MotorDriverSchematic();
            var b = SchematicEditing.Bounds(SchematicEditing.AllItems(sch), PartLibrary.Default)!.Value;
            string data = Png(600, 300);
            var logo = new SchematicShape { Kind = SheetShapeKind.Image, ImageData = data, Points = { new GridPoint(b.MaxX + 4, b.MinY), new GridPoint(b.MaxX + 24, b.MinY + 10) } };
            sch.Shapes.Add(logo);

            SchematicEditing.Rotate(new object[] { logo }, new GridPoint(b.MaxX, b.MinY));
            Assert.Equal(20, Math.Abs(logo.Points[1].X - logo.Points[0].X), 9);   // still 20 wide and 10 high
            Assert.Equal(10, Math.Abs(logo.Points[1].Y - logo.Points[0].Y), 9);
            SchematicEditing.Mirror(new object[] { logo }, new GridPoint(b.MaxX, b.MinY), false);
            Assert.Equal(20, Math.Abs(logo.Points[1].X - logo.Points[0].X), 9);
            Assert.Contains(logo, SchematicEditing.ItemsInRect(sch, PartLibrary.Default, logo.Bounds(), false));

            var pasted = SchematicFragment.FromItems(new object[] { logo }, PartLibrary.Default, "t").PasteInto(sch, PartLibrary.Default, new GridPoint(b.MaxX + 40, b.MaxY));
            Assert.Equal(data, Assert.IsType<SchematicShape>(Assert.Single(pasted.Items)).ImageData);
            var saved = Schematic.FromJson(sch.ToJson());
            Assert.Equal(2, saved.Shapes.Count(s => s.Kind == SheetShapeKind.Image && s.ImageData == data));

            string text = KiCadExporter.ExportSchematic(sch, PartLibrary.Default, "logo");
            Assert.Contains("(image", text);
            Assert.Contains("(scale 1)", text);   // 20 grid = 50.8 mm: the picture's own 300 ppi size
            var back = KiCadSchematicImport.Import(text, PartLibrary.Default).Schematic;
            var again = back.Shapes.First(s => s.Kind == SheetShapeKind.Image);
            Assert.Equal(data, again.ImageData);
            // the importer lays the sheet out again at its own scale (as every shape): the picture keeps its proportions
            Assert.InRange(Math.Abs(again.Points[1].X - again.Points[0].X) / Math.Abs(again.Points[1].Y - again.Points[0].Y), 1.8, 2.2);
        }

        [Fact]
        public void ABoardPictureIsPickedButNotMade()
        {
            var board = BoardDocument.Synchronize(DesignAndPcbTests.MotorDriverSchematic(), PartLibrary.Default);
            board.Width = 70;
            board.Height = 50;
            var picture = new BoardGraphic { Shape = GraphicShape.Image, ImageData = Png(600, 300), Points = { new PcbPoint(10, 10), new PcbPoint(35.4, 22.7) } };
            Assert.True(picture.IsComplete);
            Assert.Empty(picture.Strokes());
            Assert.Equal(0, picture.DistanceTo(20, 15), 9);   // anywhere on it picks it
            Assert.Equal(5, picture.DistanceTo(40.4, 15), 9);
            Assert.Equal((10.0, 10.0, 35.4, 22.7), picture.Bounds());
            Assert.StartsWith("resim 25.4 × 12.7", picture.Describe().Replace(',', '.'));
            board.Graphics.Add(picture);

            Assert.DoesNotContain(GerberExporter.SilkStrokes(board, CopperLayer.Top), s => s.Stroke.X1 > 10 && s.Stroke.X1 < 35 && s.Stroke.Y1 > 10 && s.Stroke.Y1 < 22.7 && s.Stroke.X2 > 10 && s.Stroke.X2 < 35);
            string pcb = KiCadExporter.ExportPcb(board, "picture");
            Assert.Contains("(image", pcb);
            Assert.Contains("(scale 0.5)", pcb);   // 25.4 mm of a 50.8 mm picture
            Assert.Equal(picture.ImageData, board.Clone().Graphics.Single().ImageData);
        }
    }
}
