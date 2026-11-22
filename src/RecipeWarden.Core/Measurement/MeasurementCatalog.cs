namespace RecipeWarden.Core.Measurement;

/// <summary>What the engine knows about a food (section 8.3 Food): its name and, if known, its density.</summary>
public sealed record FoodFacts(int Id, string Name, decimal? DensityGPerMl);

/// <summary>
/// Section 8.3 YieldConversion (BR-26): <see cref="Ratio"/> to <see cref="RatioMax"/> grams bought per gram
/// prepared. <see cref="PreparedDensityGPerMl"/> lets a volume of the prepared food ("4 cups cooked") be weighed.
/// </summary>
public sealed record YieldFacts(int FoodId, string FromState, string ToState, decimal Ratio, decimal RatioMax,
    decimal? PreparedDensityGPerMl, string Basis);

/// <summary>
/// The measurement knowledge the engine works from (9.4): units with their current factors (vague values are
/// editable, FR-MEAS-05), foods and yield conversions. Built by Infrastructure from the database; Core does no I/O.
/// </summary>
public sealed class MeasurementCatalog
{
    private readonly Dictionary<string, UnitDefinition> units;
    private readonly Dictionary<int, FoodFacts> foods;
    private readonly Dictionary<int, List<YieldFacts>> yields;

    public MeasurementCatalog(IEnumerable<UnitDefinition>? units = null, IEnumerable<FoodFacts>? foods = null,
        IEnumerable<YieldFacts>? yields = null)
    {
        this.units = UnitCatalog.All.ToDictionary(u => u.Code, StringComparer.Ordinal);
        foreach (var u in units ?? [])
        {
            this.units[u.Code] = u;
        }

        this.foods = (foods ?? []).ToDictionary(f => f.Id);
        this.yields = (yields ?? []).GroupBy(y => y.FoodId).ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>The built-in units only: no foods, no yields.</summary>
    public static MeasurementCatalog Default { get; } = new();

    public UnitDefinition? Unit(string? code) => code is not null && units.TryGetValue(code, out var u) ? u : null;

    public FoodFacts? Food(int? id) => id is { } i && foods.TryGetValue(i, out var f) ? f : null;

    /// <summary>The yield conversion for a food in a prepared state ("cooked"), if one is known.</summary>
    public YieldFacts? Yield(int? foodId, string? state)
    {
        if (foodId is not { } id || string.IsNullOrWhiteSpace(state) || !yields.TryGetValue(id, out var list))
        {
            return null;
        }

        return list.FirstOrDefault(y => string.Equals(y.FromState, state.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
