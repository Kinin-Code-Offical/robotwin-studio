using System.Globalization;
using RobotWin.Studio.Firmware;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [timeline] The Zaman çizelgesi tab: the running program's pins and interrupts over time, from the chip's own
    /// recorder (<see cref="RobotTwin.CoreSim.Firmware.McuTrace"/>), up to the instant it stopped or now.
    /// </summary>
    public partial class CodePage
    {
        private bool _timelineWired;

        private void ShowTimeline()
        {
            if (!_timelineWired)
            {
                _timelineWired = true;
                Timeline.ViewChanged += TimelineNoteUpdate;
            }

            var p = DebugKey is { } key ? FirmwareDebugger.ProgramFor(key) : null;
            Timeline.Show(p?.Cosim.Mcu, p?.Time() ?? 0);
        }

        private void TimelineNoteUpdate()
        {
            var p = DebugKey is { } key ? FirmwareDebugger.ProgramFor(key) : null;
            if (p == null)
            {
                TimelineNote.Text = "Kart çalışırken pinleri ve kesmeleri burada bir mantık analizörü gibi görürsünüz.";
                return;
            }

            string edge = Timeline.RightEdge.ToString("0.000000", CultureInfo.CurrentCulture);
            TimelineNote.Text = $"Pencere {TimelineView.Duration(Timeline.Window)} · sağ kenar t = {edge} s · tekerlek yakınlaştırır, sürükleme geri kaydırır, çift tık şimdiye döner";
        }

        /// <summary>[timeline] Brings the Zaman çizelgesi tab to the front (for checks and snapshots).</summary>
        internal void ShowTimelineTab()
        {
            _dock?.Reveal("timeline");
            ShowTimeline();
        }
    }
}
