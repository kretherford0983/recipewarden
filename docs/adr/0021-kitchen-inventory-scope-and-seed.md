# ADR 0021: Kitchen inventory starts with the standard set; appliances are inventory only

- Status: Accepted (2026-11-22, owner)
- Requirements: FR-TOOL-01 to FR-TOOL-05, BR-30, BR-33, W5

## Context
FR-TOOL-04's example ("Instant Pot added — pressure-cook variation available") needs pressure-cook versions of
recipes, which do not exist. A new inventory could start empty (BR-33 assumes a standard set) or seeded.

## Decision
1. **Seeded standard set:** every install starts with BR-33's set: ¼, ½ and 1 tsp; 1 Tbsp; ¼, ⅓, ½ and 1 cup. It is
   seeded once per database (`SeedRun` "kitchen-v1", as in ADR 0013), so tools an editor retires or deletes do not
   come back. Upgraded installs get it once too.
2. The seed is inventory version 1. Its snapshot plans exactly like BR-33's assumed set, so the seed alone creates
   no suggestions.
3. /manage/kitchen also offers one-click presets: digital scale (1 g / 0.05 oz), ⅛ tsp spoon, common containers
   (16 oz squeeze bottle, pint and quart jars, freezer tubs) and common pans (9x13, 8x8, 9-inch round, loaf, half
   sheet).
4. **Appliances and thermometers** are stored in My Kitchen, but they create no suggestions in M3. Appliance-based
   ideas wait for Export for AI's "adapt to my tools" (M5, FR-EXP-04).

## Consequences
- With every standard tool retired, the planner falls back to BR-33 and says so.
