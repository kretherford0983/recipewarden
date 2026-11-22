# ADR 0018: Display planner uses spoons, cups and scales; weight by food form

- Status: Accepted (2026-11-22, owner)
- Requirements: FR-MEAS-02, FR-REC-06, BR-21, BR-22, BR-24, BR-33, AC-32, AC-33; ADR 0012

## Context
The planner (9.4) combines active tools to show makeable amounts. My Kitchen also holds liquid measures. BR-22
prefers weight for "dry solids", but no data says which foods are dry solids.

## Decision
1. **Tools:** the planner combines active measuring spoons and measuring cups (each size), with at most 3 fills
   (BR-21), searching for the fewest fills first and then the smallest error within tolerance. If nothing fits, it
   falls back to the M2 nearest-fraction display (ADR 0012). **Liquid measures (jugs) are inventory only**; the
   planner does not use them.
2. **Where it applies:** US volume amounts in the "As written" and "US" views. The metric view keeps whole ml and
   grams (BR-28); the Weight view keeps grams.
3. **No tools:** with no active spoons or cups, the planner uses BR-33's standard US set and says so.
4. **Weight (BR-22):** a new `Food.Form` field (Dry, Liquid, Fat, Other). The seed sets it from the category, and
   editors can change it on /manage/conversions. With an active scale and a known density, weight is shown first
   when the recipe is baking/precision or the food is Dry. Grams are used when the scale supports g, else ounces;
   the volume shows as a secondary line. The scale's resolution rounds the weight (113 g on a 1 g scale).
5. Every plan carries the original amount, the error % and the reason (9.4 "Explain"), shown as the tooltip (UX-06).

## Consequences
- AC-32 and AC-33 are tested against the Core planner and through the page.
- A household that weighs liquids can still choose the Weight view.
