using RecipeWarden.Core.Kitchen;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Importers.Ingredients;
using RecipeWarden.Infrastructure.Kitchen;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.Measurement;

/// <summary>An ingredient line as the scaled view shows it; <see cref="Index"/> numbers lines across groups.</summary>
public sealed record ScaledLine(int Index, string Text, string RawText, bool NeedsReview, bool CanScaleFrom, string? Secondary,
    string? Hint, string? YieldNote, decimal? PurchaseGrams, decimal? PurchaseGramsMax)
{
    /// <summary>9.4 "Explain", UX-06: the plan's exact amount and error, for the tooltip.</summary>
    public string? Explanation { get; init; }

    public bool ByWeight { get; init; }

    /// <summary>True when the shown amount differs from the line as written (UX-06 marker).</summary>
    public bool Changed { get; init; }
}

/// <summary>The yield a container fit uses (ADR 0019), or why there is none, and the containers in My Kitchen.</summary>
public sealed record ContainerSetup(FinishedYield? Yield, string? Error, IReadOnlyList<KitchenToolInfo> Containers);

public sealed record ScaledGroup(string? Name, IReadOnlyList<ScaledLine> Lines);

/// <summary>FR-SCL-01/03, FR-MEAS-01 to 07: the reading view at a scale and in a unit system. Never stored.</summary>
public sealed record ScaledRecipe(decimal Factor, DisplayUnits Units, decimal Servings, decimal? ServingsMax,
    IReadOnlyList<ScaledGroup> Ingredients, IReadOnlyList<StepSectionView> Steps, string? TimeNote)
{
    /// <summary>FR-TOOL-04 offers for this view (ADR 0017); empty when weights are already shown.</summary>
    public IReadOnlyList<Suggestion> Suggestions { get; init; } = [];

    /// <summary>BR-33's "standard set assumed" note.</summary>
    public string? AssumedToolsNote { get; init; }

    /// <summary>True when weights come first because a reader chose "Show me" (not saved).</summary>
    public bool ShowingWeights { get; init; }

    /// <summary>BR-122: the lines a reader picked for "Show me" (null when every offered line is shown).</summary>
    public IReadOnlySet<int>? ShownWeightLines { get; init; }

    /// <summary>ADR 0020: the recipe's pan (or the one named in its steps) and alternatives at this scale.</summary>
    public Pan? Pan { get; init; }
    public bool PanFromSteps { get; init; }
    public IReadOnlyList<PanOption> PanOptions { get; init; } = [];

    /// <summary>BR-113 when the rounded amounts drift more than 5%.</summary>
    public string? RoundingWarning { get; init; }
}

/// <summary>
/// Applies the Core measurement engine (9.4) to a recipe for reading (ADR 0014). Read-only: it never writes, so
/// scaling is available wherever the recipe can be read (FR-SCL-03).
/// </summary>
public sealed class RecipeScaler(FoodCatalogService foods, KitchenService kitchen, SuggestionService suggestions)
{
    public const int MaxHaveLength = 40;

    public Task<ScaledRecipe> ScaleAsync(RecipeView recipe, decimal factor, DisplayUnits units, CancellationToken cancellationToken = default) =>
        ScaleAsync(recipe, factor, units, showWeights: false, cancellationToken);

    /// <summary>
    /// The view at <paramref name="factor"/> in <paramref name="units"/>, made with My Kitchen's active tools
    /// (FR-TOOL-03). <paramref name="showWeights"/> is a reader's "Show me" (ADR 0017): weights for this view only.
    /// </summary>
    public Task<ScaledRecipe> ScaleAsync(RecipeView recipe, decimal factor, DisplayUnits units, bool showWeights,
        CancellationToken cancellationToken = default) =>
        ScaleAsync(recipe, factor, units, showWeights, null, cancellationToken);

