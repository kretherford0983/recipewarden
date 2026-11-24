import io
import json

import pytest

from rw_worker import contract


def test_limits_match_the_spec():
    assert contract.MAX_REQUEST_BYTES == 6 * 1024 * 1024  # SEC-92
    assert contract.MAX_RESPONSE_BYTES == 2 * 1024 * 1024
    assert contract.MAX_LINES == 500
    assert contract.MAX_LINE_LENGTH == 300


@pytest.mark.req("SEC-92")
def test_an_oversized_request_is_refused():
    with pytest.raises(contract.ContractError) as error:
        contract.read_request(io.BytesIO(b" " * (contract.MAX_REQUEST_BYTES + 1)))
    assert error.value.code == "request_too_large"


@pytest.mark.parametrize(
    ("data", "code"),
    [(b"{nope", "bad_json"), (b"\xff\xfe", "bad_json"), (b"[1,2]", "bad_request")],
)
def test_bad_requests_get_a_code(data, code):
    with pytest.raises(contract.ContractError) as error:
        contract.read_request(io.BytesIO(data))
    assert error.value.code == code


def test_an_empty_request_is_an_empty_object():
    assert contract.read_request(io.BytesIO(b"  \n")) == {}


@pytest.mark.parametrize(
    ("request_body", "code"),
    [
        ({"lines": "x"}, "bad_request"),
        ({"lines": [1]}, "bad_request"),
        ({"lines": ["a"] * 501}, "too_many_items"),
        ({"lines": ["a" * 301]}, "item_too_long"),
    ],
)
def test_string_lists_are_checked(request_body, code):
    with pytest.raises(contract.ContractError) as error:
        contract.string_list(request_body, "lines", contract.MAX_LINES, contract.MAX_LINE_LENGTH)
    assert error.value.code == code


def test_text_and_flag_are_checked():
    assert contract.text({"u": "x"}, "u", 5) == "x"
    with pytest.raises(contract.ContractError):
        contract.text({"u": 1}, "u", 5)
    with pytest.raises(contract.ContractError):
        contract.text({"u": "toolong"}, "u", 5)
    assert contract.flag({}, "f", True) is True
    with pytest.raises(contract.ContractError):
        contract.flag({"f": "yes"}, "f", False)


@pytest.mark.req("SEC-92")
def test_an_oversized_response_becomes_an_error():
    body = contract.envelope(result={"big": "x" * contract.MAX_RESPONSE_BYTES})
    data = json.loads(contract.encode(body))
    assert data["ok"] is False
    assert data["error"]["code"] == "response_too_large"


def test_engine_reports_versions_and_missing_libraries(monkeypatch):
    info = contract.engine()
    assert info["worker"] == "1.0.0"
    assert info["python"].startswith("3.13")
    assert set(info["libs"]) == set(contract.LIBRARIES)
    monkeypatch.setattr(contract, "LIBRARIES", ("no-such-package-rw",))
    assert contract.engine()["libs"] == {"no-such-package-rw": "missing"}
