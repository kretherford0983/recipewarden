"""``scrape-html {html, url}``: recipe-scrapers' reading of a page the app already fetched (M9 shadow mode, M10).

The HTML always comes from SafeFetcher or paste; the url is used only to pick the site's scraper, and nothing is
fetched (SEC-90, SEC-91, AC-112). Output is plain text, capped; the app sanitizes it like any import (SEC-93).
"""

from __future__ import annotations

from typing import Any
from urllib.parse import urlsplit

from rw_worker import contract

MAX_ITEMS = 500
MAX_TEXT = 4000


def _host(url: str) -> str:
    parts = urlsplit(url)
    if parts.scheme not in ("http", "https") or not parts.hostname:
        raise contract.ContractError("bad_request", "'url' must be an http or https address")
    host = parts.hostname.lower()
    return host[4:] if host.startswith("www.") else host


def _call(scraper: Any, name: str) -> Any:
    try:
        return getattr(scraper, name)()
    except Exception:  # noqa: BLE001  a missing field is normal; the app treats it as absent
        return None


def _text(value: Any) -> str | None:
    if value is None:
        return None
    text = str(value).strip()
    return text[:MAX_TEXT] or None


def _texts(value: Any) -> list[str]:
    if not isinstance(value, list):
        return []
    return [t for t in (_text(v) for v in value[:MAX_ITEMS]) if t]


def _minutes(value: Any) -> int | None:
    return value if isinstance(value, int) and not isinstance(value, bool) and 0 < value < 100_000 else None


def run(request: dict[str, Any]) -> dict[str, Any]:
    html = contract.text(request, "html", contract.MAX_REQUEST_BYTES)
    url = contract.text(request, "url", contract.MAX_URL_LENGTH)
    host = _host(url)
    from recipe_scrapers import SCRAPERS, scrape_html
    from recipe_scrapers._exceptions import RecipeScrapersExceptions

    supported = host in SCRAPERS
    try:
        scraper = scrape_html(html, org_url=url, supported_only=False)
    except RecipeScrapersExceptions:
        return {"supported_host": supported, "found": False}

    title = _text(_call(scraper, "title"))
    ingredients = _texts(_call(scraper, "ingredients"))
    if title is None and not ingredients:
        return {"supported_host": supported, "found": False}
    return {
        "supported_host": supported,
        "found": True,
        "title": title,
        "ingredients": ingredients,
        "instructions": _texts(_call(scraper, "instructions_list")),
        "yields": _text(_call(scraper, "yields")),
        "total_minutes": _minutes(_call(scraper, "total_time")),
        "prep_minutes": _minutes(_call(scraper, "prep_time")),
        "cook_minutes": _minutes(_call(scraper, "cook_time")),
        "author": _text(_call(scraper, "author")),
        "description": _text(_call(scraper, "description")),
        "image_url": _text(_call(scraper, "image")),
        "site_name": _text(_call(scraper, "site_name")),
    }
