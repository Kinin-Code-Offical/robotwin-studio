using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Pcb;

namespace RobotWin.Studio.Pcb
{
    /// <summary>
    /// [teardrop] The board's teardrops for drawing: worked out again only when the tracks, vias, parts or the settings
    /// changed (a fingerprint per frame), since the canvas redraws on every mouse move.
    /// </summary>
    public sealed partial class BoardCanvas
    {
        private List<Teardrop> _drops = new List<Teardrop>();
        private int _dropsKey;

        private List<Teardrop> TeardropsNow(BoardDocument board)
        {
            var s = board.Teardrops;
            if (s == null || !s.Enabled) return _drops = new List<Teardrop>();
            var h = new HashCode();
            h.Add(s.LengthRatio);
            h.Add(s.MaxLength);
            h.Add(s.WidthRatio);
            h.Add(s.MaxWidth);
            h.Add(s.Curved);
            h.Add(s.OnVias);
            h.Add(s.OnPads);
            foreach (var t in board.Tracks)
            {
                h.Add(t.X1);
                h.Add(t.Y1);
                h.Add(t.X2);
                h.Add(t.Y2);
                h.Add(t.Width);
                h.Add(t.Layer);
                h.Add(t.Net);
            }

            foreach (var v in board.Vias)
            {
                h.Add(v.X);
                h.Add(v.Y);
                h.Add(v.Diameter);
                h.Add(v.Net);
            }

            foreach (var c in board.Components)
            {
                h.Add(c.X);
                h.Add(c.Y);
                h.Add(c.Rotation);
                h.Add(c.Side);
            }

            int key = h.ToHashCode();
            if (key == _dropsKey && _drops.Count >= 0 && _dropsBoard == board) return _drops;
            _dropsKey = key;
            _dropsBoard = board;
            _drops = Teardrops.Of(board);
            return _drops;
        }

        private BoardDocument? _dropsBoard;
    }
}
