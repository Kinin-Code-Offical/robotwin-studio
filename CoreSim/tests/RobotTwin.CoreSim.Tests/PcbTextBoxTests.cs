using System.Linq;
using RobotTwin.CoreSim.Pcb;
using Xunit;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>[textbox] KiCad's text box on the board: lines, wrapping to a width, a frame, and its way into KiCad.</summary>
    public class PcbTextBoxTests
    {
        [Fact]
        public void LinesWrapToTheBoxAndTheFrameIsItsSize()
        {
            var t = new BoardText { Text = "MOTOR SURUCU KARTI SURUM IKI", X = 20, Y = 10, Size = 1.5, BoxWidth = 20, Border = true };
            Assert.True(t.IsBox);
            var lines = Silk.TextBoxLines(t);
            Assert.True(lines.Count >= 2);
            Assert.All(lines, l => Assert.True(StrokeFont.Measure(l, 1.5) <= 20 - 2 * Silk.BoxMargin(t) + 1e-9));
            Assert.Equal(t.Text, string.Join(" ", lines));   // no word lost

            var strokes = Silk.TextStrokes(t);
            var (w, h) = Silk.TextBoxSize(t);
            Assert.Equal(20, w, 9);
            Assert.Equal(20 - w / 2, strokes.Min(s => System.Math.Min(s.X1, s.X2)), 6);   // the frame's left side
            Assert.Equal(10 + h / 2, strokes.Max(s => System.Math.Max(s.Y1, s.Y2)), 6);   // and its bottom
        }

        [Fact]
        public void PlainTextStaysOneCentredLineAndNewLinesMakeABox()
        {
            var plain = new BoardText { Text = "ROBOTWIN", X = 0, Y = 0, Size = 1.5 };
            Assert.False(plain.IsBox);
            var two = new BoardText { Text = "ROBOT\nWIN", X = 0, Y = 0, Size = 1.5 };
            Assert.True(two.IsBox);
            Assert.Equal(2, Silk.TextBoxLines(two).Count);
            var b = Silk.TextStrokes(two);
            double height = b.Max(s => System.Math.Max(s.Y1, s.Y2)) - b.Min(s => System.Math.Min(s.Y1, s.Y2));
            Assert.True(height > 1.5 * 1.6);   // two lines, one under the other
        }

        [Fact]
        public void ATextBoxGoesToKiCadAsOneAndIsSaved()
        {
            var board = new BoardDocument();
            board.Texts.Add(new BoardText { Text = "LOT 7\nREV B", X = 10, Y = 10, Border = true });
            board.Texts.Add(new BoardText { Text = "PLAIN", X = 30, Y = 10 });
            var text = KiCadExporter.ExportPcb(board, "t");
            Assert.Contains("(gr_text_box \"LOT 7\\nREV B\"", text);
            Assert.Contains("(border yes)", text);
            Assert.Contains("(gr_text \"PLAIN\"", text);
            var back = board.Clone();   // the board's file form read back
            Assert.True(back.Texts[0].Border);
            Assert.False(back.Texts[1].Border);
        }
    }
}
