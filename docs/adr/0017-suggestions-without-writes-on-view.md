# ADR 0017: Tool suggestions are worked out on every view; only editors write

- Status: Accepted (2026-11-22, owner)
- Requirements: FR-TOOL-04, FR-TOOL-05, BR-30, BR-31, BR-32, AC-41, AC-42; SEC-10, SEC-31, D-02

## Context
BR-31 says that opening a recipe re-evaluates suggestions and stores the inventory version on the recipe, with no
sign-in needed. Recipes are read anonymously (D-02), so that is a write on an anonymous GET, which SEC-31 ("GET and
HEAD never change state") and SEC-10 (anonymous access to read-only pages only) forbid. AC-41 also needs a dismissal
to be remembered. Options: (a) no writes on view, (b) a documented exception allowing the write and an anonymous
dismiss, (c) suggestions for editors only.

## Decision
1. **No writes on view** (option a). Every inventory version keeps a snapshot of the active tools
   (`InventorySnapshot`). On each view the suggestion engine diffs the display plan made with the snapshot at the
   recipe's `InventoryVersionSeen` against the plan made with the current inventory, in memory. Nothing is stored.
2. A suggestion is shown only when it changes at least one displayed amount, tool or method, and it has a key such
   as `scale:all-purpose flour` or `tool:tsp-1/8` (BR-32).
3. **Editors** act on suggestions with POSTs (antiforgery, write rate limit):
   - "Dismiss" stores `RecipeSuggestionState` (Dismissed, at the current version).
   - "Save to recipe" stores it as Applied and moves `InventoryVersionSeen` to the current version.
   - When every suggestion for the recipe is dismissed or applied, `InventoryVersionSeen` moves to the current version.
4. **Readers** can "Show me", which applies the suggestion to that view only (query string, ADR 0014). They cannot
   dismiss; the banner stays until an editor acts on it.

## Consequences
- BR-31 is met in substance ("re-evaluated on view") without its write. Spec BR-31 is reworded to match.
- Snapshots are small (the active tools as JSON), one per inventory version.

## Amendment (2026-11-22, during M3 build): what becomes an offer
AC-40 says "plans recalculate" when a tool is added, and AC-42 says a new spoon that changes nothing shows no banner.
Holding every spoon or cup change behind an editor's "Save to recipe" would contradict AC-40, so:
- The display planner always uses the **current** active measures (FR-TOOL-03). Adding or retiring a spoon or cup
  changes the plan at once, for everyone, with no banner.
- Only a **new scale** is an offer, because it changes the method (cups → grams), not just the fill. Its key is
  `weight:scale-{toolId}`. Weights are shown by default only when the snapshot at `InventoryVersionSeen` already
  had a scale (`SuggestionEngine.WeightAccepted`), or after an editor applies the offer.
- Readers' "Show me" is `?weights=show`, for that view only. Editors dismiss or apply at
  `/manage/recipes/{id}/suggestions` (POST, antiforgery). Apply moves `InventoryVersionSeen` to the current version.
- New recipes start with `InventoryVersionSeen` = the current version, so a recipe written after the scale was added
  shows weights at once (AC-33).
