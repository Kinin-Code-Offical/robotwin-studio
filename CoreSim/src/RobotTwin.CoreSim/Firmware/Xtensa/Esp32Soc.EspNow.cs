using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    public sealed partial class Esp32Soc
    {
        // ================================================================ ESP-NOW
        //
        // [espnow] ESP-NOW is part of Espressif's closed Wi-Fi libraries (libespnow over libnet80211), so the emulator serves
        // its API as it does the Wi-Fi driver's: esp_now_init/deinit, the peer list (add, delete, modify, get, fetch, count,
        // up to 20), the receive and send callbacks, esp_now_send. Frames go through the world's radio medium, the same
        // model the fast mode uses (vendor action frames at 1 Mbps, unicast acknowledged and retried, broadcast never),
        // and reach the firmware's callbacks at an idle point: the receive callback with an esp_now_recv_info_t (source,
        // destination, rx_ctrl with the RSSI) and the data, the send callback with a wifi_tx_info_t and its status.

        private const uint EspNowBase = 0x3000 + 100;
        private const uint ErrEspNowNotInit = EspNowBase + 1, ErrEspNowArg = EspNowBase + 2, ErrEspNowFull = EspNowBase + 4,
            ErrEspNowNotFound = EspNowBase + 5, ErrEspNowInternal = EspNowBase + 6, ErrEspNowExist = EspNowBase + 7;
        private const int EspNowMaxPeers = 20, EspNowPeerSize = 36;

        private bool _espNowOn;
        private uint _espNowRecvCb, _espNowSendCb;
        private readonly List<byte[]> _espNowPeers = new List<byte[]>();   // each peer's esp_now_peer_info_t as given
        private readonly Queue<byte[]> _espNowSendQueue = new Queue<byte[]>();   // destinations whose outcome is pending

        private void AddEspNow(Dictionary<string, Func<XtensaCore, bool>> handlers)
        {
            _espNowOn = false;
            _espNowRecvCb = _espNowSendCb = 0;
            _espNowPeers.Clear();
            _espNowSendQueue.Clear();
            handlers["esp_now_init"] = c =>
            {
                if (!_wifiStarted || _wifiNet == null || !_wifiNet.EspNowInit(true)) return Return(c, ErrEspNowInternal);
                _espNowOn = true;
                return Return(c, EspOk);
            };
            handlers["esp_now_deinit"] = c =>
            {
                _wifiNet?.EspNowInit(false);
                _espNowOn = false;
                _espNowPeers.Clear();
                _espNowRecvCb = _espNowSendCb = 0;
                return Return(c, EspOk);
            };
            handlers["esp_now_get_version"] = c =>
            {
                if (c.Argument(0) == 0) return Return(c, ErrEspNowArg);
                Write32(c.Argument(0), 1);
                return Return(c, EspOk);
            };
            handlers["esp_now_register_recv_cb"] = c => NowOn(c, () => _espNowRecvCb = c.Argument(0));
            handlers["esp_now_unregister_recv_cb"] = c => NowOn(c, () => _espNowRecvCb = 0);
            handlers["esp_now_register_send_cb"] = c => NowOn(c, () => _espNowSendCb = c.Argument(0));
            handlers["esp_now_unregister_send_cb"] = c => NowOn(c, () => _espNowSendCb = 0);
            handlers["esp_now_add_peer"] = c =>
            {
                if (!_espNowOn) return Return(c, ErrEspNowNotInit);
                if (c.Argument(0) == 0) return Return(c, ErrEspNowArg);
                var peer = ReadBytes(c.Argument(0), EspNowPeerSize);
                var mac = peer.Take(6).ToArray();
                if (mac.All(b => b == 0xFF) == false && (mac[0] & 1) != 0) return Return(c, ErrEspNowArg);   // a group address
                if (FindPeer(mac) >= 0) return Return(c, ErrEspNowExist);
                if (_espNowPeers.Count >= EspNowMaxPeers) return Return(c, ErrEspNowFull);
                _espNowPeers.Add(peer);
                return Return(c, EspOk);
            };
            handlers["esp_now_mod_peer"] = c =>
            {
                if (!_espNowOn) return Return(c, ErrEspNowNotInit);
                if (c.Argument(0) == 0) return Return(c, ErrEspNowArg);
                var peer = ReadBytes(c.Argument(0), EspNowPeerSize);
                int i = FindPeer(peer.Take(6).ToArray());
                if (i < 0) return Return(c, ErrEspNowNotFound);
                _espNowPeers[i] = peer;
                return Return(c, EspOk);
            };
            handlers["esp_now_del_peer"] = c =>
            {
                if (!_espNowOn) return Return(c, ErrEspNowNotInit);
                if (c.Argument(0) == 0) return Return(c, ErrEspNowArg);
                int i = FindPeer(ReadBytes(c.Argument(0), 6));
                if (i < 0) return Return(c, ErrEspNowNotFound);
                _espNowPeers.RemoveAt(i);
                return Return(c, EspOk);
            };
            handlers["esp_now_is_peer_exist"] = c => Return(c, _espNowOn && c.Argument(0) != 0 && FindPeer(ReadBytes(c.Argument(0), 6)) >= 0 ? 1u : 0u);
            handlers["esp_now_get_peer"] = c =>
            {
                if (!_espNowOn) return Return(c, ErrEspNowNotInit);
                if (c.Argument(0) == 0 || c.Argument(1) == 0) return Return(c, ErrEspNowArg);
                int i = FindPeer(ReadBytes(c.Argument(0), 6));
                if (i < 0) return Return(c, ErrEspNowNotFound);
                WriteBytes(c.Argument(1), _espNowPeers[i]);
                return Return(c, EspOk);
            };
            handlers["esp_now_fetch_peer"] = c =>
            {
                // from the head (true) or after the peer passed in; the walk ends with ESP_ERR_ESPNOW_NOT_FOUND
                if (!_espNowOn) return Return(c, ErrEspNowNotInit);
                if (c.Argument(1) == 0) return Return(c, ErrEspNowArg);
                int next = (c.Argument(0) & 0xFF) != 0 ? 0 : FindPeer(ReadBytes(c.Argument(1), 6)) + 1;
                if (next <= 0 && (c.Argument(0) & 0xFF) == 0) return Return(c, ErrEspNowNotFound);
                if (next >= _espNowPeers.Count) return Return(c, ErrEspNowNotFound);
                WriteBytes(c.Argument(1), _espNowPeers[next]);
                return Return(c, EspOk);
            };
            handlers["esp_now_get_peer_num"] = c =>
            {
                if (!_espNowOn) return Return(c, ErrEspNowNotInit);
                if (c.Argument(0) == 0) return Return(c, ErrEspNowArg);
                Write32(c.Argument(0), (uint)_espNowPeers.Count);
                Write32(c.Argument(0) + 4, (uint)_espNowPeers.Count(p => p[28] != 0));
                return Return(c, EspOk);
            };
            handlers["esp_now_send"] = c =>
            {
                if (!_espNowOn) return Return(c, ErrEspNowNotInit);
                uint len = c.Argument(2);
                if (c.Argument(1) == 0 || len == 0 || len > 250) return Return(c, ErrEspNowArg);
                var data = ReadBytes(c.Argument(1), (int)len);
                // NULL: every peer in the list; otherwise one that must be in it
                var targets = new List<byte[]>();
                if (c.Argument(0) == 0) targets.AddRange(_espNowPeers.Select(p => p.Take(6).ToArray()));
                else
                {
                    var mac = ReadBytes(c.Argument(0), 6);
                    if (FindPeer(mac) < 0) return Return(c, ErrEspNowNotFound);
                    targets.Add(mac);
                }

                foreach (var mac in targets)
                {
                    if (_wifiNet == null || !_wifiNet.EspNowSend(mac, data, Seconds)) return Return(c, ErrEspNowInternal);
                    _espNowSendQueue.Enqueue(mac);
                }

                return Return(c, EspOk);
            };
        }

        private bool NowOn(XtensaCore c, Action act)
        {
            if (!_espNowOn) return Return(c, ErrEspNowNotInit);
            act();
            return Return(c, EspOk);
        }

        private int FindPeer(byte[] mac)
        {
            for (int i = 0; i < _espNowPeers.Count; i++)
                if (_espNowPeers[i].Take(6).SequenceEqual(mac))
                    return i;
            return -1;
        }

        private byte[] ReadBytes(uint at, int n)
        {
            var b = new byte[n];
            for (int i = 0; i < n; i++) b[i] = Read8(at + (uint)i);
            return b;
        }

        private void WriteBytes(uint at, byte[] b)
        {
            for (int i = 0; i < b.Length; i++) Write8(at + (uint)i, b[i]);
        }

        /// <summary>
        /// [espnow] Each Wi-Fi service: the frames that arrived to the receive callback, the sends whose outcome is known to
        /// the send callback, each through a free receive buffer of the driver's pool (freed when the callback returns).
        /// </summary>
        private void ServiceEspNow(double t)
        {
            if (!_espNowOn || _wifiNet == null || _wifiPool == 0) return;
            while (true)
            {
                int slot = Array.IndexOf(_rxBusy, false);
                if (slot < 0) return;
                var f = _wifiNet.EspNowReceive(t);
                if (f == null) break;
                if (_espNowRecvCb == 0) continue;   // no callback: the frame is dropped
                uint at = _wifiPool + (uint)(slot * RxSlotSize);
                // esp_now_recv_info_t {src, des, rx_ctrl} at +0, the addresses at +16 / +24, rx_ctrl (rssi in its first byte) at +32, the data at +96
                uint src = at + 16, des = at + 24, ctrl = at + 32, data = at + 96;
                for (int i = 0; i < 96; i++) Write8(at + (uint)i, 0);
                Write32(at, src);
                Write32(at + 4, des);
                Write32(at + 8, ctrl);
                WriteBytes(src, f.Value.Source);
                WriteBytes(des, _wifiNet.Mac);
                Write8(ctrl, (byte)(sbyte)Math.Max(-128, Math.Min(127, f.Value.Rssi)));
                WriteBytes(data, f.Value.Data);
                _rxBusy[slot] = true;
                int captured = slot;
                CallFirmware(_espNowRecvCb, _ => _rxBusy[captured] = false, at, data, (uint)f.Value.Data.Length);
            }

            while (true)
            {
                int slot = Array.IndexOf(_rxBusy, false);
                if (slot < 0) return;
                var s = _wifiNet.EspNowSendDone(t);
                if (s == null) break;
                if (_espNowSendQueue.Count > 0) _espNowSendQueue.Dequeue();
                if (_espNowSendCb == 0) continue;
                uint at = _wifiPool + (uint)(slot * RxSlotSize);
                // wifi_tx_info_t {des_addr, src_addr, ifidx, data, data_len, rate, tx_status} at +0, the addresses at +32 / +40
                uint des = at + 32, src = at + 40;
                for (int i = 0; i < 48; i++) Write8(at + (uint)i, 0);
                Write32(at, des);
                Write32(at + 4, src);
                Write32(at + 8, 0);                                  // WIFI_IF_STA
                Write32(at + 24, s.Value.Delivered ? 0u : 1u);       // tx_status
                WriteBytes(des, s.Value.Destination);
                WriteBytes(src, _wifiNet.Mac);
                _rxBusy[slot] = true;
                int captured = slot;
                CallFirmware(_espNowSendCb, _ => _rxBusy[captured] = false, at, s.Value.Delivered ? 0u : 1u);
            }
        }
    }
}
