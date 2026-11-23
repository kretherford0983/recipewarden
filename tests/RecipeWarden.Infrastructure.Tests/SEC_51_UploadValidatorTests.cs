using System.IO.Compression;
using System.Text;
using RecipeWarden.Core.Security;
using RecipeWarden.Infrastructure.Import;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>SEC-50, SEC-51, AC-15: the upload validator reads with a cap and checks .docx archives.</summary>
[Trait("Req", "SEC-51")]
[Trait("Req", "SEC-50")]
[Trait("Req", "AC-15")]
public sealed class SEC_51_UploadValidatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static byte[] Zip(Action<ZipArchive> fill)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            fill(zip);
        }

        return ms.ToArray();
    }

    private static void Add(ZipArchive zip, string name, byte[] content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.SmallestSize);
        using var s = entry.Open();
        s.Write(content);
    }

    private static byte[] Docx(string contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml") => Zip(z =>
    {
        Add(z, "[Content_Types].xml", Encoding.UTF8.GetBytes($"<Types><Override PartName=\"/word/document.xml\" ContentType=\"{contentType}\"/></Types>"));
        Add(z, "word/document.xml", "<w:document/>"u8.ToArray());
    });

    private static Task<(ValidatedUpload? Upload, string? Error)> Document(byte[] bytes, string name) =>
        UploadValidator.ReadDocumentAsync(new MemoryStream(bytes), name, bytes.Length, Ct);

    [Fact]
    public async Task A_good_docx_passes()
    {
        var (upload, error) = await Document(Docx(), "chili.docx");
        error.ShouldBeNull();
        upload!.Kind.ShouldBe(UploadKind.Docx);
        upload.FileName.ShouldBe("chili.docx");
    }

    [Fact]
    public async Task Macro_content_is_refused_even_when_named_docx()
    {
        (await Document(Docx("application/vnd.ms-word.document.macroEnabled.main+xml"), "x.docx")).Error.ShouldBe(UploadRules.Macro);
        var withVba = Zip(z =>
        {
            Add(z, "[Content_Types].xml", "<Types/>"u8.ToArray());
            Add(z, "word/vbaProject.bin", [1, 2, 3]);
        });
        (await Document(withVba, "x.docx")).Error.ShouldBe(UploadRules.Macro);
    }

    [Fact]
    public async Task Zip_bombs_are_refused()
    {
        var huge = Zip(z => Add(z, "word/document.xml", new byte[51 * 1024 * 1024]));
        huge.Length.ShouldBeLessThan(1024 * 1024);
        (await Document(huge, "bomb.docx")).Error.ShouldBe(UploadValidator.DocxTooBigUnpacked);

        var many = Zip(z =>
        {
            for (var i = 0; i <= UploadRules.MaxDocxEntries; i++)
            {
                Add(z, $"word/p{i}.xml", [0]);
            }
        });
        (await Document(many, "many.docx")).Error.ShouldBe(UploadValidator.DocxTooBigUnpacked);
    }

    [Fact]
    public async Task Entries_that_climb_out_of_the_archive_are_refused()
    {
        var bad = Zip(z =>
        {
            Add(z, "[Content_Types].xml", "<Types/>"u8.ToArray());
            Add(z, "../../etc/cron.d/x", [1]);
        });
        (await Document(bad, "x.docx")).Error.ShouldBe(UploadValidator.DocxDamaged);
        (await Document([0x50, 0x4B, 0x03, 0x04, 0, 0, 0], "x.docx")).Error.ShouldBe(UploadValidator.DocxDamaged);
    }

    [Fact]
    public async Task Files_over_20_mb_are_refused_without_reading_them_all()
    {
        var stream = new CountingStream(60L * 1024 * 1024);
        var (_, error) = await UploadValidator.ReadDocumentAsync(stream, "big.pdf", stream.Length, Ct);
        error.ShouldBe(UploadRules.DocumentTooBig);
        stream.BytesRead.ShouldBeLessThanOrEqualTo(UploadRules.MaxDocumentBytes + (128 * 1024));
    }

    [Fact]
    public async Task Text_must_be_utf8_and_names_lose_their_folders()
    {
        (await Document([0x61, 0x00, 0x62], "a.md")).Error.ShouldBe(UploadRules.Mismatch);
        (await Document([0x61, 0xC3, 0x28], "a.md")).Error.ShouldBe(UploadRules.NotText);
        var (ok, _) = await Document("# Soup"u8.ToArray(), "../../etc/soup.md");
        ok!.FileName.ShouldBe("soup.md");
        (await Document("# Soup"u8.ToArray(), "   ")).Error.ShouldBe(UploadValidator.NoFileName);
    }

    [Fact]
    public async Task Images_are_checked_too()
    {
        var jpeg = TestImages.Jpeg();
        var (ok, _) = await UploadValidator.ReadImageAsync(new MemoryStream(jpeg), "a.jpg", jpeg.Length, Ct);
        ok!.Kind.ShouldBe(UploadKind.Jpeg);
        (await UploadValidator.ReadImageAsync(new MemoryStream(jpeg), "a.png", jpeg.Length, Ct)).Error.ShouldBe(UploadRules.Mismatch);
        var big = new CountingStream(11L * 1024 * 1024, [0xFF, 0xD8, 0xFF]);
        (await UploadValidator.ReadImageAsync(big, "a.jpg", big.Length, Ct)).Error.ShouldBe(UploadRules.ImageTooBig);
    }

    /// <summary>A long stream of zeros (after an optional header) that counts how much was read.</summary>
    private sealed class CountingStream(long length, byte[]? head = null) : Stream
    {
        private long position;

        public long BytesRead => position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, length - position);
            Array.Clear(buffer, offset, n);
            if (head is not null && position < head.Length)
            {
                var h = (int)Math.Min(head.Length - position, n);
                Array.Copy(head, position, buffer, offset, h);
            }

            position += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
