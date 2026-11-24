"""Shared mapping from ingredient-parser's dataclasses to plain JSON (contract v1)."""

from __future__ import annotations

from decimal import Decimal
from fractions import Fraction
from typing import Any

MAX_FOODS = 3


def decimal_text(value: object) -> str | None:
    """A quantity as an exact-enough decimal string ("1.5", "0.333333"), or None when it is not a number."""
    if isinstance(value, bool) or value is None:
        return None
    if isinstance(value, Fraction):
        number = Decimal(value.numerator) / Decimal(value.denominator)
    elif isinstance(value, int | float):
        number = Decimal(str(value))
    else:
        return None
    text = f"{number.quantize(Decimal('0.000001')):f}".rstrip("0").rstrip(".")
    return text or "0"


def foods(parsed: Any) -> list[dict[str, Any]]:
    result = []
    for food in (getattr(parsed, "foundation_foods", None) or [])[:MAX_FOODS]:
        result.append(
            {
                "fdc_id": int(food.fdc_id),
                "data_type": str(food.data_type),
                "description": str(food.text),
                "confidence": round(float(food.confidence), 4),
            }
        )
    return result


def line(parsed: Any) -> dict[str, Any]:
    amount = parsed.amount[0] if parsed.amount else None
    names = [str(n.text) for n in (parsed.name or [])]
    return {
        "quantity": decimal_text(getattr(amount, "quantity", None)) if amount else None,
        "quantity_max": _max(amount),
        "unit": (str(amount.unit) or None) if amount and amount.unit else None,
        "names": names,
        "preparation": parsed.preparation.text if parsed.preparation else None,
        "comment": parsed.comment.text if parsed.comment else None,
        "name_confidence": round(min((float(n.confidence) for n in parsed.name), default=0.0), 4),
        "amount_confidence": round(float(amount.confidence), 4) if amount else 0.0,
        "fdc": foods(parsed),
    }


def _max(amount: Any) -> str | None:
    if amount is None or not getattr(amount, "RANGE", False):
        return None
    return decimal_text(getattr(amount, "quantity_max", None))
