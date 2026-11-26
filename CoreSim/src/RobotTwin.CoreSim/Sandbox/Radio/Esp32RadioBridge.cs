using System;
using System.Linq;
using RobotTwin.CoreSim.Firmware.Esp32;
using P = RobotTwin.CoreSim.Firmware.Esp32.RadioPayload;

namespace RobotTwin.CoreSim.Sandbox.Radio
{
    /// <summary>Connects one ESP32 co-simulation to the world's <see cref="RadioMedium"/> (decodes the HAL's radio requests).</summary>
    public sealed class Esp32RadioBridge : IEsp32Radio
    {
        public Esp32RadioBridge(RadioMedium medium, Esp32RadioNode node)
        {
            Medium = medium;
            Node = node;
        }

        public RadioMedium Medium { get; }
        public Esp32RadioNode Node { get; }

        /// <summary>Wires the bridge into a co-simulation: requests go to the medium, the node is on while the sketch runs.</summary>
        public static Esp32RadioBridge Attach(RadioMedium medium, Esp32RadioNode node, Esp32CoSimulation cosim)
        {
            var bridge = new Esp32RadioBridge(medium, node);
            cosim.Radio = bridge;
            node.IsOn = () => cosim.State == Esp32BoardState.Running;
            return bridge;
        }

        private static byte[] Slice(byte[] b, int at, int length) => length <= 0 ? Array.Empty<byte>() : b.Skip(at).Take(length).ToArray();

