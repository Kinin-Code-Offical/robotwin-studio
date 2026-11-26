using System;
using System.Collections.Generic;
using System.Linq;

namespace RobotTwin.CoreSim.Sandbox.Radio
{
    /// <summary>An ESP-NOW frame on its way to a board: who sent it, what, how strong, and when it arrives (simulated s).</summary>
    public sealed class EspNowFrame
    {
        public byte[] Source = Array.Empty<byte>();
        public byte[] Data = Array.Empty<byte>();
        public double Rssi;
        public double ArriveAt;
    }

    /// <summary>The outcome of one ESP-NOW send, as its send callback reports it: to whom, acknowledged or not, when.</summary>
    public sealed class EspNowSent
    {
        public byte[] Destination = Array.Empty<byte>();
        public bool Delivered;
        public double At;
    }

    public sealed partial class RadioMedium
    {
        public const int EspNowMaxData = 250;

        /// <summary>
        /// [espnow] esp_now_init / esp_now_deinit. False (ESP_ERR_ESPNOW_INTERNAL on the chip) while the board's Wi-Fi is
        /// off: ESP-NOW rides on the Wi-Fi radio, the sketch has to set WiFi.mode() first.
        /// </summary>
        public bool EspNowInit(Esp32RadioNode e, bool on)
        {
            lock (_gate)
            {
                if (on && e.WifiMode == 0) return false;
                e.EspNowOn = on;
                e.EspNowInbox.Clear();
                e.EspNowSentQueue.Clear();
                return true;
            }
        }

        /// <summary>The Wi-Fi channel a board's radio sits on: its access point's, its own soft-AP's, or 1.</summary>
        private static int EspNowChannel(Esp32RadioNode e) => e.StaNetwork?.Channel ?? e.SoftAp?.Channel ?? 1;

        private static bool Broadcast(byte[] mac) => mac.All(b => b == 0xFF);

        /// <summary>
        /// [espnow] One vendor action frame (802.11, 1 Mbps) from <paramref name="e"/> to <paramref name="destination"/>
        /// (FF:FF:FF:FF:FF:FF: every board on the channel in reach). A unicast frame is acknowledged by its receiver and sent
        /// again up to 7 times when it is not (the ESP-NOW MAC's retries): the send callback says delivered or failed; a
        /// broadcast is never acknowledged and always reports success, as on the chip. Each copy is received or lost by the
        /// world's propagation (distance, walls, fading) at its RSSI. Returns false when ESP-NOW is not on.
        /// </summary>
        public bool EspNowSend(Esp32RadioNode e, byte[] destination, byte[] data, double t)
        {
            lock (_gate)
            {
                if (!e.EspNowOn) return false;
                bool broadcast = Broadcast(destination);
                int channel = EspNowChannel(e);
                int bits = 8 * (data.Length + 43);   // MAC header, vendor element and FCS around the data
                double airtime = bits / 1e6 + 192e-6;   // 1 Mbps with its long preamble
                e.Stats.Sent(t, data.Length);
                Emit(e, RadioTech.WiFi, t, 0.05);
                bool delivered = false;
                double done = t + airtime;
                foreach (var r in _nodes.OfType<Esp32RadioNode>())
                {
                    if (r == e || !r.EspNowOn || EspNowChannel(r) != channel) continue;
                    if (!broadcast && !r.Mac.SequenceEqual(destination) && !r.ApMac.SequenceEqual(destination)) continue;
                    int tries = broadcast ? 1 : 8;
                    for (int k = 0; k < tries; k++)
                    {
                        double at = t + (k + 1) * (airtime + 50e-6);   // each try waits for its ACK (SIFS + ACK ≈ 50 µs)
                        if (!TryHop(e, r, RadioTech.WiFi, bits, at, NextSalt(), out double rssi) || rssi < RadioPropagation.SensitivityDbm(RadioTech.WiFi))
                        {
                            if (k == tries - 1) r.Stats.Lost();
                            continue;
                        }

                        r.EspNowInbox.Enqueue(new EspNowFrame { Source = (byte[])(e.SoftAp != null && e.StaNetwork == null ? e.ApMac : e.Mac).Clone(), Data = (byte[])data.Clone(), Rssi = rssi, ArriveAt = at });
                        while (r.EspNowInbox.Count > 32) r.EspNowInbox.Dequeue();   // the receive queue overflows
                        delivered = true;
                        done = Math.Max(done, at);
                        break;
                    }
                }

                e.EspNowSentQueue.Enqueue(new EspNowSent { Destination = (byte[])destination.Clone(), Delivered = broadcast || delivered, At = done });
                while (e.EspNowSentQueue.Count > 32) e.EspNowSentQueue.Dequeue();
                return true;
            }
        }

        /// <summary>[espnow] The next frame that has arrived by <paramref name="t"/>, or null.</summary>
        public EspNowFrame? EspNowReceive(Esp32RadioNode e, double t)
        {
            lock (_gate)
            {
                if (e.EspNowInbox.Count == 0 || e.EspNowInbox.Peek().ArriveAt > t) return null;
                e.Stats.Received(t);
                return e.EspNowInbox.Dequeue();
            }
        }

        /// <summary>[espnow] The next send whose outcome is known by <paramref name="t"/>, or null.</summary>
        public EspNowSent? EspNowSendDone(Esp32RadioNode e, double t)
        {
            lock (_gate)
            {
                if (e.EspNowSentQueue.Count == 0 || e.EspNowSentQueue.Peek().At > t) return null;
                return e.EspNowSentQueue.Dequeue();
            }
        }
    }
}
