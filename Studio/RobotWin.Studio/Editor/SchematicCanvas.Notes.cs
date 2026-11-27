using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using RobotTwin.CoreSim.Design;

namespace RobotWin.Studio.Editor
{
    /// <summary>
    /// [notes] Texts and text boxes on the sheet (KiCad's text and text box): drawn in the sheet's ink, picked by their
    /// area, moved, copied and deleted like labels; the text tool (T) asks for the words where it is clicked, a double
    /// click edits them.
    /// </summary>
    public sealed partial class SchematicCanvas
    {
        /// <summary>Letter height on screen for a note of size 1 (grid units to pixels).</summary>
        private double NotePixels(SchematicNote n) => Math.Max(4, n.Size * _scale * 0.9);

        private FormattedText NoteText(SchematicNote n, Color color) =>
            new FormattedText(n.Text.Length == 0 ? " " : n.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                n.Bold ? Typefaces.UiSemi : Typefaces.Ui, NotePixels(n), SymbolRenderer.Brush(color), Dpi);

        private Rect NoteRect(SchematicNote n)
        {
            var ft = NoteText(n, TextColor);
            var at = ToScreen(n.Position);
            double pad = n.Boxed ? 0.5 * _scale : 0;
            return new Rect(at.X, at.Y, ft.WidthIncludingTrailingWhitespace + 2 * pad, ft.Height + 2 * pad);
        }

        private void DrawNotes(DrawingContext dc)
        {
            foreach (var n in SheetNotes)   // [sheets]
            {
                bool selected = _selection.Contains(n);
                var color = selected ? Accent : TextColor;
                var r = NoteRect(n);
                double pad = n.Boxed ? 0.5 * _scale : 0;
                if (n.Boxed) dc.DrawRectangle(null, MakePen(color, 1.2), r);
                else if (selected) dc.DrawRectangle(null, MakePen(Accent, 1), Rect.Inflate(r, 3, 2));
                dc.DrawText(NoteText(n, color), new Point(r.X + pad, r.Y + pad));
            }
        }

        private SchematicNote? HitNote(Point screen) => SheetNotes.Reverse().FirstOrDefault(n => Rect.Inflate(NoteRect(n), 3, 3).Contains(screen));
    }
}
