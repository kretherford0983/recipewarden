using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Infrastructure.Data;

namespace RecipeWarden.Infrastructure.Measurement;

/// <summary>
/// ADR 0013: inserts the USDA seed (foods, aliases, yield conversions) once. A <see cref="SeedRun"/> row records that
/// it ran, so foods an editor deletes stay deleted, and rows an editor changed are never overwritten. Names that
/// already exist (an editor's own food) are kept and the seed row is skipped.
/// </summary>
public sealed class MeasurementSeeder(AppDbContext db, TimeProvider clock)
{
    public const string SeedName = "measurement-v1";
    private const string ResourceName = "RecipeWarden.Seed.measurement-v1.json";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    internal sealed record SeedFood(string Name, string PluralName, string? Category, IReadOnlyList<string> Aliases, int? FdcId,
        decimal? DensityGPerMl, string? SourceNote);

    internal sealed record SeedYield(string Food, string FromState, string ToState, decimal Ratio, decimal RatioMax,
        decimal? PreparedDensityGPerMl, string Basis, string? SourceNote);

    internal sealed record SeedDocument(int Version, IReadOnlyList<SeedFood> Foods, IReadOnlyList<SeedYield> Yields);

    /// <summary>Returns true when the seed was applied by this call.</summary>
    public async Task<bool> ApplyAsync(CancellationToken cancellationToken = default)
    {
        if (await db.SeedRuns.AnyAsync(r => r.Name == SeedName, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var doc = Load();
        var now = clock.GetUtcNow();
        var names = await db.Foods.Select(f => f.NormalizedName).ToListAsync(cancellationToken).ConfigureAwait(false);
        var aliases = await db.FoodAliases.Select(a => a.NormalizedAlias).ToListAsync(cancellationToken).ConfigureAwait(false);
        var takenNames = names.ToHashSet(StringComparer.Ordinal);
        var takenAliases = aliases.Concat(names).ToHashSet(StringComparer.Ordinal);

        var added = new Dictionary<string, Food>(StringComparer.Ordinal);
        foreach (var f in doc.Foods)
        {
            var key = FoodMatcher.Normalize(f.Name);
            if (!takenNames.Add(key))
            {
                continue;
            }

            takenAliases.Add(key);
            var food = new Food
            {
                Name = f.Name,
                NormalizedName = key,
                PluralName = f.PluralName,
                Category = f.Category,
                DensityGPerMl = f.DensityGPerMl,
                FdcId = f.FdcId,
                Source = FoodSources.Usda,
                SourceNote = f.SourceNote,
                CreatedAt = now,
                UpdatedAt = now,
            };
            foreach (var alias in f.Aliases)
            {
                var aliasKey = FoodMatcher.Normalize(alias);
                if (aliasKey.Length > 0 && takenAliases.Add(aliasKey))
                {
                    food.Aliases.Add(new FoodAlias { Alias = alias, NormalizedAlias = aliasKey });
                }
            }

            db.Foods.Add(food);
            added[key] = food;
        }

        foreach (var y in doc.Yields)
        {
            if (!added.TryGetValue(FoodMatcher.Normalize(y.Food), out var food))
            {
                continue; // the household already had its own food of that name; leave its conversions to the household
            }

            db.YieldConversions.Add(new YieldConversion
            {
                Food = food,
                FromState = y.FromState,
                ToState = y.ToState,
                FromUnitKind = y.PreparedDensityGPerMl is null ? UnitKind.Mass : UnitKind.Volume,
                Ratio = y.Ratio,
                RatioMax = y.RatioMax,
                PreparedDensityGPerMl = y.PreparedDensityGPerMl,
                Basis = y.Basis,
                Source = FoodSources.Usda,
                SourceNote = y.SourceNote,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        db.SeedRuns.Add(new SeedRun { Name = SeedName, AppliedAt = now });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    internal static SeedDocument Load()
    {
        using var stream = typeof(MeasurementSeeder).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Seed resource {ResourceName} is missing from the build.");
        return JsonSerializer.Deserialize<SeedDocument>(stream, Json)
            ?? throw new InvalidOperationException($"Seed resource {ResourceName} is empty.");
    }
}
