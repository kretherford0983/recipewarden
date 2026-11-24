"""``suggest-foods {names[]}``: USDA FoodData Central candidates for each name (FR-NUT-07).

The worker only names candidates; the app resolves each FDC id against its local dataset and shows nothing it
cannot resolve (BR-116, SEC-93).
"""

from __future__ import annotations

from typing import Any

from rw_worker import contract
from rw_worker.commands import _ingredients


def run(request: dict[str, Any]) -> dict[str, Any]:
    names = contract.string_list(request, "names", contract.MAX_NAMES, contract.MAX_NAME_LENGTH)
    from ingredient_parser import parse_ingredient

    suggestions = []
    for name in names:
        if not name.strip():
            suggestions.append({"name": name, "fdc": []})
            continue
        parsed = parse_ingredient(name, string_units=True, foundation_foods=True)
        suggestions.append({"name": name, "fdc": _ingredients.foods(parsed)})
    return {"suggestions": suggestions}
