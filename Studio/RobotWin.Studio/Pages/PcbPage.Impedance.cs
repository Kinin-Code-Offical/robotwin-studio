using System;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using RobotTwin.CoreSim.Pcb;
using static RobotWin.Studio.Pcb.PcbForms;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [impedance] Controlled impedance on the net classes and the differential pairs (CoreSim Pcb/Impedance.cs): what
    /// the class's width gives on the outer and inner layers of this stackup, a target, and the width (a pair: the gap)
    /// that meets it. DRC then holds every track of the class to the target on the layer it runs.
    /// </summary>
    public partial class PcbPage
    {
        private void ClassImpedance(BoardDocument board, NetClass c, StackPanel body)
        {
            body.Children.Add(Row("Hedef empedans (Ω)", Number(c.Impedance, v => Edit(v > 0 ? $"{c.Name} hedef empedansı {Fmt(v)} Ω" : $"{c.Name} empedans hedefi kaldırıldı", () => c.Impedance = v), 0, 500, allowZero: true), 80,
                tip: Shell.Lang.T("0: hedef yok. Verilirse DRC bu sınıfın her yolunu bulunduğu katmanda bu değere (±%10) göre denetler")));
            var inner = board.CopperLayers().FirstOrDefault(l => !PcbLayers.IsOuter(l));
            bool hasInner = board.CopperLayers().Count > 2;
            double outerZ = Impedance.OfTrack(board, CopperLayer.Top, c.TrackWidth).Z0;
            string now = hasInner
                ? Shell.Lang.T($"{Fmt(c.TrackWidth)} mm: dış katmanda ≈ {outerZ:0} Ω, iç katmanda ≈ {Impedance.OfTrack(board, inner, c.TrackWidth).Z0:0} Ω")
                : Shell.Lang.T($"{Fmt(c.TrackWidth)} mm: ≈ {outerZ:0} Ω ({board.Stackup.Thickness:0.##} mm {board.Stackup.Dielectric} üstünde)");
            if (c.Impedance <= 0)
            {
                body.Children.Add(Caption(now, 0));
                return;
            }

            double outerW = Impedance.WidthFor(board, CopperLayer.Top, c.Impedance);
            string need = hasInner
                ? Shell.Lang.T($"{c.Impedance:0} Ω için dışta {outerW:0.###} mm, içte {Impedance.WidthFor(board, inner, c.Impedance):0.###} mm")
                : Shell.Lang.T($"{c.Impedance:0} Ω için {outerW:0.###} mm");
            bool off = Math.Abs(outerZ - c.Impedance) > Impedance.Tolerance * c.Impedance;
            var line = Caption(now + " · " + need, 0);
            if (off) line.Foreground = B("B.Warning");
            body.Children.Add(line);
            if (off && outerW >= board.Rules.MinTrackWidth)
            {
                body.Children.Add(Buttons(Button("Genişliği hedefe ayarla", (_, _) =>
                    Edit($"{c.Name} yol genişliği {outerW:0.###} mm ({c.Impedance:0} Ω)", () => c.TrackWidth = Math.Round(outerW, 3)),
                    tip: "Sınıfın yol genişliğini dış katmanda hedef empedansı veren değere getirir; iç katmanlarda farklı genişlik gerekir")));
            }
        }

        private void PairImpedance(BoardDocument board, DiffPair pair, StackPanel body)
        {
            body.Children.Add(Row("Fark empedansı (Ω)", Number(pair.Impedance, v => Edit(v > 0 ? $"{pair} fark empedansı {Fmt(v)} Ω" : $"{pair} empedans hedefi kaldırıldı", () => pair.Impedance = v), 0, 500, allowZero: true), 80,
                tip: Shell.Lang.T("0: hedef yok. USB 90 Ω, Ethernet ve LVDS 100 Ω, CAN 120 Ω")));
            double width = board.ClassFor(pair.Positive).TrackWidth;
            var lineZ = Impedance.OfTrack(board, CopperLayer.Top, width);
            double zd = Impedance.Differential(lineZ.Z0, pair.Gap, lineZ);
            string now = Shell.Lang.T($"{Fmt(width)} mm yol, {Fmt(pair.Gap)} mm aralık: dış katmanda ≈ {zd:0} Ω fark");
            if (pair.Impedance <= 0)
            {
                body.Children.Add(Caption(now, 0));
                return;
            }

            bool off = Math.Abs(zd - pair.Impedance) > Impedance.Tolerance * pair.Impedance;
            double gap = Impedance.GapFor(board, CopperLayer.Top, width, pair.Impedance);
            var text = Caption(now, 0);
            if (off) text.Foreground = B("B.Warning");
            body.Children.Add(text);
            if (!off) return;
            if (double.IsNaN(gap) || gap < board.ClassFor(pair.Positive).Clearance)
            {
                double w = Impedance.PairWidthFor(board, CopperLayer.Top, Math.Max(pair.Gap, board.ClassFor(pair.Positive).Clearance), pair.Impedance);
                body.Children.Add(Caption(Shell.Lang.T($"Bu yol genişliğiyle aralık yetmez: {Math.Max(pair.Gap, board.ClassFor(pair.Positive).Clearance):0.###} mm aralıkta {w:0.###} mm yol gerekir (hat sınıfında)"), 0));
                return;
            }

            body.Children.Add(Buttons(Button("Aralığı hedefe ayarla", (_, _) =>
                Edit($"{pair} aralığı {gap:0.###} mm ({pair.Impedance:0} Ω)", () => pair.Gap = Math.Round(gap, 3)),
                tip: "Çiftin aralığını dış katmanda hedef fark empedansını veren değere getirir")));
        }
    }
}