    /// <summary>
    /// As above; <paramref name="showLines"/> limits "Show me" to those ingredient lines (BR-122, indexes as in
    /// <see cref="ScaledLine"/>). Null with <paramref name="showWeights"/> shows every line the suggestion offers.
    /// </summary>
    public async Task<ScaledRecipe> ScaleAsync(RecipeView recipe, decimal factor, DisplayUnits units, bool showWeights,
        IReadOnlySet<int>? showLines, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (showLines is not null)
        {
            showWeights = false;
        }

        var catalog = await foods.LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
        var current = await kitchen.CurrentAsync(cancellationToken).ConfigureAwait(false);
        var seen = await kitchen.AtVersionAsync(recipe.InventoryVersionSeen, cancellationToken).ConfigureAwait(false);
        var accepted = SuggestionEngine.WeightAccepted(seen, current);
        var preferWeight = accepted || (showWeights && current.Scale is not null);
        var context = new ScaleContext(factor, units, recipe.IsPrecision, catalog, current, preferWeight);
        var index = 0;
        var groups = new List<ScaledGroup>();
        var inputs = new List<ScaleInput>();
        var inputLines = new List<int>();
        var raw = new List<ScaledIngredient>();
        foreach (var group in recipe.Ingredients)
        {
            var lines = new List<ScaledLine>();
            foreach (var line in group.Lines)
            {
                var lineIndex = index++;
                var measure = showLines?.Contains(lineIndex) == true && current.Scale is not null ? LineMeasure.Weight : line.Measure;
                var (shown, input, scaled) = ScaleLine(lineIndex, line, context, measure);
                lines.Add(shown);
                if (input is not null)
                {
                    inputs.Add(input with { Measure = line.Measure }); // offers follow the stored choice, not this view's
                    inputLines.Add(lineIndex);
                    raw.Add(scaled!);
                }
            }

            groups.Add(new ScaledGroup(group.Name, lines));
        }

        IReadOnlyList<Suggestion> offers = [];
        if (!preferWeight && current.Scale is not null)
        {
            var settled = await suggestions.SettledKeysAsync(recipe.Id, cancellationToken).ConfigureAwait(false);
            offers = SuggestionEngine.Find(inputs, context, seen, current, settled)
                .Select(o => o with { Lines = o.Lines.Select(l => l with { Index = inputLines[l.Index] }).ToList() }).ToList();
        }

        var pan = recipe.Pan;
        var fromSteps = false;
        if (pan is null && PanRules.FromSteps(string.Join('\n', recipe.Steps.SelectMany(x => x.Steps).Select(x => x.Text))) is { } found)
        {
            pan = found;
            fromSteps = true;
        }

        var panOptions = pan is null ? [] : PanRules.Alternatives(pan, factor,
            current.Pans.Where(p => p.PanShape is not null && p.PanWidth is not null)
                .Select(p => (p.Name, new Pan(p.PanShape!.Value, p.PanWidth!.Value, p.PanLength ?? p.PanWidth!.Value))));

        var steps = recipe.Steps
            .Select(s => new StepSectionView(s.Name,
                s.Steps.Select(t => new StepView(t.Title, TemperatureText.Convert(t.Text, units), t.ImageId)).ToList()))
            .ToList();
        var (servings, servingsMax) = ScaleRules.Servings(recipe.Servings, recipe.ServingsMax, factor);
        var timeNote = ScaleRules.NoteFor(factor) ?? (panOptions.Count > 0 ? PanRules.TimeNote : null);
        return new ScaledRecipe(factor, units, servings, servingsMax, groups, steps, timeNote)
        {
            Suggestions = offers,
            AssumedToolsNote = current.IsAssumedStandard ? KitchenTools.AssumedNote : null,
            ShowingWeights = (showWeights || showLines is { Count: > 0 }) && !accepted && current.Scale is not null,
            ShownWeightLines = showLines,
            Pan = pan,
            PanFromSteps = fromSteps,
            PanOptions = panOptions,
            RoundingWarning = ContainerRules.RoundingWarning(raw),
        };
    }

