using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Nutrition;
using RecipeWarden.Core.Organization;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Nutrition;

/// <summary>What an import did (FR-NUT-05, AC-58).</summary>
public sealed record NutrientImportResult(bool Ok, string? Error, string Version, int Foods, int Added, int Updated, int Removed);

/// <summary>
/// D-06, FR-NUT-05, AC-58, ADR 0034: reads a USDA FoodData Central CSV zip (SR Legacy or Foundation Foods; the full
/// download or the trimmed seed) and upserts nutrient rows by FDC id. Foods missing from a newer file of the same kind
/// are kept and flagged, so mappings survive. Reads a file only; never fetches anything.
/// </summary>
public sealed class NutrientDatasetImporter(AppDbContext db, TimeProvider clock)
{
    public const string SeedVersion = "SR Legacy 2018-04";
    public const string SeedRunName = "nutrients-sr-legacy-2018-04";
    public const string SeedResource = "RecipeWarden.Seed.fdc-sr-legacy-2018-04.zip";
    public const long MaxEntryBytes = 2L * 1024 * 1024 * 1024;
    public const int MaxEntries = 200;

    public const string NotFdc = "This is not a FoodData Central CSV zip (it needs food.csv, nutrient.csv and food_nutrient.csv).";
    public const string NoFoods = "The file has no SR Legacy or Foundation Foods rows.";

    public static readonly IReadOnlySet<string> DataTypes = new HashSet<string>(StringComparer.Ordinal) { "sr_legacy_food", "foundation_food" };

    private enum Panel { Calories, Fat, SatFat, Cholesterol, Sodium, Carbs, Fiber, Sugar, Protein }

    /// <summary>Nutrient ids, best first per panel value (energy: kcal, then Atwater general and specific).</summary>
    private static readonly Dictionary<int, (Panel Panel, int Rank)> Nutrients = new()
    {
        [1008] = (Panel.Calories, 0), [2047] = (Panel.Calories, 1), [2048] = (Panel.Calories, 2),
        [1004] = (Panel.Fat, 0), [1258] = (Panel.SatFat, 0), [1253] = (Panel.Cholesterol, 0), [1093] = (Panel.Sodium, 0),
        [1005] = (Panel.Carbs, 0), [1079] = (Panel.Fiber, 0), [2000] = (Panel.Sugar, 0), [1063] = (Panel.Sugar, 1),
        [1003] = (Panel.Protein, 0),
    };

    private sealed class Row
    {
        public required int FdcId { get; init; }
        public required string DataType { get; init; }
        public required string Description { get; init; }
        public string? Category { get; init; }
        public Dictionary<Panel, (decimal Value, int Rank)> Values { get; } = [];
        public Dictionary<string, decimal> Portions { get; } = new(StringComparer.Ordinal);
        public decimal? Density { get; set; }
    }

