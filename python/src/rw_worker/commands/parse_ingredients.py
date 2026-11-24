"""``parse-ingredients {lines[], fdc}``: the NLP parser's reading of each line (FR-IMP-11, D4: a second opinion)."""

from __future__ import annotations

from typing import Any

from rw_worker import contract
from rw_worker.commands import _ingredients


def run(request: dict[str, Any]) -> dict[str, Any]:
    lines = contract.string_list(request, "lines", contract.MAX_LINES, contract.MAX_LINE_LENGTH)
    fdc = contract.flag(request, "fdc", False)
    from ingredient_parser import parse_ingredient

    results = []
    for text in lines:
        if not text.strip():
            results.append(None)
            continue
        parsed = parse_ingredient(text, string_units=True, foundation_foods=fdc)
        results.append(_ingredients.line(parsed))
    return {"lines": results}
