# ADR 0013: Measurement seed data from USDA; ingredients linked to foods on save

- Status: Accepted (2026-11-21, owner)
- Requirements: FR-MEAS-01, FR-MEAS-04, FR-MEAS-05, BR-23, BR-26, section 8.2 (FoodId), 8.3, D-06

## Context
Densities and yield conversions need seed data whose licence allows a public repository, and `RecipeIngredient.FoodId`
arrives in M2 (8.2).

## Decision
1. **Sources (public domain only):**
   - Densities from USDA FoodData Central, SR Legacy household portion weights (`food_portion.csv`): CC0 1.0.
     Each seeded food keeps its `FdcId` so M6 nutrition can join on it.
   - Yield conversions from the USDA Table of Cooking Yields for Meat and Poultry (Release 2): US government work,
     public domain.
   - Vague amounts use the values already in the spec (FR-MEAS-03).
   - Publisher weight charts (e.g. baking-company charts) are not used: copyrighted.
2. **Size:** about 150 common foods with aliases and about 15 yield conversions. A script in `tools/seed/` turns
   the USDA CSVs into a checked-in JSON seed (`Food`, `FoodAlias`, `YieldConversion`), so the numbers are
   reproducible and reviewable. The raw USDA files are not committed.
3. **Linking:** an ingredient is linked to a food when the recipe is saved, by normalized name or alias, singular or
   plural (Core rule, unit-tested). A repeatable startup step links existing ingredients that have no `FoodId`. An
   unmatched ingredient keeps `FoodId` null and its original unit (BR-23: no guessing).

## Consequences
- The seed is inserted by a migration with insert-if-missing semantics, like ADR 0011; editors' later changes
  (FR-MEAS-05) are never overwritten.
- Seed values are approximations; `/manage/conversions` lets the household correct them.
