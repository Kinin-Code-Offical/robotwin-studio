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
            Timeline.Clock = p?.Owner;   // [timebase] the scope's cursor is shared on this session's time
            Timeline.Show(p?.Cosim.Mcu, p?.Time() ?? 0);
        }

        /// <summary>
        /// --snapshot debug-timeline-scope: a scope window over the middle half of what the timeline shows and the scope's
        /// pointer in its middle, as the scope sets them; then "Osiloskopla eşle".
        /// </summary>
        internal void LinkTimelineForSnapshot()
        {
            ShowTimeline();
            if (Timeline.Clock is not { } clock) return;
            double right = Timeline.RightEdge, w = Timeline.Window;
            Shell.TimeCursor.ScopeShows(clock, right - w * 0.75, right - w * 0.25);
            OnTimelineToScope(this, new System.Windows.RoutedEventArgs());
            Shell.TimeCursor.Set(clock, new object(), right - w * 0.4);
        }

        /// <summary>[timebase] The timeline shows the scope's window (the same seconds, one time axis for both).</summary>
        private void OnTimelineToScope(object sender, System.Windows.RoutedEventArgs e)
        {
            if (Shell.TimeCursor.ScopeSpan is not { } s || !ReferenceEquals(s.Clock, Timeline.Clock))
            {
                W.Status = Shell.Lang.T("Osiloskopta bu kartın simülasyonundan bir görüntü yok: devrede osiloskobu açıp çalıştırın");
                return;
            }

            W.Status = Shell.Lang.T(Timeline.ShowSpan(s.Start, s.End)
                ? "Zaman çizelgesi osiloskobun penceresini gösteriyor: iki aletin imleci birbirinde görünür"
                : "Osiloskobun penceresi zaman çizelgesinin kaydında değil (kayıt daha yeni ya da daha eski)");
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
