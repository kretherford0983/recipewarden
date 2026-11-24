"""SEC-90: the worker never touches the network.

``install()`` runs first in ``__main__``, before any third-party module is imported, so nothing can open a
connection or resolve a name, even by accident (ingredient-parser would otherwise try to download an NLTK model).
``socket.socket`` is replaced with a subclass rather than a function, because libraries subclass it at import time.
"""

from __future__ import annotations

import socket
from typing import NoReturn


class NetworkBlockedError(OSError):
    """Raised by every network entry point once the guard is installed."""


_MESSAGE = "network access is disabled in the RecipeWarden worker (SEC-90)"
_installed = False


def _refuse(*_args: object, **_kwargs: object) -> NoReturn:
    raise NetworkBlockedError(_MESSAGE)


class GuardedSocket(socket.socket):
    """A socket that can be created (some libraries probe for one) but never connects or sends anywhere."""

    def connect(self, *_args: object, **_kwargs: object) -> NoReturn:  # type: ignore[override]
        _refuse()

    def connect_ex(self, *_args: object, **_kwargs: object) -> NoReturn:  # type: ignore[override]
        _refuse()

    def sendto(self, *_args: object, **_kwargs: object) -> NoReturn:  # type: ignore[override]
        _refuse()

    def sendmsg(self, *_args: object, **_kwargs: object) -> NoReturn:  # type: ignore[override]
        _refuse()


def install() -> None:
    """Make connect, name resolution and datagram sends fail. Safe to call more than once."""
    global _installed
    if _installed:
        return
    socket.socket = GuardedSocket  # type: ignore[misc]
    socket.create_connection = _refuse  # type: ignore[assignment]
    socket.getaddrinfo = _refuse  # type: ignore[assignment]
    socket.gethostbyname = _refuse  # type: ignore[assignment]
    socket.gethostbyname_ex = _refuse  # type: ignore[assignment]
    socket.gethostbyaddr = _refuse  # type: ignore[assignment]
    socket.socketpair = _refuse  # type: ignore[assignment]
    socket.fromfd = _refuse  # type: ignore[assignment]
    _installed = True


def is_installed() -> bool:
    return _installed
