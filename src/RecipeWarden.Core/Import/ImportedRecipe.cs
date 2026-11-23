using RecipeWarden.Core.Recipes;

namespace RecipeWarden.Core.Import;

/// <summary>Where an import came from (8.5 ImportJob.SourceKind; ADR 0024 adds Paste and Text).</summary>
public enum ImportSourceKind
{
    Url,
    Paste,
    Markdown,
    Text,
    Docx,
    Pdf,
}

/// <summary>How the recipe was found in the source (FR-IMP-01 order, then the file importers).</summary>
public enum ExtractionMethod
{
    JsonLd,
    Microdata,
    Heuristic,
    RecipeMarkdown,
    Headings,
}

/// <summary>
/// A recipe read from a page or file, as the text the editor form takes (ADR 0025): the review page fills the
/// editor with these fields, and the normal save pipeline parses and validates them. Everything here is plain text
/// (BR-13); nothing is ever rendered as HTML.
/// </summary>
public sealed record ImportedRecipe
{
    public string? Title { get; init; }
    public string? Description { get; init; }

    /// <summary>"4", "6-8" (ADR 0010).</summary>
    public string? Servings { get; init; }

    /// <summary>Minutes or "20-25" (ADR 0010).</summary>
    public string? PrepTime { get; init; }
    public string? CookTime { get; init; }
    public string? TotalTime { get; init; }

    /// <summary>A finished amount, e.g. "12 fl oz" (ADR 0019); only set when it reads as an amount.</summary>
    public string? Yield { get; init; }

    public bool IsPrecision { get; init; }
    public string? Pan { get; init; }
    public string? FinishedDensity { get; init; }

    /// <summary>One ingredient per line; "Name:" lines start a group.</summary>
    public string? Ingredients { get; init; }

    /// <summary>Steps in the editor's format (<see cref="StepTextRules.Join"/>).</summary>
    public string? Steps { get; init; }

    public string? Notes { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];

    public SourceType? SourceType { get; init; }
    public string? SourceName { get; init; }
    public string? SourceUrl { get; init; }
    public string? SourceAuthor { get; init; }

    /// <summary>The finish image's address on the source site, offered for copying (FR-IMP-02, BR-12).</summary>
    public string? ImageUrl { get; init; }

    public ExtractionMethod Method { get; init; }

    /// <summary>recipe-md's <c>slug:</c>, used to match an AI reply to its recipe (ADR 0033).</summary>
    public string? Slug { get; init; }

    /// <summary>recipe-md's "## Changes" section (BR-95); it is also added to <see cref="Notes"/>.</summary>
    public string? Changes { get; init; }

    /// <summary>Notes about the extraction itself, e.g. a yield that could not be used.</summary>
    public IReadOnlyList<string> Remarks { get; init; } = [];

    /// <summary>Whether anything recipe-like was found (a title alone is not enough).</summary>
    public bool HasContent => !string.IsNullOrWhiteSpace(Ingredients) || !string.IsNullOrWhiteSpace(Steps);
}
