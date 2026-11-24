# ADR 0055: Kitchen tool fields and per-ingredient weights

- Status: Proposed (decided while building, 2026-11-24, from the owner's testing; for owner review).
- Requirements: FR-TOOL-01, FR-TOOL-05, BR-22; new BR-120 to BR-123, AC-113 to AC-116. Amends ADR 0017.

## Context
Testing a real scale ("Max 14000g x 1g : 30lb x 0.1oz") showed three problems on /manage/kitchen: every type's
fields were shown together, the resolution field gave no clue what to enter, and the units were unexplained. The scale
banner then offered four ingredients by weight in an ordinary recipe, all or nothing; the owner wanted only freshly
ground pepper by weight.

## Decision
- **BR-120.** `KitchenRules.FieldsFor(type)` names each type's fields (size, scale units, fill %) and the size field's
  label and hint. The page shows only those (kitchen.js swaps them; the server renders the posted type's fields without
  script). On save `OnlyItsFields` **clears** anything that does not belong to the type, so a stale or hand-built form
  cannot store a fill % on a spoon. The edit form never changes a tool's type, so "clear or refuse on type change" does
  not arise; if it is ever allowed, the same clearing applies.
- **BR-121.** "Resolution (smallest step)", with the hint "Max … x [step]". A bare number is grams ("1" → 1 g); a step
  over 50 g (about 1.75 oz) is refused. Units stay g and oz: the scale's ml and fl oz modes assume water, and lb is shown
  from oz.
- **BR-122.** Each ingredient line has a measure: Auto (BR-22 as before), Weight or As written. The scale banner has a
  tick per line: Save to recipe weighs the ticked lines and stores As written on the unticked ones, so a later
  inventory change does not offer them again; Show me weighs only the ticked lines for that view (`w=` or
  `weights=0,3`, no write). The editor has the same choice per line. A choice is kept with its line's text across
  saves; editing the text resets it to Auto.
- **BR-123.** A weight under five steps of the scale shows "Small amount for this scale: weigh onto a zeroed plate, or
  use ½ tsp". It never blocks the choice.

## Consequences
One new column (RecipeIngredient.Measure, text, default Auto). recipe-md export does not carry the choice yet (a
re-import starts at Auto). Readers without JavaScript who press Show me see every offered line weighed.
