import pytest
from conftest import contract, run_main

from rw_worker.commands import _ingredients


def _shape(actual, expected, path="$"):
    """Same keys and JSON types as the fixture, recursively (values differ by model version)."""
    if expected is None or actual is None:
        return
    assert type(actual) is type(expected) or {type(actual), type(expected)} <= {int, float}, path
    if isinstance(expected, dict):
        assert set(actual) == set(expected), path
        for key in expected:
            _shape(actual[key], expected[key], f"{path}.{key}")
    elif isinstance(expected, list) and expected and actual:
        for item in actual:
            _shape(item, expected[0] if expected[0] is not None else item, f"{path}[]")


@pytest.mark.parametrize("command", ["parse-ingredients", "suggest-foods", "scrape-html"])
def test_answers_have_the_shape_of_the_contract_fixtures(command):
    request = (contract.__globals__["CONTRACTS"] / f"{command}.request.json").read_bytes()
    code, body, _ = run_main([command], request)
    assert code == 0
    _shape(body, contract(f"{command}.response.json"))


@pytest.mark.req("AC-104")
def test_parse_reads_quantity_unit_name_and_keeps_blank_lines_as_null(req):
    _, body, _ = run_main(["parse-ingredients"], req({"lines": ["1-1/2 cups milk", "2 large eggs", " "]}))
    milk, eggs, blank = body["result"]["lines"]
    assert (milk["quantity"], milk["quantity_max"], milk["unit"]) == ("0.5", "1", "cup")  # the known misreading
    assert milk["fdc"] == []  # fdc defaults to off
    assert (eggs["quantity"], eggs["unit"], eggs["names"]) == ("2", None, ["eggs"])
    assert blank is None


@pytest.mark.req("AC-101")
def test_suggest_foods_names_usda_candidates_and_nothing_for_unknown_foods(req):
    _, body, _ = run_main(["suggest-foods"], req({"names": ["brown sugar", "gochujang", ""]}))
    brown, gochujang, blank = body["result"]["suggestions"]
    assert brown["fdc"], brown
    assert "sugar" in brown["fdc"][0]["description"].lower()
    assert brown["fdc"][0]["data_type"] in {"sr_legacy_food", "foundation_food", "survey_fndds_food"}
    assert 0 <= brown["fdc"][0]["confidence"] <= 1
    assert gochujang["fdc"] == []
    assert blank == {"name": "", "fdc": []}


@pytest.mark.req("AC-109")
def test_scrape_reads_schema_data_and_reports_the_host(req):
    html = (contract.__globals__["CONTRACTS"] / "scrape-html.request.json").read_text("utf-8")
    _, body, _ = run_main(["scrape-html"], html.encode())
    result = body["result"]
    assert result["found"] is True
    assert result["title"] == "Lemon Lentil Soup"
    assert result["total_minutes"] == 55
    _, body, _ = run_main(["scrape-html"], req({"html": "<html></html>", "url": "https://www.allrecipes.com/x"}))
    assert body["result"] == {"supported_host": True, "found": False}


@pytest.mark.req("SEC-91")
@pytest.mark.parametrize("url", ["file:///etc/passwd", "ftp://example.com/x", "https://", "javascript:alert(1)"])
def test_scrape_refuses_anything_but_a_web_address(req, url):
    code, body, _ = run_main(["scrape-html"], req({"html": "<html></html>", "url": url}))
    assert code == 1
    assert body["error"]["code"] == "bad_request"


def test_scrape_caps_and_cleans_fields():
    from rw_worker.commands import scrape_html

    assert scrape_html._text("  x  ") == "x"
    assert scrape_html._text("") is None
    assert len(scrape_html._text("y" * 9000)) == scrape_html.MAX_TEXT
    assert scrape_html._texts("not a list") == []
    assert scrape_html._texts(["a", "", None, "b"]) == ["a", "b"]
    assert scrape_html._minutes(30) == 30
    assert scrape_html._minutes(True) is None
    assert scrape_html._minutes(0) is None
    assert scrape_html._minutes("30") is None

    class Broken:
        def title(self):
            raise ValueError("no title")

    assert scrape_html._call(Broken(), "title") is None


@pytest.mark.parametrize(
    ("value", "text"),
    [(1, "1"), (1.5, "1.5"), (0.0, "0"), (None, None), (True, None), ("dozen", None)],
)
def test_quantities_become_decimal_text(value, text):
    assert _ingredients.decimal_text(value) == text


def test_fractions_keep_six_places():
    from fractions import Fraction

    assert _ingredients.decimal_text(Fraction(1, 3)) == "0.333333"
    assert _ingredients.decimal_text(Fraction(3, 2)) == "1.5"
