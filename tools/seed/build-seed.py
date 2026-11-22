#!/usr/bin/env python3
"""Builds the measurement seed (ADR 0013) from USDA public-domain files.

Usage:
  python3 -I tools/seed/build-seed.py <SR Legacy CSV folder> <USDA_CookingYields_MeatPoultry02.xlsx> <output.json>

Inputs (not committed; download them yourself):
  - FoodData Central, SR Legacy CSV (2018-04): food.csv, food_portion.csv.  CC0 1.0.
    https://fdc.nal.usda.gov/download-datasets
  - USDA Table of Cooking Yields for Meat and Poultry, Release 2 (xlsx). US government work, public domain.
    https://www.ars.usda.gov/ (Methods and Application of Food Composition Laboratory, cooking yields)
Needs openpyxl for the yields file. The output is deterministic: run it twice, get the same file.
"""
import csv
import hashlib
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from foods import FOODS, YIELDS  # noqa: E402

ML = {"cup": 236.5882365, "tbsp": 14.78676478125, "tsp": 4.92892159375, "fl oz": 29.5735295625}


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def load_sr(folder):
    foods = {}
    with open(os.path.join(folder, "food.csv"), newline="", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            foods[int(r["fdc_id"])] = r["description"]
    portions = {}
    with open(os.path.join(folder, "food_portion.csv"), newline="", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            portions.setdefault(int(r["fdc_id"]), []).append(
                (float(r["amount"] or 1), (r["modifier"] or "").strip(), float(r["gram_weight"])))
    return foods, portions


def find_food(foods, pattern):
    rx = re.compile(pattern)
    hits = sorted(i for i, d in foods.items() if rx.search(d))
    return hits[0] if hits else None


def density(portions, fdc_id, pattern):
    """g/ml from the first portion whose modifier matches, e.g. '1 cup' = 125 g -> 0.528."""
    rx = re.compile(pattern)
    for amount, modifier, grams in portions.get(fdc_id, []):
        if not rx.search(modifier):
            continue
        m = re.match(r"(cup|tbsp|tsp|fl oz)", modifier)
        if m and amount > 0:
            return round(grams / (amount * ML[m.group(1)]), 4), f"{amount:g} {modifier} = {grams:g} g"
    return None, None


def load_yields(path):
    import openpyxl  # local import: only needed here
    ws = openpyxl.load_workbook(path, read_only=True, data_only=True).worksheets[0]
    rows = []
    for row in ws.iter_rows(min_row=2, values_only=True):
        if row[2] is None:
            continue
        rows.append({"description": str(row[2]).strip(), "method": str(row[3] or "").strip(),
                     "yield": row[4], "min": row[7], "max": row[8]})
    return rows


def main():
    if len(sys.argv) != 4:
        print(__doc__)
        return 2
    sr, yields_path, out = sys.argv[1:]
    foods, portions = load_sr(sr)
    yield_rows = load_yields(yields_path)
    problems = []

    seed_foods = []
    seen = set()
    for name, plural, category, aliases, pattern, portion in FOODS:
        fdc = find_food(foods, pattern)
        if fdc is None:
            problems.append(f"no SR Legacy match for {name!r}: {pattern}")
            continue
        d, basis = (None, None)
        if portion is not None:
            d, basis = density(portions, fdc, portion)
            if d is None:
                problems.append(f"no portion {portion!r} for {name!r} ({fdc} {foods[fdc]})")
        for alias in [name, plural, *aliases]:
            key = alias.lower()
            if key in seen and alias not in (name, plural):
                problems.append(f"alias {alias!r} used twice")
            seen.add(key)
        seed_foods.append({
            "name": name, "pluralName": plural, "category": category,
            "aliases": sorted({a for a in aliases if a.lower() not in (name.lower(), plural.lower())}),
            "fdcId": fdc, "densityGPerMl": d,
            "sourceNote": f"USDA FoodData Central SR Legacy {fdc} ({foods[fdc]})" + (f"; {basis}" if basis else ""),
        })

    seed_yields = []
    for food, state, desc_rx, method_rx, cooked_rx, cooked_portion, basis in YIELDS:
        rows = [r for r in yield_rows if re.search(desc_rx, r["description"], re.I) and re.search(method_rx, r["method"])]
        rows = [r for r in rows if isinstance(r["min"], (int, float)) and isinstance(r["max"], (int, float))]
        if not rows:
            problems.append(f"no cooking-yield row for {food!r}: {desc_rx} / {method_rx}")
            continue
        r = rows[0]
        prepared_density, portion_basis = (None, None)
        if cooked_rx:
            cooked = find_food(foods, cooked_rx)
            if cooked is None:
                problems.append(f"no SR Legacy cooked food for {food!r}: {cooked_rx}")
            else:
                prepared_density, portion_basis = density(portions, cooked, cooked_portion)
        lo, hi = float(r["min"]), float(r["max"])
        seed_yields.append({
            "food": food, "fromState": state, "toState": "raw",
            # grams bought per gram prepared: 1 / max yield .. 1 / min yield
            "ratio": round(100 / hi, 4), "ratioMax": round(100 / lo, 4),
            "preparedDensityGPerMl": prepared_density,
            "basis": f"{basis}, about {round(lo)}–{round(hi)}% cooking yield",
            "sourceNote": f"USDA Table of Cooking Yields for Meat and Poultry R2: {r['description']}; {r['method']}"
                          + (f"; prepared density from SR Legacy: {portion_basis}" if portion_basis else ""),
        })

    doc = {
        "version": 1,
        "sources": [
            {"name": "USDA FoodData Central, SR Legacy (2018-04)", "licence": "CC0 1.0 (public domain)",
             "url": "https://fdc.nal.usda.gov/", "sha256": {f: sha256(os.path.join(sr, f)) for f in ("food.csv", "food_portion.csv")}},
            {"name": "USDA Table of Cooking Yields for Meat and Poultry, Release 2", "licence": "US government work (public domain)",
             "url": "https://www.ars.usda.gov/", "sha256": {os.path.basename(yields_path): sha256(yields_path)}},
        ],
        "foods": seed_foods,
        "yields": seed_yields,
    }
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, ensure_ascii=False, indent=1)
        f.write("\n")
    print(f"{len(seed_foods)} foods ({sum(1 for f in seed_foods if f['densityGPerMl'])} with density), "
          f"{sum(len(f['aliases']) for f in seed_foods)} aliases, {len(seed_yields)} yields -> {out}")
    for p in problems:
        print("PROBLEM:", p)
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
