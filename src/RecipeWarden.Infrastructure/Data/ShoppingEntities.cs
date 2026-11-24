using RecipeWarden.Core.Measurement;

namespace RecipeWarden.Infrastructure.Data;

/// <summary>Section 8.5 ShoppingList, ADR 0028: a named list. <see cref="FinishedAt"/> starts the 15-minute undo window.</summary>
public sealed class ShoppingList
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public List<ShoppingListRecipe> Recipes { get; } = [];
    public List<ShoppingLine> Lines { get; } = [];
    public List<ShoppingCheck> Checks { get; } = [];
}

/// <summary>ADR 0029: a recipe on a list at a scale; its lines are stored when it is added (BR-42).</summary>
public sealed class ShoppingListRecipe
{
    public int Id { get; set; }
    public int ShoppingListId { get; set; }

    /// <summary>Null once the recipe is purged; the title stays.</summary>
    public int? RecipeId { get; set; }
    public string Title { get; set; } = string.Empty;
    public decimal Scale { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}

/// <summary>ADR 0029: one ingredient line (or a manual item when <see cref="ListRecipeId"/> is null), in base units.</summary>
public sealed class ShoppingLine
{
    public int Id { get; set; }
    public int ShoppingListId { get; set; }
    public int? ListRecipeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public UnitKind? Kind { get; set; }
    public decimal? Amount { get; set; }
    public decimal? AmountMax { get; set; }
    public string? CountUnit { get; set; }
    public int? FoodId { get; set; }
    public string RawText { get; set; } = string.Empty;
    public int Position { get; set; }

    /// <summary>BR-45: added by a staples check.</summary>
    public bool IsRestock { get; set; }

    /// <summary>BR-45: a custom staple's store section (catalogue foods use the food's).</summary>
    public string? Section { get; set; }
}

/// <summary>BR-44: the Have it / Need it answer for one staple on one list.</summary>
public sealed class ShoppingStapleCheck
{
    public int ShoppingListId { get; set; }
    public string ItemKey { get; set; } = string.Empty;
    public RecipeWarden.Core.Shopping.StapleAnswer Answer { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>
/// FR-SHOP-05, ADR 0056: the household's staples. A catalogue food's staple flag stays on Food.IsPantryStaple; this row
/// holds its details. A row without a food is a non-food staple (foil, dish soap).
/// </summary>
public sealed class Staple
{
    public int Id { get; set; }
    public int? FoodId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string Location { get; set; } = RecipeWarden.Core.Shopping.StapleRules.DefaultLocation;
    public string? Restock { get; set; }
    public string? Section { get; set; }
    public DateTimeOffset? LastRestockedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>ADR 0029: a ticked item, by merged item key.</summary>
public sealed class ShoppingCheck
{
    public int ShoppingListId { get; set; }
    public string ItemKey { get; set; } = string.Empty;
    public DateTimeOffset CheckedAt { get; set; }
}

/// <summary>ADR 0028: a list name kept after the list is gone, for "Start again".</summary>
public sealed class ShoppingListName
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public DateTimeOffset LastUsedAt { get; set; }
}
