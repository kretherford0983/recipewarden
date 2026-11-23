using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RecipeWarden.Infrastructure.Backup;

/// <summary>A new key pair: the public key stays on the server; the recovery kit goes to the Admin once (FR-BKP-15).</summary>
public sealed record BackupKeyPair(string PublicKeyPem, string Fingerprint, string RecoveryKit);

/// <summary>The kit or passphrase is wrong, or the archive was encrypted for another key (SEC-75).</summary>
public sealed class BackupKeyException(string message) : Exception(message);

/// <summary>
/// FR-BKP-15, SEC-75, ADR 0039: backup encryption. Each archive gets a random AES-256 key, wrapped with the server's
/// RSA-3072 public key (OAEP-SHA256), and is written in 1 MiB AES-GCM chunks whose nonce and associated data bind
/// the header, the chunk number and the last-chunk flag, so chunks cannot be cut, reordered or swapped. The private
/// key exists only in the recovery kit, encrypted with the Admin's passphrase (PBKDF2-SHA256, AES-256).
/// </summary>
public static class BackupCrypto
{
    public const int RsaBits = 3072;
    public const int ChunkSize = 1024 * 1024;
    public const int DefaultKitIterations = 600_000;
    // Built from parts so secret scanners do not mistake the PEM header text for a key (SEC-83).
    public const string KitBegin = "-----BEGIN " + "ENCRYPTED PRIVATE KEY-----";
    public const string KitEnd = "-----END " + "ENCRYPTED PRIVATE KEY-----";
    public const string WrongKit = "The recovery kit or passphrase is wrong.";
    public const string NotThisKit = "This archive was encrypted for a different recovery kit (key {0}).";
    private static readonly CompositeFormat NotThisKitFormat = CompositeFormat.Parse(NotThisKit);
    internal static readonly byte[] Magic = "RWENC\u0001"u8.ToArray();
    internal const int TagSize = 16;
    private const int NoncePrefixSize = 8;

