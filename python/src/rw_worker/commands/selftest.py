"""``selftest``: proves the worker works offline (FR-OPS-06, AC-106): every library imports, a line parses with FDC
matching (which needs the bundled NLTK model), a page scrapes, and the network guard refuses a connection."""

from __future__ import annotations

import socket
import time
from typing import Any

from rw_worker import guard
from rw_worker.commands import parse_ingredients, scrape_html

LINE = "2 cups all-purpose flour, sifted"
PAGE = (
    '<html><head><title>Test</title><script type="application/ld+json">'
    '{"@context":"https://schema.org","@type":"Recipe","name":"Self-test Bread",'
    '"recipeIngredient":["500 g flour","350 ml water"],"recipeInstructions":[{"@type":"HowToStep","text":"Mix."}]}'
    "</script></head><body></body></html>"
)


def _network_blocked() -> bool:
    try:
        socket.create_connection(("127.0.0.1", 9), timeout=1)
    except guard.NetworkBlockedError:
        return True
    except OSError:
        return False
    return False


def run(_request: dict[str, Any]) -> dict[str, Any]:
    checks: dict[str, Any] = {"network_blocked": guard.is_installed() and _network_blocked()}
    started = time.perf_counter()
    parsed = parse_ingredients.run({"lines": [LINE], "fdc": True})["lines"][0]
    checks["parse_ms"] = round((time.perf_counter() - started) * 1000)
    checks["parse"] = parsed is not None and parsed["quantity"] == "2" and parsed["unit"] in ("cup", "cups")
    checks["fdc"] = bool(parsed and parsed["fdc"])
    started = time.perf_counter()
    scraped = scrape_html.run({"html": PAGE, "url": "https://example.com/self-test"})
    checks["scrape_ms"] = round((time.perf_counter() - started) * 1000)
    checks["scrape"] = scraped.get("title") == "Self-test Bread" and len(scraped.get("ingredients", [])) == 2
    passed = all(v for k, v in checks.items() if not k.endswith("_ms"))
    return {"passed": passed, "checks": checks}
