using System;
using System.Globalization;

namespace RobotTwin.CoreSim.Runtime
{
    public sealed class VirtualHexImage
    {
        public byte[] Data { get; }
        public int MinAddress { get; private set; } = int.MaxValue;
        public int MaxAddress { get; private set; } = -1;

        public VirtualHexImage(int sizeBytes)
        {
            Data = new byte[sizeBytes];
        }

        public bool HasData => MaxAddress >= MinAddress;

        public void WriteByte(int address, byte value)
        {
            if (address < 0 || address >= Data.Length) return;
            Data[address] = value;
            if (address < MinAddress) MinAddress = address;
            if (address > MaxAddress) MaxAddress = address;
        }
    }

    public static class VirtualHexLoader
    {
        public static bool TryLoad(string hexText, VirtualHexImage image, out string error)
        {
            error = string.Empty;
            if (image == null)
            {
                error = "HEX image buffer is null.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(hexText))
            {
                error = "HEX text is empty.";
                return false;
            }

            int upperAddress = 0;
            string[] lines = hexText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                string line = lines[lineIndex].Trim();
                if (line.Length == 0) continue;
                if (line[0] != ':')
                {
                    error = $"HEX line {lineIndex + 1} missing ':' prefix.";
                    return false;
                }

                if (line.Length < 11)
                {
                    error = $"HEX line {lineIndex + 1} too short.";
                    return false;
                }

                if (!TryParseHexByte(line, 1, out int length)) { error = $"HEX line {lineIndex + 1} invalid length."; return false; }
                if (!TryParseHexWord(line, 3, out int address)) { error = $"HEX line {lineIndex + 1} invalid address."; return false; }
                if (!TryParseHexByte(line, 7, out int recordType)) { error = $"HEX line {lineIndex + 1} invalid record type."; return false; }

                int dataStart = 9;
                int dataLengthChars = length * 2;
                int checksumIndex = dataStart + dataLengthChars;
                if (line.Length < checksumIndex + 2)
                {
                    error = $"HEX line {lineIndex + 1} missing data/checksum.";
                    return false;
                }

                int checksum = 0;
                checksum += length;
                checksum += (address >> 8) & 0xFF;
                checksum += address & 0xFF;
                checksum += recordType;

                if (recordType == 0x00)
                {
                    for (int i = 0; i < length; i++)
                    {
                        if (!TryParseHexByte(line, dataStart + i * 2, out int dataByte))
                        {
                            error = $"HEX line {lineIndex + 1} invalid data byte.";
                            return false;
                        }
                        checksum += dataByte;
                        int absAddr = (upperAddress << 16) + address + i;
                        image.WriteByte(absAddr, (byte)dataByte);
                    }
                }
                else if (recordType == 0x04)
                {
                    if (!TryParseHexWord(line, dataStart, out int upper))
                    {
                        error = $"HEX line {lineIndex + 1} invalid extended address.";
                        return false;
                    }
                    checksum += (upper >> 8) & 0xFF;
                    checksum += upper & 0xFF;
                    upperAddress = upper;
                }
                else if (recordType == 0x01)
                {
                    // EOF
                }
                else
                {
                    for (int i = 0; i < length; i++)
                    {
                        if (!TryParseHexByte(line, dataStart + i * 2, out int dataByte))
                        {
                            error = $"HEX line {lineIndex + 1} invalid data byte.";
                            return false;
                        }
                        checksum += dataByte;
                    }
                }

                if (!TryParseHexByte(line, checksumIndex, out int readChecksum))
                {
                    error = $"HEX line {lineIndex + 1} invalid checksum.";
                    return false;
                }
                checksum += readChecksum;
                if ((checksum & 0xFF) != 0)
                {
                    error = $"HEX line {lineIndex + 1} checksum mismatch.";
                    return false;
                }

                if (recordType == 0x01)
                {
                    break;
                }
            }

            if (!image.HasData)
            {
                error = "HEX contains no data records.";
                return false;
            }

            return true;
        }

        private static bool TryParseHexByte(string line, int index, out int value)
        {
            value = 0;
            if (index + 2 > line.Length) return false;
            return int.TryParse(line.Substring(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseHexWord(string line, int index, out int value)
        {
            value = 0;
            if (index + 4 > line.Length) return false;
            return int.TryParse(line.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }
    }
}
