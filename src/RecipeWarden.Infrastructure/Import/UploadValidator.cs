using System.IO.Compression;
using RecipeWarden.Core.Security;

namespace RecipeWarden.Infrastructure.Import;

/// <summary>An upload that passed every check, held in memory.</summary>
public sealed record ValidatedUpload(UploadKind Kind, string FileName, byte[] Bytes);

/// <summary>
/// SEC-50, SEC-51, AC-15: every upload goes through here. The file is read with a hard cap (never more than the
/// limit plus one byte), its extension and first bytes must agree (<see cref="UploadRules"/>), text must be UTF-8,
/// and a .docx is opened as a zip only to check it: at most 2,000 entries and 50 MB unpacked, no macros, no entry
/// paths that climb out of the archive. Nothing is written to disk here.
/// </summary>
public static class UploadValidator
{
    public const string DocxTooBigUnpacked = "The Word file unpacks to more than 50 MB or 2,000 parts, so it is not accepted";
    public const string DocxDamaged = "The Word file is damaged";
    public const string NoFileName = "The file has no name";

    public static async Task<(ValidatedUpload? Upload, string? Error)> ReadDocumentAsync(Stream stream, string? fileName, long length, CancellationToken cancellationToken = default)
    {
        var name = SafeName(fileName);
        if (name is null)
        {
            return (null, NoFileName);
        }

        var bytes = await ReadCappedAsync(stream, UploadRules.MaxDocumentBytes, cancellationToken).ConfigureAwait(false);
        var check = UploadRules.CheckDocument(name, bytes is null ? UploadRules.MaxDocumentBytes + 1 : Math.Max(length, bytes.LongLength), Head(bytes));
        if (!check.IsAllowed)
        {
            return (null, check.Error);
        }

        if (bytes is null)
        {
            return (null, UploadRules.DocumentTooBig);
        }

        if (check.Kind is UploadKind.Markdown or UploadKind.Text && !UploadRules.IsText(bytes))
        {
            return (null, UploadRules.NotText);
        }

        if (check.Kind == UploadKind.Docx)
        {
            var docxError = CheckDocx(bytes);
            if (docxError is not null)
            {
                return (null, docxError);
            }
        }

        return (new ValidatedUpload(check.Kind!.Value, name, bytes), null);
    }

    /// <summary>
    /// SEC-50, ADR 0040: a backup archive or dataset zip is streamed to <paramref name="targetPath"/> (on the data
    /// volume, never in memory) with a hard cap, after its name and first bytes are checked. On any error the file is
    /// removed. Its content is checked by its reader before anything is applied.
    /// </summary>
    public static async Task<string?> SaveArchiveAsync(Stream stream, string? fileName, long length, ArchiveUploadKind kind, long maxBytes,
        string targetPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var name = SafeName(fileName);
        if (name is null)
        {
            return NoFileName;
        }

        var head = new byte[8];
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        var error = ArchiveUploadRules.Check(kind, name, length, head.AsSpan(0, read), maxBytes);
        if (error is not null)
        {
            return error;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var output = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write);
        var ok = false;
        try
        {
            await using (output.ConfigureAwait(false))
            {
                await output.WriteAsync(head.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                var buffer = new byte[81920];
                long total = read;
                int n;
                while ((n = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += n;
                    if (total > maxBytes)
                    {
                        return ArchiveUploadRules.Check(kind, name, total, head.AsSpan(0, read), maxBytes);
                    }

                    await output.WriteAsync(buffer.AsMemory(0, n), cancellationToken).ConfigureAwait(false);
                }
            }

            ok = true;
            return null;
        }
        finally
        {
            if (!ok)
            {
                File.Delete(targetPath);
            }
        }
    }

    public static async Task<(ValidatedUpload? Upload, string? Error)> ReadImageAsync(Stream stream, string? fileName, long length, CancellationToken cancellationToken = default)
    {
        var name = SafeName(fileName);
        if (name is null)
        {
            return (null, NoFileName);
        }

        var bytes = await ReadCappedAsync(stream, UploadRules.MaxImageBytes, cancellationToken).ConfigureAwait(false);
        var check = UploadRules.CheckImage(name, bytes is null ? UploadRules.MaxImageBytes + 1 : Math.Max(length, bytes.LongLength), Head(bytes));
        if (!check.IsAllowed)
        {
            return (null, check.Error);
        }

        return bytes is null ? (null, UploadRules.ImageTooBig) : (new ValidatedUpload(check.Kind!.Value, name, bytes), null);
    }

    /// <summary>SEC-51 zip-bomb guard and SEC-50 macro check on a .docx.</summary>
    internal static string? CheckDocx(byte[] bytes)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
            if (zip.Entries.Count > UploadRules.MaxDocxEntries)
            {
                return DocxTooBigUnpacked;
            }

            long total = 0;
            foreach (var entry in zip.Entries)
            {
                total += entry.Length;
                if (total > UploadRules.MaxDocxUnpackedBytes)
                {
                    return DocxTooBigUnpacked;
                }

                var path = entry.FullName.Replace('\\', '/');
                if (path.StartsWith('/') || path.Split('/').Contains(".."))
                {
                    return DocxDamaged;
                }

                if (path.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))
                {
                    return UploadRules.Macro;
                }
            }

            var types = zip.GetEntry("[Content_Types].xml");
            if (types is null)
            {
                return DocxDamaged;
            }

            using var reader = new StreamReader(types.Open());
            var xml = reader.ReadToEnd();
            return xml.Contains("macroEnabled", StringComparison.OrdinalIgnoreCase) ? UploadRules.Macro : null;
        }
        catch (InvalidDataException)
        {
            return DocxDamaged;
        }
    }

    /// <summary>Reads at most <paramref name="max"/> bytes; null when the stream holds more.</summary>
    private static async Task<byte[]?> ReadCappedAsync(Stream stream, long max, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > max)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static byte[] Head(byte[]? bytes) => bytes is null ? [] : bytes.Length <= UploadRules.HeadLength ? bytes : bytes[..UploadRules.HeadLength];

    /// <summary>The file name without any folder part; it is only ever shown, never used as a path.</summary>
    private static string? SafeName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/')).Trim();
        name = new string(name.Where(c => !char.IsControl(c)).ToArray());
        return name.Length == 0 ? null : name.Length > 200 ? name[^200..] : name;
    }
}