    /// <summary>
    /// ADR 0019: the yield for "Fit to container": a reader's typed amount for this view, else the stored yield
    /// (measured over entered, BR-111), else an estimate from the ingredients. A weight yield needs a finished
    /// density (BR-112).
    /// </summary>
    public async Task<ContainerSetup> ContainersAsync(RecipeView recipe, string? typedYield, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var current = await kitchen.CurrentAsync(cancellationToken).ConfigureAwait(false);
        var containers = current.Containers.ToList();
        if (!string.IsNullOrWhiteSpace(typedYield))
        {
            if (!AmountText.TryParse(typedYield, out var typed))
            {
                return new ContainerSetup(null, "Enter what the recipe makes as an amount and unit, e.g. 12 fl oz.", containers);
            }

            var typedYieldMl = ContainerRules.Stored(typed!.Value, typed.Unit.Code, YieldSource.Entered, recipe.FinishedDensityGPerMl, out var typedError);
            return new ContainerSetup(typedYieldMl, typedError, containers);
        }

        var stored = ContainerRules.Stored(recipe.YieldAmount, recipe.YieldUnitCode, recipe.YieldSource, recipe.FinishedDensityGPerMl, out var error);
        if (stored is not null || error is not null)
        {
            return new ContainerSetup(stored, error, containers);
        }

        var catalog = await foods.LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
        var inputs = recipe.Ingredients.SelectMany(g => g.Lines).Where(l => l.Parsed is not null)
            .Select(l => new ScaleInput(l.Parsed!, l.FoodId, PlusAmount(l.Parsed!.Note)));
        return new ContainerSetup(ContainerRules.Estimate(inputs, catalog), null, containers);
    }

    /// <summary>
    /// FR-SCL-02: the factor from "I have <paramref name="have"/>" of the ingredient at <paramref name="lineIndex"/>.
    /// </summary>
    public async Task<IngredientFactorResult> FactorFromAsync(RecipeView recipe, int lineIndex, string? have,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        var line = recipe.Ingredients.SelectMany(g => g.Lines).ElementAtOrDefault(lineIndex);
        if (line?.Parsed is not { } recipeLine || lineIndex < 0)
        {
            return new IngredientFactorResult(null, IngredientFactor.NoAmount);
        }

        if (string.IsNullOrWhiteSpace(have) || have.Length > MaxHaveLength)
        {
            return new IngredientFactorResult(null, IngredientFactor.HaveNoAmount);
        }

        // Read "3 lb" with the ingredient parser, as if it were a line of the recipe.
        var parsedHave = IngredientLineParser.Parse(have.Trim() + " " + (recipeLine.Name.Length > 0 ? recipeLine.Name : "x"));
        var catalog = await foods.LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
        return IngredientFactor.Compute(parsedHave, recipeLine, catalog.Food(line.FoodId)?.DensityGPerMl, catalog);
    }

    private static (ScaledLine Line, ScaleInput? Input, ScaledIngredient? Scaled) ScaleLine(int index, IngredientLineView line, ScaleContext context,
        LineMeasure measure)
    {
        if (line.Parsed is not { } parsed)
        {
            return (new ScaledLine(index, line.Text, line.RawText, line.NeedsReview, false, null, null, null, null, null), null, null);
        }

        var input = new ScaleInput(parsed, line.FoodId, PlusAmount(parsed.Note), measure);
        var scaled = IngredientScaler.Scale(input, context);
        var shown = new ScaledLine(index, scaled.Text, line.RawText, line.NeedsReview, parsed.Quantity is > 0m && !line.NeedsReview,
            scaled.Secondary, scaled.Hint, scaled.YieldNote, scaled.PurchaseGrams, scaled.PurchaseGramsMax)
        {
            Explanation = scaled.Explanation,
            ByWeight = scaled.ByWeight,
            Changed = !string.Equals(scaled.Text, line.Text, StringComparison.Ordinal),
        };
        return (shown, input, scaled);
    }

    /// <summary>"plus 2 tablespoons" → 2 Tbsp, so "1 cup plus 2 tablespoons flour" scales as one amount.</summary>
    internal static ParsedIngredient? PlusAmount(string? note)
    {
        const string Plus = "plus ";
        if (note is null || !note.StartsWith(Plus, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var parsed = IngredientLineParser.Parse(note[Plus.Length..].Trim() + " x");
        return parsed.Quantity is not null && parsed.UnitCode is not null ? parsed : null;
    }
}
