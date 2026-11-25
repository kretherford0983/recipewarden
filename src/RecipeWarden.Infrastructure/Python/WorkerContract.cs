using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using RecipeWarden.Core.Python;

namespace RecipeWarden.Infrastructure.Python;

/// <summary>Worker contract v1 (plan 4.2) as strict JSON types (SEC-93): unknown members are rejected.</summary>
public sealed record WorkerEngine(string Worker, string Python, Dictionary<string, string> Libs)
{
    /// <summary>"ingredient-parser-nlp 2.10.0" style text for caches (BR-116) and the health page.</summary>
    public string Describe(params string[] libraries) =>
        string.Join(", ", (libraries.Length == 0 ? Libs.Keys.Order(StringComparer.Ordinal).ToArray() : libraries)
            .Select(l => l + " " + (Libs.TryGetValue(l, out var v) ? v : "missing")));
}

public sealed record WorkerError(string Code, string Message);

public sealed record WorkerEnvelope<T>(int V, bool Ok, WorkerEngine Engine, T? Result = null, WorkerError? Error = null)
    where T : class;

public sealed record WireFood(int FdcId, string DataType, string Description, decimal Confidence);

public sealed record WireLine(string? Quantity, string? QuantityMax, string? Unit, List<string> Names, string? Preparation,
    string? Comment, decimal NameConfidence, decimal AmountConfidence, List<WireFood> Fdc);

public sealed record ParseResult(List<WireLine?> Lines);

public sealed record WireSuggestion(string Name, List<WireFood> Fdc);

public sealed record SuggestResult(List<WireSuggestion> Suggestions);

public sealed record ScrapeResult(bool SupportedHost, bool Found, string? Title = null, List<string>? Ingredients = null,
    List<string>? Instructions = null, string? Yields = null, int? TotalMinutes = null, int? PrepMinutes = null,
    int? CookMinutes = null, string? Author = null, string? Description = null, string? ImageUrl = null, string? SiteName = null);

public sealed record SelftestChecks(bool NetworkBlocked, int ParseMs, bool Parse, bool Fdc, int ScrapeMs, bool Scrape);

public sealed record SelftestResult(bool Passed, SelftestChecks Checks);

public sealed record ParseRequest(IReadOnlyList<string> Lines, bool Fdc);

public sealed record SuggestRequest(IReadOnlyList<string> Names);

public sealed record ScrapeRequest(string Html, string Url);

public sealed record EmptyRequest;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    NumberHandling = JsonNumberHandling.Strict,
    MaxDepth = 16)]
[JsonSerializable(typeof(WorkerEnvelope<ParseResult>))]
[JsonSerializable(typeof(WorkerEnvelope<SuggestResult>))]
[JsonSerializable(typeof(WorkerEnvelope<ScrapeResult>))]
[JsonSerializable(typeof(WorkerEnvelope<SelftestResult>))]
[JsonSerializable(typeof(ParseRequest))]
[JsonSerializable(typeof(SuggestRequest))]
[JsonSerializable(typeof(ScrapeRequest))]
[JsonSerializable(typeof(EmptyRequest))]
internal sealed partial class WorkerJson : JsonSerializerContext;

/// <summary>SEC-93: checks every value in an answer before anything uses it, and maps it to Core types.</summary>
public static partial class WorkerContract
{
    public const int Version = 1;
    public const int MaxNames = 10;
    public const int MaxText = 4000;
    public const int MaxItems = 500;
    public const int MaxFoods = 5;

    private static readonly HashSet<string> DataTypes = new(StringComparer.Ordinal) { "sr_legacy_food", "foundation_food", "survey_fndds_food" };

    [GeneratedRegex(@"^\d{1,7}(\.\d{1,6})?$", RegexOptions.CultureInvariant, 250)]
    private static partial Regex QuantityPattern();

