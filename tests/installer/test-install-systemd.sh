#!/usr/bin/env bash
# Installer tests on a host with systemd (the GitHub Actions Ubuntu runner): service start, health, upgrade,
# automatic rollback (AC-92), reconfigure and uninstall. Run as root:
#   sudo bash tests/installer/test-install-systemd.sh dist/recipewarden-<version>-linux-x64.tar.gz
set -uo pipefail

here=$(cd "$(dirname "$0")" && pwd)
installer="$here/../../deploy/install.sh"
package="$1"
pass=0 fail=0
export RECIPEWARDEN_HEALTH_TIMEOUT=30

ok()   { pass=$((pass + 1)); printf 'ok   %s\n' "$1"; }
bad()  { fail=$((fail + 1)); printf 'FAIL %s\n' "$1"; [[ -z ${2:-} ]] || printf '%s\n' "$2"; }
check() { local name=$1; shift; if "$@"; then ok "$name"; else bad "$name"; fi; }
run() {
  local expected=$1 fragment=$2 name=$3; shift 4
  local out code
  out=$("$@" 2>&1); code=$?
  if [[ $code -eq $expected && ( -z $fragment || $out == *"$fragment"* ) ]]; then ok "$name"; else bad "$name (exit $code, expected $expected)" "$out"; fi
}
health() { [[ $(curl -fsS --max-time 2 "http://127.0.0.1:$1/health" 2>/dev/null) == ok ]]; }

