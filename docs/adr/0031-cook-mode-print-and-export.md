# ADR 0031: Cook mode keeps ticks on the device; print and export follow the view; bulk export is open

- Status: Accepted (2026-11-23, owner)
- Requirements: FR-VIEW-02, FR-EXP-01, FR-EXP-02, FR-EXP-03, FR-SCL-03, FR-AUTH-01, AC-23, AC-24, AC-25, BR-114

## Decision
1. **Cook mode** (`/recipes/{slug}/cook`, anonymous):
   - Ticks for ingredients and steps are kept in the browser (localStorage, per recipe), with a Clear button.
     Nothing is written to the server, so the page stays read-only (SEC-31).
   - The screen is kept awake with the Wake Lock API where the browser supports it.
   - Timers are detected in step text by `TimerRules` (Core), e.g. "bake 25 minutes", "simmer 1 to 1½ hours"
     (a range starts at the low end).
2. **Print** (`/recipes/{slug}/print`) and **export** take the same query as the recipe page:
   - The query covers scale, units, weights, and container fit. Container scaling prints and exports like any
     scale (BR-114).
   - Export at a scale other than 1x writes the scaled ingredient lines and `scale:` in the front matter.
3. **Markdown export:** `export.md`, or `export.zip` with the .md and images: `images/finish.webp` and
   `images/step-NN.webp`, linked relatively.
4. **Bulk export** (`/recipes/export.zip`, `/collections/{slug}/export.zip`) is open to everyone (FR-AUTH-01).
   - Readers get published recipes only; editors also get drafts.
   - It shares the search rate limit (60 a minute per IP).

## Consequences
Section 7.2 gains `/recipes/{slug}/cook` (already listed in spirit) and the bulk export routes.
