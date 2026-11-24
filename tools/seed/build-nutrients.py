#!/usr/bin/env python3
"""ADR 0034: trims a USDA FoodData Central CSV download to what RecipeWarden reads.

Usage: python3 -I tools/seed/build-nutrients.py [--date YYYY-MM-DD] <FDC CSV folder> [<folder> ...] <out.zip>

Several folders are merged (ADR 0052: Foundation Foods and FNDDS in one seed). --date sets the zip entries' time
(default 2018-04-01, the SR Legacy seed's). Keeps sr_legacy_food, foundation_food and survey_fndds_food rows, the panel nutrients (energy, protein, fat, carbohydrate, fibre, sugars,
sodium, cholesterol, saturated fat) and household portions. The output keeps the FDC CSV layout, so the app's
NutrientDatasetImporter reads it exactly like a full download. Deterministic: same input, same bytes.
"""
import csv
import hashlib
import io
import sys
import zipfile

NUTRIENTS = {"1003", "1004", "1005", "1008", "1079", "1093", "1253", "1258", "2000", "2047", "2048"}
TYPES = {"sr_legacy_food", "foundation_food", "survey_fndds_food"}


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


def read_all(folders, name):
    rows = []
    for folder in folders:
        try:
            rows.extend(read(folder, name))
        except FileNotFoundError:
            pass
    return rows


def food_nutrients(folders):
    """food_nutrient rows with nutrient ids. FNDDS writes the nutrient number (208) where SR Legacy and Foundation
    write the id (1008); nutrient.csv maps one to the other, and the output always holds ids."""
    for folder in folders:
        try:
            by_number = {r["nutrient_nbr"]: r["id"] for r in read(folder, "nutrient.csv") if r.get("nutrient_nbr")}
            rows = read(folder, "food_nutrient.csv")
        except FileNotFoundError:
            continue
        for r in rows:
            nid = r["nutrient_id"]
            if len(nid) < 4 and nid in by_number:
                r = dict(r, nutrient_id=by_number[nid])
            yield r


def unique(rows, key):
    """First row per key, in file order (so a single-folder build is byte-for-byte what it was before ADR 0052)."""
    seen = {}
    for r in rows:
        seen.setdefault(r[key], r)
    return list(seen.values())


def main(folders, target, date):
    foods = [r for r in read_all(folders, "food.csv") if r["data_type"] in TYPES]
    ids = {r["fdc_id"] for r in foods}
    files = {
        "food.csv": write(sorted(foods, key=lambda r: int(r["fdc_id"])),
                          ["fdc_id", "data_type", "description", "food_category_id", "publication_date"]),
        "food_category.csv": write(unique(read_all(folders, "food_category.csv"), "id"), ["id", "code", "description"]),
        "nutrient.csv": write(unique([r for r in read_all(folders, "nutrient.csv") if r["id"] in NUTRIENTS], "id"),
                              ["id", "name", "unit_name", "nutrient_nbr", "rank"]),
        "food_nutrient.csv": write(sorted((r for r in food_nutrients(folders)
                                           if r["fdc_id"] in ids and r["nutrient_id"] in NUTRIENTS),
                                          key=lambda r: (int(r["fdc_id"]), int(r["nutrient_id"]))),
                                   ["id", "fdc_id", "nutrient_id", "amount"]),
        "food_portion.csv": write(sorted((r for r in read_all(folders, "food_portion.csv") if r["fdc_id"] in ids),
                                         key=lambda r: (int(r["fdc_id"]), int(r["seq_num"] or 0), int(r["id"]))),
                                  ["id", "fdc_id", "seq_num", "amount", "measure_unit_id", "portion_description",
                                   "modifier", "gram_weight"]),
        "measure_unit.csv": write(unique(read_all(folders, "measure_unit.csv"), "id"), ["id", "name"]),
    }
    wweia = read_all(folders, "wweia_food_category.csv")
    if wweia:  # FNDDS categories (ADR 0052)
        files["wweia_food_category.csv"] = write(unique(wweia, "wweia_food_category"),
                                                 ["wweia_food_category", "wweia_food_category_description"])
    year, month, day = (int(p) for p in date.split("-"))
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for name in sorted(files):
            info = zipfile.ZipInfo(name, date_time=(year, month, day, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, files[name])
    for folder in folders:
        for name in ("food.csv", "food_nutrient.csv", "food_portion.csv", "nutrient.csv"):
            try:
                with open(f"{folder}/{name}", "rb") as f:
                    print(f"{folder}/{name}: {hashlib.sha256(f.read()).hexdigest()}")
            except FileNotFoundError:
                pass
    with open(target, "rb") as f:
        print(f"{target}: {hashlib.sha256(f.read()).hexdigest()}")
    counts = {}
    for r in foods:
        counts[r["data_type"]] = counts.get(r["data_type"], 0) + 1
    print(f"foods: {len(foods)} {counts}")


if __name__ == "__main__":
    args = sys.argv[1:]
    when = "2018-04-01"
    if args[:1] == ["--date"]:
        when, args = args[1], args[2:]
    if len(args) < 2:
        sys.exit(__doc__)
    main(args[:-1], args[-1], when)
