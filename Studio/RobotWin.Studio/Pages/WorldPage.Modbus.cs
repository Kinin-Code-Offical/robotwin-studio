using System.Linq;
using System.Text;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Pages
{
    /// <summary>[modbus] The remote I/O module's panel: where it listens, who is connected, its inputs and outputs.</summary>
    public partial class WorldPage
    {
        private void ModbusStatus(WorldObject o, WorldPhysics? physics, StringBuilder sb)
        {
            var device = physics?.ModbusDevices.FirstOrDefault(d => d.Object.Id == o.Id);
            if (device == null)
            {
                var p = ParamsOf(o);
                string bind = p.TryGetValue("bind", out var b) && !string.IsNullOrWhiteSpace(b) ? b : "127.0.0.1";
                string port = p.TryGetValue("port", out var pt) && !string.IsNullOrWhiteSpace(pt) ? pt : "5020";
                string unit = p.TryGetValue("unit", out var u) && !string.IsNullOrWhiteSpace(u) ? u : "1";
                if (p.TryGetValue("bus", out var bus) && bus.Trim() == "rtu")
                {
                    // [rtu] the serial header
                    string baud = p.TryGetValue("baud", out var bd) && !string.IsNullOrWhiteSpace(bd) ? bd : "9600";
                    string format = p.TryGetValue("format", out var fm) && !string.IsNullOrWhiteSpace(fm) ? fm : "8E1";
                    sb.AppendLine($"Modbus RTU : {baud} baud {format} · adres {unit}");
                    sb.Append("Dünya çalışırken RX ve TX bacaklarından gelen isteklere yanıt verir.");
                    return;
                }

                sb.AppendLine($"Adres      : {bind}:{port} · birim {unit}");
                sb.Append("Dünya çalışırken Modbus TCP sunucusu açılır.");
                return;
            }

            if (device.Rtu is { } rtu)
            {
                sb.AppendLine($"Modbus RTU : {rtu.Line.Baud:0} baud {rtu.Line.Format} · adres {device.Unit}");
                sb.AppendLine($"Yanıt      : {rtu.Answered} · CRC hatası {rtu.CrcErrors} · bozuk karakter {rtu.CharacterErrors}");
            }
            else
            {
                var host = _session.Modbus;
                var server = host?.Servers.FirstOrDefault(s => s.Units.ContainsValue(device.Image));
                sb.AppendLine($"Adres      : {device.Bind}:{server?.Port ?? device.Port} · birim {device.Unit}" + (server == null ? " · açılamadı" : string.Empty));
                sb.AppendLine($"Bağlı      : {server?.Clients ?? 0} istemci");
            }
            string In, Out, ai;
            long requests, writes;
            lock (device.Image.Gate)
            {
                In = string.Concat(device.Image.DiscreteInputs.Select(x => x ? '1' : '0'));
                Out = string.Concat(device.Image.Coils.Select(x => x ? '1' : '0'));
                ai = string.Join(" ", device.Image.InputRegisters.Skip(1).Take(4).Select(mv => (mv / 1000.0).ToString("0.00", System.Globalization.CultureInfo.CurrentCulture)));
                requests = device.Image.Requests;
                writes = device.Image.Writes;
            }

            string ao = string.Join(" ", Enumerable.Range(0, 2).Select(i => WorldPhysics.AnalogOutputVolts(device, i)).Select(v => double.IsNaN(v) ? "—" : v.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture)));
            sb.AppendLine($"DI1–DI8    : {In}");
            sb.AppendLine($"DO1–DO8    : {Out}");
            sb.AppendLine($"AI1–AI4    : {ai} V");
            sb.AppendLine($"AO1–AO2    : {ao} V");
            sb.Append($"İstek      : {requests} · yazma {writes}");
        }
    }
}
