using System.Collections.Generic;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// The world's Wi-Fi as one emulated ESP32 sees it: the networks in range, its station's association, and the
    /// datagrams and streams it exchanges with the other nodes (the radio medium, for one node). Times are the chip's
    /// seconds; the implementation maps them to the world's.
    /// </summary>
    public interface IEsp32WifiNetwork
    {
        /// <summary>The station's base MAC (the soft-AP's is one more).</summary>
        byte[] Mac { get; }

        /// <summary>The chip (re)started: the network forgets its association and its sockets.</summary>
        void Reset(double t);

        void Mode(int mode);

        void Begin(string ssid, string password, double t);

        void Disconnect();

        /// <summary>wl_status_t (3 connected, 1 no such network, 4 refused, 6/0 trying), RSSI, the station's IP and the gateway's (little-endian as on the wire), channel, BSSID.</summary>
        (int Status, int Rssi, uint Ip, uint Gateway, int Channel, byte[] Bssid) Status(double t);

        List<(string Ssid, int Rssi, int Channel, int Auth, byte[] Bssid)> Scan(double t);

        /// <summary>Opens the chip's own access point (192.168.4.1, the stations numbered from .2).</summary>
        bool SoftAp(string ssid, string password, int channel, int maxStations);

        void StopSoftAp();

        /// <summary>The stations on the chip's access point: MAC, address, signal.</summary>
        List<(byte[] Mac, uint Ip, int Rssi)> SoftApStations(double t);

        void UdpBind(ushort port, bool on);

        int UdpSend(uint ip, ushort dstPort, ushort srcPort, byte[] data, double t);

        (uint Ip, ushort Port, byte[] Data)? UdpReceive(ushort port, double t);

        void StreamListen(ushort port, bool on);

        /// <summary>A stream to a listening node: its id (&gt; 0), −2 unreachable, −3 refused.</summary>
        int StreamConnect(uint ip, ushort port, double t);

        (int Id, uint Ip, ushort Port) StreamAccept(ushort port);

        int StreamSend(int id, byte[] data, double t);

        /// <summary>Arrived bytes: count, 0 nothing yet, −1 closed and drained.</summary>
        int StreamReceive(int id, byte[] into, double t);

        void StreamClose(int id);

        /// <summary>[espnow] ESP-NOW on or off; false while the Wi-Fi is off (it rides on the Wi-Fi radio).</summary>
        bool EspNowInit(bool on) => false;

        /// <summary>[espnow] One frame to a MAC (FF:FF:FF:FF:FF:FF: everyone on the channel); false when ESP-NOW is off.</summary>
        bool EspNowSend(byte[] destination, byte[] data, double t) => false;

        /// <summary>[espnow] The next frame that has arrived by now: sender, data, signal.</summary>
        (byte[] Source, byte[] Data, int Rssi)? EspNowReceive(double t) => null;

        /// <summary>[espnow] The next send whose outcome is known by now: to whom, acknowledged or not.</summary>
        (byte[] Destination, bool Delivered)? EspNowSendDone(double t) => null;
    }
}
