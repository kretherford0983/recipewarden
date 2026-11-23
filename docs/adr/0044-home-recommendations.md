# ADR 0044: Home page recommendations (FR-HOME-02)

- Status: Proposed — **owner review requested** (2026-11-23).
- Requirements: FR-HOME-01, FR-HOME-02, AC-60, UX-09

## Context
FR-HOME-02 lists four rule-based ideas: season tags for the current month, highly rated but not cooked for 60+ days,
recently added and favorites. The spec has no per-user favorites; ratings are per recipe for the household.

## Decisions
1. **Rows**, above "Recently added", in this order: **In season**, **Favorites**, **Not cooked lately**. "Recently
   added" is the existing FR-HOME-01 row (8 recipes).
2. **In season**: a published recipe with a tag (untyped, or of type Season) named for the current season:
   Spring (Mar–May), Summer (Jun–Aug), Fall or Autumn (Sep–Nov), Winter (Dec–Feb), plus **Holiday** in November
   and December. `Home:Hemisphere` = `North` (default) or `South` flips the seasons; it is a config setting only.
3. **Favorites** = rated 5. **Not cooked lately** = rated 4 or 5 and not cooked for 60 days (or never), the longest
   ago first.
4. A recipe appears in one row only (the first that fits). Each row shows at most 4 and all rows together at most 8
   (AC-60: "up to 8 recommended"). Ties rotate once a day in a fixed order, so the page does not change on every
   reload and tests need no randomness.
5. Empty rows are not rendered (UX-09). Anonymous readers see the same rows (published recipes only).

## Alternatives not chosen
- Per-user favorites (a heart button): needs a new table and UI; can be added later without changing these rules.