        public int Request(int op, byte[] i, int n, byte[] o, int cap, double t)
        {
            var m = Medium;
            var e = Node;
            switch (op)
            {
                case Esp32RadioOp.Reset:
                    m.ResetNode(e, t);
                    return 0;
                case Esp32RadioOp.Info:
                    if (cap >= 6) Array.Copy(e.Mac, o, 6);
                    return 6;

                // ---------------------------------------------------- Wi-Fi
                case Esp32RadioOp.WifiMode:
                    m.WifiMode(e, P.I32(i, 0));
                    return 0;
                case Esp32RadioOp.WifiBegin:
                    m.WifiBegin(e, P.Str(i, 0, 33), P.Str(i, 33, 65), t);
                    return 0;
                case Esp32RadioOp.WifiStatus:
                {
                    var s = m.WifiStatus(e, t);
                    if (cap < 26) return -2;
                    P.Put(o, 0, s.Status);
                    P.Put(o, 4, s.Rssi);
                    P.Put(o, 8, s.Ip);
                    P.Put(o, 12, s.Gateway);
                    P.Put(o, 16, s.Channel);
                    Array.Copy(s.Bssid, 0, o, 20, 6);
                    return 26;
                }

                case Esp32RadioOp.WifiDisconnect:
                    m.WifiDisconnect(e);
                    return 0;
                case Esp32RadioOp.WifiScan:
                {
                    var nets = m.WifiScan(e, t);
                    int max = Math.Max(0, (cap - 4) / Esp32RadioOp.WifiScanRecordSize);
                    int count = Math.Min(max, nets.Count);
                    P.Put(o, 0, count);
                    for (int k = 0; k < count; k++)
                    {
                        int at = 4 + k * Esp32RadioOp.WifiScanRecordSize;
                        P.PutStr(o, at, 33, nets[k].Ssid);
                        o[at + 33] = (byte)(sbyte)Math.Max(-127, Math.Min(0, nets[k].Rssi));
                        o[at + 34] = (byte)nets[k].Channel;
                        o[at + 35] = (byte)nets[k].Auth;
                        Array.Copy(nets[k].Bssid, 0, o, at + 36, 6);
                    }

                    return count;
                }

                case Esp32RadioOp.WifiSoftAp:
                    return m.SoftAp(e, P.Str(i, 0, 33), P.Str(i, 33, 65), P.I32(i, 98), P.I32(i, 102)) ? 0 : -2;
                case Esp32RadioOp.WifiSoftApStop:
                    m.StopSoftAp(e);
                    return 0;
                case Esp32RadioOp.WifiSoftApInfo:
                    if (cap >= 4) P.Put(o, 0, m.SoftApStations(e));
                    return 4;

                // ---------------------------------------------------- sockets
                case Esp32RadioOp.EspNowInit:
                    return m.EspNowInit(e, n >= 4 && P.I32(i, 0) != 0) ? 0 : -2;
                case Esp32RadioOp.EspNowSend:
                    if (n < 6) return -1;
                    return m.EspNowSend(e, Slice(i, 0, 6), Slice(i, 6, Math.Min(n - 6, RadioMedium.EspNowMaxData)), t) ? 0 : -2;
                case Esp32RadioOp.EspNowPoll:
                {
                    if (cap < 12) return 0;
                    if (m.EspNowReceive(e, t) is { } f)
                    {
                        int len = Math.Min(f.Data.Length, cap - 12);
                        o[0] = 1;
                        o[1] = 0;
                        o[2] = unchecked((byte)(sbyte)Math.Max(-128, Math.Min(0, Math.Round(f.Rssi))));
                        o[3] = 0;
                        Array.Copy(f.Source, 0, o, 4, 6);
                        P.Put16(o, 10, len);
                        Array.Copy(f.Data, 0, o, 12, len);
                        return 1;
                    }

                    if (m.EspNowSendDone(e, t) is { } s)
                    {
                        o[0] = 2;
                        o[1] = (byte)(s.Delivered ? 0 : 1);
                        o[2] = 0;
                        o[3] = 0;
                        Array.Copy(s.Destination, 0, o, 4, 6);
                        P.Put16(o, 10, 0);
                        return 1;
                    }

                    return 0;
                }

                case Esp32RadioOp.UdpBind:
                    m.UdpBind(e, P.U16(i, 0), P.U16(i, 2) != 0);
                    return 0;
                case Esp32RadioOp.UdpSend:
                    return m.UdpSend(e, P.U32(i, 0), P.U16(i, 4), P.U16(i, 6), Slice(i, 8, n - 8), t);
                case Esp32RadioOp.UdpRecv:
                {
                    var p = m.UdpReceive(e, P.U16(i, 0), t);
                    if (p == null || cap < 8) return 0;
                    int len = Math.Min(p.Data.Length, cap - 8);
                    P.Put(o, 0, p.SrcIp);
                    P.Put16(o, 4, p.SrcPort);
                    P.Put16(o, 6, len);
                    Array.Copy(p.Data, 0, o, 8, len);
                    return 8 + len;
                }

                case Esp32RadioOp.TcpListen:
                    m.StreamListen(e, P.U16(i, 0), P.U16(i, 2) != 0);
                    return 0;
                case Esp32RadioOp.TcpConnect:
                    return m.StreamConnect(e, P.U32(i, 0), P.U16(i, 4), t);
                case Esp32RadioOp.TcpAccept:
                {
                    var (id, ip, port) = m.StreamAccept(e, P.U16(i, 0));
                    if (cap >= 10)
                    {
                        P.Put(o, 0, id);
                        P.Put(o, 4, ip);
                        P.Put16(o, 8, port);
                    }

                    return id;
                }

                case Esp32RadioOp.TcpSend:
                    return m.StreamSend(e, P.I32(i, 0), Slice(i, 4, n - 4), t);
                case Esp32RadioOp.TcpRecv:
                {
                    var buffer = new byte[Math.Max(0, cap)];
                    int got = m.StreamReceive(e, P.I32(i, 0), buffer, t);
                    if (got > 0) Array.Copy(buffer, o, got);
                    return got;
                }

                case Esp32RadioOp.TcpClose:
                    m.StreamClose(e, P.I32(i, 0));
                    return 0;
                case Esp32RadioOp.TcpState:
                {
                    var s = m.StreamState(e, P.I32(i, 0), t);
                    if (cap >= 14)
                    {
                        P.Put(o, 0, s.Connected ? 1 : 0);
                        P.Put(o, 4, s.Available);
                        P.Put(o, 8, s.Ip);
                        P.Put16(o, 12, s.Port);
                    }

                    return 14;
                }

                // ---------------------------------------------------- BLE
                case Esp32RadioOp.BleInit:
                    m.BleInit(e, P.Str(i, 0, Math.Max(1, n)));
                    if (cap >= 6) Array.Copy(e.BluetoothMac, o, 6);
                    return 6;
                case Esp32RadioOp.BleAdvertise:
                {
                    int mlen = Math.Min(31, (int)i[80]);
                    m.BleAdvertise(e, P.I32(i, 0) != 0, P.I32(i, 4) / 1000.0, P.Str(i, 8, 32), P.Str(i, 40, 40), Slice(i, 81, mlen), t);
                    return 0;
                }

                case Esp32RadioOp.BleScan:
                {
                    double from = P.U64(i, 0) * 1e-9, to = P.U64(i, 8) * 1e-9;
                    var results = m.BleScan(e, from, to);
                    int max = Math.Max(0, (cap - 4) / Esp32RadioOp.BleScanRecordSize);
                    int count = Math.Min(max, results.Count);
                    P.Put(o, 0, count);
                    for (int k = 0; k < count; k++)
                    {
                        var r = results[k];
                        int at = 4 + k * Esp32RadioOp.BleScanRecordSize;
                        Array.Copy(r.Address, 0, o, at, 6);
                        o[at + 6] = (byte)(sbyte)Math.Max(-127, Math.Min(0, (int)Math.Round(r.RssiDbm)));
                        o[at + 7] = (byte)(sbyte)Math.Max(-100, Math.Min(30, r.TxPowerDbm));
                        P.PutStr(o, at + 8, 32, r.Name);
                        P.PutStr(o, at + 40, 40, r.Uuid);
                        int ml = Math.Min(31, r.Manufacturer.Length);
                        o[at + 80] = (byte)ml;
                        Array.Copy(r.Manufacturer, 0, o, at + 81, ml);
                    }

                    return count;
                }

                case Esp32RadioOp.BleAddChar:
                    return m.BleAddCharacteristic(e, P.Str(i, 0, 40), P.Str(i, 40, 40), P.U32(i, 80));
                case Esp32RadioOp.BleSetValue:
                    m.BleSetValue(e, P.I32(i, 0), Slice(i, 8, n - 8), P.I32(i, 4) != 0, t);
                    return 0;
                case Esp32RadioOp.BleConnect:
                    return m.BleConnect(e, Slice(i, 0, 6), t);
                case Esp32RadioOp.BleFindChar:
                {
                    var (h, props) = m.BleFindCharacteristic(P.I32(i, 0), P.Str(i, 4, 40), P.Str(i, 44, 40));
                    if (cap >= 8)
                    {
                        P.Put(o, 0, h);
                        P.Put(o, 4, props);
                    }

                    return h;
                }

                case Esp32RadioOp.BleSubscribe:
                    return m.BleSubscribe(P.I32(i, 0), P.I32(i, 4), P.I32(i, 8) != 0) ? 0 : -2;
                case Esp32RadioOp.BleRead:
                {
                    var v = m.BleRead(P.I32(i, 0), P.I32(i, 4));
                    if (v == null) return -2;
                    int len = Math.Min(v.Length, cap);
                    Array.Copy(v, o, len);
                    return len;
                }

                case Esp32RadioOp.BleWrite:
                    return m.BleWrite(P.I32(i, 0), P.I32(i, 4), Slice(i, 8, n - 8), t) ? 0 : -2;
                case Esp32RadioOp.BleDisconnect:
                    m.BleDisconnect(P.I32(i, 0), t);
                    return 0;
                case Esp32RadioOp.BlePoll:
                {
                    var ev = m.BlePoll(e, t);
                    if (ev == null || cap < 20) return 0;
                    int len = Math.Min(ev.Data.Length, cap - 20);
                    P.Put(o, 0, ev.Kind);
                    P.Put(o, 4, ev.Connection);
                    P.Put(o, 8, ev.Handle);
                    Array.Copy(ev.Peer, 0, o, 12, 6);
                    P.Put16(o, 18, len);
                    Array.Copy(ev.Data, 0, o, 20, len);
                    return 1;
                }

                // ---------------------------------------------------- Bluepad32
                case Esp32RadioOp.PadEnable:
                    m.PadEnable(e, P.I32(i, 0) != 0);
                    return 0;
                case Esp32RadioOp.PadPoll:
                {
                    var slots = m.PadPoll(e, t);
                    int count = Math.Min(slots.Length, cap / Esp32RadioOp.PadRecordSize);
                    for (int k = 0; k < count; k++) WritePad(o, k * Esp32RadioOp.PadRecordSize, slots[k], e, t);
                    return count;
                }

                case Esp32RadioOp.PadOutput:
                {
                    int what = P.I32(i, 4);
                    byte r = i[8], g = i[9], b = i[10], leds = i[11], weak = i[12], strong = i[13];
                    uint ms = P.U32(i, 16);
                    m.PadFeedback(e, P.I32(i, 0), f =>
                    {
                        if ((what & 1) != 0)
                        {
                            f.Red = r;
                            f.Green = g;
                            f.Blue = b;
                            f.ColorSet = true;
                        }

                        if ((what & 2) != 0) f.PlayerLeds = leds;
                        if ((what & 4) != 0)
                        {
                            f.WeakMotor = weak;
                            f.StrongMotor = strong;
                            f.RumbleUntil = t + ms / 1000.0;
                        }
                    }, t);
                    return 0;
                }

                case Esp32RadioOp.PadForget:
                    m.PadForget(e, t);
                    return 0;
                case Esp32RadioOp.PadDisconnect:
                    m.PadDisconnect(e, P.I32(i, 0), t);
                    return 0;
                default:
                    return Esp32RadioOp.NotSupported;
            }
        }

