# ADR 0008: Recipe slugs follow the title; old slugs redirect

- Status: Accepted (2026-11-20)
- Requirements: BR-02, FR-REC-01, FR-REC-02

## Context
BR-02 says slugs "never change once published (old slugs redirect if a title changes)". Read literally the two
halves contradict each other: a slug that never changes needs no redirect. Options considered:
(a) freeze the slug at first publish; (b) let the slug follow the title and keep every earlier published slug as
a permanent redirect.

## Decision
Option (b). The guarantee BR-02 protects is that a published URL keeps working.
- While a recipe is a Draft its slug follows the title and nothing is kept (no URL was ever public).
- When a published recipe's title changes, a new slug is generated from it; the previous slug is stored in
  `RecipeSlugAlias` and `/recipes/{old}` (and its sub-routes) answers `301` to the current slug.
- A slug, current or alias, is unique across `Recipe.Slug` and `RecipeSlugAlias.Slug`; it is never reused while
  the recipe exists (trash included). Purge frees its slugs.
- Slug rules (Core `SlugRules`): lower case, accents folded to ASCII, non-alphanumerics to single hyphens,
  at most 80 characters, `-2`, `-3`… on collision. Tags and collections use the same rules without aliases.

## Consequences
- One extra table and one lookup on a slug miss; links in printed recipes and shared messages never break.
- Changing a title back to an earlier one reuses its alias (the alias row is removed, the slug becomes current).
