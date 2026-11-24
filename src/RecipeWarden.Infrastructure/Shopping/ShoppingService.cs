using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Import;
using RecipeWarden.Core.Measurement;
using RecipeWarden.Core.Organization;
using RecipeWarden.Core.Recipes;
using RecipeWarden.Core.Shopping;
using RecipeWarden.Importers.Ingredients;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Measurement;
using RecipeWarden.Infrastructure.Recipes;

namespace RecipeWarden.Infrastructure.Shopping;

/// <summary>A list on the lists page.</summary>
public sealed record ShoppingListSummary(int Id, string Name, int Recipes, int Items, DateTimeOffset UpdatedAt);

/// <summary>A recipe on a list.</summary>
public sealed record ShoppingRecipeRow(int Id, int? RecipeId, string Title, decimal Scale);

/// <summary>A manual item on a list.</summary>
public sealed record ShoppingManualRow(int Id, string Text);

/// <summary>A list as its page shows it (FR-SHOP-02/03). <see cref="DeleteAt"/> is set while a finished list waits.</summary>
public sealed record ShoppingListPage(int Id, string Name, ShoppingView View, IReadOnlyList<ShoppingRecipeRow> Recipes,
    IReadOnlyList<ShoppingManualRow> Manual, DateTimeOffset? DeleteAt);

/// <summary>The outcome of a tick: <see cref="Finished"/> when it was the last one (ADR 0028).</summary>
public sealed record ShoppingCheckResult(bool Ok, bool Finished);

/// <summary>An edit result: ok, or the message to show.</summary>
public sealed record ShoppingEditResult(bool Ok, string? Error, int? Id = null)
{
    public static ShoppingEditResult Fail(string error) => new(false, error);
}

/// <summary>
/// FR-SHOP-01 to 04, ADR 0028/0029: named shopping lists. Adding a recipe stores its lines at the chosen scale in base
/// units; the page merges them with <see cref="ShoppingRules.Merge"/>. Finished lists wait 15 minutes for Undo, then
/// are deleted; their names are remembered for "Start again".
/// </summary>
public sealed class ShoppingService(AppDbContext db, FoodCatalogService foods, TimeProvider clock)
{
    public static readonly TimeSpan UndoWindow = TimeSpan.FromMinutes(15);
    public const int RememberedNames = 8;

    public const string NameRequired = "Give the list a name.";
    public const string NameTooLong = "Keep the name under 80 characters.";
    public const string ItemRequired = "Type an item, e.g. 2 lemons.";
    public const string ItemTooLong = "Keep items under 200 characters.";
    public const string ListFull = "This list is full; start another one.";
    public const string NotFound = "That list no longer exists.";
    public const string RecipeNotFound = "That recipe no longer exists.";
    public const string BadScale = "Choose a scale between 0.1× and 100×.";

