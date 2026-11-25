using Microsoft.AspNetCore.Mvc;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Pages.Listing;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Tags;

/// <summary>FR-ORG-01: recipes with a tag. Anonymous (7.2).</summary>
public sealed class DetailsModel(RecipeSearchService search, TagService tags, CollectionService collections, Permissions permissions)
    : RecipeListPageModel(search, tags, collections, permissions)
{
    private readonly TagService tagService = tags;

    public string Slug { get; private set; } = string.Empty;
    public string TagName { get; private set; } = string.Empty;

    public override string ListPath => "/tags/" + Slug;

    public async Task<IActionResult> OnGetAsync([FromRoute] string slug)
    {
        var tag = await tagService.GetBySlugAsync(slug, HttpContext.RequestAborted);
        if (tag is null)
        {
            return NotFound();
        }

        Slug = tag.Slug;
        TagName = tag.Name;
        await LoadAsync(fixedTag: tag.Slug);
        return Page();
    }
}
