using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox.Radio
{
    /// <summary>Radio technologies carried by the <see cref="RadioMedium"/>.</summary>
    public enum RadioTech
    {
        /// <summary>Bluetooth Low Energy (advertising, scanning, GATT notifications), 1 Mbit/s GFSK.</summary>
        Ble,

        /// <summary>802.11b/g/n 2.4 GHz (station / access point, UDP and stream traffic).</summary>
        WiFi,

        /// <summary>Bluetooth Classic BR/EDR (HID game controllers such as DualSense / DualShock 4).</summary>
        Bluetooth,

        /// <summary>38 kHz modulated infrared (TV remotes): line of sight only, handled by <see cref="IrChannel"/>.</summary>
        Infrared
    }

    /// <summary>
    /// An attenuating box between radios: a world obstacle or a mechanical body. The box
    /// is axis aligned in its own frame, which is turned by <see cref="YawRad"/> about Z.
    /// </summary>
    public readonly struct RadioObstacle
    {
        public RadioObstacle(Vec3 center, Vec3 halfSize, double yawRad, double lossDb, bool opaqueToInfrared = true, string name = "")
        {
            Center = center;
            HalfSize = halfSize;
            YawRad = yawRad;
            LossDb = lossDb;
            OpaqueToInfrared = opaqueToInfrared;
            Name = name;
        }

        public Vec3 Center { get; }
        public Vec3 HalfSize { get; }
        public double YawRad { get; }

        /// <summary>Extra path loss when the direct ray passes through the box (2.4 GHz).</summary>
        public double LossDb { get; }

        public bool OpaqueToInfrared { get; }
        public string Name { get; }

        private Vec3 ToLocal(Vec3 p)
        {
            double c = Math.Cos(-YawRad), s = Math.Sin(-YawRad);
            var d = p - Center;
            return new Vec3(c * d.X - s * d.Y, s * d.X + c * d.Y, d.Z);
        }

        public bool Contains(Vec3 p)
        {
            var l = ToLocal(p);
            return Math.Abs(l.X) <= HalfSize.X && Math.Abs(l.Y) <= HalfSize.Y && Math.Abs(l.Z) <= HalfSize.Z;
        }

        /// <summary>True when the segment a→b passes through the box (slab test in the box frame). A segment that starts or ends inside does not count.</summary>
        public bool Blocks(Vec3 a, Vec3 b)
        {
            if (Contains(a) || Contains(b)) return false;
            var la = ToLocal(a);
            var lb = ToLocal(b);
            var d = lb - la;
            double t0 = 0, t1 = 1;
            if (!Slab(la.X, d.X, HalfSize.X, ref t0, ref t1)) return false;
            if (!Slab(la.Y, d.Y, HalfSize.Y, ref t0, ref t1)) return false;
            if (!Slab(la.Z, d.Z, HalfSize.Z, ref t0, ref t1)) return false;
            return t1 >= t0;
        }

        private static bool Slab(double origin, double dir, double half, ref double t0, ref double t1)
        {
            if (Math.Abs(dir) < 1e-12) return origin >= -half && origin <= half;
            double ta = (-half - origin) / dir, tb = (half - origin) / dir;
            if (ta > tb) (ta, tb) = (tb, ta);
            t0 = Math.Max(t0, ta);
            t1 = Math.Min(t1, tb);
            return t1 >= t0;
        }
    }

    /// <summary>
    /// 2.4 GHz propagation: free-space loss up to a reference distance, log-distance beyond
    /// it (exponent ≈ 2 open space, 2.7 home, 3 office, 3.5 cluttered), plus a fixed loss per
    /// obstacle on the direct ray and slow log-normal shadow fading that is a deterministic
    /// function of the link, the seed and time (identical runs give identical RSSI traces).
    /// </summary>
    public sealed class RadioPropagation
    {
        public const double SpeedOfLight = 299_792_458.0;

        public double FrequencyHz { get; set; } = 2.44e9;

        /// <summary>Distance up to which free-space loss applies (m).</summary>
        public double ReferenceDistance { get; set; } = 1.0;

        public double PathLossExponent { get; set; } = 3.0;

        /// <summary>Standard deviation of the log-normal shadowing (dB); 0 = no fading.</summary>
        public double ShadowingSigmaDb { get; set; } = 4.0;

        /// <summary>Time over which the fading decorrelates (s).</summary>
        public double FadingCorrelationTime { get; set; } = 0.35;

        public ulong Seed { get; set; } = 0x52574E4F49534531UL;

        /// <summary>Boxes that attenuate the direct ray (world obstacles, bodies).</summary>
        public List<RadioObstacle> Obstacles { get; } = new List<RadioObstacle>();

        /// <summary>Arena walls: a ray that leaves the arena through them loses this much (dB).</summary>
        public double PerimeterWallLossDb { get; set; } = 10.0;

        public double ArenaHalfWidth { get; set; } = double.PositiveInfinity;
        public double ArenaHalfDepth { get; set; } = double.PositiveInfinity;

        /// <summary>Effective noise floor of a receiver for the technology, including the processing gain of the modulation (dBm).</summary>
        public static double NoiseFloorDbm(RadioTech tech) => tech switch
        {
            RadioTech.Ble => -106.0,         // 1 Mbit/s GFSK: sensitivity ≈ −95 dBm (30.8 % PER, 37 byte PDU)
            RadioTech.WiFi => -107.0,        // 1 Mbit/s DSSS with spreading gain: sensitivity ≈ −98 dBm
            RadioTech.Bluetooth => -102.0,   // BR 1 Mbit/s: 0.1 % BER at ≈ −90 dBm
            _ => -100.0
        };

        /// <summary>Received level below which a receiver cannot even detect the preamble (dBm).</summary>
        public static double SensitivityDbm(RadioTech tech) => NoiseFloorDbm(tech) + 9.0;

        public static double FreeSpaceLossDb(double distance, double frequency) =>
            20.0 * Math.Log10(4.0 * Math.PI * Math.Max(1e-3, distance) * frequency / SpeedOfLight);

        /// <summary>Median path loss at a distance (dB).</summary>
        public double PathLossDb(double distance)
        {
            double d = Math.Max(0.01, distance);
            double d0 = Math.Max(0.01, ReferenceDistance);
            if (d <= d0) return FreeSpaceLossDb(d, FrequencyHz);
            return FreeSpaceLossDb(d0, FrequencyHz) + 10.0 * PathLossExponent * Math.Log10(d / d0);
        }

        /// <summary>Loss of the obstacles crossed by the direct ray a→b (dB).</summary>
        public double ObstacleLossDb(Vec3 a, Vec3 b, out int crossed)
        {
            crossed = 0;
            double loss = 0;
            foreach (var o in Obstacles)
            {
                if (!o.Blocks(a, b)) continue;
                loss += o.LossDb;
                crossed++;
            }

            bool insideA = Math.Abs(a.X) <= ArenaHalfWidth && Math.Abs(a.Y) <= ArenaHalfDepth;
            bool insideB = Math.Abs(b.X) <= ArenaHalfWidth && Math.Abs(b.Y) <= ArenaHalfDepth;
            if (insideA != insideB)
            {
                loss += PerimeterWallLossDb;
                crossed++;
            }

            return loss;
        }

        /// <summary>Shadow fading of a link at a time (dB): Gaussian, σ = <see cref="ShadowingSigmaDb"/>, smooth in time, symmetric in the two ends.</summary>
        public double FadingDb(string endA, string endB, double time)
        {
            if (ShadowingSigmaDb <= 0) return 0;
            ulong key = string.CompareOrdinal(endA, endB) <= 0
                ? RadioRandom.Hash(endA + "\u0001" + endB)
                : RadioRandom.Hash(endB + "\u0001" + endA);
            double tau = Math.Max(1e-3, FadingCorrelationTime);
            double u = Math.Max(0.0, time) / tau;
            long k = (long)Math.Floor(u);
            double f = u - k;
            double g0 = RadioRandom.Gaussian(Seed, key, (ulong)k);
            double g1 = RadioRandom.Gaussian(Seed, key, (ulong)(k + 1));
            // Smoothstep interpolation keeps the variance close to σ² between knots.
            double w = f * f * (3 - 2 * f);
            double g = (g0 * (1 - w) + g1 * w) / Math.Sqrt((1 - w) * (1 - w) + w * w);
            return ShadowingSigmaDb * g;
        }

        /// <summary>Median received power (no fading) in dBm.</summary>
        public double MeanRssiDbm(double txPowerDbm, double txGainDbi, double rxGainDbi, Vec3 tx, Vec3 rx) =>
            txPowerDbm + txGainDbi + rxGainDbi - PathLossDb((rx - tx).Length) - ObstacleLossDb(tx, rx, out _);

        /// <summary>Instantaneous received power (dBm) including shadow fading.</summary>
        public double RssiDbm(double txPowerDbm, double txGainDbi, double rxGainDbi, Vec3 tx, Vec3 rx, string txId, string rxId, double time) =>
            MeanRssiDbm(txPowerDbm, txGainDbi, rxGainDbi, tx, rx) + FadingDb(txId, rxId, time);

        /// <summary>Bit error rate of non-coherent binary FSK at an Eb/N0 (dB): ½·exp(−Eb/N0 / 2).</summary>
        public static double FskBitErrorRate(double ebN0Db)
        {
            double lin = Math.Pow(10.0, ebN0Db / 10.0);
            return Math.Min(0.5, 0.5 * Math.Exp(-lin / 2.0));
        }

        /// <summary>Probability that a packet of <paramref name="bits"/> bits is lost at a received level (dBm).</summary>
        public static double PacketErrorRate(RadioTech tech, double rssiDbm, int bits)
        {
            if (rssiDbm < SensitivityDbm(tech) - 6.0) return 1.0;
            double snr = rssiDbm - NoiseFloorDbm(tech);
            double ber = FskBitErrorRate(snr);
            double per = 1.0 - Math.Pow(1.0 - ber, Math.Max(1, bits));
            return Math.Max(0.0, Math.Min(1.0, per));
        }
    }

    /// <summary>Deterministic hash-based random numbers (SplitMix64 / FNV-1a): reproducible noise without shared generator state.</summary>
    public static class RadioRandom
    {
        public static ulong Mix(ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }

        public static ulong Hash(string text)
        {
            ulong h = 0xCBF29CE484222325UL;
            foreach (char c in text)
            {
                h ^= c;
                h *= 0x100000001B3UL;
            }

            return Mix(h);
        }

        /// <summary>Uniform in [0, 1) from (seed, a, b).</summary>
        public static double Uniform(ulong seed, ulong a, ulong b)
        {
            ulong x = Mix(seed ^ Mix(a ^ Mix(b + 0x632BE59BD9B4E019UL)));
            return (x >> 11) * (1.0 / 9007199254740992.0);
        }

        /// <summary>Standard normal from (seed, a, b) (Box–Muller).</summary>
        public static double Gaussian(ulong seed, ulong a, ulong b)
        {
            double u1 = Math.Max(1e-12, Uniform(seed, a, b * 2 + 1));
            double u2 = Uniform(seed ^ 0x5DEECE66DUL, a, b * 2 + 2);
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }
    }
}
