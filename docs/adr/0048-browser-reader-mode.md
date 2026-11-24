# ADR 0048: Browser reader mode on the recipe page

- Status: Accepted (owner request, 2026-11-23).
- Requirements: UX-14 (new); UX-11, UX-13; section 7.3

## Context
The owner reads recipes from other sites in the browser's reader mode (Safari Reader, Chrome Reading mode, Firefox
Reader View) and wants the same for RecipeWarden. Those modes decide on their own whether a page "is an article" and
which part of it to show: they score blocks of paragraph text and drop forms, buttons, asides and anything whose
class looks like a header, menu or sidebar. On our recipe page the tools (scale box, export menu, shopping form) sat
in the middle of the recipe, step text was not in paragraphs, and the description and notes were in different blocks
from the ingredients and steps. Mozilla Readability (Firefox Reader View) offered the page but kept only the two lists.

## Decision
1. The recipe page is one `<article>` marked as a schema.org `Recipe` (microdata: name, image, description, yield,
   prep/cook/total time as ISO 8601 durations, keywords, author, each `recipeIngredient` and `recipeInstructions`).
   Microdata, not JSON-LD, so the page keeps no inline script (SEC-23).
2. Every tool on the page is inside an `<aside>`: editor buttons, actions and the scale box, the shopping list form
   and suggestions, add to collection. Reader modes leave asides out; nothing changes on screen.
3. The description, the lists and the notes share one block (`#recipe-body`, which htmx already updates), so a
   reader takes them together. Step text is a paragraph. Class names avoid words reader modes treat as page chrome
   (the header block is `recipe-head`).
4. Checked in the browser tests with Mozilla Readability 0.6.0 (Apache-2.0, test-only, vendored under
   `tests/RecipeWarden.E2E.Tests/vendor/readability`): the page must be "probably readerable" and the extracted
   text must hold the description, every list, a group heading and the notes, and none of the tools, both for a
   reader and for an editor.

## Consequences
- Safari's and Chrome's rules are not published; they follow the same signals (one article, paragraph text, no
  controls), so this is checked with Firefox's library and on the owner's devices. Cook mode and print are not in
  scope (they already are their own simplified views).
- New tools on the recipe page go inside an aside (docs/ui-standards.md).

## Amendment 1 (2026-11-23): what Safari still wanted
On the dev build, Safari (Mac and iPhone) and Chrome did not offer reader mode. Compared with an Allrecipes recipe
page that Safari Reader accepts (saved by the owner): there, every ingredient line is a `<p>` inside its list item,
the head declares `og:type=article` with a description, and the page carries JSON-LD Recipe data. It also has about
3,300 characters of paragraph text against 350 to 750 on ours. Changes, in two pull requests so each can be tested:
1. (This one) Each ingredient line is a `<p class="ingredient-line">`; recipe pages have `meta name="description"`
   (plain text, `CardSummary`), `og:type=article`, `og:title`, `og:description` and `og:site_name`. No visible
   change.
2. (Next, ADR 0050) JSON-LD Recipe data.
Short recipes may still have too little text for Safari; a built-in reading view stays the fallback.
