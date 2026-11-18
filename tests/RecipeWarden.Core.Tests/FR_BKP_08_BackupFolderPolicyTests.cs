using RecipeWarden.Core.Backup;

namespace RecipeWarden.Core.Tests;

[Trait("Req", "FR-BKP-08")]
[Trait("Req", "AC-86")]
public sealed class FR_BKP_08_BackupFolderPolicyTests
{
    private static PathKind AllDirectories(string _) => PathKind.Directory;

    [Theory]
    [InlineData("backups", "absolute")]
    [InlineData("./backups", "absolute")]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("/home/alice/backups", "/home")]
    [InlineData("/home", "/home")]
    [InlineData("/root/backups", "/root")]
    [InlineData("/run/user/1000/x", "/run/user")]
    [InlineData("/mnt/nas/../../home/x", "..")]
    [InlineData("/mnt/./nas", "..")]
    [InlineData("/mnt/na\"s", "must not contain")]
    [InlineData("/mnt/nas%i", "must not contain")]
    [InlineData("/mnt/na\\s", "must not contain")]
    [InlineData("/mnt/na\ns", "control")]
    [InlineData("/", "root folder")]
    public void Invalid_entries_are_rejected_with_a_reason(string folder, string reasonFragment)
    {
        var result = BackupFolderPolicy.Validate([folder], AllDirectories);

        result.IsValid.ShouldBeFalse();
        result.Folders.ShouldBeEmpty();
        result.Errors.Single().Reason.ShouldContain(reasonFragment, Case.Insensitive);
    }

    [Fact]
    public void Null_entry_is_rejected()
    {
        var result = BackupFolderPolicy.Validate([null], AllDirectories);
        result.Errors.Single().Reason.ShouldContain("empty");
    }

    [Theory]
    [InlineData("/homeland/backups")]
    [InlineData("/rooted")]
    [InlineData("/run/users-not")]
    public void Similar_names_outside_forbidden_trees_are_allowed(string folder) =>
        BackupFolderPolicy.Validate([folder], AllDirectories).IsValid.ShouldBeTrue();

    [Fact]
    public void Missing_folder_is_a_warning_not_an_error()
    {
        var result = BackupFolderPolicy.Validate(["/mnt/nas/recipewarden"], _ => PathKind.Missing);

        result.IsValid.ShouldBeTrue();
        result.Folders.ShouldBe(["/mnt/nas/recipewarden"]);
        result.Warnings.Single().Folder.ShouldBe("/mnt/nas/recipewarden");
    }

    [Fact]
    public void Existing_file_is_an_error()
    {
        var result = BackupFolderPolicy.Validate(["/var/lib/recipewarden/backups"], _ => PathKind.File);
        result.IsValid.ShouldBeFalse();
        result.Errors.Single().Reason.ShouldContain("file");
    }

    [Fact]
    public void Duplicates_and_trailing_slashes_are_normalized()
    {
        var result = BackupFolderPolicy.Validate(
            ["/mnt/nas/recipewarden/", "/mnt/nas//recipewarden", "/var/lib/recipewarden/backups", "/mnt/nas/recipewarden"],
            AllDirectories);

        result.IsValid.ShouldBeTrue();
        result.Folders.ShouldBe(["/mnt/nas/recipewarden", "/var/lib/recipewarden/backups"]);
    }

    [Fact]
    public void One_bad_entry_fails_the_whole_list_but_reports_each_problem()
    {
        var result = BackupFolderPolicy.Validate(["relative", "/home/x", "/mnt/ok"], AllDirectories);

        result.IsValid.ShouldBeFalse();
        result.Errors.Count.ShouldBe(2);
    }

    [Fact]
    public void Empty_list_is_valid() =>
        BackupFolderPolicy.Validate([], AllDirectories).ShouldSatisfyAllConditions(
            r => r.IsValid.ShouldBeTrue(),
            r => r.Folders.ShouldBeEmpty());

    [Fact]
    public void Arguments_are_required()
    {
        Should.Throw<ArgumentNullException>(() => BackupFolderPolicy.Validate(null!, AllDirectories));
        Should.Throw<ArgumentNullException>(() => BackupFolderPolicy.Validate([], null!));
        Should.Throw<ArgumentNullException>(() => BackupFolderPolicy.Normalize(null!));
    }
}
