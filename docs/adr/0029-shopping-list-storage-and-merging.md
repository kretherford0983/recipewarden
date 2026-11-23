# ADR 0029: Lists store each recipe's lines; merging happens in Core when the list is shown

- Status: Accepted (2026-11-23, owner)
- Requirements: FR-SHOP-01, FR-SHOP-02, BR-40, BR-41, BR-42, AC-35, AC-50

## Context
Section 8.5 sketches ShoppingListItem plus ShoppingListItemSource. Merging at add time makes removing or rescaling a
recipe later only approximate.

## Decision
1. Adding a recipe stores one **ShoppingListRecipe** row (recipe, scale, title at the time) and one **ShoppingLine**
   per ingredient. Each line holds the scaled amount in base units (ml, g or count; the purchase state when a yield
   conversion applies, AC-35), the food it matched, its name and its original text. Manual items are lines without
   a recipe. Later edits to the recipe do not change a list already made.
2. `ShoppingRules.Merge` (Core) groups lines by food (or normalized name when no food matched) and kind:
   - Volume and mass merge when a density is known (BR-40).
   - Counts merge with counts.
   - Anything else stays a separate line under the same item.
   - Amounts are shown with `MeasureDisplay`. A known package size adds the package count: whole and half packages
     as is ("2½ sticks"); otherwise rounded up ("buy 2 cans") (BR-41).
   - Each merged item lists its recipes (BR-42).
3. Ticks are stored per merged item key (**ShoppingCheck**: list, key), so a tick survives adding another recipe
   that shares the item. A tick on an item that no longer exists is ignored.
4. Pantry staples (Food.IsPantryStaple) are listed under "You probably have" (FR-SHOP-03, AC-51). Vague amounts
   ("pinch", "to taste") show the item without an amount.

## Consequences
Section 8.5 is updated: ShoppingList (+ FinishedAt), ShoppingListRecipe, ShoppingLine, ShoppingCheck, ShoppingListName.
