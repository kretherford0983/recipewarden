import pytest
from conftest import run_main, run_process


def test_usage_and_unknown_commands():
    code, body, _ = run_main([], b"{}")
    assert code == 2
    assert body["error"]["code"] == "usage"
    code, body, _ = run_main(["rm -rf"], b"{}")
    assert code == 1
    assert body["error"]["code"] == "unknown_command"


def test_a_contract_error_is_an_envelope_not_a_traceback(req):
    code, body, err = run_main(["parse-ingredients"], req({"lines": ["a"] * 501}))
    assert code == 1
    assert body == {"v": 1, "ok": False, "engine": body["engine"], "error": body["error"]}
    assert body["error"]["code"] == "too_many_items"
    assert err == ""


@pytest.mark.req("BR-119")
def test_an_unexpected_failure_is_reported_and_logged_to_stderr(monkeypatch, req):
    from rw_worker.commands import parse_ingredients

    def boom(_request):
        raise RuntimeError("model file missing")

    monkeypatch.setattr(parse_ingredients, "run", boom)
    code, body, err = run_main(["parse-ingredients"], req({"lines": ["x"]}))
    assert code == 1
    assert body["error"] == {"code": "internal", "message": "the worker failed; see the service log"}
    assert "model file missing" in err


@pytest.mark.req("SEC-91")
def test_isolated_mode_ignores_python_environment_variables(req):
    code, body = run_process("selftest", req({}), {"PYTHONPATH": "/nonexistent", "PYTHONSTARTUP": "/nonexistent"})
    assert code == 0
    assert body["ok"] is True


@pytest.mark.req("AC-106")
def test_selftest_passes_offline_with_the_bundled_model(req):
    code, body = run_process("selftest", req({}))
    assert code == 0
    result = body["result"]
    assert result["passed"] is True, result
    assert result["checks"]["fdc"] is True


def test_selftest_without_the_nltk_model_fails_cleanly(req, tmp_path):
    code, body = run_process("selftest", req({}), {"NLTK_DATA": str(tmp_path)})
    assert code == 1
    assert body["error"]["code"] == "internal"


@pytest.mark.req("FR-OPS-06")
def test_selftest_in_process_reports_every_check():
    code, body, _ = run_main(["selftest"], b"")
    assert code == 0
    checks = body["result"]["checks"]
    assert {"network_blocked", "parse", "fdc", "scrape", "parse_ms", "scrape_ms"} <= set(checks)
    assert body["result"]["passed"] is True


def test_selftest_reports_a_connection_that_was_not_blocked(monkeypatch):
    import socket

    from rw_worker.commands import selftest

    def refused(*_a, **_k):
        raise ConnectionRefusedError

    monkeypatch.setattr(socket, "create_connection", refused)
    assert selftest._network_blocked() is False
    monkeypatch.setattr(socket, "create_connection", lambda *_a, **_k: object())
    assert selftest._network_blocked() is False
