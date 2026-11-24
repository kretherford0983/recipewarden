#!/usr/bin/env bash
# Installer tests for a clean Debian/Ubuntu container without systemd (section 9.8). Run as root:
#   docker run --rm -v "$PWD:/src:ro" -v "$PWD/dist:/dist:ro" debian:12 \
#     bash /src/tests/installer/test-install.sh /dist/recipewarden-<version>-linux-x64.tar.gz
# Without a package only the argument, OS and port checks run. With one, a full --no-start install, download
# and checksum checks, upgrade, reconfigure and uninstall are tested too. tests/installer/test-install-systemd.sh
# covers starting the service, health checks and rollback on a host with systemd.
set -uo pipefail

here=$(cd "$(dirname "$0")" && pwd)
installer="$here/../../deploy/install.sh"
package="${1:-}"
pass=0 fail=0
listener_pid=""

cleanup() { [[ -z $listener_pid ]] || kill "$listener_pid" 2>/dev/null || true; }
trap cleanup EXIT

ok()   { pass=$((pass + 1)); printf 'ok   %s\n' "$1"; }
bad()  { fail=$((fail + 1)); printf 'FAIL %s\n' "$1"; [[ -z ${2:-} ]] || printf '%s\n' "$2"; }
check() { local name=$1; shift; if "$@"; then ok "$name"; else bad "$name"; fi; }

# run <expected-exit> <expected-output-fragment|""> <description> -- <command...>
run() {
  local expected=$1 fragment=$2 name=$3; shift 4
  local out code
  out=$("$@" 2>&1); code=$?
  if [[ $code -eq $expected && ( -z $fragment || $out == *"$fragment"* ) ]]; then
    ok "$name"
  else
    bad "$name (exit $code, expected $expected)" "$out"
  fi
}

listen_on() {
  perl -MIO::Socket::INET -e '
    my $s = IO::Socket::INET->new(LocalAddr => "0.0.0.0", LocalPort => $ARGV[0], Listen => 5, ReuseAddr => 1) or die "listen: $!";
    sleep 300;' "$1" &
  listener_pid=$!
  for _ in $(seq 1 50); do
    grep -q ":$(printf '%04X' "$1") 00000000:0000 0A" /proc/net/tcp 2>/dev/null && return 0
    sleep 0.1
  done
  echo "listener on $1 did not start"; return 1
}

fake_os() { local f; f=$(mktemp); printf 'ID=%s\nVERSION_ID="%s"\n' "$1" "$2" >"$f"; echo "$f"; }
snapshot() { ls -d /etc/recipewarden /opt/recipewarden /var/lib/recipewarden /usr/local/bin/recipewarden 2>/dev/null; getent passwd recipewarden; true; }
mode_of() { stat -c '%a %U:%G' "$1" 2>/dev/null; }

[[ $EUID -eq 0 ]] || { echo "run these tests as root (inside a container)"; exit 1; }
[[ ! -e /opt/recipewarden ]] || { echo "RecipeWarden is already installed here; use a clean container"; exit 1; }
before=$(snapshot)

