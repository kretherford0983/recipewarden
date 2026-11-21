# ADR 0010: Servings and times accept ranges; times accept hours

- Status: Accepted (2026-11-21, owner)
- Requirements: FR-REC-01, BR-03, FR-SRCH-02, FR-SCL-01 (M2), section 8.2

## Context
Found on the first deployed M1 build: many recipes give servings as a range ("serves 6-8") and times as a range
or in hours ("20-25 minutes", "1 hr 15 min"). BR-03 and 8.2 only allowed one number, so such recipes could not be
entered. Ingredient amounts already support ranges (FR-REC-03).

## Decision
1. **Servings** store a low and an optional high value (`Servings`, `ServingsMax`). The editor accepts `6`,
   `6-8`, `6–8`, `6 to 8`, optionally with words such as "serves" or "servings". Shown as "6–8". BR-03 applies to
   both ends; the high end must be above the low end. In M2, scaling multiplies both ends (×2 gives 12–16) and
   "target servings" counts from the low end.
2. **Times** (prep, cook, total) store minutes as a low and an optional high value (`PrepMinutes`/`PrepMinutesMax`
   and so on). The editor accepts minutes (`20`), ranges (`20-25`, `20 to 25 min`), hours and minutes
   (`1 hr 15 min`, `1h15m`, `1:15`) and hour ranges (`1-1.5 hours`). Shown as "20–25 min", "1 hr 15 min".
3. **"Ready in" filter (FR-SRCH-02)** uses the high end, so "30 min or less" never lists a recipe that may take
   35. When total is empty, prep + cook are added end by end.
4. Parsing and formatting live in Core (`ServingsRules`, `TimeText`) with unit tests; the editor shows the same
   text it accepts, so a saved recipe opens in the editor as it was typed in normalized form.

## Consequences
- Migration `M1_Ranges` adds four nullable columns; existing recipes are unchanged (single values).
- recipe-md v1 (9.7) gains `servings: 6-8` and range times when export is built in M5.
