using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RobotTwin.CoreSim.Pcb;

namespace RobotWin.Studio.Pcb
{
    /// <summary>
    /// [origins] KiCad's grid origin and drill/place file origin. The grid runs through the grid origin (a board whose
    /// connector sits on a 2.54 mm pitch from a hole, not from the corner); the Gerber, drill and pick-and-place files
    /// measure from the drill/place origin. Both are set from the right-click menu, at the point under the pointer
    /// (held by the magnet to a pad, a hole or a corner within reach), and drawn as KiCad draws them: the grid origin a
    /// small ring with a cross, the drill/place origin a target.
    /// </summary>
    public partial class BoardCanvas
    {
        private MenuItem OriginMenu(BoardDocument board, double x, double y)
        {
            var at = Magnet(x, y);
            void Set(string what, Action change)
            {
                BeforeEdit?.Invoke(this, EventArgs.Empty);
                change();
                Edited(what);
            }

            return Sub("Orijin", new[]
            {
                Item("Izgara orijinini buraya koy", null, () => Set("Izgara orijini taşındı", () => board.GridOrigin = at)),
                Item("Delik/yerleşim orijinini buraya koy", null, () => Set("Delik/yerleşim orijini taşındı", () => board.AuxOrigin = at)),
                Item("Izgara orijinini sıfırla", null, () => Set("Izgara orijini sıfırlandı", () => board.GridOrigin = null), board.GridOrigin != null),
                Item("Delik/yerleşim orijinini sıfırla", null, () => Set("Delik/yerleşim orijini sıfırlandı", () => board.AuxOrigin = null), board.AuxOrigin != null)
            });
        }

        private void DrawOrigins(DrawingContext dc, BoardDocument board)
        {
            if (board.GridOrigin is { } g)
            {
                var c = S(g.X, g.Y);
                var pen = P(Accent, 1.2);
                dc.DrawEllipse(null, pen, c, 4, 4);
                dc.DrawLine(pen, new Point(c.X - 7, c.Y), new Point(c.X + 7, c.Y));
                dc.DrawLine(pen, new Point(c.X, c.Y - 7), new Point(c.X, c.Y + 7));
            }

            if (board.AuxOrigin is { } a)
            {
                var c = S(a.X, a.Y);
                var pen = P(Shell.CanvasColors.PcbSilk, 1.4);
                dc.DrawEllipse(null, pen, c, 6, 6);
                dc.DrawEllipse(null, pen, c, 2.5, 2.5);
                dc.DrawLine(pen, new Point(c.X - 10, c.Y), new Point(c.X + 10, c.Y));
                dc.DrawLine(pen, new Point(c.X, c.Y - 10), new Point(c.X, c.Y + 10));
            }
        }
    }
}
