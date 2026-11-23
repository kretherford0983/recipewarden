using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>Images built in memory for the image tests (no binary fixtures).</summary>
internal static class TestImages
{
    public static byte[] Encode(int width, int height, SKEncodedImageFormat format, SKColor? color = null)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color ?? new SKColor(200, 80, 40));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    public static byte[] Jpeg(int width = 300, int height = 200) => Encode(width, height, SKEncodedImageFormat.Jpeg);

    public static byte[] Png(int width = 300, int height = 200) => Encode(width, height, SKEncodedImageFormat.Png);

    /// <summary>
    /// A JPEG carrying an EXIF block with an orientation, a description and a GPS latitude, as a phone would write.
    /// </summary>
    public static byte[] JpegWithExif(ushort orientation, string description, int width = 300, int height = 200)
    {
        var jpeg = Jpeg(width, height);
        var desc = Encoding.ASCII.GetBytes(description + "\0");
        var descOffset = 8 + 2 + (3 * 12) + 4;
        var gpsOffset = descOffset + desc.Length + (desc.Length % 2);
        var tiff = new byte[gpsOffset + 2 + 12 + 4];
        "II"u8.CopyTo(tiff);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), 8);
        var p = 8;
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(p), 3);
        p += 2;
        Entry(tiff, ref p, 0x010E, 2, (uint)desc.Length, (uint)descOffset);
        Entry(tiff, ref p, 0x0112, 3, 1, orientation);
        Entry(tiff, ref p, 0x8825, 4, 1, (uint)gpsOffset);
        desc.CopyTo(tiff, descOffset);
        p = gpsOffset;
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(p), 1);
        p += 2;
        Entry(tiff, ref p, 0x0001, 2, 2, 'N');

        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var app1 = new byte[4 + payload.Length];
        app1[0] = 0xFF;
        app1[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(app1.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(app1, 4);
        return jpeg[..2].Concat(app1).Concat(jpeg[2..]).ToArray();
    }

    private static void Entry(byte[] b, ref int p, ushort tag, ushort type, uint count, uint value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p), tag);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p + 2), type);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p + 4), count);
        if (type == 3)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p + 8), (ushort)value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p + 8), value);
        }

        p += 12;
    }

    /// <summary>A PNG whose header claims huge dimensions (a decompression bomb), with no real pixel data.</summary>
    public static byte[] PngHeader(uint width, uint height)
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Chunk(ms, "IHDR", ihdr);
        Chunk(ms, "IDAT", [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length);
        s.Write(len);
        var typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        s.Write(typed);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
        s.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    public static bool Contains(byte[] haystack, string needle) =>
        haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(needle)) >= 0;
}
