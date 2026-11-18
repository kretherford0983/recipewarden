using RecipeWarden.Cli.ApplyConfig;
using RecipeWarden.Core.Backup;

namespace RecipeWarden.Cli.Tests;

[Trait("Req", "FR-BKP-09")]
public sealed class HostFileSystemTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("rw-fs-").FullName;

    [Fact]
    public void Writes_atomically_reads_back_probes_and_deletes()
    {
        var fs = new HostFileSystem();
        var path = Path.Combine(dir, "sub", "backup-folders.conf");

        fs.Probe(path).ShouldBe(PathKind.Missing);
        fs.ReadAllTextOrNull(path).ShouldBeNull();

        fs.WriteAllTextAtomic(path, "one");
        fs.WriteAllTextAtomic(path, "two");
        fs.ReadAllTextOrNull(path).ShouldBe("two");
        fs.Probe(path).ShouldBe(PathKind.File);
        fs.Probe(dir).ShouldBe(PathKind.Directory);
        Directory.GetFiles(Path.Combine(dir, "sub")).ShouldBe([path]); // no temp file left behind
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }

        fs.Delete(path);
        fs.Delete(path); // idempotent
        fs.Probe(path).ShouldBe(PathKind.Missing);
    }

    public void Dispose() => Directory.Delete(dir, recursive: true);
}