    public static BackupKeyPair CreateKeyPair(string passphrase, DateTimeOffset now, int iterations = DefaultKitIterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        using var rsa = RSA.Create(RsaBits);
        var fingerprint = Fingerprint(rsa);
        var pem = rsa.ExportEncryptedPkcs8PrivateKeyPem(passphrase,
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, iterations));
        var kit = new StringBuilder()
            .AppendLine("RecipeWarden backup recovery kit")
            .AppendLine(CultureInfo.InvariantCulture, $"Created: {now:yyyy-MM-dd HH:mm} UTC")
            .AppendLine(CultureInfo.InvariantCulture, $"Key: {fingerprint}")
            .AppendLine("Keep this file and its passphrase somewhere safe and separate from the server.")
            .AppendLine("Every encrypted backup made with this key needs both to be restored. The server does not keep a copy.")
            .AppendLine()
            .AppendLine(pem)
            .ToString();
        return new BackupKeyPair(rsa.ExportSubjectPublicKeyInfoPem(), fingerprint, kit);
    }

    /// <summary>"SHA256:" and the base64 SHA-256 of the public key, as shown in the kit and next to each archive.</summary>
    public static string Fingerprint(RSA rsa)
    {
        ArgumentNullException.ThrowIfNull(rsa);
        return "SHA256:" + Convert.ToBase64String(SHA256.HashData(rsa.ExportSubjectPublicKeyInfo())).TrimEnd('=');
    }

    public static string FingerprintOfPublicKey(string publicKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        return Fingerprint(rsa);
    }

    /// <summary>Opens the kit with the passphrase. Throws <see cref="BackupKeyException"/> when either is wrong.</summary>
    public static RSA OpenKit(string kitText, string passphrase)
    {
        var start = kitText?.IndexOf(KitBegin, StringComparison.Ordinal) ?? -1;
        var end = start < 0 ? -1 : kitText!.IndexOf(KitEnd, start, StringComparison.Ordinal);
        if (start < 0 || end < 0)
        {
            throw new BackupKeyException("That file is not a RecipeWarden recovery kit.");
        }

        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromEncryptedPem(kitText.AsSpan(start, end + KitEnd.Length - start), (passphrase ?? string.Empty).AsSpan());
            return rsa;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException)
        {
            rsa.Dispose();
            throw new BackupKeyException(WrongKit);
        }
    }

    /// <summary>True when <paramref name="header"/> starts like an encrypted archive.</summary>
    public static bool IsEncrypted(ReadOnlySpan<byte> header) => header.StartsWith(Magic);

    /// <summary>A write-only stream that encrypts into <paramref name="output"/>. Dispose it to write the last chunk.</summary>
    public static Stream Encrypt(Stream output, string publicKeyPem, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(output);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        var key = RandomNumberGenerator.GetBytes(32);
        var wrapped = rsa.Encrypt(key, RSAEncryptionPadding.OaepSHA256);
        var fp = Encoding.ASCII.GetBytes(Fingerprint(rsa));
        var prefix = RandomNumberGenerator.GetBytes(NoncePrefixSize);

        using var header = new MemoryStream();
        header.Write(Magic);
        header.WriteByte((byte)fp.Length);
        header.Write(fp);
        Span<byte> len = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(len, (ushort)wrapped.Length);
        header.Write(len);
        header.Write(wrapped);
        header.Write(prefix);
        var headerBytes = header.ToArray();
        output.Write(headerBytes);
        return new EncryptingStream(output, key, prefix, SHA256.HashData(headerBytes), leaveOpen);
    }

    /// <summary>Reads the key fingerprint an archive was encrypted for, without a key.</summary>
    public static string ReadFingerprint(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Span<byte> magic = stackalloc byte[Magic.Length];
        input.ReadExactly(magic);
        if (!magic.SequenceEqual(Magic))
        {
            throw new BackupKeyException("The file is not an encrypted RecipeWarden backup.");
        }

        var n = input.ReadByte();
        if (n <= 0)
        {
            throw new BackupKeyException("The encrypted archive is damaged.");
        }

        var fp = new byte[n];
        input.ReadExactly(fp);
        return Encoding.ASCII.GetString(fp);
    }

    /// <summary>A read-only stream that decrypts <paramref name="input"/>. Throws when the key does not match or the data was changed.</summary>
    public static Stream Decrypt(Stream input, RSA privateKey, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(privateKey);
        using var header = new MemoryStream();
        var fp = ReadFingerprint(input);
        var fpBytes = Encoding.ASCII.GetBytes(fp);
        header.Write(Magic);
        header.WriteByte((byte)fpBytes.Length);
        header.Write(fpBytes);
        if (!string.Equals(fp, Fingerprint(privateKey), StringComparison.Ordinal))
        {
            throw new BackupKeyException(string.Format(CultureInfo.InvariantCulture, NotThisKitFormat, fp));
        }

        Span<byte> len = stackalloc byte[2];
        input.ReadExactly(len);
        header.Write(len);
        var wrapped = new byte[BinaryPrimitives.ReadUInt16BigEndian(len)];
        input.ReadExactly(wrapped);
        header.Write(wrapped);
        var prefix = new byte[NoncePrefixSize];
        input.ReadExactly(prefix);
        header.Write(prefix);

        byte[] key;
        try
        {
            key = privateKey.Decrypt(wrapped, RSAEncryptionPadding.OaepSHA256);
        }
        catch (CryptographicException)
        {
            throw new BackupKeyException(WrongKit);
        }

        return new DecryptingStream(input, key, prefix, SHA256.HashData(header.ToArray()), leaveOpen);
    }

    internal static void Nonce(Span<byte> nonce, byte[] prefix, uint counter)
    {
        prefix.CopyTo(nonce);
        BinaryPrimitives.WriteUInt32BigEndian(nonce[NoncePrefixSize..], counter);
    }

    internal static byte[] Aad(byte[] headerHash, uint counter, bool last)
    {
        var aad = new byte[headerHash.Length + 5];
        headerHash.CopyTo(aad, 0);
        BinaryPrimitives.WriteUInt32BigEndian(aad.AsSpan(headerHash.Length), counter);
        aad[^1] = last ? (byte)1 : (byte)0;
        return aad;
    }

    private sealed class EncryptingStream(Stream output, byte[] key, byte[] prefix, byte[] headerHash, bool leaveOpen) : Stream
    {
        private readonly AesGcm aes = new(key, TagSize);
        private readonly byte[] buffer = new byte[ChunkSize];
        private int filled;
        private uint counter;
        private bool done;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !done;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> data)
        {
            ObjectDisposedException.ThrowIf(done, this);
            while (data.Length > 0)
            {
                var n = Math.Min(data.Length, ChunkSize - filled);
                data[..n].CopyTo(buffer.AsSpan(filled));
                filled += n;
                data = data[n..];
                if (filled == ChunkSize)
                {
                    WriteChunk(last: false);
                }
            }
        }

        public override void Flush() => output.Flush();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !done)
            {
                WriteChunk(last: true);
                done = true;
                output.Flush();
                aes.Dispose();
                CryptographicOperations.ZeroMemory(key);
                if (!leaveOpen)
                {
                    output.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        private void WriteChunk(bool last)
        {
            Span<byte> nonce = stackalloc byte[12];
            Nonce(nonce, prefix, counter);
            var cipher = new byte[filled];
            var tag = new byte[TagSize];
            aes.Encrypt(nonce, buffer.AsSpan(0, filled), cipher, tag, Aad(headerHash, counter, last));
            Span<byte> head = stackalloc byte[5];
            BinaryPrimitives.WriteUInt32BigEndian(head, (uint)filled);
            head[4] = last ? (byte)1 : (byte)0;
            output.Write(head);
            output.Write(cipher);
            output.Write(tag);
            counter = checked(counter + 1);
            filled = 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class DecryptingStream(Stream input, byte[] key, byte[] prefix, byte[] headerHash, bool leaveOpen) : Stream
    {
        private readonly AesGcm aes = new(key, TagSize);
        private byte[] plain = [];
        private int offset;
        private uint counter;
        private bool sawLast;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> destination)
        {
            while (offset == plain.Length)
            {
                if (sawLast)
                {
                    return 0;
                }

                ReadChunk();
            }

            var n = Math.Min(destination.Length, plain.Length - offset);
            plain.AsSpan(offset, n).CopyTo(destination);
            offset += n;
            return n;
        }

        private void ReadChunk()
        {
            Span<byte> head = stackalloc byte[5];
            if (input.ReadAtLeast(head, 5, throwOnEndOfStream: false) < 5)
            {
                throw new BackupKeyException("The encrypted archive is incomplete (cut short).");
            }

            var length = BinaryPrimitives.ReadUInt32BigEndian(head);
            var last = head[4] == 1;
            if (length > ChunkSize || head[4] > 1)
            {
                throw new BackupKeyException("The encrypted archive is damaged.");
            }

            var cipher = new byte[length];
            var tag = new byte[TagSize];
            input.ReadExactly(cipher);
            input.ReadExactly(tag);
            var output = new byte[length];
            Span<byte> nonce = stackalloc byte[12];
            Nonce(nonce, prefix, counter);
            try
            {
                aes.Decrypt(nonce, cipher, tag, output, Aad(headerHash, counter, last));
            }
            catch (AuthenticationTagMismatchException)
            {
                throw new BackupKeyException("The encrypted archive was changed or damaged (checksum mismatch).");
            }

            counter = checked(counter + 1);
            plain = output;
            offset = 0;
            if (last)
            {
                sawLast = true;
                if (input.ReadByte() >= 0)
                {
                    throw new BackupKeyException("The encrypted archive has data after its end.");
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                aes.Dispose();
                CryptographicOperations.ZeroMemory(key);
                if (!leaveOpen)
                {
                    input.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
