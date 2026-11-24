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

## Nutrient data (ADR 0034, M6)

`build-nutrients.py` trims a USDA FoodData Central CSV download (SR Legacy 2018-04; a Foundation Foods download works
the same way) to the foods, panel nutrients and household portions the app reads, keeping the FDC CSV layout:

```bash
python3 -I tools/seed/build-nutrients.py <FDC CSV folder> src/RecipeWarden.Infrastructure/Data/Seed/fdc-sr-legacy-2018-04.zip
```

| Input file (SR Legacy 2018-04) | SHA-256 |
| --- | --- |
| food.csv | de8b0390058db4f102368c9fbdfde144790b480ff4cb0507c0e060ef99805dee |
| food_nutrient.csv | 6d07e6fc062b09750c4d95112be5a5cdb56a2346efcfd0c96b7a7d705812d4bd |
| food_portion.csv | 6332e29da61e13f7bd950b759461af73303c76e8b0e64dc9df4e41d5347cf3d1 |
| nutrient.csv | 466ab9d011094cc1ad8d939b246693988b56817c42d0e40a98820723695c6084 |

Output `fdc-sr-legacy-2018-04.zip` (7,793 foods): SHA-256 `5a8a6253ba77f2ce12369da72dc7c7c5389a12484477023d84722602a7e0c3a3`.
The app applies it once (seed run `nutrients-sr-legacy-2018-04`) with the same importer an Admin uses for a newer
download (`recipewarden nutrition-import <zip>`). USDA FoodData Central is CC0 1.0; FDC asks to be cited as the source.

## Foundation Foods and FNDDS (ADR 0052, M9)

The ingredient matcher behind suggestions (FR-NUT-07) names SR Legacy, Foundation and FNDDS foods, so a second seed
adds the other two. Every one of its 11,105 FDC ids resolves against the three bundled datasets.

```bash
python3 -I tools/seed/build-nutrients.py --date 2026-04-30 \
  <FoodData_Central_foundation_food_csv_2026-04-30 folder> <FoodData_Central_survey_food_csv_2024-10-31 folder> \
  src/RecipeWarden.Infrastructure/Data/Seed/fdc-foundation-2026-04-fndds-2024-10.zip
```

FNDDS differs from the other two in three ways the builder and the importer both handle: `food_nutrient.nutrient_id`
holds the nutrient *number* (208) rather than the id (1008), which `nutrient.csv` maps; a portion's amount is part of
its words ("1/2 cup") and `modifier` holds a food code; and categories come from `wweia_food_category.csv`.
The 2026-10-29 Foundation download has no `food.csv` or `food_nutrient.csv`, so the 2026-04-30 release is used.

| Download | SHA-256 |
| --- | --- |
| FoodData_Central_foundation_food_csv_2026-04-30.zip | 70457ee9d9342f43bda2010318c85f04210c689fdeb9cd2da4c513b0e8dbc655 |
| FoodData_Central_survey_food_csv_2024-10-31.zip | 5ccc25ec2777a8982fbb61378a42f415316173eb11e48c9a8ba4cb19f5a4f29c |

Output `fdc-foundation-2026-04-fndds-2024-10.zip` (469 Foundation and 5,432 FNDDS foods): SHA-256
`3e5f59ff1afcaaf5951f1a7c708865431ad61069871f2b9d0174892b5cb18b09`. Applied once as seed run
`nutrients-foundation-2026-04-fndds-2024-10`, after the SR Legacy run. Rebuilding the SR Legacy seed with the new
builder gives the same bytes as before (the default `--date` is 2018-04-01).
