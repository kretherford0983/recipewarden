#!/usr/bin/env python3
"""ADR 0034: trims a USDA FoodData Central CSV download to what RecipeWarden reads.

Usage: python3 -I tools/seed/build-nutrients.py <FDC CSV folder> <out.zip>

Keeps sr_legacy_food and foundation_food rows, the panel nutrients (energy, protein, fat, carbohydrate, fibre, sugars,
sodium, cholesterol, saturated fat) and household portions. The output keeps the FDC CSV layout, so the app's
NutrientDatasetImporter reads it exactly like a full download. Deterministic: same input, same bytes.
"""
import csv
import hashlib
import io
import sys
import zipfile

NUTRIENTS = {"1003", "1004", "1005", "1008", "1079", "1093", "1253", "1258", "2000", "2047", "2048"}
TYPES = {"sr_legacy_food", "foundation_food"}


def read(folder, name):
    with open(f"{folder}/{name}", newline="", encoding="utf-8") as f:
        return list(csv.DictReader(f))


def write(rows, columns):
    out = io.StringIO()
    w = csv.DictWriter(out, fieldnames=columns, quoting=csv.QUOTE_ALL, lineterminator="\n", extrasaction="ignore")
    w.writeheader()
    for r in rows:
        w.writerow(r)
    return out.getvalue().encode("utf-8")


def main(folder, target):
    foods = [r for r in read(folder, "food.csv") if r["data_type"] in TYPES]
    ids = {r["fdc_id"] for r in foods}
    files = {
        "food.csv": write(sorted(foods, key=lambda r: int(r["fdc_id"])),
                          ["fdc_id", "data_type", "description", "food_category_id", "publication_date"]),
        "food_category.csv": write(read(folder, "food_category.csv"), ["id", "code", "description"]),
        "nutrient.csv": write([r for r in read(folder, "nutrient.csv") if r["id"] in NUTRIENTS],
                              ["id", "name", "unit_name", "nutrient_nbr", "rank"]),
        "food_nutrient.csv": write(sorted((r for r in read(folder, "food_nutrient.csv")
                                           if r["fdc_id"] in ids and r["nutrient_id"] in NUTRIENTS),
                                          key=lambda r: (int(r["fdc_id"]), int(r["nutrient_id"]))),
                                   ["id", "fdc_id", "nutrient_id", "amount"]),
        "food_portion.csv": write(sorted((r for r in read(folder, "food_portion.csv") if r["fdc_id"] in ids),
                                         key=lambda r: (int(r["fdc_id"]), int(r["seq_num"] or 0), int(r["id"]))),
                                  ["id", "fdc_id", "seq_num", "amount", "measure_unit_id", "portion_description",
                                   "modifier", "gram_weight"]),
        "measure_unit.csv": write(read(folder, "measure_unit.csv"), ["id", "name"]),
    }
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for name in sorted(files):
            info = zipfile.ZipInfo(name, date_time=(2018, 4, 1, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, files[name])
    for name in ("food.csv", "food_nutrient.csv", "food_portion.csv", "nutrient.csv"):
        with open(f"{folder}/{name}", "rb") as f:
            print(f"{name}: {hashlib.sha256(f.read()).hexdigest()}")
    print(f"foods: {len(foods)}")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
