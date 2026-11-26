using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace RobotTwin.CoreSim.Industrial.OpcUa
{
    /// <summary>[opcua-sec] MessageSecurityMode (Part 4): None, Sign, SignAndEncrypt.</summary>
    public enum UaSecurityMode
    {
        None = 1,
        Sign = 2,
        SignAndEncrypt = 3
    }

    /// <summary>
    /// [opcua-sec] The security policies (Part 7): None, and Basic256Sha256: RSA-OAEP (SHA-1) and RSA PKCS #1 v1.5 SHA-256 for
    /// the OpenSecureChannel exchange and the session's proofs, AES-256-CBC and HMAC-SHA256 for the channel's messages, the
    /// keys from the two nonces with P_SHA256.
    /// </summary>
    public static class UaSecurityPolicy
    {
        public const string None = "http://opcfoundation.org/UA/SecurityPolicy#None";
        public const string Basic256Sha256 = "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256";
        public const string RsaSha256 = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";
        public const string RsaOaep = "http://www.w3.org/2001/04/xmlenc#rsa-oaep";
        public const int NonceLength = 32;
        public const int SymmetricSignatureSize = 32;
        public const int BlockSize = 16;
        public const int MinKeyBits = 2048, MaxKeyBits = 4096;
    }

    /// <summary>[opcua-sec] One direction's symmetric keys: signing (32 bytes), encrypting (AES-256) and the IV.</summary>
    public sealed class UaChannelKeys
    {
        private UaChannelKeys(byte[] signing, byte[] encrypting, byte[] iv)
        {
            SigningKey = signing;
            EncryptingKey = encrypting;
            Iv = iv;
        }

        public byte[] SigningKey { get; }
        public byte[] EncryptingKey { get; }
        public byte[] Iv { get; }

        /// <summary>
        /// Part 6 §6.7.5: the client's keys are P_SHA256(server nonce, client nonce), the server's P_SHA256(client nonce,
        /// server nonce); each is 32 signing + 32 encrypting + 16 IV bytes.
        /// </summary>
        public static UaChannelKeys Derive(byte[] secret, byte[] seed)
        {
            var bytes = PSha256(secret, seed, 80);
            return new UaChannelKeys(bytes.Take(32).ToArray(), bytes.Skip(32).Take(32).ToArray(), bytes.Skip(64).Take(16).ToArray());
        }

        /// <summary>P_SHA256 (RFC 5246 §5): HMAC-SHA256 chained over A(i) = HMAC(secret, A(i-1)), A(0) = seed.</summary>
        public static byte[] PSha256(byte[] secret, byte[] seed, int length)
        {
            using var h = new HMACSHA256(secret);
            var output = new byte[length];
            byte[] a = seed;
            int at = 0;
            while (at < length)
            {
                a = h.ComputeHash(a);
                var block = h.ComputeHash(a.Concat(seed).ToArray());
                int n = Math.Min(block.Length, length - at);
                Array.Copy(block, 0, output, at, n);
                at += n;
            }

            return output;
        }
    }

    /// <summary>[opcua-sec] A thrown check: the message's signature, padding or certificate is not what it must be.</summary>
    public sealed class UaSecurityException : Exception
    {
        public UaSecurityException(uint status, string message) : base(message)
        {
            Status = status;
        }

        public uint Status { get; }

        public const uint BadSecurityChecksFailed = 0x80130000, BadCertificateUntrusted = 0x801A0000, BadCertificateInvalid = 0x80120000,
            BadSecurityPolicyRejected = 0x80550000, BadSecurityModeRejected = 0x80540000, BadUserAccessDenied = 0x801F0000,
            BadIdentityTokenRejected = 0x80210000, BadApplicationSignatureInvalid = 0x80580000, BadNonceInvalid = 0x80240000;
    }

    /// <summary>
    /// [opcua-sec] One side of a secure channel: its own certificate (with the private key) and the other side's, the policy
    /// and mode, and once OpenSecureChannel is done the keys each direction signs and encrypts with. It turns a message's
    /// sequence header and body into the bytes on the wire and back (Part 6 §6.7).
    /// </summary>
    public sealed class UaChannelSecurity
    {
        public UaChannelSecurity(string policy, UaSecurityMode mode, X509Certificate2? own, X509Certificate2? remote)
        {
            Policy = policy;
            Mode = policy == UaSecurityPolicy.None ? UaSecurityMode.None : mode;
            Own = own;
            Remote = remote;
        }

        public static UaChannelSecurity Unsecured { get; } = new UaChannelSecurity(UaSecurityPolicy.None, UaSecurityMode.None, null, null);

        public string Policy { get; }
        public UaSecurityMode Mode { get; }
        public X509Certificate2? Own { get; }
        public X509Certificate2? Remote { get; }
        public bool Secured => Mode != UaSecurityMode.None;

        /// <summary>The keys this side signs and encrypts with, and the other side's (for checking and decrypting), per token.</summary>
        public UaChannelKeys? Send { get; private set; }

        private readonly Dictionary<uint, UaChannelKeys> _receive = new Dictionary<uint, UaChannelKeys>();
        private uint _sendToken;

        /// <summary>The token id an unsecured channel's messages carry.</summary>
        public void SetToken(uint tokenId) => _sendToken = tokenId;

        /// <summary>Takes the keys of a new token (the previous one stays readable until the other side moves over).</summary>
        public void SetKeys(uint tokenId, UaChannelKeys send, UaChannelKeys receive)
        {
            Send = send;
            _sendToken = tokenId;
            if (_receive.Count > 1)
            {
                foreach (uint old in _receive.Keys.Where(k => k != tokenId).OrderBy(k => k).Take(_receive.Count - 1).ToList()) _receive.Remove(old);
            }

            _receive[tokenId] = receive;
        }

        // ------------------------------------------------------------------ asymmetric (OpenSecureChannel)

        /// <summary>SecurityPolicyUri, SenderCertificate, ReceiverCertificateThumbprint.</summary>
        public void WriteAsymmetricHeader(UaWriter w)
        {
            w.String(Policy);
            w.ByteString(Secured ? Own!.RawData : null);
            w.ByteString(Secured ? UaCertificates.Thumbprint(Remote!) : null);
        }

        /// <summary>
        /// A whole OPN message: header, the asymmetric security header, then the sequence header and body padded, signed with
        /// this side's key and encrypted with the other side's (Part 6 §6.7.2).
        /// </summary>
        public byte[] EncodeAsymmetric(uint channelId, byte[] inner)
        {
            var sh = new UaWriter();
            WriteAsymmetricHeader(sh);
            byte[] securityHeader = sh.ToArray();
            if (!Secured) return Assemble("OPN", 'F', channelId, securityHeader, inner);

            using var ownKey = Own!.GetRSAPrivateKey() ?? throw new UaSecurityException(UaSecurityException.BadCertificateInvalid, "Sertifikanın özel anahtarı yok.");
            using var remoteKey = Remote!.GetRSAPublicKey() ?? throw new UaSecurityException(UaSecurityException.BadCertificateInvalid, "Karşı tarafın sertifikasında RSA anahtarı yok.");
            int sigSize = ownKey.KeySize / 8;
            int cipherBlock = remoteKey.KeySize / 8;
            int plainBlock = cipherBlock - 42;   // OAEP with SHA-1
            bool extra = remoteKey.KeySize > 2048;
            int fixedPart = inner.Length + 1 + (extra ? 1 : 0) + sigSize;
            int pad = (plainBlock - fixedPart % plainBlock) % plainBlock;
            var plain = new MemoryStream();
            plain.Write(inner, 0, inner.Length);
            for (int i = 0; i <= pad; i++) plain.WriteByte((byte)(pad & 0xFF));
            if (extra) plain.WriteByte((byte)(pad >> 8));
            int blocks = (int)(plain.Length + sigSize) / plainBlock;
            int size = 12 + securityHeader.Length + blocks * cipherBlock;
            byte[] head = Header("OPN", 'F', size, channelId);
            byte[] signature = ownKey.SignData(Concat(head, securityHeader, plain.ToArray()), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            plain.Write(signature, 0, signature.Length);
            byte[] cipher = RsaBlocks(remoteKey, plain.ToArray(), plainBlock, encrypt: true);
            return Concat(head, securityHeader, cipher);
        }

        /// <summary>An OPN message's parts before it is opened: the channel id, the policy and the sender's certificate.</summary>
        public static (uint ChannelId, string Policy, byte[]? SenderCertificate, byte[]? ReceiverThumbprint, int Body) ReadAsymmetricHeader(byte[] message)
        {
            var r = new UaReader(message, 8);
            uint channel = r.UInt32();
            string policy = r.String() ?? string.Empty;
            byte[]? sender = r.ByteString();
            byte[]? thumb = r.ByteString();
            return (channel, policy, sender, thumb, r.Position);
        }

        /// <summary>Opens an OPN message for this side: decrypts with its own key, checks the sender's signature and the padding.</summary>
        public byte[] DecodeAsymmetric(byte[] message, int bodyOffset)
        {
            if (!Secured) return Slice(message, bodyOffset, message.Length - bodyOffset);
            using var ownKey = Own!.GetRSAPrivateKey() ?? throw new UaSecurityException(UaSecurityException.BadCertificateInvalid, "Sertifikanın özel anahtarı yok.");
            using var remoteKey = Remote!.GetRSAPublicKey() ?? throw new UaSecurityException(UaSecurityException.BadCertificateInvalid, "Karşı tarafın sertifikasında RSA anahtarı yok.");
            int cipherBlock = ownKey.KeySize / 8;
            int cipherLength = message.Length - bodyOffset;
            if (cipherLength <= 0 || cipherLength % cipherBlock != 0) throw new UaSecurityException(UaSecurityException.BadSecurityChecksFailed, "Şifreli bölüm blok boyuna uymuyor.");
            byte[] plain;
            try
            {
                plain = RsaBlocks(ownKey, Slice(message, bodyOffset, cipherLength), cipherBlock, encrypt: false);
            }
            catch (CryptographicException)
            {
                throw new UaSecurityException(UaSecurityException.BadSecurityChecksFailed, "İleti çözülemedi.");
            }

            int sigSize = remoteKey.KeySize / 8;
            if (plain.Length < sigSize + 9) throw new UaSecurityException(UaSecurityException.BadSecurityChecksFailed, "İleti çok kısa.");
            byte[] signed = Concat(Slice(message, 0, bodyOffset), Slice(plain, 0, plain.Length - sigSize));
            if (!remoteKey.VerifyData(signed, Slice(plain, plain.Length - sigSize, sigSize), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                throw new UaSecurityException(UaSecurityException.BadSecurityChecksFailed, "İmza doğrulanamadı.");
            bool extra = ownKey.KeySize > 2048;
            int end = plain.Length - sigSize;
            int pad = plain[end - 1 - (extra ? 1 : 0)] | (extra ? plain[end - 1] << 8 : 0);
            int innerLength = end - 1 - (extra ? 1 : 0) - pad;
            if (innerLength < 8) throw new UaSecurityException(UaSecurityException.BadSecurityChecksFailed, "Dolgu geçersiz.");
            return Slice(plain, 0, innerLength);
        }

        // ------------------------------------------------------------------ symmetric (MSG, CLO)

        /// <summary>The most body bytes one chunk carries within the other side's receive buffer.</summary>
        public int MaxBodyPerChunk(int bufferSize)
        {
            if (!Secured) return bufferSize - 16 - 8;
            if (Mode == UaSecurityMode.Sign) return bufferSize - 16 - 8 - UaSecurityPolicy.SymmetricSignatureSize;
            int plain = (bufferSize - 16) / UaSecurityPolicy.BlockSize * UaSecurityPolicy.BlockSize - UaSecurityPolicy.SymmetricSignatureSize;
            return plain - 1 - 8;
        }

        /// <summary>A MSG/CLO chunk: header, token id, then the sequence header and body signed (and padded and encrypted).</summary>
        public byte[] EncodeSymmetric(string type, char chunk, uint channelId, byte[] inner)
        {
            if (!Secured) return Assemble(type, chunk, channelId, BitConverter.GetBytes(_sendToken), inner);
            var keys = Send ?? throw new InvalidOperationException("Kanalın anahtarları yok.");
            byte[] plain = inner;
            if (Mode == UaSecurityMode.SignAndEncrypt)
            {
                int total = inner.Length + 1 + UaSecurityPolicy.SymmetricSignatureSize;
                int pad = (UaSecurityPolicy.BlockSize - total % UaSecurityPolicy.BlockSize) % UaSecurityPolicy.BlockSize;
                plain = new byte[inner.Length + pad + 1];
                Array.Copy(inner, plain, inner.Length);
                for (int i = inner.Length; i < plain.Length; i++) plain[i] = (byte)pad;
            }

            int size = 16 + plain.Length + UaSecurityPolicy.SymmetricSignatureSize;
            byte[] head = Concat(Header(type, chunk, size, channelId), BitConverter.GetBytes(_sendToken));
            byte[] signature;
            using (var h = new HMACSHA256(keys.SigningKey)) signature = h.ComputeHash(Concat(head, plain));
            byte[] data = Concat(plain, signature);
            if (Mode == UaSecurityMode.SignAndEncrypt) data = Aes(keys, data, encrypt: true);
            return Concat(head, data);
        }

        /// <summary>Opens a MSG/CLO chunk: the token's keys decrypt it and check its signature; the sequence header and body.</summary>
        public byte[] DecodeSymmetric(byte[] message)
        {
            if (message.Length < 16) throw new UaSecurityException(UaSecurityException.BadSecurityChecksFailed, "İleti çok kısa.");
            if (!Secured) return Slice(message, 16, message.Length - 16);
            uint token = BitConverter.ToUInt32(message, 12);
            if (!_receive.TryGetValue(token, out var keys)) throw new UaSecurityException(UaSecurityException.BadSecurityChecksFailed, "Bilinmeyen güvenlik belirteci " + token + ".");
            byte[] data = Slice(message, 16, message.Length - 16);
            if (Mode == UaSecurityMode.SignAndEncrypt)
            {
                if (data.Length % UaSecurityPolicy.BlockSize != 0) throw new UaSecurityException(UaSecurityException.BadSecurityChecksFailed, "Şifreli bölüm blok boyuna uymuyor.");
                data = Aes(keys, data, encrypt: false);
            }

            int sig = UaSecurityPolicy.SymmetricSignatureSize;
            if (data.Length < sig + 8) throw new UaSecurityException(UaSecurityException.BadSecurityChecksFailed, "İleti çok kısa.");
            byte[] expected;
            using (var h = new HMACSHA256(keys.SigningKey)) expected = h.ComputeHash(Concat(Slice(message, 0, 16), Slice(data, 0, data.Length - sig)));
            if (!FixedTimeEquals(expected, Slice(data, data.Length - sig, sig))) throw new UaSecurityException(UaSecurityException.BadSecurityChecksFailed, "İmza doğrulanamadı.");
            int end = data.Length - sig;
            if (Mode == UaSecurityMode.SignAndEncrypt)
            {
                int pad = data[end - 1];
                if (end - 1 - pad < 8) throw new UaSecurityException(UaSecurityException.BadSecurityChecksFailed, "Dolgu geçersiz.");
                end -= pad + 1;
            }

            return Slice(data, 0, end);
        }

        // ------------------------------------------------------------------ helpers

        private static byte[] Header(string type, char chunk, int size, uint channelId)
        {
            var head = new byte[12];
            Encoding.ASCII.GetBytes(type, 0, 3, head, 0);
            head[3] = (byte)chunk;
            BitConverter.GetBytes((uint)size).CopyTo(head, 4);
            BitConverter.GetBytes(channelId).CopyTo(head, 8);
            return head;
        }

        private static byte[] Assemble(string type, char chunk, uint channelId, byte[] securityHeader, byte[] inner) =>
            Concat(Header(type, chunk, 12 + securityHeader.Length + inner.Length, channelId), securityHeader, inner);

        private static byte[] RsaBlocks(RSA key, byte[] data, int inBlock, bool encrypt)
        {
            var output = new MemoryStream();
            for (int at = 0; at < data.Length; at += inBlock)
            {
                var block = Slice(data, at, Math.Min(inBlock, data.Length - at));
                var result = encrypt ? key.Encrypt(block, RSAEncryptionPadding.OaepSHA1) : key.Decrypt(block, RSAEncryptionPadding.OaepSHA1);
                output.Write(result, 0, result.Length);
            }

            return output.ToArray();
        }

        private static byte[] Aes(UaChannelKeys keys, byte[] data, bool encrypt)
        {
            using var aes = System.Security.Cryptography.Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.None;
            aes.Key = keys.EncryptingKey;
            aes.IV = keys.Iv;
            using var t = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor();
            return t.TransformFinalBlock(data, 0, data.Length);
        }

        internal static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        internal static byte[] Slice(byte[] data, int offset, int count)
        {
            var r = new byte[count];
            Array.Copy(data, offset, r, 0, count);
            return r;
        }

        internal static byte[] Concat(params byte[][] parts)
        {
            var r = new byte[parts.Sum(p => p.Length)];
            int at = 0;
            foreach (var p in parts)
            {
                Array.Copy(p, 0, r, at, p.Length);
                at += p.Length;
            }

            return r;
        }
    }

    /// <summary>[opcua-sec] Application certificates: making a self-signed one, its thumbprint, signing with it and checking.</summary>
    public static class UaCertificates
    {
        /// <summary>
        /// An application instance certificate as Part 6 §6.2.2 asks: RSA 2048 with SHA-256, the application URI and host
        /// names in the subject alternative name, for both server and client authentication.
        /// </summary>
        public static X509Certificate2 CreateSelfSigned(string applicationUri, string commonName, IEnumerable<string>? hostNames = null, int years = 5)
        {
            using var rsa = RSA.Create(2048);
            var subject = new X500DistinguishedName("CN=" + commonName.Replace(",", " ").Replace("=", " ") + ", O=RobotWin Studio");
            var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder();
            san.AddUri(new Uri(applicationUri));
            foreach (string host in (hostNames ?? new[] { "localhost" }).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (IPAddress.TryParse(host, out var ip)) san.AddIpAddress(ip);
                else if (host.Length > 0) san.AddDnsName(host);
            }

            request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.KeyEncipherment |
                X509KeyUsageFlags.DataEncipherment | X509KeyUsageFlags.KeyCertSign, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2") }, false));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            var now = DateTimeOffset.UtcNow.AddMinutes(-5);
            using var made = request.CreateSelfSigned(now, now.AddYears(years));
            // Through PKCS #12 so the key stays with the certificate in memory only (nothing goes into the user's key store).
            return LoadPkcs12(made.Export(X509ContentType.Pkcs12), null);
        }

        public static X509Certificate2 LoadPkcs12(byte[] data, string? password)
        {
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadPkcs12(data, password, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
#else
            return new X509Certificate2(data, password, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
#endif
        }

        public static X509Certificate2 Load(byte[] der)
        {
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadCertificate(der);
#else
            return new X509Certificate2(der);
#endif
        }

        /// <summary>The certificate's SHA-1 thumbprint as OPC UA sends it (20 bytes).</summary>
        public static byte[] Thumbprint(X509Certificate2 certificate)
        {
            using var sha1 = SHA1.Create();
            return sha1.ComputeHash(certificate.RawData);
        }

        public static string ThumbprintHex(X509Certificate2 certificate) => BitConverter.ToString(Thumbprint(certificate)).Replace("-", string.Empty);

        /// <summary>The application URI in the certificate's subject alternative name, or null.</summary>
        public static string? ApplicationUri(X509Certificate2 certificate)
        {
            foreach (var ext in certificate.Extensions)
            {
                if (ext.Oid?.Value != "2.5.29.17") continue;
                // SAN is a SEQUENCE of GeneralName; a URI is [6] IA5String.
                byte[] d = ext.RawData;
                int i = 0;
                if (d.Length < 2 || d[i++] != 0x30) return null;
                int len = ReadLength(d, ref i);
                int end = Math.Min(d.Length, i + len);
                while (i < end)
                {
                    byte tag = d[i++];
                    int l = ReadLength(d, ref i);
                    if (tag == 0x86) return Encoding.ASCII.GetString(d, i, l);
                    i += l;
                }
            }

            return null;
        }

        private static int ReadLength(byte[] d, ref int i)
        {
            int b = d[i++];
            if (b < 0x80) return b;
            int n = b & 0x7F, v = 0;
            for (int k = 0; k < n; k++) v = (v << 8) | d[i++];
            return v;
        }

        public static int KeyBits(X509Certificate2 certificate)
        {
            using var key = certificate.GetRSAPublicKey();
            return key?.KeySize ?? 0;
        }

        /// <summary>RSA PKCS #1 v1.5 SHA-256 with the certificate's private key.</summary>
        public static byte[] Sign(X509Certificate2 own, byte[] data)
        {
            using var key = own.GetRSAPrivateKey() ?? throw new UaSecurityException(UaSecurityException.BadCertificateInvalid, "Sertifikanın özel anahtarı yok.");
            return key.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        public static bool Verify(X509Certificate2 remote, byte[] data, byte[]? signature)
        {
            if (signature == null) return false;
            using var key = remote.GetRSAPublicKey();
            return key != null && key.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        /// <summary>RSA-OAEP (SHA-1) in key-sized blocks: a user's password as UserNameIdentityToken carries it.</summary>
        public static byte[] Encrypt(X509Certificate2 remote, byte[] data)
        {
            using var key = remote.GetRSAPublicKey() ?? throw new UaSecurityException(UaSecurityException.BadCertificateInvalid, "Sertifikada RSA anahtarı yok.");
            int block = key.KeySize / 8 - 42;
            var output = new MemoryStream();
            for (int at = 0; at < data.Length; at += block)
            {
                var part = key.Encrypt(UaChannelSecurity.Slice(data, at, Math.Min(block, data.Length - at)), RSAEncryptionPadding.OaepSHA1);
                output.Write(part, 0, part.Length);
            }

            return output.ToArray();
        }

        public static byte[] Decrypt(X509Certificate2 own, byte[] data)
        {
            using var key = own.GetRSAPrivateKey() ?? throw new UaSecurityException(UaSecurityException.BadCertificateInvalid, "Sertifikanın özel anahtarı yok.");
            int block = key.KeySize / 8;
            if (data.Length == 0 || data.Length % block != 0) throw new UaSecurityException(UaSecurityException.BadIdentityTokenRejected, "Şifreli parola blok boyuna uymuyor.");
            var output = new MemoryStream();
            for (int at = 0; at < data.Length; at += block)
            {
                var part = key.Decrypt(UaChannelSecurity.Slice(data, at, block), RSAEncryptionPadding.OaepSHA1);
                output.Write(part, 0, part.Length);
            }

            return output.ToArray();
        }
    }

    /// <summary>
    /// [opcua-sec] Where the application's certificate and the certificates it trusts are kept (as other OPC UA stacks do):
    /// own/ the application's own (made on first use), trusted/ the other applications it accepts, rejected/ the ones it
    /// refused, to be moved to trusted/ by the user when they are known.
    /// </summary>
    public sealed class UaPki
    {
        private readonly object _gate = new object();

        public UaPki(string folder)
        {
            Folder = folder;
        }

        /// <summary>%LOCALAPPDATA%\RobotWinStudio\pki.</summary>
        public static string DefaultFolder => DefaultFolderOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RobotWinStudio", "pki");

        /// <summary>Another place for <see cref="DefaultFolder"/> (tests keep their certificates out of the user's folder).</summary>
        public static string? DefaultFolderOverride { get; set; }

        public string Folder { get; }
        public string TrustedFolder => Path.Combine(Folder, "trusted");
        public string RejectedFolder => Path.Combine(Folder, "rejected");

        /// <summary>Accept every certificate (a closed test network); false: only those in trusted/.</summary>
        public bool TrustAll { get; set; }

        /// <summary>The application's certificate for this URI: the stored one, or a new one made and stored.</summary>
        public X509Certificate2 Own(string applicationUri, string name, IEnumerable<string>? hostNames = null)
        {
            lock (_gate)
            {
                string dir = Path.Combine(Folder, "own");
                string file = Path.Combine(dir, Safe(applicationUri) + ".pfx");
                if (File.Exists(file))
                {
                    try
                    {
                        var stored = UaCertificates.LoadPkcs12(File.ReadAllBytes(file), null);
                        if (stored.HasPrivateKey && stored.NotAfter > DateTime.Now.AddDays(1)) return stored;
                    }
                    catch (CryptographicException)
                    {
                        // damaged: a new one is made
                    }
                }

                var made = UaCertificates.CreateSelfSigned(applicationUri, name, hostNames);
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(file, made.Export(X509ContentType.Pkcs12));
                File.WriteAllBytes(Path.Combine(dir, Safe(applicationUri) + ".der"), made.RawData);
                return made;
            }
        }

        /// <summary>Is the other application's certificate trusted? An unknown one is kept in rejected/ for the user to decide.</summary>
        public bool Trusts(X509Certificate2 certificate)
        {
            if (TrustAll) return true;
            string thumb = UaCertificates.ThumbprintHex(certificate);
            lock (_gate)
            {
                if (Directory.Exists(TrustedFolder))
                {
                    foreach (var f in Directory.GetFiles(TrustedFolder))
                    {
                        try
                        {
                            using var c = UaCertificates.Load(File.ReadAllBytes(f));
                            if (UaCertificates.ThumbprintHex(c) == thumb) return true;
                        }
                        catch (CryptographicException)
                        {
                        }
                    }
                }

                try
                {
                    Directory.CreateDirectory(RejectedFolder);
                    string name = Safe(certificate.GetNameInfo(X509NameType.SimpleName, false)) + " [" + thumb + "].der";
                    File.WriteAllBytes(Path.Combine(RejectedFolder, name), certificate.RawData);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                return false;
            }
        }

        /// <summary>Trusts a certificate from now on (moves it from rejected/ to trusted/).</summary>
        public void Trust(X509Certificate2 certificate)
        {
            lock (_gate)
            {
                Directory.CreateDirectory(TrustedFolder);
                string thumb = UaCertificates.ThumbprintHex(certificate);
                string name = Safe(certificate.GetNameInfo(X509NameType.SimpleName, false)) + " [" + thumb + "].der";
                File.WriteAllBytes(Path.Combine(TrustedFolder, name), certificate.RawData);
                string rejected = Path.Combine(RejectedFolder, name);
                if (File.Exists(rejected)) File.Delete(rejected);
            }
        }

        private static string Safe(string s)
        {
            var b = new StringBuilder();
            foreach (char ch in s) b.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '.' || ch == ' ' ? ch : '_');
            return b.Length == 0 ? "certificate" : b.ToString();
        }
    }
}
