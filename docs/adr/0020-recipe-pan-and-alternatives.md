# ADR 0020: Recipes have a pan field; scaled recipes suggest pans by area

- Status: Accepted (2026-11-22, owner)
- Requirements: FR-MEAS-07 (pan part), BR-29; ADR 0012

## Context
FR-MEAS-07 wants pan-size alternatives when a recipe is scaled, based on area. Recipes store no pan today.

## Decision
1. `Recipe` gets an optional pan: shape (Rectangle, Square, Round, Loaf, Other) and dimensions in inches, entered as
   text in the editor ("9x13", "9-inch round", "8x8 square"). The field is prefilled from the steps when they name
   a pan ("9x13-inch pan"), and the editor can change it.
2. When the recipe is scaled, the reading view suggests pans whose area is within ±15% of the scaled area: first
   the pans in My Kitchen, then a built-in standard list. Combinations of the same pan are allowed ("2 × 9x13").
   BR-29's note says that bake times change with pan changes.
3. Depth is not modelled; the note says to check doneness.

## Consequences
- Pan parsing and area matching are Core rules with tests.
