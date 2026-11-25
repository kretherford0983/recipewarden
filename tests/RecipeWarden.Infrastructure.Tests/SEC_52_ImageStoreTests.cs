using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Infrastructure.Media;
using RecipeWarden.Tests.Shared;
using SkiaSharp;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>SEC-52, ADR 0022, AC-11: images are re-encoded as WebP, stripped, resized, and served only when allowed.</summary>
[Trait("Req", "SEC-52")]
[Trait("Req", "ADR-0022")]
public sealed class SEC_52_ImageStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Req", "AC-11")]
    public void Exif_and_gps_are_removed_and_orientation_applied()
    {
        var jpeg = TestImages.JpegWithExif(orientation: 6, description: "SECRET-SPOT", width: 300, height: 200);
        TestImages.Contains(jpeg, "SECRET-SPOT").ShouldBeTrue();

        var (image, error) = ImageEncoder.Encode(jpeg);

        error.ShouldBeNull();
        TestImages.Contains(image!.Full, "Exif").ShouldBeFalse();
        TestImages.Contains(image.Full, "SECRET-SPOT").ShouldBeFalse();
        TestImages.Contains(image.Thumb, "Exif").ShouldBeFalse();
        (image.Width, image.Height).ShouldBe((200, 300)); // rotated upright
        SKCodec.Create(SKData.CreateCopy(image.Full))!.EncodedFormat.ShouldBe(SKEncodedImageFormat.Webp);
    }

    [Fact]
    public void Large_images_are_scaled_to_2400_and_thumbnails_to_640()
    {
        var (image, _) = ImageEncoder.Encode(TestImages.Png(3000, 1500));
        (image!.Width, image.Height).ShouldBe((2400, 1200));
        using var thumb = SKBitmap.Decode(image.Thumb);
        (thumb.Width, thumb.Height).ShouldBe((640, 320));

        var (small, _) = ImageEncoder.Encode(TestImages.Jpeg(100, 50));
        (small!.Width, small.Height).ShouldBe((100, 50));
    }

    [Fact]
    public void Bombs_other_formats_and_junk_are_refused()
    {
        ImageEncoder.Encode(TestImages.PngHeader(20000, 20000)).Error.ShouldBe(ImageEncoder.TooManyPixels);
        ImageEncoder.Encode(TestImages.Encode(10, 10, SKEncodedImageFormat.Png).Take(20).ToArray()).Error.ShouldNotBeNull();
        ImageEncoder.Encode("not an image"u8.ToArray()).Error.ShouldBe(ImageEncoder.Unreadable);
        ImageEncoder.Encode("GIF89a\u0001\0\u0001\0\0\0\0;"u8.ToArray()).Error.ShouldNotBeNull();
    }

    [Fact]
    public async Task Stored_images_get_random_ids_and_live_outside_the_web_root()
    {
        await using var t = await RecipeTestScope.CreateAsync();
        var (id, error) = await t.Images.SaveAsync(TestImages.Jpeg(), "https://example.com/a.jpg", RecipeTestScope.UserId, Ct);

        error.ShouldBeNull();
        ImageStore.IsId(id).ShouldBeTrue();
        var row = await t.Db.Images.SingleAsync(i => i.Id == id, Ct);
        row.ContentType.ShouldBe("image/webp");
        row.SourceUrl.ShouldBe("https://example.com/a.jpg");
        row.Sha256.Length.ShouldBe(64);
        File.Exists(Path.Combine(t.Data.MediaDirectory, row.StoragePath)).ShouldBeTrue();
        Directory.GetFiles(t.Data.MediaDirectory, "*", SearchOption.AllDirectories).Length.ShouldBe(2);
    }

    [Fact]
    [Trait("Req", "ADR-0022")]
    public async Task Only_published_recipes_images_are_open_to_everyone()
    {
        await using var t = await RecipeTestScope.CreateAsync();
        var (id, _) = await t.Images.SaveAsync(TestImages.Jpeg(), null, RecipeTestScope.UserId, Ct);

        (await t.Images.FindAsync(id, thumb: false, canSeeAll: false, Ct)).ShouldBeNull(); // not attached
        (await t.Images.FindAsync(id, thumb: false, canSeeAll: true, Ct)).ShouldNotBeNull(); // editors

        var draft = await t.AddAsync("Draft Soup", publish: false);
        await t.Recipes.SetFinishImageAsync(draft.Id, id, RecipeTestScope.UserId, Ct);
        (await t.Images.FindAsync(id, false, false, Ct)).ShouldBeNull();

        var published = await t.AddAsync("Published Soup");
        await t.Recipes.SetStepImageAsync(published.Id, 0, id, RecipeTestScope.UserId, Ct);
        var file = await t.Images.FindAsync(id, false, false, Ct);
        file.ShouldNotBeNull();
        (await t.Images.FindAsync(id, thumb: true, false, Ct))!.Path.ShouldEndWith(".thumb.webp");

        (await t.Images.FindAsync("../../etc/passwd", false, true, Ct)).ShouldBeNull();
        (await t.Images.FindAsync(new string('a', 32), false, true, Ct)).ShouldBeNull();
        (await t.Images.FindAsync(id!.ToUpperInvariant(), false, true, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Orphans_older_than_a_day_are_deleted_and_used_images_kept()
    {
        var clock = new ManualClock();
        await using var t = await RecipeTestScope.CreateAsync(clock);
        var (orphan, _) = await t.Images.SaveAsync(TestImages.Jpeg(), null, null, Ct);
        var (used, _) = await t.Images.SaveAsync(TestImages.Jpeg(), null, null, Ct);
        var (trashed, _) = await t.Images.SaveAsync(TestImages.Jpeg(), null, null, Ct);
        var keep = await t.AddAsync("Keep");
        await t.Recipes.SetFinishImageAsync(keep.Id, used, RecipeTestScope.UserId, Ct);
        var bin = await t.AddAsync("Bin");
        await t.Recipes.SetFinishImageAsync(bin.Id, trashed, RecipeTestScope.UserId, Ct);
        await t.Recipes.DeleteAsync(bin.Id, RecipeTestScope.UserId, Ct);

        (await t.Images.PurgeOrphansAsync(Ct)).ShouldBe(0); // too new
        clock.Advance(TimeSpan.FromHours(25));
        (await t.Images.PurgeOrphansAsync(Ct)).ShouldBe(1);

        (await t.Images.ExistsAsync(orphan, Ct)).ShouldBeFalse();
        (await t.Images.ExistsAsync(used, Ct)).ShouldBeTrue();
        (await t.Images.ExistsAsync(trashed, Ct)).ShouldBeTrue();
        Directory.GetFiles(t.Data.MediaDirectory, "*", SearchOption.AllDirectories).Length.ShouldBe(4);
    }

    [Fact]
    [Trait("Req", "FR-SRCH-02")]
    public async Task Has_image_filters_the_list_and_cards_carry_the_image()
    {
        await using var t = await RecipeTestScope.CreateAsync();
        var (id, _) = await t.Images.SaveAsync(TestImages.Jpeg(), null, null, Ct);
        var with = await t.AddAsync("Photo Pie");
        await t.AddAsync("Plain Pie");
        await t.Recipes.SetFinishImageAsync(with.Id, id, RecipeTestScope.UserId, Ct);

        var page = await t.Search.SearchAsync(Core.Search.SearchCriteria.Parse(new Core.Search.SearchInput { HasImage = "1" }), cancellationToken: Ct);
        page.Items.ShouldHaveSingleItem().ImageId.ShouldBe(id);
        (await t.SearchAsync()).Items.Count.ShouldBe(2);
    }
}
