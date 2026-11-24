"""Shared helpers. The real worker is always exercised: these tests are never skipped (docs/testing.md)."""

from __future__ import annotations

import io
import json
import os
import subprocess
import sys
from pathlib import Path
from typing import Any

import pytest

ROOT = Path(__file__).resolve().parents[2]
CONTRACTS = ROOT / "tests" / "contracts"


def nltk_data() -> str:
    path = os.environ.get("NLTK_DATA") or str(ROOT / "python" / ".nltk_data")
    if not Path(path, "taggers", "averaged_perceptron_tagger_eng").is_dir():
        raise RuntimeError(f"NLTK tagger missing under {path}: run build/python.sh dev first")
    return path


os.environ["NLTK_DATA"] = nltk_data()


def run_main(command: list[str], request: bytes) -> tuple[int, dict[str, Any], str]:
    """Run the worker's main() in process (for coverage); the guard is installed by importing rw_worker.__main__."""
    from rw_worker.__main__ import main

    out, err = io.BytesIO(), io.StringIO()
    code = main(command, io.BytesIO(request), out, err)
    return code, json.loads(out.getvalue()), err.getvalue()


def run_process(command: str, request: bytes, extra_env: dict[str, str] | None = None) -> tuple[int, dict[str, Any]]:
    """Run the worker exactly as the app does (SEC-91): isolated mode, allow-listed environment, stdin/stdout."""
    env = {"LANG": "C.UTF-8", "NLTK_DATA": nltk_data(), "PYTHONHASHSEED": "0"} | (extra_env or {})
    done = subprocess.run(
        [sys.executable, "-I", "-B", "-m", "rw_worker", command],
        input=request,
        capture_output=True,
        env=env,
        timeout=60,
        check=False,
    )
    return done.returncode, json.loads(done.stdout)


def contract(name: str) -> dict[str, Any]:
    return json.loads((CONTRACTS / name).read_text("utf-8"))


@pytest.fixture
def req():
    def build(body: Any) -> bytes:
        return json.dumps(body).encode("utf-8")

    return build
