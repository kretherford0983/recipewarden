using RecipeWarden.Core.Backup;

namespace RecipeWarden.Core.Tests;

[Trait("Req", "FR-BKP-10")]
[Trait("Req", "SEC-74")]
public sealed class FR_BKP_10_DestinationRulesTests
{
    private static readonly string[] Allowed = ["/var/lib/recipewarden/backups", "/mnt/nas/recipewarden"];

    [Theory]
    [InlineData("/mnt/nas/recipewarden", "/mnt/nas/recipewarden")]
    [InlineData("/mnt/nas/recipewarden/", "/mnt/nas/recipewarden")]
    [InlineData("//mnt//nas/recipewarden", "/mnt/nas/recipewarden")]
    [InlineData("/var/lib/recipewarden/backups", "/var/lib/recipewarden/backups")]
    [Trait("Req", "AC-87")]
    public void The_saved_value_is_always_the_config_entry(string requested, string saved) =>
        BackupDestinationRules.Resolve(requested, Allowed).ShouldBe(saved);

    [Theory]
    [InlineData("/mnt/nas")]
    [InlineData("/mnt/nas/recipewarden/sub")]
    [InlineData("/etc")]
    [InlineData("mnt/nas/recipewarden")]
    [InlineData("/mnt/nas/recipewarden\n")]
    [InlineData("")]
    [InlineData(null)]
    [Trait("Req", "AC-87")]
    public void Folders_not_in_the_list_are_refused(string? requested) =>
        BackupDestinationRules.Resolve(requested, Allowed).ShouldBeNull();

    [Fact]
    [Trait("Req", "FR-BKP-13")]
    [Trait("Req", "AC-88")]
    public void Archives_are_written_under_a_hidden_partial_name_first()
    {
        var name = BackupDestinationRules.ArchiveName(new DateTimeOffset(2026, 11, 23, 2, 30, 5, TimeSpan.Zero), encrypted: true);
        name.ShouldBe("backup-20261123-023005.tar.gz.rwenc");
        BackupDestinationRules.PartialName(name).ShouldBe(".backup-20261123-023005.tar.gz.rwenc.partial");
        BackupDestinationRules.ArchiveName(DateTimeOffset.UnixEpoch, encrypted: false).ShouldEndWith(".tar.gz");
    }

    [Theory]
    [InlineData(FolderProblem.ReadOnly, null, "apply-config")]
    [InlineData(FolderProblem.ReadOnly, "Read-only file system : '/mnt/x'.", "Read-only file system : '/mnt/x'")]
    [InlineData(FolderProblem.PermissionDenied, null, "owned by the recipewarden user")]
    [InlineData(FolderProblem.MarkerMissing, null, "not mounted or is the wrong location")]
    [InlineData(FolderProblem.NotFound, null, "mounted")]
    [InlineData(FolderProblem.NoSpace, null, "retention")]
    [InlineData(FolderProblem.Other, "boom", "boom")]
    [InlineData(FolderProblem.None, null, "writable")]
    [Trait("Req", "FR-BKP-11")]
    [Trait("Req", "FR-BKP-12")]
    public void Failures_carry_the_os_message_and_a_hint(FolderProblem problem, string? os, string fragment) =>
        BackupDestinationRules.Describe(problem, os).ShouldContain(fragment);

    [Theory]
    [InlineData("short", "short", "at least 12")]
    [InlineData("long enough phrase", "different phrase!!", "do not match")]
    [Trait("Req", "FR-BKP-15")]
    public void New_passphrases_are_checked(string passphrase, string confirm, string fragment) =>
        RestoreRules.ValidateNewPassphrase(passphrase, confirm).ShouldNotBeNull().ShouldContain(fragment);

    [Fact]
    [Trait("Req", "FR-BKP-15")]
    public void Good_passphrase_and_confirmation_word()
    {
        RestoreRules.ValidateNewPassphrase("correct horse battery", "correct horse battery").ShouldBeNull();
        RestoreRules.ValidateNewPassphrase(new string('x', 1025), new string('x', 1025)).ShouldNotBeNull();
        RestoreRules.IsConfirmed(" RESTORE ").ShouldBeTrue();
        RestoreRules.IsConfirmed("restore").ShouldBeFalse();
        RestoreRules.IsConfirmed(null).ShouldBeFalse();
    }
}
