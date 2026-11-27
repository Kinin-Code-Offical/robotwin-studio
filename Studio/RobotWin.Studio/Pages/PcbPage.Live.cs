using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using RobotTwin.CoreSim.Pcb;
using RobotWin.Studio.Pcb;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [crossprobe] The selected part's simulation values on the PCB too (the roadmap's "her yöne CrossProbe … simülasyon
    /// değerleri"): while the circuit runs, the selection panel lists what flows through the part, its temperature, the
    /// rating it is closest to and its pads' voltages (<see cref="PartLive"/>), a few times a second. Comes and goes with the
    /// simulation.
    /// </summary>
    public partial class PcbPage
    {
        private TextBlock? _liveText;
        private string? _liveRef;
        private readonly Stopwatch _pcbLiveClock = Stopwatch.StartNew();

        private void HookLive()
        {
            W.Simulation.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SimulationSession.State) && Canvas.SelectedComponent != null) UpdateSelection();
            };
            W.Simulation.Updated += (_, _) =>
            {
                if (_liveText == null || _pcbLiveClock.ElapsedMilliseconds < 200) return;
                _pcbLiveClock.Restart();
                RefreshPcbLive();
            };
        }

        /// <summary>The live lines of a selected part, under its place on the board (only while the simulation runs).</summary>
        private void LiveRows(Panel host, BoardComponent c)
        {
            _liveText = null;
            _liveRef = null;
            if (!W.Simulation.IsActive) return;
            host.Children.Add(PcbForms.Caption(Shell.Lang.T("Canlı değerler"), 4));
            _liveText = new TextBlock { Style = PcbForms.S("Text.Mono"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) };
            _liveRef = c.RefDes;
            host.Children.Add(_liveText);
            RefreshPcbLive();
        }

        private void RefreshPcbLive()
        {
            if (_liveText == null || _liveRef == null) return;
            if (Canvas.SelectedComponent is not { } c || c.RefDes != _liveRef)
            {
                _liveText = null;
                return;
            }

            var lines = PartLive.Lines(W.Simulation, c.RefDes, c.PadNets);
            _liveText.Text = lines.Count > 0 ? string.Join("\n", lines) : Shell.Lang.T("Bu parça devrede simüle edilmiyor.");
        }

        /// <summary>--snapshot: the panel built again after the run's steps (its values as they stand).</summary>
        internal void RefreshLiveForSnapshot() => UpdateSelection();
    }
}
