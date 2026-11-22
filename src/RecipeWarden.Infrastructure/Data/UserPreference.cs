using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Preferences;

namespace RecipeWarden.Infrastructure.Data;

/// <summary>Section 8.5 UserPreference: theme (FR-UI-01), unit system and default scale (FR-UI-02).</summary>
public sealed class UserPreference
{
    public string UserId { get; set; } = string.Empty;

    public Theme Theme { get; set; } = Theme.System;

    /// <summary>FR-UI-02: null until the user picks one.</summary>
    public DisplayUnits? Units { get; set; }

    /// <summary>FR-UI-02: the scale a recipe opens at; null means 1x.</summary>
    public decimal? DefaultScale { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
