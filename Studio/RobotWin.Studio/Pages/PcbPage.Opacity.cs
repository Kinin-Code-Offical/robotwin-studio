using System;
using RobotWin.Studio.Shell;
using static RobotWin.Studio.Pcb.PcbForms;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [opacity] KiCad's Objects tab: how opaque tracks (with their teardrops), vias, pads and pours are drawn, each a slider
    /// with a typed % box, remembered in the settings. Only the view changes.
    /// </summary>
    public partial class PcbPage
    {
        private void OpacityRows()
        {
            LayersPanel.Children.Add(Overline("Saydamlık"));
            void Row(string label, double value, Action<double> apply)
            {
                LayersPanel.Children.Add(SliderRow(label, 10, 100, value * 100, "0", " %", v =>
                {
                    apply(v / 100);
                    AppSettings.Current.Save();
                    Canvas.InvalidateVisual();
                }));
            }

            Row("Yollar", Canvas.TrackOpacity, v => AppSettings.Current.PcbTrackOpacity = Canvas.TrackOpacity = v);
            Row("Via'lar", Canvas.ViaOpacity, v => AppSettings.Current.PcbViaOpacity = Canvas.ViaOpacity = v);
            Row("Pedler", Canvas.PadOpacity, v => AppSettings.Current.PcbPadOpacity = Canvas.PadOpacity = v);
            Row("Dolgular", Canvas.ZoneOpacity, v => AppSettings.Current.PcbZoneOpacity = Canvas.ZoneOpacity = v);
        }
    }
}
