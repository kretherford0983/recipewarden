using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RecipeWarden.Core.Nutrition;
using RecipeWarden.Core.Search;
using RecipeWarden.Infrastructure.Data;
using RecipeWarden.Infrastructure.Nutrition;

namespace RecipeWarden.Infrastructure.Tests;

/// <summary>
/// FR-NUT-01 to 05, FR-ALG-01/02, BR-50 to BR-54, AC-55 to AC-58, ADR 0034 to 0036: nutrition, allergens, the unmatched
/// report, mapping, label foods, the dataset and the "free of" filter, against a real database.
/// </summary>
[Trait("Req", "FR-NUT-02")]
public sealed class FR_NUT_02_NutritionServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("Req", "D-06")]
    public async Task The_bundled_sr_legacy_dataset_is_applied_once_with_densities_and_portions()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        (await s.Db.NutrientRefs.CountAsync(n => n.DataType == "sr_legacy_food", Ct)).ShouldBe(7793);
        var egg = await s.Db.NutrientRefs.SingleAsync(n => n.FdcId == 171287, Ct);
        egg.Description.ShouldBe("Egg, whole, raw, fresh");
        egg.Calories.ShouldBe(143);
        egg.PortionsJson!.ShouldContain("\"large\":50");
        (await s.Db.NutrientRefs.SingleAsync(n => n.FdcId == 168894, Ct)).DensityGPerMl.ShouldBe(0.5283m); // 1 cup flour = 125 g
        var sr = await s.Db.NutrientDatasets.SingleAsync(d => d.Version == NutrientDatasetImporter.SeedVersion, Ct);
        sr.Foods.ShouldBe(7793);
        (await s.NutrientImporter.ApplySeedAsync(Ct)).ShouldBeFalse();
    }

    [Fact]
    [Trait("Req", "FR-NUT-07")]
    [Trait("Req", "ADR-0052")]
    public async Task Foundation_foods_and_fndds_ship_next_to_sr_legacy()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        (await s.Db.NutrientRefs.CountAsync(n => n.DataType == "foundation_food", Ct)).ShouldBe(469);
        (await s.Db.NutrientRefs.CountAsync(n => n.DataType == "survey_fndds_food", Ct)).ShouldBe(5432);

        // FNDDS: nutrients written by number, a WWEIA category, and "1 cup" portions that give a density.
        var milk = await s.Db.NutrientRefs.SingleAsync(n => n.FdcId == 2705384, Ct);
        milk.Description.ShouldBe("Milk, NFS");
        milk.Category.ShouldNotBeNullOrEmpty();
        milk.Calories.ShouldNotBeNull();
        milk.Calories!.Value.ShouldBeInRange(40m, 80m);
        milk.ProteinG.ShouldNotBeNull();
        milk.DensityGPerMl!.Value.ShouldBeInRange(0.95m, 1.1m);
        (await s.Db.NutrientRefs.SingleAsync(n => n.FdcId == 748967, Ct)).Description.ShouldBe("Eggs, Grade A, Large, egg whole");

        var summary = (await s.Nutrition.DatasetSummaryAsync(Ct))!;
        summary.Versions.ShouldBe([NutrientDatasetImporter.SeedVersion, NutrientDatasetImporter.SecondSeedVersion]);
        summary.Foods.ShouldBe(7793 + 469 + 5432);
        (await s.Db.SeedRuns.CountAsync(r => r.Name.StartsWith("nutrients-"), Ct)).ShouldBe(2);
    }

    [Fact]
    [Trait("Req", "AC-58")]
    [Trait("Req", "ADR-0052")]
    public async Task A_newer_fndds_file_reads_nutrient_numbers_and_flags_only_fndds_foods()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var zip = Zip(
            ("survey/food.csv",
                "\"fdc_id\",\"data_type\",\"description\",\"food_category_id\"\n\"2705384\",\"survey_fndds_food\",\"Milk, NFS\",\"1004\"\n"),
            ("survey/nutrient.csv", "\"id\",\"name\",\"unit_name\",\"nutrient_nbr\"\n\"1008\",\"Energy\",\"KCAL\",\"208\"\n\"1003\",\"Protein\",\"G\",\"203\"\n"),
            ("survey/food_nutrient.csv",
                "\"id\",\"fdc_id\",\"nutrient_id\",\"amount\"\n\"1\",\"2705384\",\"208\",\"61\"\n\"2\",\"2705384\",\"203\",\"3.3\"\n\"3\",\"2705384\",\"301\",\"120\"\n"),
            ("survey/food_portion.csv",
                "\"id\",\"fdc_id\",\"seq_num\",\"amount\",\"measure_unit_id\",\"portion_description\",\"modifier\",\"gram_weight\"\n\"1\",\"2705384\",\"1\",\"\",\"9999\",\"1/2 cup\",\"10205\",\"122\"\n"),
            ("survey/measure_unit.csv", "\"id\",\"name\"\n\"9999\",\"undetermined\"\n"),
            ("survey/wweia_food_category.csv", "\"wweia_food_category\",\"wweia_food_category_description\"\n\"1004\",\"Milk, reduced fat\"\n"));
        var result = await s.NutrientImporter.ImportAsync(new MemoryStream(zip), "FNDDS 2026-10", "admin", Ct);
        result.Ok.ShouldBeTrue(result.Error);
        result.Removed.ShouldBe(5431); // every other FNDDS food; SR Legacy and Foundation are other kinds

        var milk = await s.Db.NutrientRefs.AsNoTracking().SingleAsync(n => n.FdcId == 2705384, Ct);
        milk.Calories.ShouldBe(61);
        milk.ProteinG.ShouldBe(3.3m);
        milk.Category.ShouldBe("Milk, reduced fat");
        milk.DensityGPerMl.ShouldBe(Math.Round(122m / (0.5m * 236.5882365m), 4));
        (await s.Db.NutrientRefs.CountAsync(n => n.RemovedInVersion != null && n.DataType != "survey_fndds_food", Ct)).ShouldBe(0);
    }

    [Fact]
    [Trait("Req", "AC-55")]
    public async Task Calculated_nutrition_is_per_serving_with_completeness_and_manual_then_imported_win()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var r = await s.AddAsync("Pancakes", "2 cups all-purpose flour\n2 large eggs\n1 cup whole milk\nsalt to taste\n2 Tbsp gochujang");
        var n = (await s.Nutrition.GetAsync(r.Id, Ct))!;
        n.Calculated.Counted.ShouldBe(4);   // "to taste" is not counted
        n.Calculated.Matched.ShouldBe(3);   // gochujang has no food
        n.Panel!.Source.ShouldBe(NutritionSource.Calculated);
        n.Panel.CompletenessPct.ShouldBe(75);
        n.Panel.IsPartial.ShouldBeFalse();
        n.HasUnmatched.ShouldBeTrue();
        // 250 g flour (364 kcal/100 g) + 100 g egg (143) + 244 g milk (61) over 4 servings (the default).
        Math.Round(n.Panel.PerServing.Calories!.Value).ShouldBeInRange(300, 310);

        (await s.Nutrition.SetImportedAsync(r.Id, new NutrientValues(410, 14), RecipeTestScope.UserId, Ct)).Ok.ShouldBeTrue();
        (await s.Nutrition.GetAsync(r.Id, Ct))!.Panel!.Source.ShouldBe(NutritionSource.Imported);
        (await s.Nutrition.SetManualAsync(r.Id, new NutrientValues(300), RecipeTestScope.UserId, Ct)).Ok.ShouldBeTrue();
        var manual = (await s.Nutrition.GetAsync(r.Id, Ct))!;
        manual.Panel!.Source.ShouldBe(NutritionSource.Manual);
        manual.Imported.ShouldBe(new NutrientValues(410, 14));
        (await s.Nutrition.SetManualAsync(r.Id, null, RecipeTestScope.UserId, Ct)).Ok.ShouldBeTrue();
        (await s.Nutrition.GetAsync(r.Id, Ct))!.Panel!.Source.ShouldBe(NutritionSource.Imported); // clearing manual keeps imported
        (await s.Nutrition.SetManualAsync(r.Id, new NutrientValues(-5), RecipeTestScope.UserId, Ct)).Error.ShouldBe(NutritionRules.BadValue);
        (await s.Nutrition.SetManualAsync(99999, new NutrientValues(5), RecipeTestScope.UserId, Ct)).Error.ShouldBe(NutritionService.NotFound);
        (await s.Nutrition.GetAsync(99999, Ct)).ShouldBeNull();
    }

    [Fact]
    [Trait("Req", "BR-51")]
    public async Task Cooked_chicken_counts_its_raw_weight_and_ranges_use_the_low_serving()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var edit = RecipeTestScope.Edit("Chicken Bowl", "4 cups cooked shredded chicken") with { Servings = 6m, ServingsMax = 8m };
        var saved = await s.Recipes.CreateAsync(edit, RecipeTestScope.UserId, Ct);
        var n = (await s.Nutrition.GetAsync(saved.Id, Ct))!;
        var line = n.Calculated.Lines.Single();
        line.State.ShouldBe(NutritionLineState.Matched);
        line.Grams!.Value.ShouldBeInRange(700m, 900m); // about 1¾ lb raw
        n.Panel!.PerServing.Calories!.Value.ShouldBe(line.Values!.Calories!.Value / 6, 0.01m);
    }

    [Fact]
    [Trait("Req", "AC-57")]
    [Trait("Req", "FR-NUT-03")]
    public async Task Gochujang_tops_the_unmatched_report_and_one_mapping_completes_every_recipe()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var ids = new List<int>();
        foreach (var t in new[] { "Bibimbap", "Tteokbokki", "Gochujang Wings" })
        {
            ids.Add((await s.AddAsync(t, "2 Tbsp gochujang\n1 cup water")).Id);
        }

        await s.AddAsync("Other", "1 tsp yuzu kosho\n1 cup water");
        var report = await s.Nutrition.UnmatchedAsync(Ct);
        report[0].ShouldBe(new UnmatchedRow("gochujang", null, null, 3, 3, NutritionService.NoFood));
        report.ShouldContain(r => r.Name == "yuzu kosho");
        (await s.Nutrition.GetAsync(ids[0], Ct))!.Calculated.CompletenessPct.ShouldBe(50);

        var choices = await s.Nutrition.SearchAsync("chili sauce", Ct);
        choices.ShouldNotBeEmpty();

        // FR-NUT-06: AI candidates fall back to any-word matches, best first, when the strict search finds nothing.
        (await s.Nutrition.SearchAsync("chili paste", Ct)).ShouldBeEmpty();
        var loose = await s.Nutrition.SuggestionCandidatesAsync("chili paste", Ct);
        loose.ShouldNotBeEmpty();
        loose.Count.ShouldBeLessThanOrEqualTo(NutritionService.SearchLimit);
        loose.ShouldAllBe(c => !c.Removed && (c.Description.Contains("chili", StringComparison.OrdinalIgnoreCase) || c.Description.Contains("paste", StringComparison.OrdinalIgnoreCase)));
        (await s.Nutrition.SuggestionCandidatesAsync("chili sauce", Ct)).Select(c => c.Id).ShouldBe(choices.Where(c => !c.Removed).Select(c => c.Id));
        (await s.Nutrition.SuggestionCandidatesAsync("zq", Ct)).ShouldBeEmpty();
        var chili = choices[0];
        (await s.Nutrition.MapAsync(null, "gochujang", chili.Id, RecipeTestScope.UserId, Ct)).Ok.ShouldBeTrue();

        foreach (var id in ids)
        {
            (await s.Nutrition.GetAsync(id, Ct))!.Calculated.CompletenessPct.ShouldBe(100);
        }

        (await s.Nutrition.UnmatchedAsync(Ct)).ShouldNotContain(r => r.Name == "gochujang");
        (await s.Nutrition.MapAsync(null, "gochujang", chili.Id, RecipeTestScope.UserId, Ct)).Ok.ShouldBeFalse(); // the name is taken now
        (await s.Nutrition.MapAsync(null, "x", 999999, RecipeTestScope.UserId, Ct)).Error.ShouldBe(NutritionService.NotFound);
        (await s.Nutrition.MapAsync(999999, null, chili.Id, RecipeTestScope.UserId, Ct)).Error.ShouldBe(NutritionService.NotFound);
    }

    [Fact]
    [Trait("Req", "AC-57")]
    [Trait("Req", "FR-NUT-04")]
    [Trait("Req", "BR-54")]
    public async Task A_label_food_is_stored_per_100_g_and_a_mapping_beats_the_automatic_match()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var (result, id) = await s.Nutrition.AddLabelFoodAsync("House granola", 30, false, null, new NutrientValues(110, 4), RecipeTestScope.UserId, Ct);
        result.Ok.ShouldBeTrue();
        var row = await s.Db.NutrientRefs.SingleAsync(n => n.Id == id, Ct);
        row.Source.ShouldBe(NutrientSources.Custom);
        Math.Round(row.Calories!.Value).ShouldBe(367);
        (await s.Nutrition.SearchAsync("house granola", Ct))[0].Id.ShouldBe(id!.Value);
        (await s.Nutrition.AddLabelFoodAsync("", 30, false, null, new NutrientValues(1), RecipeTestScope.UserId, Ct)).Result.Ok.ShouldBeFalse();
        (await s.Nutrition.AddLabelFoodAsync("x", 0, false, null, new NutrientValues(1), RecipeTestScope.UserId, Ct)).Result.Error.ShouldBe(NutritionRules.BadServing);

        // BR-54: water's automatic USDA match is replaced by an editor mapping.
        var r = await s.AddAsync("Water Test", "100 g water");
        (await s.Nutrition.GetAsync(r.Id, Ct))!.Panel!.PerServing.Calories.ShouldBe(0);
        var water = await s.Db.Foods.SingleAsync(f => f.Name == "water", Ct);
        (await s.Nutrition.MapAsync(water.Id, null, id.Value, RecipeTestScope.UserId, Ct)).Ok.ShouldBeTrue();
        Math.Round((await s.Nutrition.GetAsync(r.Id, Ct))!.Panel!.PerServing.Calories!.Value).ShouldBe(Math.Round(367m / 4));
        (await s.Nutrition.SearchAsync(" ", Ct)).ShouldBeEmpty();
    }

    [Fact]
    [Trait("Req", "AC-56")]
    [Trait("Req", "BR-52")]
    public async Task Soy_sauce_gives_soy_and_wheat_and_an_override_survives_re_detection()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var r = await s.AddAsync("Stir Fry", "2 Tbsp soy sauce\n1 cup broccoli");
        var n = (await s.Nutrition.GetAsync(r.Id, Ct))!;
        n.Allergens.Keys.ShouldBe([AllergenCode.Wheat, AllergenCode.Soy, AllergenCode.Gluten], ignoreOrder: true);

        // An editor removes Wheat and Gluten (gluten-free tamari).
        (await s.Nutrition.SetAllergensAsync(r.Id, new Dictionary<AllergenCode, AllergenStatus>
        {
            [AllergenCode.Wheat] = AllergenStatus.NotPresent, [AllergenCode.Gluten] = AllergenStatus.NotPresent,
        }, Ct)).Ok.ShouldBeTrue();

        // Re-detection on the next save keeps the override.
        var edit = (await s.Recipes.GetForEditAsync(r.Id, Ct))!;
        await s.Recipes.UpdateAsync(r.Id, edit.Edit with { IngredientsText = "2 Tbsp soy sauce\n1 cup broccoli\n1 tsp sesame oil" }, edit.Version, RecipeTestScope.UserId, false, Ct);
        var after = (await s.Nutrition.GetAsync(r.Id, Ct))!;
        after.Allergens.Keys.ShouldBe([AllergenCode.Soy, AllergenCode.Sesame], ignoreOrder: true);
        after.Detected.ShouldContainKey(AllergenCode.Wheat);
        (await s.Nutrition.SetAllergensAsync(99999, new Dictionary<AllergenCode, AllergenStatus>(), Ct)).Ok.ShouldBeFalse();
        (await s.Nutrition.SetAllergensAsync(r.Id, new Dictionary<AllergenCode, AllergenStatus>(), Ct)).Ok.ShouldBeTrue();
        (await s.Nutrition.GetAsync(r.Id, Ct))!.Allergens.ShouldContainKey(AllergenCode.Wheat);
    }

    [Fact]
    [Trait("Req", "BR-53")]
    public async Task Free_of_hides_detected_and_unverified_recipes_unless_asked()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var milk = await s.AddAsync("Milk Soup", "1 cup whole milk");
        var plain = await s.AddAsync("Water Soup", "1 cup water");
        var unverified = await s.AddAsync("Mystery Soup", "1 cup zorbleberries");
        var overridden = await s.AddAsync("Vegan Soup", "1 cup whole milk");
        await s.Nutrition.SetAllergensAsync(overridden.Id, new Dictionary<AllergenCode, AllergenStatus> { [AllergenCode.Milk] = AllergenStatus.NotPresent }, Ct);
        var manualOnly = await s.AddAsync("Shared Kitchen Soup", "1 cup water");
        await s.Nutrition.SetAllergensAsync(manualOnly.Id, new Dictionary<AllergenCode, AllergenStatus> { [AllergenCode.Milk] = AllergenStatus.MayContain }, Ct);

        async Task<string[]> Titles(SearchInput input) =>
            [.. (await s.Search.SearchAsync(SearchCriteria.Parse(input), cancellationToken: Ct)).Items.Select(i => i.Title).Order()];

        (await Titles(new SearchInput { FreeOf = ["milk"] })).ShouldBe(["Vegan Soup", "Water Soup"]);
        (await Titles(new SearchInput { FreeOf = ["milk"], IncludeUnverified = "1" })).ShouldBe(["Mystery Soup", "Vegan Soup", "Water Soup"]);
        (await Titles(new SearchInput { FreeOf = ["bogus"] })).Length.ShouldBe(5);
        _ = milk;
        _ = plain;
        _ = unverified;
    }

    private static byte[] Zip(params (string Name, string Text)[] files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in files)
            {
                using var w = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
                w.Write(text);
            }
        }

        return ms.ToArray();
    }

    [Fact]
    [Trait("Req", "AC-58")]
    [Trait("Req", "FR-NUT-05")]
    public async Task A_newer_dataset_keeps_mappings_flags_removed_foods_and_updates_the_version()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        var r = await s.AddAsync("Egg Bake", "2 large eggs\n1 Tbsp gochujang");
        // The SR Legacy chili sauce (ADR 0052 adds FNDDS and Foundation foods, which an SR Legacy file never flags).
        var srIds = await s.Db.NutrientRefs.Where(n => n.DataType == "sr_legacy_food").Select(n => n.Id).ToListAsync(Ct);
        var chili = (await s.Nutrition.SearchAsync("chili sauce", Ct)).First(f => srIds.Contains(f.Id));
        await s.Nutrition.MapAsync(null, "gochujang", chili.Id, RecipeTestScope.UserId, Ct);
        var chiliFdc = (await s.Db.NutrientRefs.SingleAsync(n => n.Id == chili.Id, Ct)).FdcId;

        // A "newer" SR Legacy file: egg changes, a new food appears, everything else (chili sauce included) is gone.
        var zip = Zip(
            ("FoodData_Central_sr_legacy_food_csv_2026-04/food.csv",
                "\"fdc_id\",\"data_type\",\"description\",\"food_category_id\"\n\"171287\",\"sr_legacy_food\",\"Egg, whole, raw, fresh\",\"1\"\n\"999001\",\"sr_legacy_food\",\"Test food, \"\"quoted\"\", raw\",\"1\"\n\"5\",\"branded_food\",\"Ignored\",\"1\"\n"),
            ("FoodData_Central_sr_legacy_food_csv_2026-04/nutrient.csv", "\"id\",\"name\"\n\"1008\",\"Energy\"\n"),
            ("FoodData_Central_sr_legacy_food_csv_2026-04/food_nutrient.csv",
                "\"id\",\"fdc_id\",\"nutrient_id\",\"amount\"\n\"1\",\"171287\",\"1008\",\"150\"\n\"2\",\"999001\",\"2047\",\"10\"\n\"3\",\"999001\",\"1003\",\"x\"\n"),
            ("FoodData_Central_sr_legacy_food_csv_2026-04/food_portion.csv",
                "\"id\",\"fdc_id\",\"seq_num\",\"amount\",\"measure_unit_id\",\"modifier\",\"gram_weight\"\n\"1\",\"171287\",\"1\",\"1\",\"9999\",\"large\",\"51\"\n"),
            ("FoodData_Central_sr_legacy_food_csv_2026-04/food_category.csv", "\"id\",\"code\",\"description\"\n\"1\",\"0100\",\"Dairy and Egg Products\"\n"));
        var result = await s.NutrientImporter.ImportAsync(new MemoryStream(zip), "SR Legacy 2026-04", "admin", Ct);
        result.ShouldBe(new NutrientImportResult(true, null, "SR Legacy 2026-04", 2, 1, 1, 7792));

        var egg = await s.Db.NutrientRefs.AsNoTracking().SingleAsync(n => n.FdcId == 171287, Ct);
        egg.Calories.ShouldBe(150);
        egg.PortionsJson.ShouldBe("{\"large\":51}");
        (await s.Db.NutrientRefs.AsNoTracking().SingleAsync(n => n.FdcId == 999001, Ct)).Description.ShouldBe("Test food, \"quoted\", raw");
        (await s.Db.NutrientRefs.AsNoTracking().SingleAsync(n => n.FdcId == chiliFdc, Ct)).RemovedInVersion.ShouldBe("SR Legacy 2026-04");
        (await s.Nutrition.DatasetAsync(Ct))!.Version.ShouldBe("SR Legacy 2026-04");

        // The mapping is kept, and the report asks for it to be mapped again.
        var report = await s.Nutrition.UnmatchedAsync(Ct);
        report.ShouldContain(u => u.Name == "gochujang" && u.Reason == NutritionService.NeedsRemap);
        (await s.Nutrition.GetAsync(r.Id, Ct))!.Calculated.Matched.ShouldBe(2);
        (await s.Nutrition.SearchAsync("chili sauce", Ct)).First(f => f.Id == chili.Id).Removed.ShouldBeTrue();
    }

    [Fact]
    public async Task Files_that_are_not_fdc_zips_change_nothing()
    {
        await using var s = await RecipeTestScope.CreateAsync();
        (await s.NutrientImporter.ImportAsync(new MemoryStream(Encoding.ASCII.GetBytes("not a zip")), "v", "admin", Ct)).Error.ShouldBe(NutrientDatasetImporter.NotFdc);
        (await s.NutrientImporter.ImportAsync(new MemoryStream(Zip(("food.csv", "x"))), "v", "admin", Ct)).Error.ShouldBe(NutrientDatasetImporter.NotFdc);
        (await s.NutrientImporter.ImportAsync(new MemoryStream(Zip(("food.csv", "a,b\n"), ("food_nutrient.csv", "fdc_id,nutrient_id,amount\n"))), "v", "admin", Ct))
            .Error.ShouldBe(NutrientDatasetImporter.NotFdc);
        (await s.NutrientImporter.ImportAsync(new MemoryStream(Zip(("food.csv", "fdc_id,data_type,description\n1,branded_food,x\n"),
            ("food_nutrient.csv", "fdc_id,nutrient_id,amount\n"))), "", "admin", Ct)).Error.ShouldBe(NutrientDatasetImporter.NoFoods);
        (await s.Db.NutrientRefs.CountAsync(n => n.RemovedInVersion != null, Ct)).ShouldBe(0);
        (await s.Db.NutrientDatasets.CountAsync(Ct)).ShouldBe(NutrientDatasetImporter.Seeds.Count);
    }
}
