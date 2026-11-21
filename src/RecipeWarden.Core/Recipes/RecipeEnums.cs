namespace RecipeWarden.Core.Recipes;

/// <summary>BR-01: a recipe is a Draft until it has everything needed to publish.</summary>
public enum RecipeStatus
{
    Draft,
    Published,
}

/// <summary>Section 8.2 Recipe.SourceType (BR-10, BR-11).</summary>
public enum SourceType
{
    Manual,
    Web,
    File,
    Personal,
}

/// <summary>Editable recipe fields, in form order; used for messages, diffs (BR-06) and linking errors to fields (UX-08).</summary>
public enum RecipeField
{
    Title,
    Description,
    Servings,
    PrepMinutes,
    CookMinutes,
    TotalMinutes,
    IsPrecision,
    Notes,
    SourceType,
    SourceName,
    SourceUrl,
    SourceAuthor,
    Ingredients,
    Steps,
    Tags,
}
