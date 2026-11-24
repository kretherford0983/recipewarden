# ADR 0050: Recipe JSON-LD

- Status: **Proposed — owner review requested** (built overnight 2026-11-23 at the owner's request to "try the
  markup changes"; merging the pull request is the approval).
- Requirements: UX-14; SEC-22, SEC-23; ADR 0048 amendment 1

## Context
Reader modes still did not appear on the dev build. The Allrecipes recipe page Safari Reader accepts carries its
recipe as schema.org JSON-LD in a `<script type="application/ld+json">` block; RecipeWarden had microdata only.
Pages had no inline `<script>` elements at all, and two tests enforce that (SEC-23, SEC-22).

## Decision
1. Recipe pages carry one JSON-LD block: name, plain-text description, image, author, yield, ISO 8601 prep/cook/total
   time, keywords (tags), every ingredient line and every step (`HowToStep`). Empty values are left out.
2. It is data, never run: the browser does not execute `application/ld+json`, so the CSP (`script-src 'self'`) has
   nothing to block or report (checked in a browser test with the CSP on).
3. Safety: the JSON is written by System.Text.Json with `JavaScriptEncoder.Default`, which writes `< > & ' " +` as
   `\uXXXX`, so recipe text cannot close the element or start markup. `RecipeJsonLd.Html` is the second allowed
   `new HtmlString(...)` (after the Markdown sanitizer), named word for word in the source scan.
4. Test exceptions, kept as narrow as possible (owner review requested):
   - `M8_SecurityPassTests.No_raw_sql_or_raw_html_anywhere_in_the_source`: one more exact allowed line.
   - `FR_REC_02_RecipeEditorTests.Editor_and_reading_pages_have_no_inline_script_or_handlers`: a
     `<script type="application/ld+json">` block whose contents contain no `<` at all is set aside before the
     unchanged check; any other inline script still fails.
   The page-level `SEC_62` home-page check is unchanged (no JSON-LD there).
5. The script tag helper is kept off this element (`<!script>`), so the type is written as `application/ld+json`
   (the helper would encode the `+`).

## Consequences
- Search engines and link previews can read the recipe; reader modes get the same signal as on recipe sites.
- If the owner rejects the exception, close the pull request; part A stands on its own.
