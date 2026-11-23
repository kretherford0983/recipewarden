# ADR 0025: The review page is the editor form; a recipe-md writer arrives in M4

- Status: Accepted (2026-11-22, owner)
- Requirements: FR-IMP-04, FR-IMP-06, FR-IMP-07, BR-04, BR-10, AC-14, AC-16, W3, W4

## Context
FR-IMP-06 needs a review screen. W3 describes it as side by side. AC-14 needs an export then re-import to be
identical, but export (FR-EXP-02) is M5.

## Decision
1. **Review page** `/manage/import/{jobId}/review`:
   - It is the existing editor form (`_EditorForm`), prefilled from the parse.
   - Above the form, a warnings panel lists lines with confidence below 0.8 (BR-04), missing parts (BR-01) and
     whether an image was found.
   - A possible-duplicate notice links to the existing recipe (FR-IMP-07) and requires a choice before saving:
     "Open existing" or "Save anyway" (AC-16).
   - For Web imports the source credit is shown and locked (BR-10).
   - The image opt-in (BR-12) is ticked here or on the import page.
   - Saving creates the recipe through `RecipeService` (Draft or Publish, BR-01) and marks the job done.
2. **Several recipes in one file** (W4): one ImportJob per recipe, each with its own review. The import page lists
   them.
3. **recipe-md writer:** `RecipeMarkdown.Write` (Core) writes recipe-md v1 (9.7) in M4, so AC-14's round trip
   (write → read → same fields) is tested now. The export download and UI stay in M5.

## Consequences
- One form to maintain. Side-by-side viewing of the source is limited to a collapsible "Source text" panel.
