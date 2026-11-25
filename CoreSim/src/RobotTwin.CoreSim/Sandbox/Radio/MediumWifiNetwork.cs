using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Firmware.Xtensa;

namespace RobotTwin.CoreSim.Sandbox.Radio
{
    /// <summary>
    /// The radio medium as one emulated ESP32's Wi-Fi sees it (hardware-compatible mode): the same node, networks,
    /// datagrams and streams the fast mode reaches through its HAL, for the firmware's own lwIP.
    /// </summary>
    public sealed class MediumWifiNetwork : IEsp32WifiNetwork
    {
        private readonly RadioMedium _m;
        private readonly Esp32RadioNode _e;
        private readonly Func<double, double> _world;

        /// <param name="toWorld">The world's time for a chip time (the board's clock offset); identity when they agree.</param>
        public MediumWifiNetwork(RadioMedium medium, Esp32RadioNode node, Func<double, double>? toWorld = null)
        {
            _m = medium;
            _e = node;
            _world = toWorld ?? (t => t);
        }

        public Esp32RadioNode Node => _e;

        public byte[] Mac => _e.Mac;

        public void Reset(double t) => _m.ResetNode(_e, _world(t));

        /// <summary>Plugs an emulated board into the medium: its Wi-Fi reaches the node, which is on while the board runs.</summary>
        public static MediumWifiNetwork Attach(RadioMedium medium, Esp32RadioNode node, Firmware.Esp32.Esp32EmulatedBoard board)
        {
            var net = new MediumWifiNetwork(medium, node, t => t + board.ChipTimeOrigin);
            board.Wifi = net;
            node.IsOn = () => board.State == Firmware.Esp32.Esp32BoardState.Running;
            return net;
        }

        public void Mode(int mode) => _m.WifiMode(_e, mode);

        public void Begin(string ssid, string password, double t) => _m.WifiBegin(_e, ssid, password, _world(t));

        public void Disconnect() => _m.WifiDisconnect(_e);

        public (int Status, int Rssi, uint Ip, uint Gateway, int Channel, byte[] Bssid) Status(double t) => _m.WifiStatus(_e, _world(t));

        public List<(string Ssid, int Rssi, int Channel, int Auth, byte[] Bssid)> Scan(double t) => _m.WifiScan(_e, _world(t));

        public bool SoftAp(string ssid, string password, int channel, int maxStations) => _m.SoftAp(_e, ssid, password, channel, maxStations);

        public void StopSoftAp() => _m.StopSoftAp(_e);

        public List<(byte[] Mac, uint Ip, int Rssi)> SoftApStations(double t) => _m.SoftApMembers(_e, _world(t));

        public void UdpBind(ushort port, bool on) => _m.UdpBind(_e, port, on);

        public int UdpSend(uint ip, ushort dstPort, ushort srcPort, byte[] data, double t) => _m.UdpSend(_e, ip, dstPort, srcPort, data, _world(t));

        public (uint Ip, ushort Port, byte[] Data)? UdpReceive(ushort port, double t)
        {
            var p = _m.UdpReceive(_e, port, _world(t));
            return p == null ? null : (p.SrcIp, p.SrcPort, p.Data);
        }

        public void StreamListen(ushort port, bool on) => _m.StreamListen(_e, port, on);

        public int StreamConnect(uint ip, ushort port, double t) => _m.StreamConnect(_e, ip, port, _world(t));

        public (int Id, uint Ip, ushort Port) StreamAccept(ushort port) => _m.StreamAccept(_e, port);

        public int StreamSend(int id, byte[] data, double t) => _m.StreamSend(_e, id, data, _world(t));

        public int StreamReceive(int id, byte[] into, double t) => _m.StreamReceive(_e, id, into, _world(t));

        public void StreamClose(int id) => _m.StreamClose(_e, id);
    }
}
