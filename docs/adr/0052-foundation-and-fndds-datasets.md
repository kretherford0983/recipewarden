# ADR 0052: Ship Foundation Foods and FNDDS with SR Legacy

- Status: Accepted (owner, 2026-11-23: D2). Amends ADR 0034 point 1.
- Requirements: D-06, FR-NUT-03 to 07, BR-54, BR-116

## Context
ADR 0034 shipped SR Legacy only. Foundation Foods was deferred because its download was blocked from the build
machine. ingredient-parser matches against SR Legacy, Foundation and FNDDS together, and many of its best
matches are Foundation or FNDDS ids. In testing, restricting it to SR Legacy gave clearly wrong foods (diced
tomatoes became green tomatoes, whole milk became dry milk).

## Decision
- The bundled data adds **Foundation Foods** and **FNDDS** (both CC0) next to SR Legacy.
- `NutrientDatasetImporter` keeps `survey_fndds_food` rows as well as `sr_legacy_food` and `foundation_food`.
  Rows are keyed by FDC id, so existing mappings are unaffected.
- The search UI shows each food's dataset so Editors can tell, for example, an SR Legacy "Milk, whole" from an
  FNDDS one.
- Suggestions are shown only for ids present locally (BR-116).

## Consequences
The seed grows, and manual search finds more foods. AC-58 (refresh and re-mapping) is extended to cover the new
data types.
