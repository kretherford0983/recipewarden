# ADR 0034: M6 nutrient dataset, mappings and custom foods

- Status: Accepted by the owner ("use your recommendations", 2026-11-23); details below were decided while building
  and are **for owner review**.
- Requirements: D-06, FR-NUT-01 to 06, BR-50, BR-54, AC-57, AC-58; section 8.3 NutrientRef

## Decisions
1. **Dataset.** USDA FoodData Central **SR Legacy (2018-04)**, CC0, is bundled: 7,793 foods with the 9 panel
   nutrients per 100 g and their household portions. `tools/seed/build-nutrients.py` trims the USDA CSV download to
   those columns and writes `Data/Seed/fdc-sr-legacy-2018-04.zip` (same CSV layout, about 1 MB). **Foundation Foods**
   was the second recommended set, but its download is blocked from the build machine. The importer reads the
   Foundation Foods CSV zip unchanged, so it can be added with the CLI command (point 6) or shipped in a later
   release.
   The branded and survey (FNDDS) sets are left out.
2. **One importer for seed and refresh.** `NutrientDatasetImporter` reads any FoodData Central CSV zip (`food.csv`,
   `nutrient.csv`, `food_nutrient.csv`, optional `food_portion.csv`, `food_category.csv`), keeps sr_legacy_food and
   foundation_food rows, and upserts `NutrientRef` rows by FDC id. Energy is nutrient 1008, else 2047/2048 (Atwater).
   Foods missing from a newer file are kept and flagged `RemovedInVersion`; mappings to them stay but show in the
   report as "needs re-mapping" (FR-NUT-05, AC-58). The app never downloads anything.
3. **Seed run `nutrients-sr-legacy-2018-04`** applies the bundled zip once at startup, like the other seeds.
4. **Mapping precedence (BR-54).** An ingredient's nutrients come from its food: `Food.NutrientRefId` (an Editor
   mapping, USDA or custom) wins over `Food.FdcId` (the seeded automatic match). Mapping an unmatched ingredient
   name creates a household food with that name (`Source = Custom`), the chosen nutrient row and the row's density,
   then links every line with that name. So one mapping applies to every recipe (FR-NUT-03), and the food is also
   used by scaling, shopping and allergens.
5. **Custom foods from a label (FR-NUT-04).** Serving size in g (or in ml with an optional density; without one,
   1 g/ml is assumed and shown). Values are stored per 100 g as a `NutrientRef` with `Source = Custom`, searchable and
   mappable like USDA rows.
6. **Dataset refresh UI waits for the admin area (M7).** M6 adds the importer, its tests, and an Admin-only CLI
   command, `sudo recipewarden nutrition-import <zip>`. The upload page `/admin/nutrition-data` comes with the M7
   admin pages, with a 100 MB cap for FDC zips only.
7. **FR-NUT-06 (AI mapping suggestions)** moves to M8 with the AI parser.
