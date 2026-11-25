# ADR 0057: Staples — "Check before you buy" and restock

- Status: Accepted for D1 to D5 (owner, 2026-11-24); D6 and D7 proposed (for owner review).
- Requirements: FR-SHOP-03 (changed), new FR-SHOP-05; BR-43 to BR-48; AC-51 (revised), AC-52 to AC-54, AC-125.
  Amends ADR 0028 and ADR 0030.

## Context
Staples (Food.IsPantryStaple) went to an informational "You probably have" section. The owner wanted to confirm each
one before buying (a roux needs butter and flour, usually on hand) and to restock staples no recipe needs.

## Decision
- D1: one household staples list; any Editor changes it on /manage/staples; changes are audited (AC-74).
- D2: non-food staples (foil, dish soap) are allowed, with a store section of their own.
- D3: staples have a kitchen location (Fridge, Freezer, Pantry, Spice rack, Under the sink, Other, in that order);
  the walk-through goes by location, the list by store section. The location list is fixed in Core for now.
- D4: staples are checked on the list, not when a recipe is added.
- D5: one walk-through, /shopping/{id}/staples (Editor): recipe staples first (unticked = Have it), then every other
  staple by location with "last bought …"; ticked ones go on the list. "Start with a staples check" on a new list
  opens it.
- Storage: Food.IsPantryStaple stays the flag for catalogue foods (it is also set on /manage/conversions and by the
  shopping seed); a `Staple` row holds the details and is made on first edit. Rows without a food are household items.
  This keeps one flag per food rather than migrating it into a second table.
- **D6 (proposed): Have it / Need it work without signing in,** like ticks (ADR 0028: antiforgery, the shopping
  rate limit, only keys of staples on that list). The staples page and the walk-through need an Editor.
- **D7 (proposed): open lists keep their routing (BR-48).** A line records whether its food was a staple when added,
  so changing the staples list never moves items already on a list.
- The section heading is now "Check before you buy"; unanswered staples never stop a list from finishing (BR-46).

## Consequences
New tables Staples and ShoppingStapleChecks; ShoppingLines gains IsStaple, IsRestock and Section. Existing lines are
marked IsStaple from today's flags by the migration, so open lists look the same after upgrading.

## Amendment 1 (2026-11-25): editable kitchen locations (BR-49)
The fixed list becomes the starting list, stored in the setting `Shopping.StapleLocations` once edited. Editors add,
rename, reorder (by number) and remove locations on /manage/staples. Renaming moves the staples with it; removing
moves them to "Other", which is always last and fixed. At most 20 locations of up to 24 characters.
