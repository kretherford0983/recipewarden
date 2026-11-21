# ADR 0012: M2 scope — display planner, container scaling and pan sizes move to M3

- Status: Accepted (2026-11-21, owner)
- Requirements: FR-MEAS-02, FR-REC-06, FR-SCL-04/05/06, FR-MEAS-07, BR-21, BR-22, BR-24, BR-28, BR-33,
  BR-110–114, AC-32, AC-33, AC-37–39; section 9.10

## Context
Section 9.10 puts the display planner in M2 (Measurement), but the planner rounds to "measures makeable with the
tools in the kitchen inventory" (FR-MEAS-02), and the inventory (`KitchenTool`, 8.4) is M3. Container scaling
(FR-SCL-04 to 06) and pan-size alternatives (FR-MEAS-07, second half) also need containers and pans from the
inventory. Options considered for the planner: (a) BR-33's standard US set plus a temporary "kitchen scale" setting,
(b) a minimal `KitchenTool` table early, (c) always assume a scale, (d) defer the planner to M3.

## Decision
1. **The display planner moves to M3** with the kitchen inventory (option d): tool combinations ("1 cup + 2 Tbsp"),
   the 3-fill cap and tolerances (BR-21), weight preference when a scale exists (BR-22), the standard-set fallback
   (BR-33) and the baking/precision behaviour of FR-REC-06. AC-32 and AC-33 move to M3.
2. **M2 shows scaled amounts as plain amounts**: the nearest allowed fraction (BR-28: denominators 2, 3, 4, 8, and
   16 for tsp; metric decimals) in the recipe's own unit, or in the unit system the reader picks. Simple unit
   escalation (BR-24, e.g. 16 Tbsp → 1 cup) applies, since it does not depend on owned tools. This is the planner's
   documented fallback ("nearest fraction", 9.4), so M3 adds to it rather than replacing it.
3. Volume↔weight conversion with densities (FR-MEAS-01, BR-23) ships in M2 when the reader picks weight or metric
   units; it is not chosen automatically until the M3 scale rule (BR-22).
4. **Container scaling** (FR-SCL-04/05/06, BR-110–114, AC-37–39) moves to M3.
5. **Pan-size alternatives** (FR-MEAS-07, second half) move to M3. °F/°C display stays in M2.

## Consequences
- M2 ACs: AC-30, AC-31, AC-34, AC-35 (the shopping-list half needs M5), AC-36.
- Spec 9.10 is updated: M3 becomes "Kitchen — inventory, versioning, suggestions, display planner, container
  scaling, pan sizes".
- `Recipe.IsPrecision` keeps its checkbox; it has no effect until the planner arrives.
