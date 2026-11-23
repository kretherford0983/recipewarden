# ADR 0033: AI exchange (M5b) — scope and flow, decided while the owner was away

- Status: Proposed — **owner review requested** (2026-11-23). Built as described.
- Requirements: FR-EXP-04 to 08, BR-90 to BR-97, AC-26 to AC-29, W10; section 7.2 export-ai and ai-response rows

## Context
ADR 0027 split the AI exchange into M5b. The owner asked for work to continue without questions and for open points
to be recorded for review. The spec leaves these open: where goal templates live, what is stored, how a reply is
matched to its recipe, and what a saved variant looks like.

## Decisions
1. **No AI calls (BR-90).** Export for AI builds text in the browser's page; the household copies it to their own AI
   assistant. Import AI response only reads pasted text or an uploaded .md/.txt file (through UploadValidator).
2. **Goal templates are built in (Core).** The catalogue in section 4.9 is code with tests. Editing templates and the
   per-recipe prompt history (FR-EXP-08, *Could*; tables AiGoalTemplate and AiPromptHistory) are **deferred**. This
   keeps Export for AI a read-only GET page that anyone may use (7.2: Anonymous; SEC-31).
3. **Export for AI** at `/recipes/{slug}/export-ai`, `/collections/{slug}/export-ai` and `/recipes/export-ai` (all
   recipes; FR-EXP-04 asks for it, 7.2 did not list it). Readers get published recipes only; editors drafts too.
   Collection and all-recipe exports share the search rate limit. Recipes are exported at 1x in recipe-md v1 with
   their source.
   - Goals, details, "what happened last time", constraints, "ask me questions first", "include My Kitchen" and
     "include the recipe file" are GET fields. The page shows an editable preview.
   - Copy and Download act on exactly the text in the preview (BR-97), in the browser (`ai-export.js`).
   - Without JavaScript the text can still be selected and copied by hand.
   - Free text the user types is put in double quotes on one line, with any quotes inside it changed to single quotes,
     so it reads as data and never as template wording (BR-97).
   - BR-96 conflicts: "Reduce" and "Increase" naming the same ingredient (case-insensitive) block the preview until
     the user ticks "Keep both", which adds a note to the prompt.
   - "Include My Kitchen" adds the active tools by name and capacity, and the unit system the reader is viewing in
     (BR-91: nothing else). "Adapt to my tools" turns it on.
4. **Import AI response** at `/manage/import/ai-response` (Editor).
   - Paste or upload the reply. Every recipe-md block is extracted from the chat text: a front matter starting
     `format: recipe-md/1`, inside a code fence or not.
   - Each block is matched to its recipe by its `slug:`. When the page was opened from a recipe (`?parent=`), a
     single block belongs to that recipe whatever its slug.
   - A block with no title, ingredients or steps, or with no matching recipe, is listed with what is missing.
     Nothing is saved for it (AC-27).
   - The review shows the "## Changes" section and a field-by-field and line-by-line diff against the current recipe.
   - Saving re-reads the posted reply on the server; nothing is kept between the two steps.
5. **Saving.** The default is **Save as variant** (BR-93). It creates a new **draft** recipe with ParentRecipeId set.
   Editors can publish it, or **Replace the original** (FR-EXP-05), which checks the version they reviewed (BR-06).
   - The source (type, name, author, URL) always comes from the original recipe, not the reply (BR-92).
   - Notes become: the reply's notes, then "Changes:" with the Changes section, then the line
     "Adapted with AI assistance on <date>."
   - A variant whose title equals the original's gets " (AI variant)" added.
   - The recipe page shows "Variant of …" and lists a recipe's variants.
6. **Schema (FR-EXP-06).** `/docs/recipe-md.schema.json` is a static file (JSON Schema 2020-12) describing the front
   matter. Core has a matching validator, which import and the tests use, so export output is checked against the
   same rules (AC-25).

## Consequences
- FR-EXP-08 stays open (deferred).
- 7.2 gains `/recipes/export-ai` and `/docs/recipe-md.schema.json`.
- If the owner wants prompt history, it can be added later without changing the export page's behaviour.