        private void WritePad(byte[] o, int at, PadSlot slot, Esp32RadioNode host, double t)
        {
            var pad = slot.Pad;
            P.Put(o, at, pad != null ? 1 : 0);
            if (pad == null)
            {
                P.Put(o, at + 64, slot.Sequence);
                return;
            }

            var s = slot.Seen;
            Array.Copy(pad.Mac, 0, o, at + 4, 6);
            o[at + 10] = (byte)pad.Model;
            o[at + 11] = s.Battery;
            P.Put(o, at + 12, s.AxisX);
            P.Put(o, at + 16, s.AxisY);
            P.Put(o, at + 20, s.AxisRX);
            P.Put(o, at + 24, s.AxisRY);
            P.Put(o, at + 28, s.Brake);
            P.Put(o, at + 32, s.Throttle);
            P.Put16(o, at + 36, s.Buttons);
            o[at + 38] = s.Dpad;
            o[at + 39] = s.MiscButtons;
            P.Put(o, at + 40, s.GyroX);
            P.Put(o, at + 44, s.GyroY);
            P.Put(o, at + 48, s.GyroZ);
            P.Put(o, at + 52, s.AccelX);
            P.Put(o, at + 56, s.AccelY);
            P.Put(o, at + 60, s.AccelZ);
            P.Put(o, at + 64, slot.Sequence);
            P.Put(o, at + 68, double.IsNaN(pad.LastRssi) ? 0 : (int)Math.Round(pad.LastRssi));
            P.PutStr(o, at + 72, 24, pad.ModelName);
        }
    }
}
