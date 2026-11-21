# ADR 0009: M1 scope — images to M4, "free of" filter to M6, model gaps filled

- Status: Accepted (2026-11-20)
- Requirements: FR-REC-01, FR-VIEW-01, FR-SRCH-02, BR-53, FR-REC-03, BR-05, section 8.2, section 9.10

## Context
Section 9.10 puts model, CRUD, ingredient parser, reading view, search, tags and collections in M1. Some
requirements touched by M1 depend on work scheduled later, and section 8.2 leaves gaps the parser and trash need.

## Decision
1. **Images move to M4.** Finish and step image upload (FR-REC-01, W2 step 5), the image part of UploadValidator
   (SEC-50 to 52) and `/media/{id}` are built once, in M4, together with document uploads. M1 recipes are text
   only; the `Image` table and `Recipe.FinishImageId` / `Step.ImageId` columns arrive with M4's migration. The
   "has image" search filter (FR-SRCH-02) lands with them.
2. **BR-53 and the "free of" filter move to M6.** Without allergen detection every recipe would count as
   unverified and the filter would hide everything. Allergen badges and nutrition in the reading view
   (FR-VIEW-01) are M6 as already planned.
3. **Model gaps in 8.2, filled now:**
   - `RecipeIngredient.Name`: the parsed ingredient text ("all-purpose flour"). AC-03 and the unmatched report
     (FR-NUT-03) need it whether or not a catalogue food matches. `FoodId` arrives with the Food catalogue (M2).
   - `Unit` (8.3) is created and seeded in M1 from a catalogue in Core; the parser uses the same in-memory
     catalogue, so parser and database cannot drift. Densities and the display planner stay in M2.
   - `Recipe.DeletedById` for the trash list (BR-05).
   - `RecipeSlugAlias` (ADR 0008).
   - `Tag.NormalizedName` and `Collection.NormalizedName`, unique: SQLite `NOCASE` folds ASCII only.
4. **Drafts are not public.** Anonymous readers get 404 for a Draft and never see one in browse or search;
   Editors and Admins can open it from `/manage`.

## Consequences
- M1 is smaller and the upload security code is written and tested in one place.
- AC-23 closes in M5 (cook mode); M1 covers its "no edit controls for readers" part.
- Rating filter and last-cooked sort (FR-SRCH-02) work on their columns; values are entered with FR-REC-07.
