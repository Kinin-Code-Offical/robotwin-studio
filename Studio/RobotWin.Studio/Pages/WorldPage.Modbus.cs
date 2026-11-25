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
                sb.AppendLine($"Adres      : {bind}:{port} · birim {unit}");
                sb.Append("Dünya çalışırken Modbus TCP sunucusu açılır.");
                return;
            }

            var host = _session.Modbus;
            var server = host?.Servers.FirstOrDefault(s => s.Units.ContainsValue(device.Image));
            sb.AppendLine($"Adres      : {device.Bind}:{server?.Port ?? device.Port} · birim {device.Unit}" + (server == null ? " · açılamadı" : string.Empty));
            sb.AppendLine($"Bağlı      : {server?.Clients ?? 0} istemci");
            string In, Out;
            long requests, writes;
            lock (device.Image.Gate)
            {
                In = string.Concat(device.Image.DiscreteInputs.Select(x => x ? '1' : '0'));
                Out = string.Concat(device.Image.Coils.Select(x => x ? '1' : '0'));
                requests = device.Image.Requests;
                writes = device.Image.Writes;
            }

            sb.AppendLine($"DI1–DI8    : {In}");
            sb.AppendLine($"DO1–DO8    : {Out}");
            sb.Append($"İstek      : {requests} · yazma {writes}");
        }
    }
}
