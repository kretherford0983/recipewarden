"""Worker contract v1 (plan 4.2): one JSON request on stdin, one JSON envelope on stdout.

Limits are SEC-92's; the .NET client enforces them too and treats anything else as untrusted (SEC-93).
"""

from __future__ import annotations

import importlib.metadata
import json
import platform
from typing import IO, Any

from rw_worker import __version__

CONTRACT_VERSION = 1
MAX_REQUEST_BYTES = 6 * 1024 * 1024
MAX_RESPONSE_BYTES = 2 * 1024 * 1024
MAX_LINES = 500
MAX_LINE_LENGTH = 300  # IngredientLineParser.MaxParseLength
MAX_NAMES = 2000
MAX_NAME_LENGTH = 200
MAX_URL_LENGTH = 2048

LIBRARIES = ("ingredient-parser-nlp", "recipe-scrapers")


class ContractError(Exception):
    """A request that breaks the contract. ``code`` is stable and goes to the caller; the message is plain text."""

    def __init__(self, code: str, message: str) -> None:
        super().__init__(message)
        self.code = code
        self.message = message


def engine() -> dict[str, Any]:
    libs: dict[str, str] = {}
    for name in LIBRARIES:
        try:
            libs[name] = importlib.metadata.version(name)
        except importlib.metadata.PackageNotFoundError:
            libs[name] = "missing"
    return {"worker": __version__, "python": platform.python_version(), "libs": libs}


def read_request(stream: IO[bytes]) -> dict[str, Any]:
    data = stream.read(MAX_REQUEST_BYTES + 1)
    if len(data) > MAX_REQUEST_BYTES:
        raise ContractError("request_too_large", f"request is larger than {MAX_REQUEST_BYTES} bytes")
    if not data.strip():
        return {}
    try:
        request = json.loads(data.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as error:
        raise ContractError("bad_json", "request is not valid UTF-8 JSON") from error
    if not isinstance(request, dict):
        raise ContractError("bad_request", "request must be a JSON object")
    return request


def string_list(request: dict[str, Any], key: str, max_items: int, max_length: int) -> list[str]:
    value = request.get(key)
    if not isinstance(value, list) or not all(isinstance(item, str) for item in value):
        raise ContractError("bad_request", f"'{key}' must be a list of strings")
    if len(value) > max_items:
        raise ContractError("too_many_items", f"'{key}' has more than {max_items} items")
    if any(len(item) > max_length for item in value):
        raise ContractError("item_too_long", f"an item in '{key}' is longer than {max_length} characters")
    return value


def text(request: dict[str, Any], key: str, max_length: int) -> str:
    value = request.get(key)
    if not isinstance(value, str):
        raise ContractError("bad_request", f"'{key}' must be a string")
    if len(value) > max_length:
        raise ContractError("item_too_long", f"'{key}' is longer than {max_length} characters")
    return value


def flag(request: dict[str, Any], key: str, default: bool) -> bool:
    value = request.get(key, default)
    if not isinstance(value, bool):
        raise ContractError("bad_request", f"'{key}' must be true or false")
    return value


def envelope(result: dict[str, Any] | None = None, error: ContractError | None = None) -> dict[str, Any]:
    body: dict[str, Any] = {"v": CONTRACT_VERSION, "ok": error is None, "engine": engine()}
    if error is None:
        body["result"] = result if result is not None else {}
    else:
        body["error"] = {"code": error.code, "message": error.message}
    return body


def encode(body: dict[str, Any]) -> bytes:
    data = json.dumps(body, ensure_ascii=False, separators=(",", ":"), allow_nan=False).encode("utf-8")
    if len(data) > MAX_RESPONSE_BYTES:
        too_large = ContractError("response_too_large", f"response is larger than {MAX_RESPONSE_BYTES} bytes")
        data = json.dumps(envelope(error=too_large), separators=(",", ":")).encode("utf-8")
    return data
