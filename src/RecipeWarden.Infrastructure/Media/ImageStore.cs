using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Media;

/// <summary>An image file to serve.</summary>
public sealed record MediaFile(string Path, long Length);

/// <summary>
/// ADR 0022, SEC-52, 8.2 Image: stores re-encoded images under <c>&lt;data&gt;/media/&lt;2 chars&gt;/&lt;id&gt;.webp</c>
/// (outside the web root) with a random 128-bit id, decides who may open them, and deletes images that nothing
/// uses any more.
/// </summary>
public sealed class ImageStore(AppDbContext db, DataOptions data, TimeProvider clock)
{
    public static readonly TimeSpan OrphanAge = TimeSpan.FromHours(24);

    /// <summary>Validated upload or fetched bytes → a stored image id, or why the image could not be used.</summary>
    public async Task<(string? Id, string? Error)> SaveAsync(byte[] bytes, string? sourceUrl, string? userId, CancellationToken cancellationToken = default)
    {
        var (encoded, error) = ImageEncoder.Encode(bytes);
        if (encoded is null)
        {
            return (null, error);
        }

        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var relative = Path.Combine(id[..2], id + ".webp");
        var full = Path.Combine(data.MediaDirectory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await WriteAtomicAsync(full, encoded.Full, cancellationToken).ConfigureAwait(false);
        await WriteAtomicAsync(ThumbPath(full), encoded.Thumb, cancellationToken).ConfigureAwait(false);

        db.Images.Add(new Image
        {
            Id = id,
            StoragePath = relative.Replace('\\', '/'),
            Width = encoded.Width,
            Height = encoded.Height,
            Bytes = encoded.Full.LongLength,
            ThumbBytes = encoded.Thumb.LongLength,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(encoded.Full)),
            SourceUrl = sourceUrl,
            CreatedAt = clock.GetUtcNow(),
            CreatedById = userId,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (id, null);
    }

    /// <summary>
    /// ADR 0022: an image anyone may open belongs to a published recipe (finish image, step photo or, ADR 0058, card). Editors may
    /// open any stored image. Anything else is "not found".
    /// </summary>
    public async Task<MediaFile?> FindAsync(string? id, bool thumb, bool canSeeAll, CancellationToken cancellationToken = default)
    {
        if (!IsId(id))
        {
            return null;
        }

        var image = await db.Images.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, cancellationToken).ConfigureAwait(false);
        if (image is null)
        {
            return null;
        }

        if (!canSeeAll)
        {
            var published = await db.Recipes.AnyAsync(r => r.Status == RecipeStatus.Published
                    && (r.FinishImageId == id || r.Steps.Any(s => s.ImageId == id)), cancellationToken)
                .ConfigureAwait(false)
                || await db.RecipeCardImages.AnyAsync(c => c.ImageId == id
                    && db.Recipes.Any(r => r.Id == c.RecipeId && r.Status == RecipeStatus.Published), cancellationToken).ConfigureAwait(false); // ADR 0058 D7
            if (!published)
            {
                return null;
            }
        }

        var full = Path.Combine(data.MediaDirectory, image.StoragePath);
        var path = thumb ? ThumbPath(full) : full;
        return File.Exists(path) ? new MediaFile(path, new FileInfo(path).Length) : null;
    }

    /// <summary>
    /// ADR 0022 clean-up: images older than a day that no recipe (including recipes in the trash) and no step
    /// uses. Purged recipes release their images, which are deleted on the next run.
    /// </summary>
    public async Task<int> PurgeOrphansAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = clock.GetUtcNow() - OrphanAge;
        var candidates = await db.Images.Where(i => i.CreatedAt < cutoff).Select(i => new { i.Id, i.StoragePath }).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return 0;
        }

        var used = (await db.Recipes.IgnoreQueryFilters().Where(r => r.FinishImageId != null).Select(r => r.FinishImageId!).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Concat(await db.Steps.Where(s => s.ImageId != null).Select(s => s.ImageId!).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Concat(await db.RecipeCardImages.Select(c => c.ImageId).ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToHashSet(StringComparer.Ordinal);
        var orphans = candidates.Where(c => !used.Contains(c.Id)).ToList();
        foreach (var orphan in orphans)
        {
            var full = Path.Combine(data.MediaDirectory, orphan.StoragePath);
            File.Delete(full);
            File.Delete(ThumbPath(full));
        }

        var ids = orphans.Select(o => o.Id).ToList();
        await db.Images.Where(i => ids.Contains(i.Id)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        return orphans.Count;
    }

    public async Task<bool> ExistsAsync(string? id, CancellationToken cancellationToken = default) =>
        IsId(id) && await db.Images.AnyAsync(i => i.Id == id, cancellationToken).ConfigureAwait(false);

    /// <summary>32 lower-case hex characters.</summary>
    public static bool IsId(string? id) => id is { Length: 32 } && id.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private static string ThumbPath(string full) => full[..^".webp".Length] + ".thumb.webp";

    private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken ct)
    {
        var temp = path + ".partial";
        await File.WriteAllBytesAsync(temp, bytes, ct).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }
}
