using SkiaSharp;

namespace RecipeWarden.Infrastructure.Media;

/// <summary>A decoded and re-encoded image (ADR 0022).</summary>
public sealed record EncodedImage(byte[] Full, byte[] Thumb, int Width, int Height);

/// <summary>
/// SEC-52, ADR 0022: decodes a JPEG, PNG or WebP with SkiaSharp and re-encodes it as WebP (quality 82), at most
/// 2400 px on the long side, plus a 640 px thumbnail. Re-encoding drops every metadata block (EXIF, GPS, XMP,
/// ICC comments) and anything hidden after the image data. The camera's orientation is applied first, so photos
/// stay upright. Images over 50 megapixels are refused before decoding (decompression-bomb guard).
/// </summary>
public static class ImageEncoder
{
    public const int MaxSide = 2400;
    public const int ThumbSide = 640;
    public const int Quality = 82;
    public const long MaxPixels = 50_000_000;

    public const string Unreadable = "The image could not be read";
    public const string TooManyPixels = "The image is too large (over 50 megapixels)";
    public const string WrongFormat = "Only JPEG, PNG and WebP images can be used";

    public static (EncodedImage? Image, string? Error) Encode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null)
        {
            return (null, Unreadable);
        }

        if (codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
        {
            return (null, WrongFormat);
        }

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0)
        {
            return (null, Unreadable);
        }

        if ((long)info.Width * info.Height > MaxPixels)
        {
            return (null, TooManyPixels);
        }

        using var decoded = SKBitmap.Decode(codec, new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (decoded is null)
        {
            return (null, Unreadable);
        }

        using var upright = Orient(decoded, codec.EncodedOrigin);
        var full = Resize(upright, MaxSide);
        var thumb = Resize(upright, ThumbSide);
        try
        {
            return (new EncodedImage(Webp(full), Webp(thumb), full.Width, full.Height), null);
        }
        finally
        {
            if (!ReferenceEquals(full, upright))
            {
                full.Dispose();
            }

            if (!ReferenceEquals(thumb, upright))
            {
                thumb.Dispose();
            }
        }
    }

    private static SKBitmap Resize(SKBitmap source, int maxSide)
    {
        var longest = Math.Max(source.Width, source.Height);
        if (longest <= maxSide)
        {
            return source;
        }

        var scale = (double)maxSide / longest;
        var info = new SKImageInfo(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale)), SKColorType.Rgba8888, SKAlphaType.Premul);
        return source.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell)) ?? throw new InvalidOperationException("Resize failed");
    }

    private static byte[] Webp(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, Quality);
        return encoded.ToArray();
    }

    /// <summary>Applies the EXIF orientation, returning a new upright bitmap (or a copy when already upright).</summary>
    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var w = swap ? source.Height : source.Width;
        var h = swap ? source.Width : source.Height;
        var result = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Scale(-1, 1, w / 2f, 0);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.RotateDegrees(180, w / 2f, h / 2f);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Scale(1, -1, 0, h / 2f);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.Translate(w, 0);
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1, 0, source.Height / 2f);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(w, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(0, h);
                canvas.RotateDegrees(-90);
                canvas.Scale(1, -1, 0, source.Height / 2f);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, h);
                canvas.RotateDegrees(-90);
                break;
        }

        using (var image = SKImage.FromBitmap(source))
        {
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        }
        canvas.Flush();
        return result;
    }
}
