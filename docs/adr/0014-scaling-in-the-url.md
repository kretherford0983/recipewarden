# ADR 0014: Scale and units travel in the URL and render on the server

- Status: Accepted (2026-11-21, owner)
- Requirements: FR-SCL-01, FR-SCL-02, FR-SCL-03, FR-UI-02, section 7.2 (print accepts scale and unit), 7.3 item 3

## Context
The recipe toolbar needs scale presets, a custom factor, target servings, scale-from-ingredient and a unit choice.
Print (M5) must accept the same values.

## Decision
1. The view state is query-string only: `/recipes/{slug}?scale=1.5&units=us|metric|weight`, or `servings=6`
   (target servings) instead of `scale`. Values are parsed and clamped in Core (factor 0.1–100, FR-SCL-01);
   invalid values fall back to 1x with a message, never an error page.
2. The toolbar is a plain GET form. With JS, htmx swaps the ingredient panel and pushes the URL; without JS the
   page reloads. All math runs in the Core measurement engine on the server; no client-side copy.
3. FR-UI-02 defaults (unit system, default scale) come from the reader's preference, stored like the theme
   (cookie, plus profile for signed-in users); the query string overrides them.
4. Scaling is read-only (FR-SCL-03): GET only, no writes, available anonymously like the recipe page.

## Consequences
- Links to a scaled view can be shared and bookmarked, and print can reuse them in M5.