    /// <summary>Checks the envelope; on success returns null, otherwise why it is not acceptable.</summary>
    public static string? CheckEnvelope<T>(WorkerEnvelope<T>? envelope)
        where T : class
    {
        if (envelope is null)
        {
            return "empty answer";
        }

        if (envelope.V != Version)
        {
            return "contract version " + envelope.V.ToString(CultureInfo.InvariantCulture);
        }

        if (Short(envelope.Engine.Worker, 40) is null || Short(envelope.Engine.Python, 40) is null || envelope.Engine.Libs.Count > 10
            || envelope.Engine.Libs.Any(l => Short(l.Key, 60) is null || Short(l.Value, 40) is null))
        {
            return "bad engine block";
        }

        return envelope.Ok switch
        {
            true when envelope.Result is null || envelope.Error is not null => "ok answer without a result",
            false when envelope.Error is null || envelope.Result is not null => "error answer without an error",
            false when Short(envelope.Error.Code, 40) is null || envelope.Error.Message.Length > 500 => "bad error block",
            _ => null,
        };
    }

    public static IReadOnlyList<IngredientReading?>? Readings(ParseResult result, int expected)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Lines.Count != expected)
        {
            return null;
        }

        var readings = new List<IngredientReading?>(expected);
        foreach (var line in result.Lines)
        {
            if (line is null)
            {
                readings.Add(null);
                continue;
            }

            if (!Quantity(line.Quantity, out var q) || !Quantity(line.QuantityMax, out var max) || line.Names.Count > MaxNames
                || line.Names.Any(n => n.Length > 300) || (line.Unit is not null && Short(line.Unit, 40) is null)
                || line.Preparation?.Length > MaxText || line.Comment?.Length > MaxText
                || !Confidence(line.NameConfidence) || !Confidence(line.AmountConfidence) || Foods(line.Fdc) is not { } foods)
            {
                return null;
            }

            readings.Add(new IngredientReading(q, max, line.Unit, [.. line.Names], line.Preparation, line.Comment, foods));
        }

        return readings;
    }

    public static IReadOnlyList<FoodCandidates>? Suggestions(SuggestResult result, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(names);
        if (result.Suggestions.Count != names.Count)
        {
            return null;
        }

        var list = new List<FoodCandidates>(names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            // The answer must be about the name that was asked, in order; anything else is not trusted.
            if (!string.Equals(result.Suggestions[i].Name, names[i], StringComparison.Ordinal) || Foods(result.Suggestions[i].Fdc) is not { } foods)
            {
                return null;
            }

            list.Add(new FoodCandidates(names[i], foods));
        }

        return list;
    }

    public static SiteRecipeReading? Recipe(ScrapeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var ingredients = result.Ingredients ?? [];
        var instructions = result.Instructions ?? [];
        string?[] texts = [result.Title, result.Yields, result.Author, result.Description, result.ImageUrl, result.SiteName];
        if (ingredients.Count > MaxItems || instructions.Count > MaxItems
            || ingredients.Concat(instructions).Concat(texts.OfType<string>()).Any(t => t.Length > MaxText)
            || new[] { result.TotalMinutes, result.PrepMinutes, result.CookMinutes }.Any(m => m is < 0 or > 100_000))
        {
            return null;
        }

        return new SiteRecipeReading(result.SupportedHost, result.Found, result.Title, [.. ingredients], [.. instructions], result.Yields,
            result.TotalMinutes, result.PrepMinutes, result.CookMinutes, result.Author, result.Description, result.ImageUrl, result.SiteName);
    }

    private static List<FdcCandidate>? Foods(List<WireFood> foods)
    {
        if (foods.Count > MaxFoods || foods.Any(f => f.FdcId is <= 0 or > 99_999_999 || !DataTypes.Contains(f.DataType)
                                                    || Short(f.Description, 300) is null || !Confidence(f.Confidence)))
        {
            return null;
        }

        return [.. foods.Select(f => new FdcCandidate(f.FdcId, f.DataType, f.Description, f.Confidence))];
    }

    private static bool Quantity(string? text, out decimal? value)
    {
        value = null;
        if (text is null)
        {
            return true;
        }

        if (!QuantityPattern().IsMatch(text) || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d))
        {
            return false;
        }

        value = d;
        return true;
    }

    private static bool Confidence(decimal c) => c is >= 0m and <= 1m;

    private static string? Short(string? text, int max) => text is { Length: > 0 } t && t.Length <= max ? t : null;

    internal static JsonSerializerOptions Options => WorkerJson.Default.Options;
}
