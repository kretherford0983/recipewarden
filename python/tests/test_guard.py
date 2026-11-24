import socket

import pytest

from rw_worker import guard


@pytest.mark.req("SEC-90")
def test_every_network_entry_point_is_refused():
    guard.install()
    guard.install()  # idempotent
    assert guard.is_installed()
    for call in (
        lambda: socket.create_connection(("127.0.0.1", 9)),
        lambda: socket.getaddrinfo("example.com", 443),
        lambda: socket.gethostbyname("example.com"),
        lambda: socket.gethostbyname_ex("example.com"),
        lambda: socket.gethostbyaddr("127.0.0.1"),
        lambda: socket.socketpair(),
        lambda: socket.fromfd(0, socket.AF_INET, socket.SOCK_STREAM),
    ):
        with pytest.raises(guard.NetworkBlockedError):
            call()


@pytest.mark.req("SEC-90")
def test_a_socket_can_be_made_but_never_connects_or_sends():
    guard.install()
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        assert isinstance(s, guard.GuardedSocket)
        with pytest.raises(guard.NetworkBlockedError):
            s.connect(("127.0.0.1", 9))
        with pytest.raises(guard.NetworkBlockedError):
            s.connect_ex(("127.0.0.1", 9))
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as u:
        with pytest.raises(guard.NetworkBlockedError):
            u.sendto(b"x", ("127.0.0.1", 53))
        with pytest.raises(guard.NetworkBlockedError):
            u.sendmsg([b"x"], [], 0, ("127.0.0.1", 53))


@pytest.mark.req("AC-106")
def test_libraries_cannot_reach_the_network_inside_the_worker(req):
    # urllib (used by nltk.download and requests' stack) goes through the guarded socket module.
    from conftest import run_process

    code, body = run_process("selftest", req({}))
    assert code == 0
    assert body["result"]["checks"]["network_blocked"] is True