    public async Task<IReadOnlyList<ShoppingListSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        await PurgeFinishedAsync(cancellationToken).ConfigureAwait(false);
        var lists = await db.ShoppingLists.AsNoTracking().Where(l => l.FinishedAt == null)
            .Select(l => new { l.Id, l.Name, l.UpdatedAt, Recipes = l.Recipes.Count, Items = l.Lines.Count })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // SQLite cannot order by DateTimeOffset; sort in memory (a handful of lists).
        return lists.OrderByDescending(l => l.UpdatedAt).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .Select(l => new ShoppingListSummary(l.Id, l.Name, l.Recipes, l.Items, l.UpdatedAt)).ToList();
    }

    /// <summary>ADR 0028: names to start again with, most recent first, leaving out names of lists still open.</summary>
    public async Task<IReadOnlyList<string>> RememberedAsync(CancellationToken cancellationToken = default)
    {
        var open = (await db.ShoppingLists.AsNoTracking().Where(l => l.FinishedAt == null).Select(l => l.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false)).Select(NameRules.Normalize).ToHashSet(StringComparer.Ordinal);
        var names = await db.ShoppingListNames.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        return names.Where(n => !open.Contains(n.NormalizedName)).OrderByDescending(n => n.LastUsedAt)
            .Take(RememberedNames).Select(n => n.Name).ToList();
    }

    public async Task<ShoppingEditResult> CreateAsync(string? name, string userId, CancellationToken cancellationToken = default)
    {
        var clean = ImportText.Line(name);
        if (string.IsNullOrEmpty(clean))
        {
            return ShoppingEditResult.Fail(NameRequired);
        }

        if (clean.Length > ShoppingRules.MaxListNameLength)
        {
            return ShoppingEditResult.Fail(NameTooLong);
        }

        var now = clock.GetUtcNow();
        var list = new ShoppingList { Name = clean, CreatedById = userId, CreatedAt = now, UpdatedAt = now };
        db.ShoppingLists.Add(list);
        await RememberAsync(clean, now, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ShoppingEditResult(true, null, list.Id);
    }

    public async Task<ShoppingListPage?> GetAsync(int id, UnitSystem system, CancellationToken cancellationToken = default)
    {
        await PurgeFinishedAsync(cancellationToken).ConfigureAwait(false);
        var list = await db.ShoppingLists.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken).ConfigureAwait(false);
        if (list is null)
        {
            return null;
        }

        var view = await ViewAsync(id, system, cancellationToken).ConfigureAwait(false);
        var recipes = await db.ShoppingListRecipes.AsNoTracking().Where(r => r.ShoppingListId == id).OrderBy(r => r.Id)
            .Select(r => new ShoppingRecipeRow(r.Id, r.RecipeId, r.Title, r.Scale)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var manual = await db.ShoppingLines.AsNoTracking().Where(l => l.ShoppingListId == id && l.ListRecipeId == null).OrderBy(l => l.Position)
            // restock items are listed (and removable) with the manual ones
            .Select(l => new ShoppingManualRow(l.Id, l.RawText)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new ShoppingListPage(list.Id, list.Name, view, recipes, manual, list.FinishedAt + UndoWindow);
    }

    /// <summary>FR-SHOP-01, ADR 0029: stores the recipe's lines at <paramref name="scale"/> (drafts included; Editor only).</summary>
    public async Task<ShoppingEditResult> AddRecipeAsync(int listId, string slug, decimal scale, CancellationToken cancellationToken = default)
    {
        if (scale < ScaleRules.MinFactor || scale > ScaleRules.MaxFactor)
        {
            return ShoppingEditResult.Fail(BadScale);
        }

        var list = await OpenListAsync(listId, cancellationToken).ConfigureAwait(false);
        if (list is null)
        {
            return ShoppingEditResult.Fail(NotFound);
        }

        var recipe = await db.Recipes.AsNoTracking().Where(r => r.Slug == slug && r.DeletedAt == null)
            .Select(r => new { r.Id, r.Title, r.IsPrecision }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (recipe is null)
        {
            return ShoppingEditResult.Fail(RecipeNotFound);
        }

        var ingredients = (await db.RecipeIngredients.AsNoTracking().Include(i => i.Unit).Include(i => i.Group)
            .Where(i => i.RecipeId == recipe.Id).ToListAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(i => i.Group?.SortOrder ?? -1).ThenBy(i => i.SortOrder).ToList();
        var count = await db.ShoppingLines.CountAsync(l => l.ShoppingListId == listId, cancellationToken).ConfigureAwait(false);
        var recipeCount = await db.ShoppingListRecipes.CountAsync(r => r.ShoppingListId == listId, cancellationToken).ConfigureAwait(false);
        if (count + ingredients.Count > ShoppingRules.MaxLinesPerList || recipeCount >= ShoppingRules.MaxRecipesPerList)
        {
            return ShoppingEditResult.Fail(ListFull);
        }

        var catalog = await foods.LoadCatalogAsync(cancellationToken).ConfigureAwait(false);
        var staples = await StapleFoodIdsAsync(cancellationToken).ConfigureAwait(false);
        var context = new ScaleContext(scale, DisplayUnits.AsWritten, recipe.IsPrecision, catalog);
        var now = clock.GetUtcNow();
        var row = new ShoppingListRecipe { Title = recipe.Title, RecipeId = recipe.Id, Scale = scale, AddedAt = now };
        list.Recipes.Add(row);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var position = count;
        foreach (var ingredient in ingredients)
        {
            var parsed = RecipeService.ToParsed(ingredient);
            var plus = RecipeScaler.PlusAmount(parsed.Note);
            var scaled = parsed.Quantity is null ? null : IngredientScaler.Scale(new ScaleInput(parsed, ingredient.FoodId, plus), context);
            var (kind, amount, amountMax, countUnit) = ShoppingRules.Amount(parsed, scale, plus, scaled?.PurchaseGrams, scaled?.PurchaseGramsMax);
            var name = parsed.Name.Length > 0 ? parsed.Name : ingredient.RawText;
            db.ShoppingLines.Add(new ShoppingLine
            {
                ShoppingListId = listId,
                ListRecipeId = row.Id,
                Name = Cut(name, ShoppingRules.MaxItemLength),
                Kind = kind,
                Amount = amount,
                AmountMax = amountMax,
                CountUnit = countUnit,
                FoodId = ingredient.FoodId,
                RawText = Cut(ingredient.RawText, ShoppingRules.MaxItemLength * 2),
                Position = position++,
                IsStaple = ingredient.FoodId is { } fid && staples.Contains(fid), // BR-48
            });
        }

        Touch(list, now);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ShoppingEditResult(true, null, row.Id);
    }

    public async Task<bool> RemoveRecipeAsync(int listId, int listRecipeId, CancellationToken cancellationToken = default)
    {
        var removed = await db.ShoppingListRecipes.Where(r => r.Id == listRecipeId && r.ShoppingListId == listId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) > 0;
        if (removed)
        {
            await TouchAsync(listId, cancellationToken).ConfigureAwait(false);
        }

        return removed;
    }

    /// <summary>FR-SHOP-03: a manual item, read like an ingredient line ("2 lemons") and matched to a food.</summary>
    public async Task<ShoppingEditResult> AddItemAsync(int listId, string? text, CancellationToken cancellationToken = default)
    {
        var clean = ImportText.Line(text);
        if (string.IsNullOrEmpty(clean))
        {
            return ShoppingEditResult.Fail(ItemRequired);
        }

        if (clean.Length > ShoppingRules.MaxItemLength)
        {
            return ShoppingEditResult.Fail(ItemTooLong);
        }

        var list = await OpenListAsync(listId, cancellationToken).ConfigureAwait(false);
        if (list is null)
        {
            return ShoppingEditResult.Fail(NotFound);
        }

        var count = await db.ShoppingLines.CountAsync(l => l.ShoppingListId == listId, cancellationToken).ConfigureAwait(false);
        if (count >= ShoppingRules.MaxLinesPerList)
        {
            return ShoppingEditResult.Fail(ListFull);
        }

        var parsed = IngredientLineParser.Parse(clean);
        var matcher = await foods.LoadMatcherAsync(cancellationToken).ConfigureAwait(false);
        var name = parsed.Name.Length > 0 ? parsed.Name : clean;
        var foodId = matcher.Match(name);
        var (kind, amount, amountMax, countUnit) = ShoppingRules.Amount(parsed, 1m, RecipeScaler.PlusAmount(parsed.Note));
        var line = new ShoppingLine
        {
            ShoppingListId = listId,
            Name = name,
            Kind = kind,
            Amount = amount,
            AmountMax = amountMax,
            CountUnit = countUnit,
            FoodId = foodId,
            RawText = clean,
            Position = count,
            IsStaple = foodId is { } fid && (await StapleFoodIdsAsync(cancellationToken).ConfigureAwait(false)).Contains(fid), // BR-48
        };
        db.ShoppingLines.Add(line);
        Touch(list, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ShoppingEditResult(true, null, line.Id);
    }

    public async Task<bool> RemoveItemAsync(int listId, int lineId, CancellationToken cancellationToken = default)
    {
        var removed = await db.ShoppingLines.Where(l => l.Id == lineId && l.ShoppingListId == listId && l.ListRecipeId == null)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) > 0;
        if (removed)
        {
            await TouchAsync(listId, cancellationToken).ConfigureAwait(false);
        }

        return removed;
    }

    /// <summary>
    /// ADR 0028: ticks or unticks one item (the one anonymous write). Only keys of items on the list are stored. The
    /// last tick finishes the list; an untick on a finished list (Undo) opens it again.
    /// </summary>
    public async Task<ShoppingCheckResult> CheckAsync(int listId, string? key, bool isChecked, CancellationToken cancellationToken = default)
    {
        await PurgeFinishedAsync(cancellationToken).ConfigureAwait(false);
        var list = await db.ShoppingLists.FirstOrDefaultAsync(l => l.Id == listId, cancellationToken).ConfigureAwait(false);
        if (list is null || string.IsNullOrEmpty(key) || key.Length > ShoppingRules.MaxItemLength + 2)
        {
            return new ShoppingCheckResult(false, false);
        }

        var view = await ViewAsync(listId, UnitSystem.US, cancellationToken).ConfigureAwait(false);
        var item = view.ToBuy.Concat(view.Pantry).FirstOrDefault(i => i.Key == key);
        if (item is null)
        {
            return new ShoppingCheckResult(false, list.FinishedAt is not null);
        }

        var existing = await db.ShoppingChecks.FirstOrDefaultAsync(c => c.ShoppingListId == listId && c.ItemKey == key, cancellationToken)
            .ConfigureAwait(false);
        var now = clock.GetUtcNow();
        if (isChecked && existing is null)
        {
            db.ShoppingChecks.Add(new ShoppingCheck { ShoppingListId = listId, ItemKey = key, CheckedAt = now });
        }
        else if (!isChecked && existing is not null)
        {
            db.ShoppingChecks.Remove(existing);
        }

        var keys = (await db.ShoppingChecks.Where(c => c.ShoppingListId == listId && c.ItemKey != key).Select(c => c.ItemKey)
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        if (isChecked)
        {
            keys.Add(key);
        }

        var finished = view.ToBuy.Any() && view.ToBuy.All(i => keys.Contains(i.Key));
        list.FinishedAt = finished ? list.FinishedAt ?? now : null;
        list.UpdatedAt = now;
        if (isChecked && item.IsRestock)
        {
            await MarkRestockedAsync(key, item.Name, now, cancellationToken).ConfigureAwait(false); // BR-47
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ShoppingCheckResult(true, finished);
    }

    /// <summary>
    /// BR-44, ADR 0028: Have it / Need it for a staple a recipe on the list needs (anonymous, like a tick). A null
    /// answer asks again. Only staples on this list can be answered.
    /// </summary>
    public async Task<bool> AnswerStapleAsync(int listId, string? key, StapleAnswer? answer, CancellationToken cancellationToken = default)
    {
        await PurgeFinishedAsync(cancellationToken).ConfigureAwait(false);
        var list = await db.ShoppingLists.FirstOrDefaultAsync(l => l.Id == listId, cancellationToken).ConfigureAwait(false);
        if (list is null || string.IsNullOrEmpty(key) || key.Length > ShoppingRules.MaxItemLength + 2)
        {
            return false;
        }

        var view = await ViewAsync(listId, UnitSystem.US, cancellationToken).ConfigureAwait(false);
        if (!view.ToBuy.Concat(view.Pantry).Any(i => i.Key == key && (i.IsPantry || i.Answer is not null)))
        {
            return false;
        }

        var existing = await db.ShoppingStapleChecks.FirstOrDefaultAsync(c => c.ShoppingListId == listId && c.ItemKey == key, cancellationToken)
            .ConfigureAwait(false);
        var now = clock.GetUtcNow();
        if (answer is null)
        {
            if (existing is not null)
            {
                db.ShoppingStapleChecks.Remove(existing);
            }
        }
        else if (existing is null)
        {
            db.ShoppingStapleChecks.Add(new ShoppingStapleCheck { ShoppingListId = listId, ItemKey = key, Answer = answer.Value, At = now });
        }
        else
        {
            existing.Answer = answer.Value;
            existing.At = now;
        }

        if (answer == StapleAnswer.Need)
        {
            list.FinishedAt = null; // something new to buy
        }

        list.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// FR-SHOP-05, BR-45: adds the chosen staples as restock items, at their usual amount when set. A staple already
    /// restocked on this list is not added twice; one a recipe needs merges with it (BR-40).
    /// </summary>
    public async Task<ShoppingEditResult> AddRestockAsync(int listId, IReadOnlyCollection<StapleInfo> staples, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(staples);
        var list = await OpenListAsync(listId, cancellationToken).ConfigureAwait(false);
        if (list is null)
        {
            return ShoppingEditResult.Fail(NotFound);
        }

        var lines = await db.ShoppingLines.Where(l => l.ShoppingListId == listId).Select(l => new { l.FoodId, l.Name, l.IsRestock })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (lines.Count + staples.Count > ShoppingRules.MaxLinesPerList)
        {
            return ShoppingEditResult.Fail(ListFull);
        }

        var restocked = lines.Where(l => l.IsRestock).Select(l => ShoppingRules.KeyFor(l.FoodId, l.Name)).ToHashSet(StringComparer.Ordinal);
        var position = lines.Count;
        var added = 0;
        foreach (var staple in staples)
        {
            if (!restocked.Add(ShoppingRules.KeyFor(staple.FoodId, staple.Name)))
            {
                continue;
            }

            var text = string.IsNullOrWhiteSpace(staple.Restock) ? staple.Name : StapleRules.RestockAmount(staple.Restock) + " " + staple.Name;
            var parsed = IngredientLineParser.Parse(text);
            var (kind, amount, amountMax, countUnit) = string.IsNullOrWhiteSpace(staple.Restock)
                ? (null, null, null, null)
                : ShoppingRules.Amount(parsed, 1m);
            db.ShoppingLines.Add(new ShoppingLine
            {
                ShoppingListId = listId,
                Name = Cut(staple.Name, ShoppingRules.MaxItemLength),
                Kind = kind,
                Amount = amount,
                AmountMax = amountMax,
                CountUnit = countUnit,
                FoodId = staple.FoodId,
                RawText = Cut(text, ShoppingRules.MaxItemLength * 2),
                Position = position++,
                IsRestock = true,
                Section = staple.FoodId is null ? staple.Section : null,
            });
            added++;
        }

        Touch(list, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ShoppingEditResult(true, null, added);
    }

    private async Task<HashSet<int>> StapleFoodIdsAsync(CancellationToken cancellationToken) =>
        (await db.Foods.AsNoTracking().Where(f => f.IsPantryStaple).Select(f => f.Id).ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();

    /// <summary>BR-47: a restock item was ticked, so its staple was bought now.</summary>
    private async Task MarkRestockedAsync(string key, string name, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Staple? row;
        if (key.StartsWith("f:", StringComparison.Ordinal) && int.TryParse(key[2..], System.Globalization.CultureInfo.InvariantCulture, out var foodId))
        {
            row = await db.Staples.FirstOrDefaultAsync(s => s.FoodId == foodId, cancellationToken).ConfigureAwait(false);
            if (row is null && await db.Foods.AnyAsync(f => f.Id == foodId && f.IsPantryStaple, cancellationToken).ConfigureAwait(false))
            {
                row = new Staple { FoodId = foodId, Name = name, NormalizedName = NameRules.Normalize(name), CreatedAt = now };
                db.Staples.Add(row);
            }
        }
        else
        {
            var normalized = key.StartsWith("n:", StringComparison.Ordinal) ? key[2..] : NameRules.Normalize(name);
            row = await db.Staples.FirstOrDefaultAsync(s => s.FoodId == null && s.NormalizedName == normalized, cancellationToken).ConfigureAwait(false);
        }

        if (row is not null)
        {
            row.LastRestockedAt = now;
        }
    }

    public async Task<bool> DeleteAsync(int listId, CancellationToken cancellationToken = default) =>
        await db.ShoppingLists.Where(l => l.Id == listId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false) > 0;

    /// <summary>ADR 0028: deletes lists finished more than <see cref="UndoWindow"/> ago.</summary>
    public async Task<int> PurgeFinishedAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = clock.GetUtcNow() - UndoWindow;
        var finished = await db.ShoppingLists.Where(l => l.FinishedAt != null).Select(l => new { l.Id, l.FinishedAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var due = finished.Where(l => l.FinishedAt <= cutoff).Select(l => l.Id).ToList();
        return due.Count == 0 ? 0 : await db.ShoppingLists.Where(l => due.Contains(l.Id)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The merged list; lines whose recipe was removed are gone with it (cascade).</summary>
    private async Task<ShoppingView> ViewAsync(int listId, UnitSystem system, CancellationToken cancellationToken)
    {
        var lines = await (from l in db.ShoppingLines.AsNoTracking()
                           where l.ShoppingListId == listId
                           join r in db.ShoppingListRecipes on l.ListRecipeId equals r.Id into rs
                           from r in rs.DefaultIfEmpty()
                           orderby l.Position
                           select new { Line = l, Title = r == null ? null : r.Title }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var foodIds = lines.Where(l => l.Line.FoodId is not null).Select(l => l.Line.FoodId!.Value).Distinct().ToList();
        var foodRows = await (from f in db.Foods.AsNoTracking()
                              where foodIds.Contains(f.Id)
                              join u in db.Units on f.PackageUnitId equals u.Id into us
                              from u in us.DefaultIfEmpty()
                              select new { Food = f, Unit = u }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var shoppingFoods = foodRows.ToDictionary(f => f.Food.Id, f => ToShoppingFood(f.Food, f.Unit));
        var checks = (await db.ShoppingChecks.AsNoTracking().Where(c => c.ShoppingListId == listId).Select(c => c.ItemKey)
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        var answers = await db.ShoppingStapleChecks.AsNoTracking().Where(c => c.ShoppingListId == listId)
            .ToDictionaryAsync(c => c.ItemKey, c => c.Answer, StringComparer.Ordinal, cancellationToken).ConfigureAwait(false);
        var inputs = lines.Select(l => new ShoppingLineInput(l.Line.Id, l.Line.Name, l.Line.Kind, l.Line.Amount, l.Line.AmountMax,
            l.Line.CountUnit, l.Line.FoodId, l.Line.RawText, l.Title, l.Line.ListRecipeId is null && !l.Line.IsRestock, l.Line.IsRestock, l.Line.Section, l.Line.IsStaple));
        return ShoppingRules.Merge(inputs, shoppingFoods, checks, system, answers: answers);
    }

    internal static ShoppingFood ToShoppingFood(Food food, Unit? unit)
    {
        decimal? packageBase = null;
        UnitKind? packageKind = null;
        if (food.PackageSize is { } size && size > 0 && !string.IsNullOrWhiteSpace(food.PackageName))
        {
            if (unit is null)
            {
                packageBase = size;
                packageKind = UnitKind.Count;
            }
            else if (unit.Kind is UnitKind.Volume or UnitKind.Mass)
            {
                packageBase = size * unit.ToBase;
                packageKind = unit.Kind;
            }
        }

        return new ShoppingFood(food.Id, food.Name, food.DensityGPerMl, food.StoreSection, food.IsPantryStaple, packageBase, packageKind,
            packageBase is null ? null : food.PackageName);
    }

    private async Task<ShoppingList?> OpenListAsync(int listId, CancellationToken cancellationToken)
    {
        await PurgeFinishedAsync(cancellationToken).ConfigureAwait(false);
        return await db.ShoppingLists.FirstOrDefaultAsync(l => l.Id == listId, cancellationToken).ConfigureAwait(false);
    }

    private static void Touch(ShoppingList list, DateTimeOffset now)
    {
        list.UpdatedAt = now;
        list.FinishedAt = null; // adding to a finished list keeps it (Undo by adding)
    }

    private async Task TouchAsync(int listId, CancellationToken cancellationToken)
    {
        var list = await db.ShoppingLists.FirstOrDefaultAsync(l => l.Id == listId, cancellationToken).ConfigureAwait(false);
        if (list is not null)
        {
            list.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RememberAsync(string name, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var normalized = NameRules.Normalize(name);
        var existing = await db.ShoppingListNames.FirstOrDefaultAsync(n => n.NormalizedName == normalized, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            db.ShoppingListNames.Add(new ShoppingListName { Name = name, NormalizedName = normalized, LastUsedAt = now });
        }
        else
        {
            existing.Name = name;
            existing.LastUsedAt = now;
        }
    }

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];
}
