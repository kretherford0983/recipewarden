"""Entry point: ``python -I -B -m rw_worker <command>`` (SEC-91).

The network guard is installed before anything else is imported (SEC-90). Commands are imported only when used,
so a broken library cannot stop the others, and errors are reported in the envelope, never as a traceback on stdout.
"""

from rw_worker import guard

guard.install()

import sys  # noqa: E402  (the guard must come first)
import traceback  # noqa: E402
from collections.abc import Callable  # noqa: E402
from typing import IO, Any  # noqa: E402

from rw_worker import contract  # noqa: E402


def _command(name: str) -> Callable[[dict[str, Any]], dict[str, Any]] | None:
    if name == "selftest":
        from rw_worker.commands import selftest

        return selftest.run
    if name == "parse-ingredients":
        from rw_worker.commands import parse_ingredients

        return parse_ingredients.run
    if name == "suggest-foods":
        from rw_worker.commands import suggest_foods

        return suggest_foods.run
    if name == "scrape-html":
        from rw_worker.commands import scrape_html

        return scrape_html.run
    return None


def main(argv: list[str], stdin: IO[bytes], stdout: IO[bytes], stderr: IO[str]) -> int:
    if len(argv) != 1:
        body = contract.envelope(error=contract.ContractError("usage", "usage: rw_worker <command>"))
        stdout.write(contract.encode(body))
        return 2
    try:
        command = _command(argv[0])
        if command is None:
            raise contract.ContractError("unknown_command", f"unknown command '{argv[0][:40]}'")
        body = contract.envelope(result=command(contract.read_request(stdin)))
        code = 0
    except contract.ContractError as error:
        body = contract.envelope(error=error)
        code = 1
    except Exception:  # noqa: BLE001  BR-119: report, never crash without an answer
        traceback.print_exc(file=stderr)
        body = contract.envelope(error=contract.ContractError("internal", "the worker failed; see the service log"))
        code = 1
    stdout.write(contract.encode(body))
    stdout.flush()
    return code


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:], sys.stdin.buffer, sys.stdout.buffer, sys.stderr))