[[ $EUID -eq 0 && -d /run/systemd/system ]] || { echo "run as root on a host with systemd"; exit 1; }
[[ ! -e /opt/recipewarden ]] || { echo "RecipeWarden is already installed here"; exit 1; }
version=$({ tar -tzf "$package" || true; } | head -1 | cut -d/ -f1); version=${version#recipewarden-}
pw=$(mktemp); echo "correct horse battery staple" > "$pw"

# ------------------------------------------------------------------ fresh install (FR-INST-01, AC-90)
run 0 "is installed" "fresh install starts the service" -- \
  bash "$installer" --package "$package" --yes --unattended --admin-email admin@example.com --admin-password-file "$pw"
check "service is active" systemctl is-active --quiet recipewarden
check "service starts at boot" systemctl is-enabled --quiet recipewarden
check "/health answers ok on 127.0.0.1:5080" health 5080
check "service runs as the recipewarden user" test "$(ps -o user= -p "$(systemctl show -p MainPID --value recipewarden)")" == recipewarden
check "home page shows the installed version" bash -c "curl -fsS http://127.0.0.1:5080/ | grep -q 'RecipeWarden $version<'"
check "CLI health: the database is sound" bash -c "recipewarden health | grep -q 'quick_check: ok'"
check "/health/ready answers" bash -c "curl -sS -o /dev/null -w '%{http_code}' http://127.0.0.1:5080/health/ready | grep -qE '^(200|503)$'"
run 1 "already exists" "the Admin account was created" -- bash -c "recipewarden admin create --email admin@example.com --password-stdin < $pw"
check "no world-readable settings" test "$(stat -c %a /etc/recipewarden/appsettings.Production.json)" == 640

# ------------------------------------------------------------------ CLI backup and restore while the service runs (AC-85)
archive=$(recipewarden backup 2>&1 | head -1)
check "FR-BKP-01: CLI backup made an archive" test -f "$archive"
newer_dir=$(mktemp -d)
tar -xzf "$archive" -C "$newer_dir"
sed -i 's/"appVersion": *"[^"]*"/"appVersion": "99.0.0"/' "$newer_dir/manifest.json"
mapfile -t parts < <(cd "$newer_dir" && ls)
tar -czf /root/rw-newer.tar.gz -C "$newer_dir" "${parts[@]}"
run 1 "Install version 99.0.0 or later" "AC-85/BR-65: an archive from a newer version is refused" -- recipewarden restore /root/rw-newer.tar.gz --yes
check "the service was not stopped for a refused archive" systemctl is-active --quiet recipewarden
run 0 "Restore complete" "AC-85: sudo recipewarden restore stops, restores, migrates and restarts" -- recipewarden restore "$archive" --yes
check "healthy after the CLI restore" health 5080
check "a pre-restore backup was made" bash -c "ls /var/lib/recipewarden/backups/backup-*.tar.gz | wc -l | grep -qv '^1$'"
run 1 "already exists" "the restored data still has the Admin" -- bash -c "recipewarden admin create --email admin@example.com --password-stdin < $pw"
rm -rf "$newer_dir" /root/rw-newer.tar.gz

# ------------------------------------------------------------------ upgrade (FR-INST-03)
first=$(readlink -f /opt/recipewarden/current)
sleep 1
run 0 "upgrading the existing installation" "upgrade in place" -- bash "$installer" --package "$package" --yes --unattended
check "new release active" test "$(readlink -f /opt/recipewarden/current)" != "$first"
check "healthy after upgrade" health 5080
check "pre-upgrade snapshot exists" compgen -G "/var/lib/recipewarden/backups/pre-upgrade-*/db/recipewarden.db"
check "FR-INST-03: backup archives are kept across the upgrade" compgen -G "/var/lib/recipewarden/backups/backup-*.tar.gz"

# ------------------------------------------------------------------ automatic rollback (AC-92)
good=$(readlink -f /opt/recipewarden/current)
broken_dir=$(mktemp -d)
tar -xzf "$package" -C "$broken_dir"
printf '#!/bin/sh\necho "simulated broken release" >&2\nexit 1\n' > "$broken_dir/recipewarden-$version/web/RecipeWarden.Web"
broken="$broken_dir/broken.tar.gz"
tar -czf "$broken" -C "$broken_dir" "recipewarden-$version"
sleep 1
run 4 "rolled back" "AC-92: a release that fails health is rolled back" -- bash "$installer" --package "$broken" --yes --unattended
check "the previous release is active again" test "$(readlink -f /opt/recipewarden/current)" == "$good"
check "and healthy" health 5080

# ------------------------------------------------------------------ reconfigure (FR-INST-02)
run 0 "network.json" "reconfigure moves the port" -- bash "$installer" --reconfigure --port 5090 --yes
check "healthy on the new port" health 5090
check "old port closed" bash -c '! curl -fsS -o /dev/null --max-time 2 http://127.0.0.1:5080/health'

# ------------------------------------------------------------------ LAN bind behind the host firewall (SEC-60)
# The installer and the CLI check health from this host against the bind address; the nftables rule must not drop
# them (dev.17 did: an upgrade on a LAN-bound test host failed its health check and rolled back).
lan_ip=$(ip -4 route get 1.1.1.1 2>/dev/null | sed -n 's/.* src \([0-9.]*\).*/\1/p')
command -v nft >/dev/null || apt-get install -y -qq nftables >/dev/null 2>&1 || true
lan_health() { [[ $(curl -fsS --max-time 2 "http://$lan_ip:5090/health" 2>/dev/null) == ok ]]; }
if [[ -n $lan_ip ]] && command -v nft >/dev/null && ! { ufw status 2>/dev/null | grep -q "Status: active"; }; then
  run 0 "network.json" "SEC-60: reconfigure to a LAN bind with a tunnel host" -- \
    bash "$installer" --reconfigure --bind "$lan_ip" --trusted-proxy 192.0.2.10 --yes
  # nft prints a one-address set without braces ("ip saddr != 192.0.2.10"), several with them.
  check "SEC-60: the nftables rule limits the port to the tunnel host" \
    bash -c "nft list table inet recipewarden | grep -Eq 'saddr != \\{? ?192\\.0\\.2\\.10 ?\\}? drop'"
  check "SEC-60: connections from this host (lo) are accepted before the drop" \
    bash -c "nft list table inet recipewarden | grep -q 'iif \"lo\" accept'"
  check "SEC-60: this host still reaches its own bind address" lan_health
  run 0 "upgrading the existing installation" "SEC-60: an upgrade on a LAN bind passes its health check" -- \
    bash "$installer" --package "$package" --yes --unattended
  check "SEC-60: healthy after the LAN-bind upgrade" lan_health
  run 0 "network.json" "back to loopback" -- bash "$installer" --reconfigure --bind 127.0.0.1 --yes
  check "SEC-60: a loopback bind removes the rule" bash -c '! nft list table inet recipewarden >/dev/null 2>&1'
else
  echo "skip SEC-60 LAN-bind tests: no LAN address or nft, or ufw is active"
fi

# ------------------------------------------------------------------ uninstall
run 0 "Kept:" "uninstall keeps data" -- bash "$installer" --uninstall
check "service removed" bash -c '! systemctl cat recipewarden >/dev/null 2>&1'
check "data kept" test -f /var/lib/recipewarden/db/recipewarden.db
run 0 "Removed the app, its data" "uninstall --purge removes everything" -- bash "$installer" --uninstall --purge
check "nothing left" bash -c '! ls -d /opt/recipewarden /etc/recipewarden /var/lib/recipewarden 2>/dev/null && ! id recipewarden 2>/dev/null'

echo "systemd installer tests: $pass passed, $fail failed"
if (( fail > 0 )); then journalctl -u recipewarden -n 60 --no-pager || true; fi
[[ $fail -eq 0 ]]
