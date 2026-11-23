using System;
using System.Collections.Generic;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware;
using RobotTwin.CoreSim.Firmware.Esp32;
using RobotTwin.CoreSim.Sandbox;
using RobotTwin.CoreSim.Sandbox.Radio;
using RobotWin.Studio.Input;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [radio] Radio medium, controllers and USB cables of a running world. Kept in its own
    /// file; WorldSession.cs only calls StartRadio / StopRadio / AdvanceRadio / RadioFrame /
    /// TrackBoard / SerialRouted.
    /// </summary>
    public sealed partial class WorldSession
    {
        private readonly Dictionary<object, string> _boardObject = new Dictionary<object, string>();
        private readonly Dictionary<string, IGamepadSource> _padSources = new Dictionary<string, IGamepadSource>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The world's radio environment while simulating (null when stopped).</summary>
        public RadioMedium? Radio { get; private set; }

        /// <summary>A controller paired with an ESP32 (controller object id, ESP32 node) — the page stores the bond.</summary>
        public event Action<string, Esp32RadioNode>? GamepadPaired;

        private void StartRadio()
        {
            _boardObject.Clear();
            _padSources.Clear();
            Radio = null;
            if (World == null || Circuit == null) return;
            try
            {
                Radio = WorldRadio.Build(World, Circuit, _library);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                Log.Add(new SimLogEntry(0, "radyo", "Radyo ortamı kurulamadı: " + ex.Message, AnalogEventKind.Warning));
                return;
            }

            foreach (var r in Radio.Ir.Receivers)
            {
                int vs = r.SupplyNode, gnd = r.GroundNode;
                if (vs >= 0) r.IsPowered = () => Simulator != null && Simulator.NodeVoltage(vs) - (gnd > 0 ? Simulator.NodeVoltage(gnd) : 0) > 2.5;
            }

            int pads = Radio.Nodes.OfType<GamepadNode>().Count();
            if (Radio.Nodes.Count + Radio.Ir.Emitters.Count + Radio.Ir.Receivers.Count > 0)
            {
                Log.Add(new SimLogEntry(0, "radyo", $"Radyo ortamı: {Radio.Nodes.Count} verici/alıcı ({pads} kumanda), {Radio.Ir.Emitters.Count} IR verici, {Radio.Ir.Receivers.Count} IR alıcı, {Radio.Propagation.Obstacles.Count} engel · yol kaybı üssü {Radio.Propagation.PathLossExponent:0.0}", AnalogEventKind.StateChange));
            }
        }

        private void StopRadio()
        {
            foreach (var s in _padSources.Values)
            {
                try
                {
                    s.SetFeedback(new GamepadFeedback { Blue = 64 }, 0);
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is ObjectDisposedException)
                {
                }
            }

            _padSources.Clear();
            _boardObject.Clear();
            Radio = null;
        }

        /// <summary>Hook from the firmware attach paths: remembers which object a co-simulation belongs to and plugs ESP32s into the medium.</summary>
        private void TrackBoard(object cosim, WorldProgrammable prog)
        {
            _boardObject[cosim] = prog.ObjectId;
            if (cosim is Esp32CoSimulation esp && Radio != null && World != null && WorldRadio.NodeOf(Radio, prog, World) is { } node)
            {
                Esp32RadioBridge.Attach(Radio, node, esp);
            }
        }

        /// <summary>Whether a board's UART reaches the serial monitor (USB cable plugged, or no cable in a legacy world).</summary>
        private bool SerialRouted(string objectId) => World == null || WorldUsb.SerialConnected(World, objectId);

        private bool SerialRouted(object cosim) => !_boardObject.TryGetValue(cosim, out var id) || SerialRouted(id);

        /// <summary>Per circuit slice: IR receiver outputs and radio housekeeping.</summary>
        private void AdvanceRadio(double now, double dt)
        {
            if (Radio == null) return;
            Radio.Advance(now, dt);
        }

        /// <summary>Per rendered frame: controller inputs in, lightbar/rumble out, radio events to the log.</summary>
        private void RadioFrame()
        {
            var radio = Radio;
            if (radio == null || World == null) return;
            foreach (var pad in radio.Nodes.OfType<GamepadNode>())
            {
                var o = World.Find(pad.Id);
                if (o == null) continue;
                string want = o.Parameters.TryGetValue("source", out var s) ? s : "otomatik";
                if (!_padSources.TryGetValue(pad.Id, out var src) || !src.IsConnected || (src is VirtualGamepadSource && want != "sanal" && GamepadInputs.Connected.Count > 0) || SourceMismatch(src, want))
                {
                    src = GamepadInputs.Resolve(want);
                    _padSources[pad.Id] = src;
                }

                try
                {
                    if (!src.Read(pad.Input)) pad.Input.Clear();
                    src.SetFeedback(pad.Feedback, Time);
                    Journal?.AddPad(Time, pad.Id, pad.Input);   // [replay] the report as the world receives it
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is ObjectDisposedException || ex is DllNotFoundException)
                {
                    pad.Input.Clear();
                }
            }

            foreach (var e in radio.DrainEvents())
            {
                string name = World.Find(e.NodeId)?.Name ?? radio.Find(e.NodeId)?.Name ?? e.NodeId;
                Log.Add(new SimLogEntry(e.Time, name, e.Message, e.Warning ? AnalogEventKind.Warning : AnalogEventKind.StateChange));
                if (e.Message.StartsWith("Eşleştirildi", StringComparison.Ordinal) && radio.Find(e.NodeId) is GamepadNode p && p.Host != null)
                {
                    GamepadPaired?.Invoke(p.Id, p.Host);
                }
            }
        }

        private static bool SourceMismatch(IGamepadSource src, string want) => want switch
        {
            "sanal" => src.Kind != "virtual",
            "xinput" => src.Kind != "xinput" && GamepadInputs.Connected.Any(c => c.Kind == "xinput"),
            "dualsense" => src.Kind != "dualsense" && src.Kind != "dualshock4" && GamepadInputs.Connected.Any(c => c.Kind is "dualsense" or "dualshock4"),
            _ => false
        };

        /// <summary>Name of the input feeding a controller object right now.</summary>
        public string? GamepadSourceName(string objectId) => _padSources.TryGetValue(objectId, out var s) ? s.Name : null;

        public GamepadNode? GamepadNodeOf(string objectId) => Radio?.Find(objectId) as GamepadNode;

        /// <summary>The ESP32 radio node of a board object (free-standing or the first ESP32 on a designed board).</summary>
        public Esp32RadioNode? Esp32NodeOf(string objectId) =>
            Radio?.Nodes.OfType<Esp32RadioNode>().FirstOrDefault(n => string.Equals(n.Id, objectId, StringComparison.OrdinalIgnoreCase) || n.Id.StartsWith(objectId + "/", StringComparison.OrdinalIgnoreCase));

        /// <summary>Puts a controller into pairing mode (like holding PS + Create on a DualSense).</summary>
        public bool StartPairing(string objectId)
        {
            if (GamepadNodeOf(objectId) is not { } pad || Radio == null) return false;
            Radio.StartPairing(pad);
            Note(RobotTwin.CoreSim.Automation.InputEvent.Pairing, objectId);
            Log.Add(new SimLogEntry(Time, World?.Find(objectId)?.Name ?? objectId, "Eşleştirme modu: Bluepad32 çalıştıran bir ESP32 aranıyor (ışık çubuğu yanıp sönüyor)", AnalogEventKind.StateChange));
            return true;
        }

        public void SetGamepadPower(string objectId, bool on)
        {
            if (GamepadNodeOf(objectId) is not { } pad) return;
            pad.PoweredOn = on;
            Note(RobotTwin.CoreSim.Automation.InputEvent.GamepadPower, objectId, on ? 1 : 0);
        }

        /// <summary>Plugs / unplugs a USB cable while simulating: VBUS switch and serial routing follow the "plugged" parameter.</summary>
        public void SetUsbPlugged(WorldObject host, bool plugged)
        {
            host.Parameters["plugged"] = plugged ? "true" : "false";
            Note(RobotTwin.CoreSim.Automation.InputEvent.Usb, host.Id, plugged ? 1 : 0);
            if (Circuit == null || World == null) return;
            if (!host.Parameters.TryGetValue("target", out var target) || string.IsNullOrWhiteSpace(target)) return;
            bool switched = WorldUsb.SetPlugged(Circuit, target, plugged);
            string board = World.Find(target)?.Name ?? target;
            Log.Add(new SimLogEntry(Time, host.Name, plugged
                ? $"USB takıldı → {board}: 5 V VBUS ve seri monitör bağlı"
                : $"USB çıkarıldı → {board}: güç ve seri bağlantı kesildi", AnalogEventKind.StateChange));
            if (!switched) Log.Add(new SimLogEntry(Time, host.Name, "Bu kartın USB beslemesi kablo ile kurulmadı; değişiklik bir sonraki çalıştırmada geçerli olur.", AnalogEventKind.Warning));
        }

        /// <summary>One NEC key press from an IR remote object.</summary>
        public bool SendIr(string objectId, byte command)
        {
            if (Radio == null || World?.Find(objectId) is not { } o) return false;
            var e = Radio.Ir.Emitters.FirstOrDefault(x => x.Id == objectId);
            if (e == null) return false;
            byte address = WorldRadio.ParseByte(o.Parameters.TryGetValue("address", out var a) ? a : null);
            Radio.Ir.SendNec(e, Time + 1e-4, address, command);
            Note(RobotTwin.CoreSim.Automation.InputEvent.Ir, objectId, command);
            int seen = Radio.Ir.Receivers.Count(r => Radio.Ir.LineOfSight(e, r));
            Log.Add(new SimLogEntry(Time, o.Name, $"IR NEC adres 0x{address:X2} komut 0x{command:X2} gönderildi · görüş hattındaki alıcı: {seen}", AnalogEventKind.StateChange));
            return true;
        }
    }
}