# ------------------------------------------------------------------ arguments
run 0 "Usage:" "--help prints usage" -- bash "$installer" --help
run 1 "unknown option" "unknown flag is rejected" -- bash "$installer" --bogus
run 1 "--port must be" "non-numeric port is rejected" -- bash "$installer" --port abc
run 1 "--port must be" "port 0 is rejected" -- bash "$installer" --port 0
run 1 "--port must be" "port 70000 is rejected" -- bash "$installer" --port 70000
run 1 "needs a value" "missing value is rejected" -- bash "$installer" --port
run 1 "--bind must be" "invalid bind address is rejected" -- bash "$installer" --bind 300.1.1.1
run 1 "--bind must be" "shell metacharacters are rejected" -- bash "$installer" --bind '127.0.0.1;id'
run 1 "not a valid address" "invalid allowed-ips is rejected" -- bash "$installer" --allowed-ips 10.0.0.0/40
run 1 "--version must look like" "malformed version is rejected" -- bash "$installer" --version 'latest;id'
run 1 "--channel must be" "unknown channel is rejected" -- bash "$installer" --channel beta
run 1 "--purge is only valid" "--purge needs --uninstall" -- bash "$installer" --purge
run 1 "choose only one" "two modes are rejected" -- bash "$installer" --reconfigure --uninstall
run 1 "absolute path" "relative data dir is rejected" -- bash "$installer" --data-dir data
# shellcheck disable=SC2016  # the literal $(id) is the point of the test
run 1 "absolute path" "data dir with \$ is rejected" -- bash "$installer" --data-dir '/srv/$(id)'
run 1 "not a valid email" "bad admin email is rejected" -- bash "$installer" --admin-email 'x;id'
run 1 "not a valid email" "admin email with # after the @ is rejected (AC-100)" -- bash "$installer" --admin-email 'first.last@#example.com'
run 1 "not a valid email" "admin email without a domain dot is rejected (AC-100)" -- bash "$installer" --admin-email 'name@example'
run 1 "not a valid email" "admin email with .. is rejected (AC-100)" -- bash "$installer" --admin-email 'na..me@example.com'
run 1 "file not found" "missing package is rejected" -- bash "$installer" --package /nope.tar.gz
run 1 "--restore: file not found" "--restore with a missing archive is refused" -- bash "$installer" --restore /tmp/no-such-backup.tar.gz
run 1 "--restore-kit needs --restore" "a recovery kit without --restore is refused" -- bash "$installer" --restore-kit /etc/hostname
run 1 "is for a new install" "--restore with --reconfigure is refused" -- bash "$installer" --reconfigure --restore /etc/hostname

# ------------------------------------------------------------------ root, OS, systemd, port
run 2 "run as root" "non-root is refused" -- setpriv --reuid=65534 --regid=65534 --clear-groups bash "$installer"
run 2 "unsupported OS" "Fedora is refused" -- env OS_RELEASE_FILE="$(fake_os fedora 40)" bash "$installer"
run 2 "unsupported OS" "Debian 11 is refused" -- env OS_RELEASE_FILE="$(fake_os debian 11)" bash "$installer"
run 2 "unsupported OS" "Ubuntu 25.10 (not LTS) is refused" -- env OS_RELEASE_FILE="$(fake_os ubuntu 25.10)" bash "$installer"
for v in "debian 12" "debian 13" "ubuntu 24.04" "ubuntu 26.04"; do
  # shellcheck disable=SC2086  # "id version" splits into fake_os's two arguments on purpose
  run 1 "--trusted-proxy is required" "NFR-02: $v passes the OS check" -- \
    env OS_RELEASE_FILE="$(fake_os $v)" bash "$installer" --no-start --yes --bind 192.168.1.15
done
run 1 "Ubuntu 22.04 is no longer supported" "ADR 0054: Ubuntu 22.04 warns" -- \
  env OS_RELEASE_FILE="$(fake_os ubuntu 22.04)" bash "$installer" --no-start --yes --bind 192.168.1.15
run 1 "--trusted-proxy is required" "ADR 0054: Ubuntu 22.04 still passes the OS check" -- \
  env OS_RELEASE_FILE="$(fake_os ubuntu 22.04)" bash "$installer" --no-start --yes --bind 192.168.1.15
if [[ ! -d /run/systemd/system ]]; then
  run 2 "systemd is not running" "a host without systemd needs --no-start" -- bash "$installer" --yes
fi
run 2 "not installed" "reconfigure needs an installation" -- bash "$installer" --reconfigure --no-start
run 1 "--trusted-proxy is required" "LAN bind needs a trusted proxy" -- bash "$installer" --no-start --yes --bind 192.168.1.15

port=5080
while grep -q ":$(printf '%04X' $port) 00000000:0000 0A" /proc/net/tcp; do port=$((port + 10)); done
listen_on "$port"
run 2 "already in use. Try --port $((port + 1))" "AC-91: port in use stops and suggests the next free port" -- \
  bash "$installer" --no-start --yes --port "$port"
after=$(snapshot)
check "AC-91: nothing changed by refused runs" test "$before" == "$after"

