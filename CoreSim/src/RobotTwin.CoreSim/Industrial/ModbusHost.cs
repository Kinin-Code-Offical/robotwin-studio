using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Industrial
{
    /// <summary>
    /// [modbus] Puts a running world's remote I/O modules on the network: one <see cref="ModbusTcpServer"/> per listening
    /// address and port, each module under its unit id (two modules may share a port, as behind a gateway). Opened when the
    /// world starts and closed when it stops; a port that cannot be opened is reported, not fatal.
    /// </summary>
    public sealed class ModbusHost : IDisposable
    {
        private readonly List<ModbusTcpServer> _servers = new List<ModbusTcpServer>();

        public ModbusHost(IEnumerable<WorldPhysics.ModbusDevice> devices)
        {
            foreach (var group in devices.GroupBy(d => (d.Bind, d.Port)))
            {
                if (!IPAddress.TryParse(group.Key.Bind, out var address))
                {
                    Problems.Add($"{group.Key.Bind}: geçerli bir IP adresi değil.");
                    continue;
                }

                var server = new ModbusTcpServer(address, group.Key.Port);
                foreach (var d in group)
                {
                    if (server.Units.ContainsKey(d.Unit)) Problems.Add($"{d.Object.Name}: {group.Key.Port} portunda {d.Unit} birim kimliği iki kez kullanılmış.");
                    else server.Units[d.Unit] = d.Image;
                }

                try
                {
                    server.Start();
                    _servers.Add(server);
                    Listening.Add($"{group.Key.Bind}:{server.Port} ({string.Join(", ", group.Select(d => d.Object.Name + " #" + d.Unit))})");
                }
                catch (SocketException ex)
                {
                    server.Dispose();
                    Problems.Add($"{group.Key.Bind}:{group.Key.Port} açılamadı: {ex.Message}");
                }
            }
        }

        /// <summary>Where it listens ("127.0.0.1:5020 (IO1 #1)") and what could not be opened.</summary>
        public List<string> Listening { get; } = new List<string>();

        public List<string> Problems { get; } = new List<string>();

        public IReadOnlyList<ModbusTcpServer> Servers => _servers;

        public int Clients => _servers.Sum(s => s.Clients);

        public void Dispose()
        {
            foreach (var s in _servers) s.Dispose();
            _servers.Clear();
        }
    }
}
