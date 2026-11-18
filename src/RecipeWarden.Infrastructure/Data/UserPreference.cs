using RecipeWarden.Core.Preferences;

namespace RecipeWarden.Infrastructure.Data;

/// <summary>Section 8.5 UserPreference (M0: theme only; FR-UI-02 adds unit system and default scale).</summary>
public sealed class UserPreference
{
    public string UserId { get; set; } = string.Empty;

    public Theme Theme { get; set; } = Theme.System;

    public DateTimeOffset UpdatedAt { get; set; }
}
