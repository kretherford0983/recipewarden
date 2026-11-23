# ADR 0030: Store sections, pantry staples and package sizes are seeded and editable

- Status: Accepted (2026-11-23, owner)
- Requirements: FR-SHOP-03, BR-41, AC-50, AC-51, FR-MEAS-05; section 8.3 Food

## Context
Food already has StoreSection, IsPantryStaple, PackageSize and PackageUnitId (8.3), but the M2 seed left them
empty. Without them every item lands in "Other" and BR-41 cannot count butter sticks.

## Decision
1. A one-time seed `shopping-v1` (a SeedRun, ADR 0013):
   - Sets each seeded food's store section from its category (e.g. Dairy and Eggs, Produce, Meat and Seafood,
     Baking, Spices, Pantry, Frozen, Other).
   - Marks a starter pantry list: salt, black pepper, oils, flour, sugars, baking soda and powder, vanilla, dried
     spices, vinegar, soy sauce.
   - Adds common package sizes: butter ½ cup stick, 15 oz cans of beans, 14.5 oz cans of tomatoes, 12-egg cartons.
   - It never overwrites a value an editor has set.
2. Editors change section, pantry and package size per food on `/manage/conversions` (FR-MEAS-05).
3. Store sections have a fixed walking order for the list page; foods without a section go under "Other".

## Consequences
Households can tune the defaults to their own store. A new food added by an editor starts in "Other".
