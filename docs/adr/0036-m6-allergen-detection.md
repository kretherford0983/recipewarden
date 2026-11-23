# ADR 0036: M6 allergen detection and the "free of" filter

- Status: Accepted by the owner ("use your recommendations", 2026-11-23); the word lists are **for owner review**.
- Requirements: FR-ALG-01 to 03, BR-52, BR-53, AC-56; section 8.2 RecipeAllergen

## Decisions
1. **Allergens:** milk, egg, fish, crustacean shellfish, tree nuts, peanuts, wheat, soy, sesame, plus gluten. Tree
   nuts follow FDA's 2025 list of 12 (almond, Brazil nut, cashew, hazelnut, macadamia, pecan, pine nut, pistachio and
   the walnuts). Coconut and chestnut are not tree nuts there.
2. **Rules (Core `AllergenRules`)** read each line's ingredient name and its food's name, by whole words.
   - Gluten: wheat, barley and rye. Oats count as "may contain" gluten unless the line says gluten-free.
   - "May contain" is used for vague lines: plain "broth" or "stock", "bouillon", "chocolate", "curry paste", "granola".
   - "Gluten-free", "dairy-free", "egg-free", "vegan" and "plant-based" on a line remove the matching allergens.
   - Plant milks and nut butters are not milk, eggplant is not egg, and so on.
3. **Stored for search:** automatic results are written as `RecipeAllergen` rows (`Origin = Auto`) whenever a recipe
   is saved, and once for existing recipes (seed run `allergens-v1`). A new rules version re-runs the backfill.
   Editor overrides are `Origin = Manual` rows that win and are never touched by detection (BR-52). There is no
   FoodAllergen table: rules on food names cover the catalogue.
4. **"Free of" (BR-53)** hides recipes where the allergen is detected or set (Contains or May contain), and recipes
   with unmatched lines unless "include unverified" is ticked.
5. **Disclaimer (FR-ALG-03):** the footer line stays. The recipe's allergen badges and nutrition panel also say
   they are estimates and that labels should be checked.
