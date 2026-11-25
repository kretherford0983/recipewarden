"""SEC-95, AC-108: the Python packages that ship must have an allowed licence and come from hashed wheels.

Usage (from python/, with the dev environment): .venv/bin/python tools/check_supply_chain.py
Reads uv.lock for the runtime dependency tree, the installed metadata for each package's licence,
licences-allowed.txt and sdist-allowed.txt. Exit 1 lists every problem.
"""

from __future__ import annotations

import importlib.metadata
import re
import sys
import tomllib
from collections.abc import Iterable
from pathlib import Path

HERE = Path(__file__).resolve().parents[1]
PROJECT = "rw-worker"

CLASSIFIERS = {
    "MIT License": "MIT",
    "BSD License": "BSD",
    "Apache Software License": "Apache-2.0",
    "Python Software Foundation License": "PSF-2.0",
    "W3C License": "W3C",
    "ISC License (ISCL)": "ISC",
    "Mozilla Public License 2.0 (MPL 2.0)": "MPL-2.0",
    "GNU General Public License v3 (GPLv3)": "GPL-3.0",
    "GNU General Public License v2 (GPLv2)": "GPL-2.0",
    "GNU Lesser General Public License v3 (LGPLv3)": "LGPL-3.0",
}
FREE_TEXT = {
    "mit license": "MIT",
    "psfl": "PSF-2.0",
    "apache 2.0": "Apache-2.0",
    "apache license, version 2.0": "Apache-2.0",
    "bsd": "BSD",
}


def normalize(name: str) -> str:
    return re.sub(r"[-_.]+", "-", name).lower()


def read_allowed(path: Path) -> tuple[set[str], dict[str, set[str]]]:
    licences: set[str] = set()
    named: dict[str, set[str]] = {}
    for raw in path.read_text("utf-8").splitlines():
        line = raw.split("#", 1)[0].strip()
        if not line:
            continue
        if "=" in line:
            package, licence = (part.strip() for part in line.split("=", 1))
            named.setdefault(normalize(package), set()).add(licence)
        else:
            licences.add(line)
    return licences, named


def read_list(path: Path) -> set[str]:
    return {normalize(line.split("#", 1)[0].strip()) for line in path.read_text("utf-8").splitlines()} - {""}


def expression_terms(expression: str) -> list[set[str]]:
    """'A AND (B OR C)' -> [{A}, {B, C}]: every group must hold, and one id in a group is enough."""
    groups = []
    for part in re.split(r"\s+AND\s+", expression.strip().strip("()")):
        groups.append({t.strip().strip("()") for t in re.split(r"\s+OR\s+", part.strip().strip("()")) if t.strip()})
    return groups


def licence_terms(metadata: importlib.metadata.PackageMetadata) -> list[set[str]]:
    expression = metadata.get("License-Expression")
    if expression:
        return expression_terms(expression)
    classifiers = [c.split(" :: ")[-1] for c in metadata.get_all("Classifier") or [] if c.startswith("License ::")]
    mapped = {CLASSIFIERS.get(c, c) for c in classifiers if c != "OSI Approved"}
    if mapped:
        return [mapped]  # several classifiers mean "any of these"
    text = (metadata.get("License") or "").strip()
    if not text:
        return [{"UNKNOWN"}]
    if text.lower() in FREE_TEXT:
        return [{FREE_TEXT[text.lower()]}]
    if re.fullmatch(r"[A-Za-z0-9.+\-() ]+", text) and len(text) < 80:
        return expression_terms(text)
    return [{"UNKNOWN"}]


def licence_problems(package: str, terms: list[set[str]], allowed: set[str], named: dict[str, set[str]]) -> list[str]:
    ok_here = allowed | named.get(normalize(package), set())
    bad = [group for group in terms if not group & ok_here]
    if not bad:
        return []
    shown = " AND ".join(" OR ".join(sorted(g)) for g in terms)
    return [f"{package}: licence '{shown}' is not in licences-allowed.txt (ADR 0053)"]


def applies(dependency: dict) -> bool:
    """True when a dependency's marker holds on the target (Linux x86_64, CPython 3.13): skips Windows-only ones."""
    marker = dependency.get("marker")
    if not marker:
        return True
    from packaging.markers import Marker  # dev environment only

    target = {
        "sys_platform": "linux",
        "platform_system": "Linux",
        "os_name": "posix",
        "platform_machine": "x86_64",
        "implementation_name": "cpython",
        "platform_python_implementation": "CPython",
        "python_version": "3.13",
        "python_full_version": "3.13.0",
        "extra": "",
    }
    return Marker(marker).evaluate(target)


def runtime_packages(lock: dict) -> dict[str, dict]:
    """Every package the worker needs at run time: rw-worker's dependencies, walked, without the dev group."""
    by_name = {normalize(p["name"]): p for p in lock["package"]}
    seen: dict[str, dict] = {}
    todo = [normalize(d["name"]) for d in by_name[PROJECT].get("dependencies", []) if applies(d)]
    while todo:
        name = todo.pop()
        if name in seen:
            continue
        seen[name] = by_name[name]
        todo.extend(normalize(d["name"]) for d in by_name[name].get("dependencies", []) if applies(d))
    return seen


def source_problems(packages: dict[str, dict], sdist_allowed: set[str]) -> list[str]:
    problems = []
    for name, package in sorted(packages.items()):
        if package.get("wheels"):
            continue
        if name not in sdist_allowed:
            problems.append(f"{name}: no wheel in uv.lock and not in sdist-allowed.txt (SEC-95)")
        elif not package.get("sdist", {}).get("hash"):
            problems.append(f"{name}: source archive has no hash in uv.lock (SEC-95)")
    return problems


def check(
    lock_path: Path, allowed_path: Path, sdist_path: Path, metadata_of=importlib.metadata.metadata
) -> tuple[list[str], int]:
    lock = tomllib.loads(lock_path.read_text("utf-8"))
    packages = runtime_packages(lock)
    allowed, named = read_allowed(allowed_path)
    problems = source_problems(packages, read_list(sdist_path))
    for name in sorted(packages):
        try:
            metadata = metadata_of(name)
        except importlib.metadata.PackageNotFoundError:
            problems.append(f"{name}: not installed; run build/python.sh dev")
            continue
        problems.extend(licence_problems(name, licence_terms(metadata), allowed, named))
    return problems, len(packages)


def main(argv: Iterable[str] = ()) -> int:
    del argv
    problems, count = check(HERE / "uv.lock", HERE / "licences-allowed.txt", HERE / "sdist-allowed.txt")
    for problem in problems:
        print(f"::error::{problem}")
    print(f"Python supply chain: {count} runtime packages checked, {len(problems)} problem(s)")
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
