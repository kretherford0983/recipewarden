using RecipeWarden.Core.Backup;

namespace RecipeWarden.Core.Tests;

[Trait("Req", "FR-BKP-09")]
[Trait("Req", "AC-86")]
public sealed class FR_BKP_09_DropInTests
{
    [Fact]
    public void Exactly_one_ReadWritePaths_line_per_folder()
    {
        var content = BackupFoldersDropIn.Render(["/var/lib/recipewarden/backups", "/mnt/nas/recipewarden"])!;

        content.ShouldBe(
            "# Written by \"recipewarden apply-config\" from Backup:AllowedFolders - don't edit\n" +
            "[Service]\n" +
            "ReadWritePaths=\"-/var/lib/recipewarden/backups\"\n" +
            "ReadWritePaths=\"-/mnt/nas/recipewarden\"\n");
    }

    [Fact]
    public void Empty_list_renders_nothing_so_the_file_is_removed() =>
        BackupFoldersDropIn.Render([]).ShouldBeNull();

    [Fact]
    public void Parse_reads_back_rendered_folders()
    {
        string[] folders = ["/a/b", "/mnt/nas share/x"];
        BackupFoldersDropIn.Parse(BackupFoldersDropIn.Render(folders)).ShouldBe(folders);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[Service]\nReadWritePaths=/no/dash\n")]
    public void Parse_ignores_missing_or_foreign_content(string? content) =>
        BackupFoldersDropIn.Parse(content).ShouldBeEmpty();

    [Fact]
    public void Drop_in_never_targets_override_conf() =>
        BackupFoldersDropIn.FilePath.ShouldNotBe(BackupFoldersDropIn.OverrideFilePath);

    [Fact]
    public void Render_requires_a_list() =>
        Should.Throw<ArgumentNullException>(() => BackupFoldersDropIn.Render(null!));
}
