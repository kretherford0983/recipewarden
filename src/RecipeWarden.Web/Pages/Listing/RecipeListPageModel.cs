using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RecipeWarden.Core.Search;
using RecipeWarden.Infrastructure.Recipes;
using RecipeWarden.Web.Security;

namespace RecipeWarden.Web.Pages.Listing;

/// <summary>
/// FR-SRCH-01/02: shared by /search, /recipes, /tags/{slug} and /collections/{slug}. Query-string values are only
/// read into <see cref="SearchInput"/> and checked by <see cref="SearchCriteria.Parse"/> (allow-lists and ranges).
/// </summary>
public abstract class RecipeListPageModel(RecipeSearchService search, TagService tags, CollectionService collections, Permissions permissions)
    : PageModel
{
    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true, Name = "tag")]
    public string[]? Tag { get; set; }

    [BindProperty(SupportsGet = true, Name = "collection")]
    public string? Collection { get; set; }

    [BindProperty(SupportsGet = true, Name = "time")]
    public string? MaxMinutes { get; set; }

    [BindProperty(SupportsGet = true, Name = "rating")]
    public string? MinRating { get; set; }

    [BindProperty(SupportsGet = true, Name = "sort")]
    public string? Sort { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public string? PageNumber { get; set; }

    /// <summary>Editors only: "draft" lists drafts (ADR 0009); ignored for everyone else.</summary>
    [BindProperty(SupportsGet = true, Name = "status")]
    public string? Status { get; set; }

    public SearchCriteria Criteria { get; private set; } = new();
    public SearchPage Results { get; private set; } = new([], 0, 1, 1);
    public IReadOnlyList<TagSummary> AllTags { get; private set; } = [];
    public IReadOnlyList<CollectionSummary> AllCollections { get; private set; } = [];
    public bool CanEdit { get; private set; }
    public bool ShowingDrafts { get; private set; }

    /// <summary>Path the filter form and pager point at.</summary>
    public abstract string ListPath { get; }

    protected async Task LoadAsync(string? fixedTag = null, string? fixedCollection = null)
    {
        var ct = HttpContext.RequestAborted;
        CanEdit = await permissions.CanEditAsync(User);
        ShowingDrafts = CanEdit && string.Equals(Status, "draft", StringComparison.Ordinal);

        var tagsWanted = (Tag ?? []).ToList();
        if (fixedTag is not null)
        {
            tagsWanted.Insert(0, fixedTag);
        }

        Criteria = SearchCriteria.Parse(new SearchInput
        {
            Q = Q,
            Tags = tagsWanted,
            Collection = fixedCollection ?? Collection,
            MaxMinutes = MaxMinutes,
            MinRating = MinRating,
            Sort = Sort,
            Page = PageNumber,
        });
        Results = await search.SearchAsync(Criteria, ShowingDrafts ? RecipeVisibility.Drafts : RecipeVisibility.Published, ct);
        AllTags = (await tags.ListAsync(cancellationToken: ct)).Where(t => t.RecipeCount > 0).ToList();
        AllCollections = await collections.ListAsync(ct);
    }

    /// <summary>The current query with one value changed (pager and sort links); values are URL-encoded.</summary>
    public string LinkWith(string key, string? value)
    {
        var q = new QueryBuilder();
        void Add(string k, string? v)
        {
            if (k == key)
            {
                if (!string.IsNullOrEmpty(value)) q.Add(k, value);
            }
            else if (!string.IsNullOrEmpty(v))
            {
                q.Add(k, v);
            }
        }

        Add("q", Q);
        foreach (var t in Tag ?? [])
        {
            if (key != "tag") q.Add("tag", t);
        }

        if (key == "tag" && !string.IsNullOrEmpty(value)) q.Add("tag", value);
        Add("collection", Collection);
        Add("time", MaxMinutes);
        Add("rating", MinRating);
        Add("sort", Sort);
        Add("status", ShowingDrafts ? "draft" : null);
        Add("page", key == "page" ? value : null);
        return ListPath + q.ToQueryString();
    }
}
