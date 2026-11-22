using RecipeWarden.Core.Measurement;

namespace RecipeWarden.Core.Kitchen;

/// <summary>
/// The active tools the display planner may use (FR-TOOL-03, ADR 0018): spoons and cups, and the best scale.
/// Liquid measures, appliances and thermometers are inventory only. With no spoons or cups active, BR-33's standard
/// set is assumed and <see cref="IsAssumedStandard"/> says so.
/// </summary>
public sealed class KitchenTools
{
    public const string AssumedNote = "No measuring spoons or cups are in My Kitchen, so a standard US set is assumed.";

    private KitchenTools(IReadOnlyList<KitchenToolInfo> measures, KitchenToolInfo? scale, bool assumed, IReadOnlyList<KitchenToolInfo> all)
    {
        Measures = measures;
        Scale = scale;
        IsAssumedStandard = assumed;
        All = all;
    }

    /// <summary>Spoons and cups, largest first.</summary>
    public IReadOnlyList<KitchenToolInfo> Measures { get; }

    /// <summary>The finest active scale, or null.</summary>
    public KitchenToolInfo? Scale { get; }

    public bool IsAssumedStandard { get; }

    /// <summary>Every active tool (containers and pans included).</summary>
    public IReadOnlyList<KitchenToolInfo> All { get; }

    public IEnumerable<KitchenToolInfo> Containers => All.Where(t => t.Type == ToolType.Container);

    public IEnumerable<KitchenToolInfo> Pans => All.Where(t => t.Type == ToolType.Pan);

    /// <summary>BR-33's set, nothing else.</summary>
    public static KitchenTools Standard { get; } = From([]);

    public static KitchenTools From(IEnumerable<KitchenToolInfo> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        var active = tools.Where(t => t.IsActive).ToList();
        var measures = active.Where(t => t.Type is ToolType.MeasuringSpoon or ToolType.MeasuringCup && t.CapacityMl > 0m)
            .GroupBy(t => decimal.Round(t.CapacityMl!.Value, 4)).Select(g => g.First())
            .OrderByDescending(t => t.CapacityMl).ToList();
        var assumed = measures.Count == 0;
        if (assumed)
        {
            measures = KitchenRules.StandardSet.OrderByDescending(t => t.CapacityMl).ToList();
        }

        var scale = active.Where(t => t.Type == ToolType.Scale && t.Resolution > 0m)
            .OrderBy(t => GramsPerStep(t)).FirstOrDefault();
        return new KitchenTools(measures, scale, assumed, active);
    }

    /// <summary>The scale's step in grams (0.05 oz ≈ 1.42 g).</summary>
    public static decimal GramsPerStep(KitchenToolInfo scale)
    {
        ArgumentNullException.ThrowIfNull(scale);
        return scale.Resolution!.Value * (scale.ResolutionUnit == "oz" ? UnitCatalog.FindByCode("oz")!.ToBase : 1m);
    }
}