    /// <summary>Seed run: applies the bundled SR Legacy zip once (ADR 0034).</summary>
    public async Task<bool> ApplySeedAsync(CancellationToken cancellationToken = default)
    {
        if (await db.SeedRuns.AnyAsync(r => r.Name == SeedRunName, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        await using var stream = typeof(NutrientDatasetImporter).Assembly.GetManifestResourceStream(SeedResource)
            ?? throw new InvalidOperationException("The bundled nutrient dataset is missing.");
        var result = await ImportAsync(stream, SeedVersion, "seed", cancellationToken).ConfigureAwait(false);
        if (!result.Ok)
        {
            throw new InvalidOperationException("The bundled nutrient dataset could not be read: " + result.Error);
        }

        db.SeedRuns.Add(new SeedRun { Name = SeedRunName, AppliedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<NutrientImportResult> ImportAsync(Stream zipStream, string version, string appliedBy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zipStream);
        version = string.IsNullOrWhiteSpace(version) ? "unnamed" : version.Trim();
        if (version.Length > 100)
        {
            version = version[..100];
        }

        // Hash and read from a seekable copy (uploads and resources may not seek).
        await using var buffer = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        await zipStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        buffer.Position = 0;
        var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(buffer, cancellationToken).ConfigureAwait(false));
        buffer.Position = 0;

        Dictionary<int, Row> rows;
        try
        {
            using var zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count > MaxEntries)
            {
                return Fail(NotFdc);
            }

            ZipArchiveEntry? Entry(string name) => zip.Entries.FirstOrDefault(e =>
                string.Equals(Path.GetFileName(e.FullName), name, StringComparison.OrdinalIgnoreCase) && e.Length <= MaxEntryBytes);

            if (Entry("food.csv") is not { } foodEntry || Entry("food_nutrient.csv") is not { } nutrientEntry)
            {
                return Fail(NotFdc);
            }

            var categories = ReadCategories(Entry("food_category.csv"));
            rows = ReadFoods(foodEntry, categories);
            if (rows.Count == 0)
            {
                return Fail(NoFoods);
            }

            ReadNutrients(nutrientEntry, rows, cancellationToken);
            ReadPortions(Entry("food_portion.csv"), ReadUnits(Entry("measure_unit.csv")), rows, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or FormatException)
        {
            return Fail(NotFdc);
        }

        return await SaveAsync(rows, version, sha, appliedBy, cancellationToken).ConfigureAwait(false);

        NutrientImportResult Fail(string error) => new(false, error, version, 0, 0, 0, 0);
    }

    private async Task<NutrientImportResult> SaveAsync(Dictionary<int, Row> rows, string version, string sha, string appliedBy, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var types = rows.Values.Select(r => r.DataType).ToHashSet(StringComparer.Ordinal);
        var existing = await db.NutrientRefs.Where(n => n.FdcId != null).ToDictionaryAsync(n => n.FdcId!.Value, cancellationToken).ConfigureAwait(false);
        int added = 0, updated = 0, removed = 0;
        var auto = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            foreach (var row in rows.Values)
            {
                if (!existing.TryGetValue(row.FdcId, out var entity))
                {
                    entity = new NutrientRef { FdcId = row.FdcId, Source = NutrientSources.Usda, CreatedAt = now };
                    db.NutrientRefs.Add(entity);
                    added++;
                }
                else
                {
                    updated++;
                }

                entity.DataType = row.DataType;
                entity.Description = row.Description.Length > 300 ? row.Description[..300] : row.Description;
                entity.NormalizedDescription = NameRules.Normalize(entity.Description);
                entity.Category = row.Category is { Length: > 100 } c ? c[..100] : row.Category;
                entity.DatasetVersion = version;
                entity.RemovedInVersion = null;
                entity.DensityGPerMl = row.Density;
                entity.PortionsJson = row.Portions.Count == 0 ? null : JsonSerializer.Serialize(row.Portions.Take(30).ToDictionary());
                entity.SetValues(new NutrientValues(
                    Value(row, Panel.Calories), Value(row, Panel.Fat), Value(row, Panel.SatFat), Value(row, Panel.Cholesterol),
                    Value(row, Panel.Sodium), Value(row, Panel.Carbs), Value(row, Panel.Fiber), Value(row, Panel.Sugar), Value(row, Panel.Protein)));
                entity.UpdatedAt = now;
                if (existing.ContainsKey(row.FdcId))
                {
                    db.Entry(entity).State = EntityState.Modified;
                }
            }

            foreach (var gone in existing.Values.Where(n => n.RemovedInVersion is null && n.DataType is { } t && types.Contains(t)
                                                           && !rows.ContainsKey(n.FdcId!.Value)))
            {
                gone.RemovedInVersion = version;
                gone.UpdatedAt = now;
                db.Entry(gone).State = EntityState.Modified;
                removed++;
            }

            db.NutrientDatasets.Add(new NutrientDataset
            {
                Version = version, FileSha256 = sha, Foods = rows.Count, Added = added, Updated = updated, Removed = removed,
                AppliedAt = now, AppliedBy = appliedBy.Length > 100 ? appliedBy[..100] : appliedBy,
            });
            db.ChangeTracker.DetectChanges();
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            db.ChangeTracker.AutoDetectChangesEnabled = auto;
        }

        return new NutrientImportResult(true, null, version, rows.Count, added, updated, removed);
    }

    private static decimal? Value(Row row, Panel panel) => row.Values.TryGetValue(panel, out var v) ? v.Value : null;

    private static Dictionary<string, string> ReadCategories(ZipArchiveEntry? entry)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (entry is null)
        {
            return result;
        }

        using var reader = new StreamReader(entry.Open());
        var csv = new FdcCsv(reader);
        if (!csv.ReadHeader())
        {
            return result;
        }

        while (csv.ReadRow() is { } row)
        {
            if (FdcCsv.Get(row, csv, "id") is { } id && FdcCsv.Get(row, csv, "description") is { } d)
            {
                result[id] = d;
            }
        }

        return result;
    }

    private static Dictionary<string, string> ReadUnits(ZipArchiveEntry? entry)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (entry is null)
        {
            return result;
        }

        using var reader = new StreamReader(entry.Open());
        var csv = new FdcCsv(reader);
        if (!csv.ReadHeader())
        {
            return result;
        }

        while (csv.ReadRow() is { } row)
        {
            if (FdcCsv.Get(row, csv, "id") is { } id && FdcCsv.Get(row, csv, "name") is { } name)
            {
                result[id] = name;
            }
        }

        return result;
    }

