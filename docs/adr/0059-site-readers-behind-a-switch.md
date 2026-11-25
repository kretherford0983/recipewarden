# ADR 0059: Site readers in the URL import order, behind an Admin switch (M10)

- Status: Accepted (owner, 2026-11-24)
- Requirements: FR-IMP-12, BR-117 (revised), AC-110 to AC-112, new AC-131. Builds on ADR 0051 (Python adapters) and
  the M9 `scrape-html` command.

## Context
The plan (ADR 0051, D6) was to build M10 only after `sudo recipewarden import-compare` on the test host showed that
recipe-scrapers' site readers complete imports the .NET extractor cannot. The owner chose (2026-11-24) to build it now,
switched off, and turn it on once the numbers show a gain. The spec's earlier wording put the switch in a config value
(`Import:SiteScrapers`); an Admin switch next to the Python worker's own switch is easier to try and undo.

## Decision
- **Order (BR-117).** For a fetched page, or pasted HTML with a page address: JSON-LD, then microdata. When that
  reading is complete (a title, at least one ingredient and one step), it is used. Otherwise, when site readers are on,
  the host is supported and the site reader's reading is complete, the site reader's reading is used. Otherwise the
  .NET reading (incomplete structured data or the heading heuristic) is used, as before. The AI parser's fallback
  (FR-IMP-09) comes after all of this, unchanged.
- **Switch.** Setting `Import.SiteReaders`, shown on **Admin → Health** in the Python section as "Use site readers for
  web imports". Off unless an Admin turns it on. It also needs the Python features to be on (BR-119); when the
  worker is off, unavailable or fails, the import goes on with the .NET reading and one log line (BR-119).
- **Method shown.** The review page says how the recipe was read: "Recipe data (JSON-LD)", "Recipe data
  (microdata)", "Page headings", "Site reader (recipe-scrapers)" and so on.
- **What the site reader gets.** Only HTML the app already holds: the SafeFetcher response or the pasted text (AC-112).
  The worker never fetches (SEC-90). Its reading is mapped in Core: text lines only, capped in count and length, image
  addresses only when absolute http(s); the source credit is the page (BR-10).

## Consequences
- With the switch off, imports behave exactly as before (AC-131).
- Turning it on adds one short worker call (about 1 s) only to imports whose structured data was missing or
  incomplete.
- `import-compare` stays the way to decide; the switch makes the decision a click instead of a release.
