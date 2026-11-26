using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>[enclosure] A wall of the box, as the board is seen from above in the editor (Y grows downwards).</summary>
    public enum EnclosureWall
    {
        Left,     // x−
        Right,    // x+
        Top,      // y−, the edge at the top of the screen
        Bottom,   // y+
    }

    /// <summary>[enclosure] A cut-out in a wall a connector passes through (a USB socket, a DC jack, a header).</summary>
    public sealed class EnclosureOpening
    {
        public string Name { get; set; } = string.Empty;
        public EnclosureWall Wall { get; set; }

        /// <summary>Centre along the wall, in board coordinates (x for the top and bottom walls, y for the side walls), mm.</summary>
        public double Position { get; set; }

        public double Width { get; set; } = 10;

        /// <summary>Lower edge of the cut-out above the floor, mm.</summary>
        public double Bottom { get; set; }

        public double Height { get; set; } = 8;
    }

    /// <summary>
    /// [enclosure] The box the board is built into, measured on the inside: its length along the board's X, width along Y,
    /// height from the floor to the lid, where the board's corner sits inside it, and how high the standoffs hold it. The
    /// check holds every part's body (its courtyard to its height, under the board for bottom-side parts) and the
    /// through-hole leads under the board against the walls, the floor and the lid.
    /// </summary>
    public sealed class BoardEnclosure
    {
        public double InnerLength { get; set; } = 80;
        public double InnerWidth { get; set; } = 60;
        public double InnerHeight { get; set; } = 30;

        /// <summary>Distance from the inner left wall to the board's x = 0, mm.</summary>
        public double OffsetX { get; set; } = 2;

        /// <summary>Distance from the inner top wall to the board's y = 0, mm.</summary>
        public double OffsetY { get; set; } = 2;

        /// <summary>Floor to the board's underside, mm.</summary>
        public double Standoff { get; set; } = 5;

        /// <summary>How far trimmed through-hole leads stand under the board, mm.</summary>
        public double LeadLength { get; set; } = 1.5;

        /// <summary>Gap under which a body near a wall or the lid is warned about (assembly tolerance), mm.</summary>
        public double Margin { get; set; } = 0.5;

        public List<EnclosureOpening> Openings { get; set; } = new List<EnclosureOpening>();

        /// <summary>A box the board and its parts fit with a few millimetres all round.</summary>
        public static BoardEnclosure Around(BoardDocument board)
        {
            double tallest = 0, deepest = 0;
            foreach (var c in board.Components)
            {
                var fp = FootprintLibrary.Get(c.FootprintId);
                if (fp == null) continue;
                if (c.Side == CopperLayer.Top) tallest = Math.Max(tallest, fp.Height);
                else deepest = Math.Max(deepest, fp.Height);
            }

            var e = new BoardEnclosure { OffsetX = 3, OffsetY = 3, InnerLength = board.Width + 6, InnerWidth = board.Height + 6 };
            e.Standoff = Math.Max(5, Math.Ceiling(Math.Max(deepest, e.LeadLength) + 2));
            e.InnerHeight = Math.Ceiling(e.Standoff + board.Stackup.Thickness + tallest + 3);
            return e;
        }
    }

    /// <summary>[enclosure] The board in its box: what touches or goes through a wall, the floor or the lid where there is no opening.</summary>
    public static class EnclosureCheck
    {
        public const string Rule = "Kutu";

        public static List<DrcViolation> Check(BoardDocument board, BoardEnclosure box)
        {
            var v = new List<DrcViolation>();
            double x0 = -box.OffsetX, x1 = box.InnerLength - box.OffsetX, y0 = -box.OffsetY, y1 = box.InnerWidth - box.OffsetY;
            double zBottom = box.Standoff, zTop = box.Standoff + board.Stackup.Thickness;

            // The board itself
            void Fits(string what, double over, double x, double y)
            {
                if (over > 1e-6) v.Add(new DrcViolation(DrcSeverity.Error, Rule, $"Kart {what} {over:0.0#} mm taşıyor", x, y));
            }

            Fits("sol duvardan", x0 - 0, 0, board.Height / 2);
            Fits("sağ duvardan", board.Width - x1, board.Width, board.Height / 2);
            Fits("üst duvardan", y0 - 0, board.Width / 2, 0);
            Fits("alt duvardan", board.Height - y1, board.Width / 2, board.Height);
            if (zTop > box.InnerHeight) v.Add(new DrcViolation(DrcSeverity.Error, Rule, $"Kart kapağa değiyor: ayaklar {box.Standoff:0.#} mm, kutu {box.InnerHeight:0.#} mm", board.Width / 2, board.Height / 2));

            foreach (var c in board.Components)
            {
                var fp = FootprintLibrary.Get(c.FootprintId);
                if (fp == null) continue;
                var (minX, minY, maxX, maxY) = c.Courtyard(fp);
                double h = Math.Max(0, fp.Height);
                double bz0 = c.Side == CopperLayer.Top ? zTop : zBottom - h, bz1 = c.Side == CopperLayer.Top ? zTop + h : zBottom;
                double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;

                // the walls: how far past each, unless an opening there takes the body's cross-section
                void Wall(EnclosureWall wall, double past, double along0, double along1, string name)
                {
                    if (past <= -box.Margin) return;
                    bool through = box.Openings.Any(o => o.Wall == wall
                        && along0 >= o.Position - o.Width / 2 - 1e-6 && along1 <= o.Position + o.Width / 2 + 1e-6
                        && bz0 >= o.Bottom - 1e-6 && bz1 <= o.Bottom + o.Height + 1e-6);
                    if (through) return;
                    if (past > 0) v.Add(new DrcViolation(DrcSeverity.Error, Rule, $"{c.RefDes} {name} duvara {past:0.0#} mm giriyor" + (box.Openings.Any(o => o.Wall == wall) ? " (oradaki açıklık gövdesini almıyor)" : string.Empty), cx, cy));
                    else v.Add(new DrcViolation(DrcSeverity.Warning, Rule, $"{c.RefDes} {name} duvara {-past:0.0#} mm kalıyor (pay {box.Margin:0.#} mm)", cx, cy));
                }

                Wall(EnclosureWall.Left, x0 - minX, minY, maxY, "sol");
                Wall(EnclosureWall.Right, maxX - x1, minY, maxY, "sağ");
                Wall(EnclosureWall.Top, y0 - minY, minX, maxX, "üst");
                Wall(EnclosureWall.Bottom, maxY - y1, minX, maxX, "alt");

                if (bz1 > box.InnerHeight - box.Margin)
                {
                    double over = bz1 - box.InnerHeight;
                    v.Add(over > 0
                        ? new DrcViolation(DrcSeverity.Error, Rule, $"{c.RefDes} kapağa {over:0.0#} mm giriyor (gövdesi {h:0.#} mm, kartın üstünde {box.InnerHeight - zTop:0.#} mm var)", cx, cy)
                        : new DrcViolation(DrcSeverity.Warning, Rule, $"{c.RefDes} kapağa {-over:0.0#} mm kalıyor (pay {box.Margin:0.#} mm)", cx, cy));
                }

                if (bz0 < box.Margin)
                {
                    v.Add(bz0 < 0
                        ? new DrcViolation(DrcSeverity.Error, Rule, $"{c.RefDes} tabana {-bz0:0.0#} mm giriyor (alt yüzde, gövdesi {h:0.#} mm, ayaklar {box.Standoff:0.#} mm)", cx, cy)
                        : new DrcViolation(DrcSeverity.Warning, Rule, $"{c.RefDes} tabana {bz0:0.0#} mm kalıyor (pay {box.Margin:0.#} mm)", cx, cy));
                }
            }

            // Through-hole leads stand out on the far side of the board from their part
            double clearance = box.Standoff - box.LeadLength;
            if (clearance < 0)
            {
                var leads = board.Pads().Where(p => p.Pad.IsThroughHole && !p.Pad.NonPlated && p.Component.Side == CopperLayer.Top).ToList();
                if (leads.Count > 0)
                {
                    var first = leads[0];
                    v.Add(new DrcViolation(DrcSeverity.Error, Rule,
                        $"Delikli parçaların uçları tabana değiyor: kartın altında {box.LeadLength:0.#} mm uç, tabana {box.Standoff:0.#} mm var ({leads.Select(p => p.Component.RefDes).Distinct().Count()} parça)", first.X, first.Y));
                }
            }

            double above = box.InnerHeight - zTop - box.LeadLength;
            if (above < 0)
            {
                var leads = board.Pads().Where(p => p.Pad.IsThroughHole && !p.Pad.NonPlated && p.Component.Side == CopperLayer.Bottom).ToList();
                if (leads.Count > 0)
                    v.Add(new DrcViolation(DrcSeverity.Error, Rule, $"Alt yüzdeki delikli parçaların uçları kapağa değiyor ({-above:0.0#} mm)", leads[0].X, leads[0].Y));
            }

            return v;
        }
    }
}