    private static Dictionary<int, Row> ReadFoods(ZipArchiveEntry entry, Dictionary<string, string> categories)
    {
        var rows = new Dictionary<int, Row>();
        using var reader = new StreamReader(entry.Open());
        var csv = new FdcCsv(reader);
        if (!csv.ReadHeader() || !csv.Has("fdc_id") || !csv.Has("data_type") || !csv.Has("description"))
        {
            throw new InvalidDataException("food.csv");
        }

        while (csv.ReadRow() is { } row)
        {
            var type = FdcCsv.Get(row, csv, "data_type");
            if (type is null || !DataTypes.Contains(type) || !int.TryParse(FdcCsv.Get(row, csv, "fdc_id"), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            var description = FdcCsv.Get(row, csv, "description")?.Trim();
            if (string.IsNullOrEmpty(description))
            {
                continue;
            }

            var category = FdcCsv.Get(row, csv, "food_category_id") is { } cid && categories.TryGetValue(cid, out var name) ? name : null;
            rows[id] = new Row { FdcId = id, DataType = type, Description = description, Category = category };
        }

        return rows;
    }

    private static void ReadNutrients(ZipArchiveEntry entry, Dictionary<int, Row> rows, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(entry.Open());
        var csv = new FdcCsv(reader);
        if (!csv.ReadHeader() || !csv.Has("fdc_id") || !csv.Has("nutrient_id") || !csv.Has("amount"))
        {
            throw new InvalidDataException("food_nutrient.csv");
        }

        var n = 0;
        while (csv.ReadRow() is { } row)
        {
            if (++n % 100_000 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!int.TryParse(FdcCsv.Get(row, csv, "nutrient_id"), NumberStyles.None, CultureInfo.InvariantCulture, out var nid)
                || !Nutrients.TryGetValue(nid, out var panel)
                || !int.TryParse(FdcCsv.Get(row, csv, "fdc_id"), NumberStyles.None, CultureInfo.InvariantCulture, out var fid)
                || !rows.TryGetValue(fid, out var food)
                || !decimal.TryParse(FdcCsv.Get(row, csv, "amount"), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)
                || amount < 0 || amount > 1_000_000)
            {
                continue;
            }

            if (!food.Values.TryGetValue(panel.Panel, out var current) || panel.Rank < current.Rank)
            {
                food.Values[panel.Panel] = (amount, panel.Rank);
            }
        }
    }

    private static void ReadPortions(ZipArchiveEntry? entry, Dictionary<string, string> units, Dictionary<int, Row> rows, CancellationToken cancellationToken)
    {
        if (entry is null)
        {
            return;
        }

        using var reader = new StreamReader(entry.Open());
        var csv = new FdcCsv(reader);
        if (!csv.ReadHeader())
        {
            return;
        }

        var n = 0;
        while (csv.ReadRow() is { } row)
        {
            if (++n % 100_000 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!int.TryParse(FdcCsv.Get(row, csv, "fdc_id"), NumberStyles.None, CultureInfo.InvariantCulture, out var fid)
                || !rows.TryGetValue(fid, out var food)
                || !decimal.TryParse(FdcCsv.Get(row, csv, "gram_weight"), NumberStyles.Float, CultureInfo.InvariantCulture, out var grams) || grams <= 0)
            {
                continue;
            }

            var amount = decimal.TryParse(FdcCsv.Get(row, csv, "amount"), NumberStyles.Float, CultureInfo.InvariantCulture, out var a) && a > 0 ? a : 1m;
            var unit = FdcCsv.Get(row, csv, "measure_unit_id") is { } uid && units.TryGetValue(uid, out var u) ? u : null;
            var modifier = FdcCsv.Get(row, csv, "modifier");
            if (string.IsNullOrWhiteSpace(modifier))
            {
                modifier = FdcCsv.Get(row, csv, "portion_description");
            }

            food.Density ??= NutritionRules.DensityFromPortion(modifier, unit, amount, grams);
            if (NutritionRules.PortionKey(modifier, unit) is { } key && !food.Portions.ContainsKey(key))
            {
                food.Portions[key] = Math.Round(grams / amount, 2);
            }
        }
    }
}
