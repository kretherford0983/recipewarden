# ADR 0045: How accessibility (AC-96) and performance (AC-95) are checked

- Status: Proposed — **owner review requested** (2026-11-23).
- Requirements: NFR-01, NFR-03, AC-61, AC-95, AC-96

## Accessibility
1. **No axe-core.** It is MPL-2.0 (file-level copyleft); the working agreement allows permissive licences only.
2. Instead `tests/RecipeWarden.E2E.Tests/a11y-check.js` runs in the page and checks the rules axe rates **serious
   or critical**: colour contrast (4.5:1, 3:1 for large text, against the real background, both themes), image alt,
   form labels, button and link names, ARIA references and duplicate ids, valid roles, list structure, nested
   controls, page language and title, zoom not disabled; plus one `main` landmark and an `h1`.
3. It runs on 23 pages (reader, editor, admin, account) in light and dark. A self-test plants one problem of each kind
   and requires the checker to find them all, so a silent checker cannot pass. A second test tabs through the home
   page and requires a visible focus ring on every stop.
4. The theme's first paint (AC-61) is checked with JavaScript turned off on the return visit.
5. Screen-reader testing by a person is still recommended before relying on it for a household member who needs it.

## Performance
1. `AC_95_PerformanceTests` (Web.Tests) clones one recipe saved through the real pipeline into 5,000 published
   recipes with varied titles, tags, ratings and cook dates, then times 60 page requests (home, list pages, recipe
   pages) and 60 searches in process after a warm-up. Budgets: p95 under 300 ms for pages and 200 ms for search.
2. It runs alone after the parallel tests (`DisableParallelization`), so other classes do not skew timings.
3. It found one problem, fixed: search sent every matching id back to SQLite as a list (`IN` over JSON), which cost
   ~200 ms when a word matched all 5,000 recipes. Matching is now a subquery on the FTS table and ranking is worked
   out only when sorting by relevance. On a 2-vCPU container (the spec's reference host): pages p95 ≈ 40 ms,
   search p95 ≈ 30 ms (was 226 ms).
4. Timings are in process (no network or TLS), so they measure the app, which is what NFR-01 is about.
