using System;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    /// <summary>
    /// [crypto] The ESP32's SHA and AES accelerators as IDF's mbedTLS port drives them (TRM ch. 22–23).
    /// SHA (0x3FF03000): the message block in SHA_TEXT (16 words for SHA-1/256, 32 for SHA-384/512, each the message's
    /// big-endian word, as sha_ll_fill_text_block swaps them in), then a type's START (a new hash from its initial
    /// value) or CONTINUE (on from the state), LOAD puts the state into SHA_TEXT (a 64-bit word as its high, then its low
    /// half) and BUSY reads 0: the engine is done at once. AES (0x3FF01000): the key in AES_KEY (bytes in memory order),
    /// MODE (0–2 encrypt with a 128/192/256-bit key, 4–6 decrypt), a block in AES_TEXT, START transforms it in place,
    /// IDLE reads 1.
    /// </summary>
    public sealed partial class Esp32Soc
    {
        private const uint ShaBase = 0x3FF03000, AesBase = 0x3FF01000;
        private readonly uint[] _sha1 = new uint[5];
        private readonly uint[] _sha256 = new uint[8];
        private readonly ulong[] _sha384 = new ulong[8], _sha512 = new ulong[8];

        private bool WriteCryptoRegister(uint register, uint value)
        {
            if (register >= ShaBase + 0x80 && register < ShaBase + 0xC0)
            {
                int type = (int)((register - ShaBase - 0x80) / 0x10), op = (int)((register - ShaBase - 0x80) % 0x10);
                if (op == 0x0C) return true;   // BUSY is read-only
                if ((value & 1) == 0) return true;
                switch (op)
                {
                    case 0x0: ShaRun(type, start: true); break;
                    case 0x4: ShaRun(type, start: false); break;
                    case 0x8: ShaLoad(type); break;
                }

                _registers[ShaBase + 0x80 + (uint)type * 0x10 + 0x0C] = 0;   // done
                return true;
            }

            if (register == AesBase && (value & 1) != 0)
            {
                AesRun();
                _registers[AesBase + 0x04] = 1;   // AES_IDLE
                return true;
            }

            return false;
        }

        private uint ShaText(int i) => Reg(ShaBase + 4 * (uint)i);

        private void ShaRun(int type, bool start)
        {
            switch (type)
            {
                case 0:
                    if (start) Array.Copy(new uint[] { 0x67452301, 0xEFCDAB89, 0x98BADCFE, 0x10325476, 0xC3D2E1F0 }, _sha1, 5);
                    Sha1Block(_sha1);
                    break;
                case 1:
                    if (start) Array.Copy(new uint[] { 0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19 }, _sha256, 8);
                    Sha256Block(_sha256);
                    break;
                case 2:
                    if (start) Array.Copy(new ulong[] { 0xcbbb9d5dc1059ed8, 0x629a292a367cd507, 0x9159015a3070dd17, 0x152fecd8f70e5939, 0x67332667ffc00b31, 0x8eb44a8768581511, 0xdb0c2e0d64f98fa7, 0x47b5481dbefa4fa4 }, _sha384, 8);
                    Sha512Block(_sha384);
                    break;
                default:
                    if (start) Array.Copy(new ulong[] { 0x6a09e667f3bcc908, 0xbb67ae8584caa73b, 0x3c6ef372fe94f82b, 0xa54ff53a5f1d36f1, 0x510e527fade682d1, 0x9b05688c2b3e6c1f, 0x1f83d9abfb41bd6b, 0x5be0cd19137e2179 }, _sha512, 8);
                    Sha512Block(_sha512);
                    break;
            }
        }

        private void ShaLoad(int type)
        {
            uint at = ShaBase;
            switch (type)
            {
                case 0:
                    for (int i = 0; i < 5; i++) _registers[at + 4 * (uint)i] = _sha1[i];
                    break;
                case 1:
                    for (int i = 0; i < 8; i++) _registers[at + 4 * (uint)i] = _sha256[i];
                    break;
                default:
                    var s = type == 2 ? _sha384 : _sha512;
                    for (int i = 0; i < 8; i++)
                    {
                        _registers[at + 8 * (uint)i] = (uint)(s[i] >> 32);
                        _registers[at + 8 * (uint)i + 4] = (uint)s[i];
                    }

                    break;
            }
        }

        private static uint Rol(uint x, int n) => x << n | x >> (32 - n);
        private static uint Ror(uint x, int n) => x >> n | x << (32 - n);
        private static ulong Ror64(ulong x, int n) => x >> n | x << (64 - n);

        private void Sha1Block(uint[] h)
        {
            var w = new uint[80];
            for (int i = 0; i < 16; i++) w[i] = ShaText(i);
            for (int i = 16; i < 80; i++) w[i] = Rol(w[i - 3] ^ w[i - 8] ^ w[i - 14] ^ w[i - 16], 1);
            uint a = h[0], b = h[1], c = h[2], d = h[3], e = h[4];
            for (int i = 0; i < 80; i++)
            {
                uint f, k;
                if (i < 20) { f = (b & c) | (~b & d); k = 0x5A827999; }
                else if (i < 40) { f = b ^ c ^ d; k = 0x6ED9EBA1; }
                else if (i < 60) { f = (b & c) | (b & d) | (c & d); k = 0x8F1BBCDC; }
                else { f = b ^ c ^ d; k = 0xCA62C1D6; }
                uint t = unchecked(Rol(a, 5) + f + e + k + w[i]);
                e = d;
                d = c;
                c = Rol(b, 30);
                b = a;
                a = t;
            }

            unchecked
            {
                h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e;
            }
        }

        private static readonly uint[] K256 =
        {
            0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
            0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
            0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
            0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
            0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
            0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
        };

        private void Sha256Block(uint[] h)
        {
            var w = new uint[64];
            for (int i = 0; i < 16; i++) w[i] = ShaText(i);
            unchecked
            {
                for (int i = 16; i < 64; i++)
                {
                    uint s0 = Ror(w[i - 15], 7) ^ Ror(w[i - 15], 18) ^ (w[i - 15] >> 3);
                    uint s1 = Ror(w[i - 2], 17) ^ Ror(w[i - 2], 19) ^ (w[i - 2] >> 10);
                    w[i] = w[i - 16] + s0 + w[i - 7] + s1;
                }

                uint a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
                for (int i = 0; i < 64; i++)
                {
                    uint t1 = hh + (Ror(e, 6) ^ Ror(e, 11) ^ Ror(e, 25)) + ((e & f) ^ (~e & g)) + K256[i] + w[i];
                    uint t2 = (Ror(a, 2) ^ Ror(a, 13) ^ Ror(a, 22)) + ((a & b) ^ (a & c) ^ (b & c));
                    hh = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
                }

                h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
            }
        }

        private static readonly ulong[] K512 =
        {
            0x428a2f98d728ae22, 0x7137449123ef65cd, 0xb5c0fbcfec4d3b2f, 0xe9b5dba58189dbbc, 0x3956c25bf348b538, 0x59f111f1b605d019, 0x923f82a4af194f9b, 0xab1c5ed5da6d8118,
            0xd807aa98a3030242, 0x12835b0145706fbe, 0x243185be4ee4b28c, 0x550c7dc3d5ffb4e2, 0x72be5d74f27b896f, 0x80deb1fe3b1696b1, 0x9bdc06a725c71235, 0xc19bf174cf692694,
            0xe49b69c19ef14ad2, 0xefbe4786384f25e3, 0x0fc19dc68b8cd5b5, 0x240ca1cc77ac9c65, 0x2de92c6f592b0275, 0x4a7484aa6ea6e483, 0x5cb0a9dcbd41fbd4, 0x76f988da831153b5,
            0x983e5152ee66dfab, 0xa831c66d2db43210, 0xb00327c898fb213f, 0xbf597fc7beef0ee4, 0xc6e00bf33da88fc2, 0xd5a79147930aa725, 0x06ca6351e003826f, 0x142929670a0e6e70,
            0x27b70a8546d22ffc, 0x2e1b21385c26c926, 0x4d2c6dfc5ac42aed, 0x53380d139d95b3df, 0x650a73548baf63de, 0x766a0abb3c77b2a8, 0x81c2c92e47edaee6, 0x92722c851482353b,
            0xa2bfe8a14cf10364, 0xa81a664bbc423001, 0xc24b8b70d0f89791, 0xc76c51a30654be30, 0xd192e819d6ef5218, 0xd69906245565a910, 0xf40e35855771202a, 0x106aa07032bbd1b8,
            0x19a4c116b8d2d0c8, 0x1e376c085141ab53, 0x2748774cdf8eeb99, 0x34b0bcb5e19b48a8, 0x391c0cb3c5c95a63, 0x4ed8aa4ae3418acb, 0x5b9cca4f7763e373, 0x682e6ff3d6b2b8a3,
            0x748f82ee5defb2fc, 0x78a5636f43172f60, 0x84c87814a1f0ab72, 0x8cc702081a6439ec, 0x90befffa23631e28, 0xa4506cebde82bde9, 0xbef9a3f7b2c67915, 0xc67178f2e372532b,
            0xca273eceea26619c, 0xd186b8c721c0c207, 0xeada7dd6cde0eb1e, 0xf57d4f7fee6ed178, 0x06f067aa72176fba, 0x0a637dc5a2c898a6, 0x113f9804bef90dae, 0x1b710b35131c471b,
            0x28db77f523047d84, 0x32caab7b40c72493, 0x3c9ebe0a15c9bebc, 0x431d67c49c100d4c, 0x4cc5d4becb3e42b6, 0x597f299cfc657e2a, 0x5fcb6fab3ad6faec, 0x6c44198c4a475817
        };

        private void Sha512Block(ulong[] h)
        {
            var w = new ulong[80];
            for (int i = 0; i < 16; i++) w[i] = (ulong)ShaText(2 * i) << 32 | ShaText(2 * i + 1);
            unchecked
            {
                for (int i = 16; i < 80; i++)
                {
                    ulong s0 = Ror64(w[i - 15], 1) ^ Ror64(w[i - 15], 8) ^ (w[i - 15] >> 7);
                    ulong s1 = Ror64(w[i - 2], 19) ^ Ror64(w[i - 2], 61) ^ (w[i - 2] >> 6);
                    w[i] = w[i - 16] + s0 + w[i - 7] + s1;
                }

                ulong a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
                for (int i = 0; i < 80; i++)
                {
                    ulong t1 = hh + (Ror64(e, 14) ^ Ror64(e, 18) ^ Ror64(e, 41)) + ((e & f) ^ (~e & g)) + K512[i] + w[i];
                    ulong t2 = (Ror64(a, 28) ^ Ror64(a, 34) ^ Ror64(a, 39)) + ((a & b) ^ (a & c) ^ (b & c));
                    hh = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
                }

                h[0] += a; h[1] += b; h[2] += c; h[3] += d; h[4] += e; h[5] += f; h[6] += g; h[7] += hh;
            }
        }

        /// <summary>One AES block in place in AES_TEXT, with the key in AES_KEY and the direction and size from AES_MODE.</summary>
        private void AesRun()
        {
            uint mode = Reg(AesBase + 0x08);
            int keyWords = (int)(mode & 3) switch { 1 => 6, 2 => 8, _ => 4 };
            var key = new byte[keyWords * 4];
            for (int i = 0; i < keyWords; i++) BitConverter.GetBytes(Reg(AesBase + 0x10 + 4 * (uint)i)).CopyTo(key, 4 * i);
            var block = new byte[16];
            for (int i = 0; i < 4; i++) BitConverter.GetBytes(Reg(AesBase + 0x30 + 4 * (uint)i)).CopyTo(block, 4 * i);
            using var aes = System.Security.Cryptography.Aes.Create();
            aes.Mode = System.Security.Cryptography.CipherMode.ECB;
            aes.Padding = System.Security.Cryptography.PaddingMode.None;
            aes.Key = key;
            using var t = (mode & 4) != 0 ? aes.CreateDecryptor() : aes.CreateEncryptor();
            var output = new byte[16];
            t.TransformBlock(block, 0, 16, output, 0);
            for (int i = 0; i < 4; i++) _registers[AesBase + 0x30 + 4 * (uint)i] = BitConverter.ToUInt32(output, 4 * i);
        }
    }
}
