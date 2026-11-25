using RecipeWarden.Core.Import;

namespace RecipeWarden.Infrastructure.Data;

/// <summary>Where an import job is (8.5 ImportJob.Status, ADR 0025).</summary>
public enum ImportStatus
{
    Review,
    Saved,
    Discarded,
}

/// <summary>
/// Section 8.5 ImportJob, ADR 0024/0025: one parsed recipe waiting for review. A file with several recipes gives
/// several jobs with one <see cref="BatchId"/>. The raw payload is kept 30 days (FR-IMP-08).
/// </summary>
public sealed class ImportJob
{
    public int Id { get; set; }
    public string BatchId { get; set; } = string.Empty;
    public ImportSourceKind SourceKind { get; set; }

    /// <summary>The page address or the file name.</summary>
    public string SourceRef { get; set; } = string.Empty;
    public ImportStatus Status { get; set; }

    /// <summary>Relative to the imports directory; shared by a batch's jobs.</summary>
    public string? RawPath { get; set; }

    /// <summary>The <see cref="ImportedRecipe"/> as JSON.</summary>
    public string ParsedJson { get; set; } = "{}";
    public string WarningsJson { get; set; } = "[]";
    public string? ImageUrl { get; set; }

    /// <summary>BR-12: whether the image copy was ticked on the import page; the review page can change it.</summary>
    public bool CopyImage { get; set; }
    public int? DuplicateOfRecipeId { get; set; }
    public int? CreatedRecipeId { get; set; }
    public string? CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>FR-IMP-09: the recipe came from the AI parser; the review page marks its fields.</summary>
    public bool ParsedByAi { get; set; }

    /// <summary>FR-IMP-13: how many photos the batch folder holds (photo-0 … photo-n); 0 for other imports (ADR 0058).</summary>
    public int PhotoCount { get; set; }

    /// <summary>BR-131: "Save the card photo with the recipe", ticked by default for photo imports.</summary>
    public bool KeepPhotos { get; set; }
}
