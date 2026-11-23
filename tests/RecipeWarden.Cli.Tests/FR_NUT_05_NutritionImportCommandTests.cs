using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Configuration;
using RecipeWarden.Cli.Nutrition;

namespace RecipeWarden.Cli.Tests;

/// <summary>FR-NUT-05, AC-58, ADR 0034: "recipewarden nutrition-import" applies a dataset file from the server.</summary>
[Trait("Req", "FR-NUT-05")]
public sealed class FR_NUT_05_NutritionImportCommandTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("rw-cli-nut-").FullName;

    private IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Data:Directory"] = dir })
        .Build();

    private string Zip(string name)
    {
        var path = Path.Combine(dir, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Add(string entry, string text)
        {
            using var w = new StreamWriter(zip.CreateEntry(entry).Open(), Encoding.UTF8);
            w.Write(text);
        }

        Add("food.csv", "fdc_id,data_type,description\n171287,sr_legacy_food,\"Egg, whole, raw, fresh\"\n");
        Add("food_nutrient.csv", "fdc_id,nutrient_id,amount\n171287,1008,150\n");
        return path;
    }

    [Fact]
    [Trait("Req", "AC-58")]
    public async Task A_dataset_file_is_applied_and_reported()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await NutritionImportCommand.RunAsync(Config(), Zip("sr-2026.zip"), null, output, error, TestContext.Current.CancellationToken);
        exit.ShouldBe(0, error.ToString());
        output.ToString().ShouldStartWith("Nutrient dataset \"sr-2026\": 1 foods (0 new, 1 updated, 7,792 no longer in the data");
    }

    [Fact]
    public async Task Missing_wrong_or_broken_files_are_refused()
    {
        var error = new StringWriter();
        (await NutritionImportCommand.RunAsync(Config(), Path.Combine(dir, "none.zip"), null, TextWriter.Null, error, TestContext.Current.CancellationToken)).ShouldBe(1);
        error.ToString().ShouldContain("does not exist");

        var txt = Path.Combine(dir, "data.txt");
        await File.WriteAllTextAsync(txt, "x", TestContext.Current.CancellationToken);
        (await NutritionImportCommand.RunAsync(Config(), txt, null, TextWriter.Null, error, TestContext.Current.CancellationToken)).ShouldBe(1);
        error.ToString().ShouldContain("FoodData Central CSV .zip");

        var bad = Path.Combine(dir, "bad.zip");
        await File.WriteAllTextAsync(bad, "not a zip", TestContext.Current.CancellationToken);
        (await NutritionImportCommand.RunAsync(Config(), bad, "v", TextWriter.Null, error, TestContext.Current.CancellationToken)).ShouldBe(1);
        error.ToString().ShouldContain("not a FoodData Central CSV zip");
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(dir, recursive: true);
    }
}
