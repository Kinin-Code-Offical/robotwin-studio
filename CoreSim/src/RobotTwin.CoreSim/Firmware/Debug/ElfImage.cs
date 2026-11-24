using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RobotTwin.CoreSim.Firmware.Debug
{
    /// <summary>A symbol of an ELF file: a function or a variable with its address and size.</summary>
    public readonly struct ElfSymbol
    {
        public ElfSymbol(string name, uint value, uint size, byte type, ushort section)
        {
            Name = name;
            Value = value;
            Size = size;
            Type = type;
            Section = section;
        }

        public string Name { get; }
        public uint Value { get; }
        public uint Size { get; }

        /// <summary>STT_* (1 = object, 2 = function).</summary>
        public byte Type { get; }

        public ushort Section { get; }
        public bool IsFunction => Type == 2;
        public bool IsObject => Type == 1;
    }

    /// <summary>
    /// [debug] The parts of a 32-bit little-endian ELF file a debugger needs (avr-gcc's output for the Uno and Nano):
    /// sections by name and the symbol table. Nothing is executed or relocated; the program itself comes from the HEX.
    /// </summary>
    public sealed class ElfImage
    {
        private readonly byte[] _data;
        private readonly Dictionary<string, (int Offset, int Size)> _sections = new Dictionary<string, (int, int)>(StringComparer.Ordinal);

        private ElfImage(byte[] data)
        {
            _data = data;
        }

        public ushort Machine { get; private set; }

        public IReadOnlyList<ElfSymbol> Symbols { get; private set; } = Array.Empty<ElfSymbol>();

        public static ElfImage Read(byte[] data)
        {
            if (data.Length < 52 || data[0] != 0x7F || data[1] != (byte)'E' || data[2] != (byte)'L' || data[3] != (byte)'F')
            {
                throw new InvalidDataException("ELF dosyası değil.");
            }

            if (data[4] != 1 || data[5] != 1) throw new InvalidDataException("Yalnız 32 bit, little-endian ELF okunur.");
            var elf = new ElfImage(data);
            elf.Machine = U16(data, 18);
            int shoff = (int)U32(data, 32);
            int shentsize = U16(data, 46), shnum = U16(data, 48), shstrndx = U16(data, 50);
            if (shoff <= 0 || shentsize < 40 || shnum == 0 || shoff + (long)shnum * shentsize > data.Length)
            {
                throw new InvalidDataException("ELF bölüm tablosu bozuk.");
            }

            var headers = new (uint Name, uint Type, int Offset, int Size, uint Link)[shnum];
            for (int i = 0; i < shnum; i++)
            {
                int h = shoff + i * shentsize;
                uint type = U32(data, h + 4);
                int offset = (int)U32(data, h + 16), size = (int)U32(data, h + 20);
                if (type == 8) size = 0;   // SHT_NOBITS (.bss): no bytes in the file
                if (offset < 0 || size < 0 || (long)offset + size > data.Length) size = 0;
                headers[i] = (U32(data, h), type, offset, size, U32(data, h + 24));
            }

            int strOffset = shstrndx < shnum ? headers[shstrndx].Offset : 0;
            for (int i = 0; i < shnum; i++)
            {
                string name = CString(data, strOffset + (int)headers[i].Name);
                if (name.Length > 0 && !elf._sections.ContainsKey(name)) elf._sections[name] = (headers[i].Offset, headers[i].Size);
            }

            // SHT_SYMTAB with its string table (sh_link)
            var symbols = new List<ElfSymbol>();
            for (int i = 0; i < shnum; i++)
            {
                if (headers[i].Type != 2 || headers[i].Link >= shnum) continue;
                int strtab = headers[(int)headers[i].Link].Offset;
                for (int s = headers[i].Offset; s + 16 <= headers[i].Offset + headers[i].Size; s += 16)
                {
                    string name = CString(data, strtab + (int)U32(data, s));
                    if (name.Length == 0) continue;
                    symbols.Add(new ElfSymbol(name, U32(data, s + 4), U32(data, s + 8), (byte)(data[s + 12] & 0xF), U16(data, s + 14)));
                }
            }

            elf.Symbols = symbols;
            return elf;
        }

        /// <summary>The bytes of a section (empty when the file has no such section).</summary>
        public ReadOnlySpan<byte> Section(string name) =>
            _sections.TryGetValue(name, out var s) ? new ReadOnlySpan<byte>(_data, s.Offset, s.Size) : ReadOnlySpan<byte>.Empty;

        public bool HasSection(string name) => _sections.TryGetValue(name, out var s) && s.Size > 0;

        internal static ushort U16(byte[] d, int o) => (ushort)(d[o] | (d[o + 1] << 8));

        internal static uint U32(byte[] d, int o) => (uint)(d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24));

        internal static string CString(byte[] d, int o)
        {
            if (o < 0 || o >= d.Length) return string.Empty;
            int end = o;
            while (end < d.Length && d[end] != 0) end++;
            return Encoding.UTF8.GetString(d, o, end - o);
        }
    }
}
