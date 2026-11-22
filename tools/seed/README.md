# Measurement seed (ADR 0013)

`build-seed.py` turns USDA public-domain files into
`src/RecipeWarden.Infrastructure/Data/Seed/measurement-v1.json`, which the app applies once at startup.
`foods.py` is the curated list: our own names and aliases, and the USDA rows each food takes its numbers from.

## Sources and licences

| Data | Source | Licence |
| --- | --- | --- |
| Densities (household portion weights, e.g. 1 cup flour = 125 g) and FDC ids | USDA FoodData Central, SR Legacy CSV (2018-04): `food.csv`, `food_portion.csv` — <https://fdc.nal.usda.gov/download-datasets> | CC0 1.0 (public domain). FDC asks users to cite it as the source. |
| Cooking yields (cooked weight as % of raw, min–max) | USDA Table of Cooking Yields for Meat and Poultry, Release 2 (xlsx) — USDA ARS, Methods and Application of Food Composition Laboratory | US government work (public domain) |

Publisher weight charts are not used: they are copyrighted. The raw USDA files are not committed; the seed file
records their SHA-256 so a rebuild can be checked.

## Rebuild

```bash
python3 -I tools/seed/build-seed.py <SR Legacy CSV folder> <USDA_CookingYields_MeatPoultry02.xlsx> /tmp/measurement.json
```

Needs Python 3 and `openpyxl`. The output is deterministic. `measurement-v1.json` is applied once per database (a
`SeedRun` row records it), so **do not edit it after release**: a changed seed is a new file (`measurement-v2.json`)
with a new run name in `MeasurementSeeder`, which adds only what is missing.
