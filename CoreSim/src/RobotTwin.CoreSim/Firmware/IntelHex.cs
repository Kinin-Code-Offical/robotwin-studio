using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace RobotTwin.CoreSim.Firmware
{
    /// <summary>Intel HEX image: contiguous flash blocks.</summary>
    public static class IntelHex
    {
        public static List<(uint Address, byte[] Data)> Parse(string text)
        {
            var bytes = new SortedDictionary<uint, byte>();
            uint upper = 0;
            int lineNo = 0;
            foreach (var raw in text.Split('\n'))
            {
                lineNo++;
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line[0] != ':') throw new InvalidDataException($"HEX satır {lineNo}: ':' ile başlamıyor.");
                byte[] rec = new byte[(line.Length - 1) / 2];
                for (int i = 0; i < rec.Length; i++)
                {
                    rec[i] = byte.Parse(line.Substring(1 + 2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                }

                int count = rec[0];
                if (rec.Length < count + 5) throw new InvalidDataException($"HEX satır {lineNo}: kısa kayıt.");
                byte sum = 0;
                foreach (byte b in rec) sum += b;
                if (sum != 0) throw new InvalidDataException($"HEX satır {lineNo}: sağlama toplamı hatalı.");
                uint address = (uint)((rec[1] << 8) | rec[2]);
                switch (rec[3])
                {
                    case 0x00:
                        for (int i = 0; i < count; i++) bytes[upper + address + (uint)i] = rec[4 + i];
                        break;
                    case 0x01:
                        goto done;
                    case 0x02:
                        upper = (uint)(((rec[4] << 8) | rec[5]) << 4);
                        break;
                    case 0x04:
                        upper = (uint)(((rec[4] << 8) | rec[5]) << 16);
                        break;
                }
            }

            done:
            var blocks = new List<(uint, byte[])>();
            var current = new List<byte>();
            uint start = 0, next = 0;
            foreach (var kv in bytes)
            {
                if (current.Count > 0 && kv.Key != next)
                {
                    blocks.Add((start, current.ToArray()));
                    current.Clear();
                }

                if (current.Count == 0) start = kv.Key;
                current.Add(kv.Value);
                next = kv.Key + 1;
            }

            if (current.Count > 0) blocks.Add((start, current.ToArray()));
            return blocks;
        }
    }
}
