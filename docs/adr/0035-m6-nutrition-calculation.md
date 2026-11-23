# ADR 0035: M6 nutrition calculation and display

- Status: Accepted by the owner ("use your recommendations", 2026-11-23); details below were decided while building
  and are **for owner review**.
- Requirements: FR-NUT-01, FR-NUT-02, BR-50, BR-51, AC-55; section 8.2 NutritionFacts

## Decisions
1. **Calculated nutrition is computed on view**, from the current lines and mappings, so it is always up to date
   (FR-NUT-02) and nothing needs a recalculation job. `NutritionFacts` stores only **manual** and **imported**
   values (one row per recipe, with its source). Precedence is manual > imported > calculated (BR-50).
2. **Grams per line (Core `NutritionRules`):** weight units directly; volume × the food's density (else the nutrient
   row's density from its cup, tablespoon or teaspoon portion); counts × a portion weight from the nutrient row
   ("large", "medium", "clove", "slice"…). A "stick" is ½ cup, and a package count ("1 can") uses the food's package
   size when it has one. A cooked line with a yield conversion uses the purchase weight with the raw food's
   nutrients (BR-26).
3. **Completeness** is the share of counted lines with grams and nutrients. "To taste" and lines without an amount
   are not counted; vague amounts ("pinch") count as matched. Below 70% the panel says "partial".
4. **Per serving** divides by the low end of a servings range ("6–8" → 6) and is the same at any scale (BR-51).
5. **Imported nutrition:** new imports keep a page's schema.org `nutrition` and recipe-md's `nutrition.per_serving`;
   recipes imported before M6 show calculated values (re-import to pick up the page's).
6. **Manual nutrition** is entered, or cleared, on the recipe's Nutrition and allergens page (Editor).