if [[ -z $package ]]; then
  echo "installer tests (no package given, preflight only): $pass passed, $fail failed"
  [[ $fail -eq 0 ]]; exit
fi

# ------------------------------------------------------------------ download path and checksums (file:// stands in for GitHub)
version=$({ tar -tzf "$package" || true; } | head -1 | cut -d/ -f1); version=${version#recipewarden-}
rel=$(mktemp -d); mkdir -p "$rel/v$version"
cp "$package" "$rel/v$version/"
(cd "$rel/v$version" && sha256sum -- *.tar.gz > SHA256SUMS.txt)
bad_rel=$(mktemp -d); mkdir -p "$bad_rel/v$version"
cp "$package" "$bad_rel/v$version/"
printf '%064d  %s\n' 0 "recipewarden-$version-linux-x64.tar.gz" > "$bad_rel/v$version/SHA256SUMS.txt"

run 4 "checksum mismatch" "a tampered checksum refuses the download" -- \
  env RECIPEWARDEN_DOWNLOAD="file://$bad_rel" bash "$installer" --no-start --yes --unattended --version "v$version" --port "$((port + 1))"
run 4 "download failed" "a missing release fails cleanly" -- \
  env RECIPEWARDEN_DOWNLOAD="file://$rel" bash "$installer" --no-start --yes --unattended --version v9.9.9 --port "$((port + 1))"
check "refused downloads changed nothing" test ! -e /opt/recipewarden

# ------------------------------------------------------------------ full install (downloaded, verified), no systemd
run 0 "is installed" "FR-INST-01: fresh install from a verified download" -- \
  env RECIPEWARDEN_DOWNLOAD="file://$rel" bash "$installer" --no-start --yes --unattended --version "v$version" \
    --bind 192.168.1.15 --trusted-proxy 192.168.1.20 --allowed-ips 10.0.0.0/24 --port "$((port + 1))" --backup-dir /srv/rw-backups

check "service user exists" id recipewarden
check "binaries are root-owned and current points at them" test "$(stat -c %U "$(readlink -f /opt/recipewarden/current)")" == root
check "web app is present" test -x /opt/recipewarden/current/web/RecipeWarden.Web
check "CLI wrapper on PATH" test -x /usr/local/bin/recipewarden
check "settings 0640 root:recipewarden" test "$(mode_of /etc/recipewarden/appsettings.Production.json)" == "640 root:recipewarden"
check "network.json 0640 root:recipewarden" test "$(mode_of /etc/recipewarden/network.json)" == "640 root:recipewarden"
check "data folder 0750 recipewarden" test "$(mode_of /var/lib/recipewarden)" == "750 recipewarden:recipewarden"
check "network.json has the bind address and port" grep -q "\"Url\": \"http://192.168.1.15:$((port + 1))\"" /etc/recipewarden/network.json
check "network.json has the trusted proxy" grep -q '"TrustedProxies": \[ "192.168.1.20" \]' /etc/recipewarden/network.json
check "network.json allows the LAN range and the host's own bind IP" grep -q '"AllowedSourceIps": \[ "10.0.0.0/24", "192.168.1.15" \]' /etc/recipewarden/network.json
check "backup folder added to Backup:AllowedFolders" grep -q '/srv/rw-backups' /etc/recipewarden/appsettings.Production.json
check "systemd unit installed" grep -q 'ExecStart=/opt/recipewarden/current/web/RecipeWarden.Web' /etc/systemd/system/recipewarden.service
check "apply-config wrote the backup drop-in" grep -q 'ReadWritePaths="-/srv/rw-backups"' /etc/systemd/system/recipewarden.service.d/backup-folders.conf
check "FR-BKP-12: the built-in backup folder has its marker" test -f /var/lib/recipewarden/backups/.recipewarden-backup-target
check "SEC-15: without an Admin a one-time /setup token was issued" test -f /var/lib/recipewarden/setup/token.json
run 0 "$version" "CLI reports the package version" -- /usr/local/bin/recipewarden --version

pwfile=$(mktemp); echo "correct horse battery staple" > "$pwfile"; chmod 0644 "$pwfile"
run 0 "created" "FR-INST-04: Admin account from the CLI as the service user" -- \
  /usr/local/bin/recipewarden admin create --email admin@example.com --password-stdin < "$pwfile"
check "database belongs to the service user" test "$(stat -c %U /var/lib/recipewarden/db/recipewarden.db)" == recipewarden
# The health command also judges this machine's disk (BR-81), so only the database line is asserted here.
run_health() { { /usr/local/bin/recipewarden health 2>&1 || true; } | grep -q "quick_check: ok"; }
check "CLI health: the database is sound" run_health
# ADR 0051: the bundled Python worker runs offline as the service user (AC-107) from a read-only release (SEC-94).
py_selftest() { /usr/local/bin/recipewarden python-selftest >/dev/null 2>&1; }
py_readonly() { local d; d="$(readlink -f /opt/recipewarden/current)/python"; [[ -x $d/bin/python3 && -z $(find "$d" \( ! -user root -o -perm /022 \) -print -quit) ]]; }
check "AC-107: python-selftest passes after install" py_selftest
check "SEC-94: the Python runtime is root-owned and not writable by others" py_readonly

# ------------------------------------------------------------------ upgrade
echo '{ "Data": { "Directory": "/var/lib/recipewarden" }, "Backup": { "AllowedFolders": [ "/var/lib/recipewarden/backups" ] }, "Mine": 1 }' \
  > /etc/recipewarden/appsettings.Production.json
first=$(readlink -f /opt/recipewarden/current)
# An older release's CLI has no "backup" command and answers "backup --help" with its general help and exit 0
# (System.CommandLine); the installer must notice and skip the full backup instead of running it and failing.
mv "$first/cli/recipewarden" "$first/cli/recipewarden.real"
cat > "$first/cli/recipewarden" <<'OLDCLI'
#!/usr/bin/env bash
for a in "$@"; do
  if [[ $a == --help ]]; then printf 'Commands:\n  admin         Account administration\n  health        Print a health summary\n'; exit 0; fi
done
echo "Unrecognized command or argument '$1'." >&2; exit 1
OLDCLI
chmod 0755 "$first/cli/recipewarden"
sleep 1
upgrade_out=$(bash "$installer" --no-start --yes --unattended --package "$package" 2>&1); upgrade_code=$?
mv -f "$first/cli/recipewarden.real" "$first/cli/recipewarden"
check "FR-INST-03: running again upgrades in place" test "$upgrade_code" -eq 0
skipped_backup() { [[ $upgrade_out == *"upgrading the existing installation"* && $upgrade_out != *"pre-upgrade backup"* && $upgrade_out != *Unrecognized* ]]; }
names_proxy() { [[ $upgrade_out == *"Only the tunnel host (192.168.1.20)"* ]]; }
check "an older CLI without backup is detected and skipped" skipped_backup
check "upgrade summary names this host's tunnel proxy" names_proxy
check "database snapshot taken by the older-CLI upgrade" compgen -G "/var/lib/recipewarden/backups/pre-upgrade-*/db/recipewarden.db"
[[ $upgrade_code -eq 0 ]] || printf '%s\n' "$upgrade_out"

second=$(readlink -f /opt/recipewarden/current)
sleep 1
run 0 "pre-upgrade backup" "FR-INST-03: a CLI with backup makes the full pre-upgrade backup" -- \
  bash "$installer" --no-start --yes --unattended --package "$package"
check "a new release folder is active" test "$(readlink -f /opt/recipewarden/current)" != "$second"
check "the previous release is kept" test -d "$first"
check "settings were not overwritten" grep -q '"Mine": 1' /etc/recipewarden/appsettings.Production.json
check "network settings kept" grep -q "http://192.168.1.15:$((port + 1))" /etc/recipewarden/network.json
check "database snapshot taken before upgrade" compgen -G "/var/lib/recipewarden/backups/pre-upgrade-*/db/recipewarden.db"
check "FR-INST-03: full pre-upgrade backup archive" compgen -G "/var/lib/recipewarden/backups/backup-*.tar.gz"
check "data still healthy after upgrade" run_health
check "AC-107: python-selftest passes after upgrade" py_selftest
check "SEC-94: the upgraded Python runtime is read-only" py_readonly

# ------------------------------------------------------------------ reconfigure
run 0 "network.json" "reconfigure changes the port and keeps the proxy" -- \
  bash "$installer" --no-start --reconfigure --port "$((port + 2))"
check "new port written" grep -q "http://192.168.1.15:$((port + 2))" /etc/recipewarden/network.json
check "proxy kept (and nothing else added)" grep -q '"TrustedProxies": \[ "192.168.1.20" \]' /etc/recipewarden/network.json
check "allowed range kept" grep -q '"10.0.0.0/24"' /etc/recipewarden/network.json
run 2 "already in use" "reconfigure refuses a busy port" -- bash "$installer" --no-start --reconfigure --port "$port"

# ------------------------------------------------------------------ uninstall
run 0 "Kept:" "uninstall keeps data and settings" -- bash "$installer" --no-start --uninstall
check "binaries removed" test ! -e /opt/recipewarden
check "wrapper removed" test ! -e /usr/local/bin/recipewarden
check "data kept" test -f /var/lib/recipewarden/db/recipewarden.db
check "settings kept" test -f /etc/recipewarden/appsettings.Production.json
run 0 "Removed the app, its data" "uninstall --purge removes everything" -- bash "$installer" --no-start --uninstall --purge
check "host is back to its starting state" test "$before" == "$(snapshot)"

# ------------------------------------------------------------------ loopback install: empty allowed list, reconfigure
run 0 "is installed" "loopback install with defaults" -- bash "$installer" --no-start --yes --unattended --package "$package" --port "$((port + 3))"
check "loopback: proxy defaults to the bind IP" grep -q '"TrustedProxies": \[ "127.0.0.1" \]' /etc/recipewarden/network.json
check "loopback: no extra allowed addresses" grep -q '"AllowedSourceIps": \[  \]' /etc/recipewarden/network.json
run 0 "network.json" "reconfigure works with an empty allowed list" -- bash "$installer" --no-start --reconfigure --port "$((port + 4))"
check "loopback: new port written" grep -q "http://127.0.0.1:$((port + 4))" /etc/recipewarden/network.json

# ------------------------------------------------------------------ AC-83: move to a new host with --restore
run 0 "created" "an Admin on host A" -- /usr/local/bin/recipewarden admin create --email mover@example.com --password-stdin < "$pwfile"
archive=$(/usr/local/bin/recipewarden backup 2>&1 | head -1)
check "FR-BKP-01: CLI backup prints the archive path" test -f "$archive"
cp "$archive" /root/rw-move.tar.gz
run 0 "Removed the app, its data" "host A is wiped" -- bash "$installer" --no-start --uninstall --purge
run 0 "is installed" "AC-83: install --restore on a clean host" -- \
  bash "$installer" --no-start --yes --unattended --package "$package" --port "$((port + 5))" --restore /root/rw-move.tar.gz
run 1 "already exists" "AC-83: the Admin came from the backup" -- /usr/local/bin/recipewarden admin create --email mover@example.com --password-stdin < "$pwfile"
check "AC-83: the new host's port is used, not host A's" grep -q "http://127.0.0.1:$((port + 5))" /etc/recipewarden/network.json
check "BR-66: /setup is disabled after a restore" test -f /var/lib/recipewarden/setup/complete
run 1 "already installed" "--restore on an installed host points to the CLI" -- \
  bash "$installer" --no-start --yes --unattended --package "$package" --restore /root/rw-move.tar.gz
bad_archive=$(mktemp --suffix=.tar.gz); echo "not a backup" > "$bad_archive"
run 0 "Removed the app, its data" "clean up" -- bash "$installer" --no-start --uninstall --purge
run 4 "the backup was refused" "AC-83: a damaged archive is refused" -- \
  bash "$installer" --no-start --yes --unattended --package "$package" --port "$((port + 6))" --restore "$bad_archive"
run 0 "Removed the app, its data" "clean up" -- bash "$installer" --no-start --uninstall --purge
rm -f /root/rw-move.tar.gz "$bad_archive"

echo "installer tests: $pass passed, $fail failed"
[[ $fail -eq 0 ]]
