using System;
using System.Linq;
using System.Windows.Media.Media3D;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [thermal] Live thermal camera on the world page (Görünüm → Isı kamerası): the parts' models are
    /// coloured by their temperature from the running thermal network; hovering a part lists every
    /// node of it (a TT motor's gearbox, can and winding; a battery; a board).
    /// </summary>
    public partial class WorldPage
    {
        public bool ThermalView { get; private set; }

        private double _thermalHigh;

        public void SetThermalView(bool on)
        {
            ThermalView = on;
            _thermalHigh = 0;
            Scene.ThermalHoverText = on ? ThermalHover : null;
            if (!on) Scene.SetThermalCamera(null, 0, 0);
            else UpdateThermalView();
        }

        private void UpdateThermalView()
        {
            if (!ThermalView || World is not { } world) return;
            double amb = world.Environment.AmbientC;
            var physics = _session.State == SimState.Stopped ? null : _session.Physics;
            double hot = amb + 5;
            if (physics != null)
                foreach (var o in world.Objects)
                    hot = Math.Max(hot, physics.TemperatureOf(o.Id));
            // The range only widens while running, as a camera on auto-range that never jumps back.
            _thermalHigh = Math.Max(_thermalHigh, hot);
            Scene.SetThermalCamera((id, _) => physics?.TemperatureOf(id) ?? amb, amb, _thermalHigh);
        }

        private string? ThermalHover(string id, Point3D local)
        {
            var o = World?.Find(id);
            if (o == null) return null;
            var physics = _session.State == SimState.Stopped ? null : _session.Physics;
            if (physics == null) return $"{o.Name}: {World!.Environment.AmbientC:0.0} °C (simülasyon durdu: ortam sıcaklığı)";
            var parts = physics.PartTemperatures(id);
            string detail = parts.Count > 1 ? "\n" + string.Join(" · ", parts.Select(p => $"{p.Label} {p.T:0.0} °C")) : string.Empty;
            double q = physics.HeatOf(id), air = physics.AirSpeedOver(id);
            string extra = (q > 0.005 ? $"\nüretilen ısı {q:0.00} W" : string.Empty) + (air > 0.3 ? $" · üstünden geçen hava {air:0.0} m/s" : string.Empty);
            return $"{o.Name}: {physics.TemperatureOf(id):0.0} °C ({physics.MaterialStateOf(id)}){detail}{extra}";
        }
    }
}
