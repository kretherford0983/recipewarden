# ADR 0049: Ounces on liquids

- Status: Accepted (owner, 2026-11-23: chose option 1).
- Requirements: BR-115 (new); BR-20, BR-22, BR-23, BR-111; FR-SCL-01, FR-MEAS-01, UX-06

## Context
US recipes write "24 oz. chicken broth" or "14 oz coconut milk", meaning fluid ounces (the can or carton size). The
unit catalog reads a bare "oz" as weight, so the owner's dev build showed "1½ lb chicken broth" in US units and grams
in metric. Cooks measure broth by volume; a pound of broth is wrong for them. The same "oz" on honey, cheese or
canned tomatoes really is weight, and an unmatched line gives no way to tell.

Options put to the owner on 2026-11-23:
1. Read a bare "oz" as fluid ounces on water-like liquids only, decided by the linked food.
2. Ask on each import or edit (a review warning on every "oz" liquid line).
3. Leave it as weight and let the user write "fl oz".

The owner chose option 1.

## Decision
1. **BR-115:** a bare "oz" counts as fluid ounces when the linked food has Form = Liquid and a density from 0.85 to
   1.15 g/ml (broth, stock, milk, cream, wine, juice, coconut milk). Thick liquids (honey, syrups, molasses), other
   forms (fats, solids, canned tomatoes), foods without a density and unmatched lines keep weight.
2. Within that window the difference between a fluid ounce and an ounce of weight is under 15%, so a line written
   either way stays close; outside it (honey at 1.43 g/ml) the two differ too much to swap.
3. The rule is one Core function, `OunceRules.Effective`, used wherever an amount is converted, scaled or summed:
   `IngredientScaler` (US, Metric and Weight views, scaled amounts, "plus" amounts, the shown volume for BR-113) and
   `ContainerRules.Estimate` (BR-111).
4. **As written stays as written:** at 1x with Units = As written, the line is shown exactly as typed ("24 oz
   chicken broth"). BR-22's automatic weight is decided on the written unit, so a scale in My Kitchen does not turn
   a written ounce line into grams.
5. Stored data does not change: the parsed unit stays "oz". Linking the food (or changing its form or density)
   changes how the line converts, with no migration.

## Consequences
- "24 oz. chicken broth" reads "24 fl oz" in US units, "710 ml" in metric, "48 fl oz" at 2x, and "747 g" with
  "24 fl oz" second in the Weight view.
- A liquid sold by weight (some sauces) converts as volume; within the density window the error is small, and the
  editor can write "lb" or a gram amount to be exact.
- Tests: `Core.Tests/BR_115_OuncesOnLiquidsTests` and the updated `Web.Tests/UX_06_ScaleBoxAndConvertedLinesTests`
  (its US expectation was "1½ lb chicken broth", the behaviour this ADR replaces).
