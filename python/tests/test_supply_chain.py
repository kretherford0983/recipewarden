"""AC-108, SEC-95, ADR 0053: CI fails on a disallowed licence or a source build that is not allowed by name."""

import importlib.metadata
import sys
from email.message import Message
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
import check_supply_chain as chain

HERE = Path(__file__).resolve().parents[1]


def meta(expression=None, classifiers=(), licence=None):
    m = Message()
    if expression:
        m["License-Expression"] = expression
    for c in classifiers:
        m["Classifier"] = c
    if licence:
        m["License"] = licence
    return m


def problems(name, metadata):
    allowed, named = chain.read_allowed(HERE / "licences-allowed.txt")
    return chain.licence_problems(name, chain.licence_terms(metadata), allowed, named)


@pytest.mark.req("AC-108")
def test_the_locked_runtime_tree_passes():
    found, count = chain.check(HERE / "uv.lock", HERE / "licences-allowed.txt", HERE / "sdist-allowed.txt")
    assert found == []
    assert count > 30


@pytest.mark.req("AC-108")
@pytest.mark.parametrize(
    "metadata",
    [
        meta("GPL-3.0-or-later"),
        meta(classifiers=["License :: OSI Approved :: GNU General Public License v3 (GPLv3)"]),
        meta(licence="AGPL-3.0"),
        meta("MIT AND LGPL-3.0"),
        meta(),
        meta(licence="All rights reserved. Do not copy this software without written permission from the owner."),
    ],
)
def test_copyleft_unknown_or_missing_licences_fail(metadata):
    assert problems("somepackage", metadata)


@pytest.mark.req("AC-108")
def test_mpl_is_allowed_only_for_the_named_packages():
    assert (
        problems("certifi", meta(classifiers=["License :: OSI Approved :: Mozilla Public License 2.0 (MPL 2.0)"])) == []
    )
    assert problems("tqdm", meta(licence="MPL-2.0 AND MIT")) == []
    assert problems("other-mpl-package", meta(licence="MPL-2.0"))


@pytest.mark.parametrize(
    "metadata",
    [
        meta("MIT"),
        meta("Apache-2.0 OR BSD-2-Clause"),
        meta("BSD-3-Clause AND 0BSD AND MIT AND Zlib AND CC0-1.0"),
        meta("Apache-2.0 AND CNRI-Python"),
        meta(classifiers=["License :: OSI Approved :: BSD License"]),
        meta(classifiers=["License :: OSI Approved :: W3C License"]),
        meta(licence="PSFL"),
        meta(licence="Apache License, Version 2.0"),
    ],
)
def test_permissive_licences_pass(metadata):
    assert problems("somepackage", metadata) == []


def test_or_needs_one_allowed_and_and_needs_all():
    assert chain.expression_terms("(A OR B) AND C") == [{"A", "B"}, {"C"}]
    assert problems("x", meta("GPL-3.0 OR MIT")) == []
    assert problems("x", meta("GPL-3.0 AND MIT"))


@pytest.mark.req("AC-108")
def test_a_package_without_a_wheel_must_be_allowed_by_name():
    packages = {
        "jstyleson": {"sdist": {"hash": "sha256:abc"}},
        "wheelless": {"sdist": {"hash": "sha256:def"}},
        "unhashed": {"sdist": {}},
        "fine": {"wheels": [{"hash": "sha256:123"}]},
    }
    found = chain.source_problems(packages, {"jstyleson", "unhashed"})
    assert found == [
        "unhashed: source archive has no hash in uv.lock (SEC-95)",
        "wheelless: no wheel in uv.lock and not in sdist-allowed.txt (SEC-95)",
    ]


def test_a_package_missing_from_the_environment_is_reported(tmp_path):
    def missing(_name):
        raise importlib.metadata.PackageNotFoundError

    found, _ = chain.check(HERE / "uv.lock", HERE / "licences-allowed.txt", HERE / "sdist-allowed.txt", missing)
    assert found and all("not installed" in p for p in found)


def test_windows_only_dependencies_are_skipped():
    assert chain.applies({"name": "x"})
    assert chain.applies({"name": "x", "marker": "sys_platform == 'linux'"})
    assert not chain.applies({"name": "colorama", "marker": "sys_platform == 'win32'"})


def test_main_prints_a_summary(capsys):
    assert chain.main() == 0
    assert "runtime packages checked, 0 problem(s)" in capsys.readouterr().out
