using RecipeWarden.Core.Backup;

namespace RecipeWarden.Core.Tests;

[Trait("Req", "SEC-54")]
[Trait("Req", "BR-62")]
public sealed class SEC_54_ArchiveRulesTests
{
    private static readonly string Sha = new('a', 64);

    private static BackupManifest Manifest(params ManifestFile[] files) =>
        new(BackupManifest.FormatName, 1, "0.7.0", "20261123174526_M6_Nutrition", DateTimeOffset.UnixEpoch, "Manual",
            files.Length == 0 ? [new ManifestFile(ArchiveRules.DatabaseEntry, 10, Sha)] : files);

    [Theory]
    [InlineData("manifest.json")]
    [InlineData("db/recipewarden.db")]
    [InlineData("media/ab/abcdef.webp")]
    [InlineData("keys/key-1.xml")]
    [InlineData("config/appsettings.Production.json")]
    [InlineData("media/")]
    [InlineData("db/")]
    public void Expected_entries_are_accepted(string name) => ArchiveRules.CheckEntryName(name).ShouldBeNull();

    [Theory]
    [InlineData("/etc/passwd", "absolute")]
    [InlineData("../../etc/passwd", "..")]
    [InlineData("media/../../etc/cron.d/x", "..")]
    [InlineData("media/./x", "..")]
    [InlineData("media//x", "..")]
    [InlineData("media\\..\\x", "absolute")]
    [InlineData("C:/x", "absolute")]
    [InlineData("db/other.db", "not part")]
    [InlineData("random.txt", "not part")]
    [InlineData("", "no name")]
    [InlineData("media/a\nb", "control")]
    public void Unsafe_or_foreign_entries_are_refused(string name, string fragment) =>
        ArchiveRules.CheckEntryName(name).ShouldNotBeNull().ShouldContain(fragment);

    [Fact]
    public void Very_long_names_are_refused() =>
        ArchiveRules.CheckEntryName("media/" + new string('a', 600)).ShouldNotBeNull();

    [Fact]
    public void A_complete_manifest_passes() => ArchiveRules.CheckManifest(Manifest()).ShouldBeNull();

    [Fact]
    public void Manifest_problems_are_named()
    {
        ArchiveRules.CheckManifest(null).ShouldNotBeNull().ShouldContain("no manifest");
        ArchiveRules.CheckManifest(Manifest() with { Format = "other" }).ShouldNotBeNull().ShouldContain("not a RecipeWarden");
        ArchiveRules.CheckManifest(Manifest() with { FormatVersion = 0 }).ShouldNotBeNull().ShouldContain("incomplete");
        ArchiveRules.CheckManifest(Manifest() with { SchemaVersion = "" }).ShouldNotBeNull().ShouldContain("incomplete");
        ArchiveRules.CheckManifest(Manifest(new ManifestFile("media/a.webp", 1, Sha))).ShouldNotBeNull().ShouldContain("database");
        ArchiveRules.CheckManifest(Manifest(new ManifestFile(ArchiveRules.DatabaseEntry, 1, Sha), new ManifestFile("../x", 1, Sha)))
            .ShouldNotBeNull().ShouldContain("unsafe");
        ArchiveRules.CheckManifest(Manifest(new ManifestFile(ArchiveRules.DatabaseEntry, 1, "xyz"))).ShouldNotBeNull().ShouldContain("SHA-256");
        ArchiveRules.CheckManifest(Manifest(new ManifestFile(ArchiveRules.DatabaseEntry, -1, Sha))).ShouldNotBeNull().ShouldContain("SHA-256");
        ArchiveRules.CheckManifest(Manifest(new ManifestFile(ArchiveRules.DatabaseEntry, 1, Sha), new ManifestFile(ArchiveRules.DatabaseEntry, 1, Sha)))
            .ShouldNotBeNull().ShouldContain("twice");
        ArchiveRules.CheckManifest(Manifest(new ManifestFile(ArchiveRules.DatabaseEntry, 1, Sha), new ManifestFile("manifest.json", 1, Sha)))
            .ShouldNotBeNull().ShouldContain("unsafe");
    }

    [Fact]
    [Trait("Req", "BR-65")]
    public void A_newer_format_asks_for_a_newer_install() =>
        ArchiveRules.CheckManifest(Manifest() with { FormatVersion = 2, AppVersion = "1.4.0" })
            .ShouldNotBeNull().ShouldContain("Install version 1.4.0 or later, then restore");

    [Fact]
    [Trait("Req", "BR-65")]
    [Trait("Req", "AC-85")]
    public void Archives_from_a_newer_version_or_unknown_schema_are_refused_and_older_ones_accepted()
    {
        string[] known = ["20261117_M0", "20261123174526_M6_Nutrition"];
        var m = Manifest();

        ArchiveRules.CheckVersion(m, "0.7.0", known).ShouldBeNull();
        ArchiveRules.CheckVersion(m with { AppVersion = "0.6.0-dev.14" }, "0.7.0", known).ShouldBeNull();
        ArchiveRules.CheckVersion(m with { AppVersion = "0.8.0" }, "0.7.0", known).ShouldNotBeNull().ShouldContain("Install version 0.8.0 or later, then restore");
        ArchiveRules.CheckVersion(m with { SchemaVersion = "20270101_M9" }, "0.7.0", known).ShouldNotBeNull().ShouldContain("Install version");
    }

    [Theory]
    [InlineData("0.7.0", "0.7.0", 0)]
    [InlineData("0.7.0-dev.3", "0.7.0-dev.12", -1)]
    [InlineData("0.7.0-dev.12", "0.7.0", -1)]
    [InlineData("0.7.0", "0.7.0-dev.12", 1)]
    [InlineData("0.7.1", "0.7.0", 1)]
    [InlineData("1.0.0", "0.9.9", 1)]
    [InlineData("v1.0.0+abc", "1.0.0", 0)]
    [InlineData("0.7.0-beta.1", "0.7.0-dev.1", -1)]
    [InlineData("", "0.0.1", -1)]
    public void Versions_compare_as_semantic_versions(string a, string b, int sign) =>
        Math.Sign(ArchiveRules.CompareVersions(a, b)).ShouldBe(sign);

    [Fact]
    public void Unknown_version_still_gives_a_readable_message() =>
        ArchiveRules.InstallNewer(null).ShouldContain("a newer version");
}
