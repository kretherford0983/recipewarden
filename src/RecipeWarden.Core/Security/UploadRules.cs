using System.Text;

namespace RecipeWarden.Core.Security;

/// <summary>What an upload turned out to be (SEC-50).</summary>
public enum UploadKind
{
    Markdown,
    Text,
    Docx,
    Pdf,
    Jpeg,
    Png,
    Webp,
}

/// <summary>An upload's kind, or why it is refused.</summary>
public sealed record UploadCheck(UploadKind? Kind, string? Error)
{
    public bool IsAllowed => Kind is not null && Error is null;
}

/// <summary>
/// SEC-50, SEC-51: uploads are allowed by extension and by their first bytes (magic bytes), and both must agree.
/// Macro formats (.docm) and legacy .doc are refused. Documents may be up to 20 MB, images up to 10 MB; a .docx
/// may unpack to at most 50 MB in at most 2,000 entries (checked by the upload validator).
/// </summary>
public static class UploadRules
{
    public const long MaxDocumentBytes = 20L * 1024 * 1024;
    public const long MaxImageBytes = 10L * 1024 * 1024;
    public const long MaxDocxUnpackedBytes = 50L * 1024 * 1024;
    public const int MaxDocxEntries = 2000;
    public const int MaxFilesPerImport = 10;

    /// <summary>How many leading bytes <see cref="Check"/> needs.</summary>
    public const int HeadLength = 16;

    public const string Empty = "The file is empty";
    public const string DocumentTooBig = "Documents must be 20 MB or smaller";
    public const string ImageTooBig = "Images must be 10 MB or smaller";
    public const string NotAllowed = "Only .md, .txt, .docx and .pdf files can be imported";
    public const string ImageNotAllowed = "Only JPEG, PNG and WebP images can be uploaded";
    public const string Macro = "Macro-enabled Word files (.docm) are not accepted; save it as a .docx";
    public const string LegacyDoc = "Old Word files (.doc) are not accepted; save it as a .docx";
    public const string Mismatch = "The file's contents do not match its extension";
    public const string NotText = "The file is not plain text (UTF-8)";

    public static bool IsDocument(UploadKind kind) => kind is UploadKind.Markdown or UploadKind.Text or UploadKind.Docx or UploadKind.Pdf;

    public static bool IsImage(UploadKind kind) => kind is UploadKind.Jpeg or UploadKind.Png or UploadKind.Webp;

    /// <summary>Checks a document upload (import files).</summary>
    public static UploadCheck CheckDocument(string? fileName, long length, ReadOnlySpan<byte> head)
    {
        if (length <= 0)
        {
            return new UploadCheck(null, Empty);
        }

        var ext = Extension(fileName);
        if (ext == ".docm" || ext == ".dotm")
        {
            return new UploadCheck(null, Macro);
        }

        if (ext == ".doc" || ext == ".dot" || StartsWith(head, [0xD0, 0xCF, 0x11, 0xE0]))
        {
            return new UploadCheck(null, LegacyDoc);
        }

        var kind = ext switch
        {
            ".md" or ".markdown" => UploadKind.Markdown,
            ".txt" => UploadKind.Text,
            ".docx" => UploadKind.Docx,
            ".pdf" => UploadKind.Pdf,
            _ => (UploadKind?)null,
        };
        if (kind is null)
        {
            return new UploadCheck(null, NotAllowed);
        }

        if (length > MaxDocumentBytes)
        {
            return new UploadCheck(null, DocumentTooBig);
        }

        var matches = kind switch
        {
            UploadKind.Docx => StartsWith(head, "PK\u0003\u0004"u8),
            UploadKind.Pdf => StartsWith(head, "%PDF-"u8),
            _ => LooksLikeText(head),
        };
        return matches ? new UploadCheck(kind, null) : new UploadCheck(null, Mismatch);
    }

    /// <summary>Checks an image upload (finish and step photos).</summary>
    public static UploadCheck CheckImage(string? fileName, long length, ReadOnlySpan<byte> head)
    {
        if (length <= 0)
        {
            return new UploadCheck(null, Empty);
        }

        var ext = Extension(fileName);
        var kind = ext switch
        {
            ".jpg" or ".jpeg" => UploadKind.Jpeg,
            ".png" => UploadKind.Png,
            ".webp" => UploadKind.Webp,
            _ => (UploadKind?)null,
        };
        if (kind is null)
        {
            return new UploadCheck(null, ImageNotAllowed);
        }

        if (length > MaxImageBytes)
        {
            return new UploadCheck(null, ImageTooBig);
        }

        var sniffed = SniffImage(head);
        return sniffed == kind ? new UploadCheck(kind, null) : new UploadCheck(null, Mismatch);
    }

    /// <summary>The image type from its first bytes, for copied images that have no file name (BR-12).</summary>
    public static UploadKind? SniffImage(ReadOnlySpan<byte> head)
    {
        if (StartsWith(head, [0xFF, 0xD8, 0xFF]))
        {
            return UploadKind.Jpeg;
        }

        if (StartsWith(head, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return UploadKind.Png;
        }

        if (head.Length >= 12 && StartsWith(head, "RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
        {
            return UploadKind.Webp;
        }

        return null;
    }

    /// <summary>
    /// A whole text file must be UTF-8 without NUL bytes (control characters other than tab, CR and LF are
    /// refused too), so a binary renamed to .md or .txt is caught.
    /// </summary>
    public static bool IsText(ReadOnlySpan<byte> bytes)
    {
        var body = bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? bytes[3..] : bytes;
        try
        {
            var text = new UTF8Encoding(false, true).GetString(body);
            return !text.Any(c => char.IsControl(c) && c is not ('\t' or '\r' or '\n'));
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> head)
    {
        foreach (var b in head)
        {
            if (b == 0 || (b < 0x20 && b is not (0x09 or 0x0A or 0x0D)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StartsWith(ReadOnlySpan<byte> head, ReadOnlySpan<byte> magic) => head.StartsWith(magic);

    private static string Extension(string? fileName) =>
        Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
}
