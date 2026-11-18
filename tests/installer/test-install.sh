#!/usr/bin/env bash
# Installer container tests (section 9.8). Run as root inside a clean Debian/Ubuntu container:
#   docker run --rm -v "$PWD:/src:ro" debian:12 bash /src/tests/installer/test-install.sh
# Covers M0 scope: argument parsing, OS check, port check (AC-91) and "nothing changed".
set -uo pipefail

here=$(cd "$(dirname "$0")" && pwd)
installer="$here/../../deploy/install.sh"
pass=0 fail=0
listener_pid=""

cleanup() { [[ -z $listener_pid ]] || kill "$listener_pid" 2>/dev/null || true; }
trap cleanup EXIT

# run <expected-exit> <expected-output-fragment|""> <description> -- <command...>
run() {
  local expected=$1 fragment=$2 name=$3; shift 4
  local out code
  out=$("$@" 2>&1); code=$?
  if [[ $code -eq $expected && ( -z $fragment || $out == *"$fragment"* ) ]]; then
    pass=$((pass + 1)); printf 'ok   %s\n' "$name"
  else
    fail=$((fail + 1)); printf 'FAIL %s (exit %s, expected %s)\n%s\n' "$name" "$code" "$expected" "$out"
  fi
}

listen_on() {
  perl -MIO::Socket::INET -e '
    my $s = IO::Socket::INET->new(LocalAddr => "0.0.0.0", LocalPort => $ARGV[0], Listen => 5, ReuseAddr => 1) or die "listen: $!";
    sleep 120;' "$1" &
  listener_pid=$!
  for _ in $(seq 1 50); do
    grep -q ":$(printf '%04X' "$1") 00000000:0000 0A" /proc/net/tcp 2>/dev/null && return 0
    sleep 0.1
  done
  echo "listener on $1 did not start"; return 1
}

fake_os() { local f; f=$(mktemp); printf 'ID=%s\nVERSION_ID="%s"\n' "$1" "$2" >"$f"; echo "$f"; }

snapshot() { ls -d /etc/recipewarden /opt/recipewarden /var/lib/recipewarden /etc/systemd/system/recipewarden.service 2>/dev/null; getent passwd recipewarden; true; }

[[ $EUID -eq 0 ]] || { echo "run these tests as root (inside a container)"; exit 1; }
before=$(snapshot)

# Arguments
run 0 "Usage:" "--help prints usage" -- bash "$installer" --help
run 1 "unknown option" "unknown flag is rejected" -- bash "$installer" --bogus
run 1 "--port must be" "non-numeric port is rejected" -- bash "$installer" --port abc
run 1 "--port must be" "port 0 is rejected" -- bash "$installer" --port 0
run 1 "--port must be" "port 70000 is rejected" -- bash "$installer" --port 70000
run 1 "needs a value" "missing value is rejected" -- bash "$installer" --port
run 1 "--bind must be" "invalid bind address is rejected" -- bash "$installer" --bind 300.1.1.1
run 1 "--bind must be" "shell metacharacters are rejected" -- bash "$installer" --bind '127.0.0.1;id'
run 1 "--trusted-proxy is required" "LAN bind needs a trusted proxy" -- bash "$installer" --bind 192.168.1.15
run 1 "not a valid address" "invalid allowed-ips is rejected" -- bash "$installer" --allowed-ips 10.0.0.0/40
run 1 "--purge is only valid" "--purge needs --uninstall" -- bash "$installer" --purge
run 1 "choose only one" "two modes are rejected" -- bash "$installer" --upgrade --uninstall
run 1 "absolute path" "relative data dir is rejected" -- bash "$installer" --data-dir data
run 1 "file not found" "restore archive must exist" -- bash "$installer" --restore /nope.tar.gz

# Root and OS
run 2 "run as root" "non-root is refused" -- setpriv --reuid=65534 --regid=65534 --clear-groups bash "$installer"
run 2 "unsupported OS" "Fedora is refused" -- env OS_RELEASE_FILE="$(fake_os fedora 40)" bash "$installer"
run 2 "unsupported OS" "Debian 11 is refused" -- env OS_RELEASE_FILE="$(fake_os debian 11)" bash "$installer"
run 3 "Preflight passed" "Ubuntu 22.04 is accepted" -- env OS_RELEASE_FILE="$(fake_os ubuntu 22.04)" bash "$installer" --port 5180

# Port check (AC-91)
port=5080
while grep -q ":$(printf '%04X' $port) 00000000:0000 0A" /proc/net/tcp; do port=$((port + 10)); done
listen_on "$port"
run 2 "already in use. Try --port $((port + 1))" "AC-91: port in use stops and suggests the next free port" -- bash "$installer" --port "$port"
run 3 "Port: $((port + 1)) is free" "suggested port passes" -- bash "$installer" --port "$((port + 1))"
run 3 "trusted-proxy=192.168.1.20" "LAN bind with tunnel container passes preflight" -- \
  bash "$installer" --bind 192.168.1.15 --port "$((port + 1))" --trusted-proxy 192.168.1.20 --allowed-ips 10.0.0.0/24,10.0.1.5

# Nothing changed (AC-91: "before changing anything")
after=$(snapshot)
if [[ "$before" == "$after" ]]; then pass=$((pass + 1)); echo "ok   host is unchanged"; else fail=$((fail + 1)); echo "FAIL host changed: $after"; fi

echo "installer tests: $pass passed, $fail failed"
[[ $fail -eq 0 ]]
