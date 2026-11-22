namespace RecipeWarden.Core.Measurement;

/// <summary>
/// The reader's unit choice on the recipe toolbar (7.3 item 3, ADR 0014). <see cref="AsWritten"/> keeps each amount
/// in the recipe's own system; <see cref="Weight"/> shows grams where a density is known (BR-23), with the volume as
/// a secondary line (BR-22).
/// </summary>
public enum DisplayUnits
{
    AsWritten = 0,
    US = 1,
    Metric = 2,
    Weight = 3,
}

/// <summary>FR-UI-02, ADR 0014: query-string, cookie and profile values for <see cref="DisplayUnits"/>.</summary>
public static class DisplayUnitsRules
{
    public const string CookieName = "rw-units";

    /// <summary>Unknown or missing values mean <see cref="DisplayUnits.AsWritten"/>.</summary>
    public static DisplayUnits Parse(string? value) => TryParse(value, out var units) ? units : DisplayUnits.AsWritten;

    public static bool TryParse(string? value, out DisplayUnits units)
    {
        switch (value?.Trim().ToUpperInvariant())
        {
            case "ASWRITTEN" or "AS-WRITTEN" or "ORIGINAL": units = DisplayUnits.AsWritten; return true;
            case "US": units = DisplayUnits.US; return true;
            case "METRIC": units = DisplayUnits.Metric; return true;
            case "WEIGHT": units = DisplayUnits.Weight; return true;
            default: units = DisplayUnits.AsWritten; return false;
        }
    }

    public static string ToValue(DisplayUnits units) => units switch
    {
        DisplayUnits.US => "us",
        DisplayUnits.Metric => "metric",
        DisplayUnits.Weight => "weight",
        _ => "as-written",
    };

    public static string Label(DisplayUnits units) => units switch
    {
        DisplayUnits.US => "US",
        DisplayUnits.Metric => "Metric",
        DisplayUnits.Weight => "Weight",
        _ => "As written",
    };

    /// <summary>The query string wins, then the signed-in profile, then the cookie (same order as the theme, FR-UI-01).</summary>
    public static DisplayUnits Resolve(string? query, DisplayUnits? profile, string? cookie) =>
        TryParse(query, out var fromQuery) ? fromQuery : profile ?? Parse(cookie);
}
