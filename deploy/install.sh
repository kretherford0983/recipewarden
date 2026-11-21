#!/usr/bin/env bash
# RecipeWarden one-command install, upgrade and uninstall for Linux servers (FR-INST-01..04, section 9.5, ADR 0007).
#
#   Newest release (production):
#     curl -fsSL https://github.com/kretherford0983/recipewarden/releases/latest/download/install.sh | sudo bash
#   A specific build (e.g. a development build on the test host):
#     curl -fsSL https://github.com/kretherford0983/recipewarden/releases/download/<tag>/install.sh | sudo bash -s -- --version <tag>
#   Prefer to read it first?  curl -fsSLO <url>/install.sh ; less install.sh ; sudo bash install.sh [options]
#
# The same command upgrades an existing installation (data snapshot first, previous release kept, automatic
# rollback if the new release fails its health check). Only bash, curl, tar, gzip and sha256sum are used (ADR 0005).
#
# Files:  /opt/recipewarden/releases/<version>-<time>/   binaries (3 newest kept)   /opt/recipewarden/current -> active
#         /etc/recipewarden/appsettings.Production.json  your settings (written once, never overwritten)
#         /etc/recipewarden/network.json                 bind address, port, trusted proxy, allowed IPs
#                                                        (written by this installer; edit it, then restart the service)
#         /var/lib/recipewarden/                         database, images, keys, backups
#         /var/log/recipewarden/                         rolling log files (also: journalctl -u recipewarden)
#
# SEC-24: user input is validated and never evaluated or interpolated into a command string.
set -euo pipefail
umask 022

readonly APP=recipewarden
readonly REPO="${RECIPEWARDEN_REPO:-kretherford0983/recipewarden}"
readonly API="${RECIPEWARDEN_API:-https://api.github.com/repos/$REPO}"
readonly DOWNLOAD="${RECIPEWARDEN_DOWNLOAD:-https://github.com/$REPO/releases/download}"
readonly OPT=/opt/$APP ETC=/etc/$APP DATA_DEFAULT=/var/lib/$APP LOG=/var/log/$APP
readonly UNIT=/etc/systemd/system/$APP.service DROPIN_DIR=/etc/systemd/system/$APP.service.d
readonly CMD=/usr/local/bin/$APP
readonly EXIT_OK=0 EXIT_USAGE=1 EXIT_PREFLIGHT=2 EXIT_NOT_IMPLEMENTED=3 EXIT_FAILED=4
# Test hook: lets the container tests simulate other distributions. Read, never executed.
readonly OS_RELEASE_FILE="${OS_RELEASE_FILE:-/etc/os-release}"
# Seconds to wait for /health after a start (tests shorten it).
readonly HEALTH_TIMEOUT="${RECIPEWARDEN_HEALTH_TIMEOUT:-60}"

mode=install
tag=""; channel=auto; package=""
port=""; bind=""; trusted_proxy=""; allowed_ips=""
data_dir=""; backup_dir=""; restore=""
admin_email=""; admin_password_file=""
yes=0; unattended=0; no_start=0; purge=0; framework_dependent=0

log()  { printf '%s\n' "$*"; }
step() { printf '==> %s\n' "$*"; }
warn() { printf 'warning: %s\n' "$*" >&2; }
die()  { local code=$1; shift; printf 'error: %s\n' "$*" >&2; exit "$code"; }

usage() {
  cat <<USAGE
Usage: curl -fsSL <url>/install.sh | sudo bash -s -- [options]
       sudo bash install.sh [options]

Which build:
  --version <tag>            Install exactly this release, e.g. v0.1.0 or v0.1.0-dev.12
  --channel auto|release|dev Without --version: auto = newest release, or the newest development build while
                             there is no release yet (default); release = releases only; dev = development builds
  --package <file.tar.gz>    Install a package already on this host (no download)

Network (new install or --reconfigure; written to $ETC/network.json):
  --port <n>                 Listening port (default 5080)
  --bind <ipv4>              127.0.0.1 when cloudflared runs on this host (default); this host's LAN IP when
                             cloudflared runs in its own container/VM; 0.0.0.0 not recommended
  --trusted-proxy <ip>[,..]  The cloudflared host(s). Required for a LAN bind; defaults to the bind IP on loopback
  --allowed-ips <ip|cidr>[,..]  Extra addresses that may connect directly (e.g. your LAN while testing)

First install:
  --admin-email <email>      Create the first Admin account (asks for the password; or --admin-password-file)
  --admin-password-file <f>  Read the Admin password from a file (unattended installs)
  --data-dir <path>          Data directory (default $DATA_DEFAULT)
  --backup-dir <path>        Also allow scheduled backups to this folder (Backup:AllowedFolders)

Other:
  --reconfigure              Change port, bind, trusted proxy or allowed IPs of an existing install
  --uninstall [--purge]      Remove the app; --purge also deletes data, settings, logs and the service user
  --yes                      Don't wait 5 seconds before changing anything
  --unattended               Never prompt (implies --yes)
  --no-start                 Install everything but don't touch systemd (containers and tests)
  -h, --help                 Show this help
USAGE
}

need_value() { [[ $# -ge 2 && -n "$2" && "$2" != --* ]] || die $EXIT_USAGE "$1 needs a value"; }

is_ipv4() {
  local ip=$1 IFS=. octets o
  [[ $ip =~ ^[0-9]{1,3}(\.[0-9]{1,3}){3}$ ]] || return 1
  read -r -a octets <<<"$ip"
  for o in "${octets[@]}"; do (( 10#$o <= 255 )) || return 1; done
}

# shellcheck disable=SC2317  # called indirectly through validate_list
is_ipv4_or_cidr() {
  local value=$1
  if [[ $value == */* ]]; then
    local prefix=${value#*/}
    [[ $prefix =~ ^[0-9]{1,2}$ ]] && (( 10#$prefix <= 32 )) && is_ipv4 "${value%/*}"
  else
    is_ipv4 "$value"
  fi
}

validate_list() {
  local flag=$1 list=$2 checker=$3 item
  local IFS=,
  for item in $list; do
    $checker "$item" || die $EXIT_USAGE "$flag: '$item' is not a valid address"
  done
}

is_safe_path() { [[ $1 == /* && $1 != *..* && ! $1 =~ [[:cntrl:]\"\\%\$\`\'] ]]; }

set_mode() {
  [[ $mode == install ]] || die $EXIT_USAGE "choose only one of --reconfigure, --uninstall"
  mode=$1
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --version)             need_value "$@"; tag=$2; shift 2 ;;
      --channel)             need_value "$@"; channel=$2; shift 2 ;;
      --package)             need_value "$@"; package=$2; shift 2 ;;
      --port)                need_value "$@"; port=$2; shift 2 ;;
      --bind)                need_value "$@"; bind=$2; shift 2 ;;
      --trusted-proxy)       need_value "$@"; trusted_proxy=$2; shift 2 ;;
      --allowed-ips)         need_value "$@"; allowed_ips=$2; shift 2 ;;
      --data-dir)            need_value "$@"; data_dir=$2; shift 2 ;;
      --backup-dir)          need_value "$@"; backup_dir=$2; shift 2 ;;
      --admin-email)         need_value "$@"; admin_email=$2; shift 2 ;;
      --admin-password-file) need_value "$@"; admin_password_file=$2; shift 2 ;;
      --restore)             need_value "$@"; restore=$2; shift 2 ;;
      --upgrade)             shift ;;   # accepted for compatibility: an existing install is always upgraded
      --reconfigure)         set_mode reconfigure; shift ;;
      --uninstall)           set_mode uninstall; shift ;;
      --purge)               purge=1; shift ;;
      --framework-dependent) framework_dependent=1; shift ;;
      --yes|-y)              yes=1; shift ;;
      --unattended)          unattended=1; yes=1; shift ;;
      --no-start)            no_start=1; shift ;;
      -h|--help)             usage; exit $EXIT_OK ;;
      *)                     usage >&2; die $EXIT_USAGE "unknown option: $1" ;;
    esac
  done

  [[ -z $tag || $tag =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-[a-z]+\.[0-9]+)?$ ]] \
    || die $EXIT_USAGE "--version must look like v0.1.0 or v0.1.0-dev.12"
  [[ $channel =~ ^(auto|release|dev)$ ]] || die $EXIT_USAGE "--channel must be auto, release or dev"
  [[ -z $package || -f $package ]] || die $EXIT_USAGE "--package: file not found: $package"
  [[ -z $package || -z $tag ]] || die $EXIT_USAGE "use either --package or --version, not both"
  if [[ -n $port ]]; then
    if ! [[ $port =~ ^[0-9]{1,5}$ ]] || (( 10#$port < 1 || 10#$port > 65535 )); then
      die $EXIT_USAGE "--port must be 1-65535"
    fi
    port=$((10#$port))
  fi
  [[ -z $bind ]] || is_ipv4 "$bind" || die $EXIT_USAGE "--bind must be an IPv4 address"
  [[ -z $trusted_proxy ]] || validate_list --trusted-proxy "$trusted_proxy" is_ipv4
  [[ -z $allowed_ips ]] || validate_list --allowed-ips "$allowed_ips" is_ipv4_or_cidr
  [[ -z $data_dir ]] || is_safe_path "$data_dir" || die $EXIT_USAGE "--data-dir must be an absolute path without special characters"
  [[ -z $backup_dir ]] || is_safe_path "$backup_dir" || die $EXIT_USAGE "--backup-dir must be an absolute path without special characters"
  [[ -z $admin_email ]] || valid_email "$admin_email" \
    || die $EXIT_USAGE "--admin-email is not a valid email address (letters, digits and . _ + - before the @; a domain such as example.com after it)"
  [[ -z $admin_password_file || -r $admin_password_file ]] || die $EXIT_USAGE "--admin-password-file: cannot read $admin_password_file"
  (( purge == 0 )) || [[ $mode == uninstall ]] || die $EXIT_USAGE "--purge is only valid with --uninstall"
  (( framework_dependent == 0 )) || die $EXIT_NOT_IMPLEMENTED "--framework-dependent is not available yet (self-contained builds only, D-05)"
  [[ -z $restore ]] || die $EXIT_NOT_IMPLEMENTED "--restore arrives with backups in milestone M7"
}

require_root() {
  [[ ${EUID:-$(id -u)} -eq 0 ]] || die $EXIT_PREFLIGHT "run as root, e.g.  curl -fsSL <url>/install.sh | sudo bash"
}

check_os() {
  [[ -r $OS_RELEASE_FILE ]] || die $EXIT_PREFLIGHT "cannot read $OS_RELEASE_FILE; unsupported OS"
  local id version
  id=$(sed -n 's/^ID=//p' "$OS_RELEASE_FILE" | tr -d '"')
  version=$(sed -n 's/^VERSION_ID=//p' "$OS_RELEASE_FILE" | tr -d '"')
  case "$id:$version" in
    debian:12|debian:13|ubuntu:22.04|ubuntu:24.04|ubuntu:26.04) log "OS: $id $version (supported)" ;;
    *) die $EXIT_PREFLIGHT "unsupported OS '$id $version'. Supported: Debian 12/13, Ubuntu 22.04/24.04/26.04 LTS (NFR-02)" ;;
  esac
  [[ $(uname -m) == x86_64 ]] || die $EXIT_PREFLIGHT "architecture $(uname -m) is not supported yet (x86_64 only in v1)"
}

check_tools() {
  local c
  for c in curl tar gzip sha256sum sed awk; do
    command -v "$c" >/dev/null || die $EXIT_PREFLIGHT "'$c' is required (apt-get install -y $c)"
  done
  if (( no_start == 0 )); then
    [[ -d /run/systemd/system ]] || die $EXIT_PREFLIGHT "systemd is not running on this host (use --no-start inside a container)"
  fi
}

# Listening TCP ports from /proc (no dependency on ss/netstat, which minimal images lack).
listening_ports() {
  local f hex
  for f in /proc/net/tcp /proc/net/tcp6; do
    [[ -r $f ]] || continue
    # Column 2 is local_address as HEXIP:HEXPORT; state 0A is LISTEN.
    awk 'NR > 1 && $4 == "0A" { n = split($2, a, ":"); print a[n] }' "$f"
  done | while read -r hex; do echo $((16#$hex)); done | sort -un
}

port_in_use() { listening_ports | grep -qx "$1"; }

next_free_port() {
  local candidate=$(( $1 + 1 )) used
  used=$(listening_ports)
  while (( candidate <= 65535 )); do
    grep -qx "$candidate" <<<"$used" || { echo "$candidate"; return 0; }
    candidate=$((candidate + 1))
  done
  return 1
}

check_port() {
  if port_in_use "$port"; then
    local suggestion
    suggestion=$(next_free_port "$port") || suggestion="(none found)"
    die $EXIT_PREFLIGHT "port $port is already in use. Try --port $suggestion"
  fi
  log "Port: $port is free"
}

installed() { [[ -L $OPT/current && -f $ETC/network.json ]]; }

# Addresses in one list of network.json (written by this script with each list on one line), one per line.
list_from_network() {
  { grep "\"$1\"" "$ETC/network.json" || true; } | { grep -oE '[0-9]+(\.[0-9]+){3}(/[0-9]+)?' || true; }
}

current_url() {
  local url
  url=$(sed -n 's/.*"Url": *"\([^"]*\)".*/\1/p' "$ETC/network.json" | head -1)
  echo "${url/0.0.0.0/127.0.0.1}"
}

# ---------------------------------------------------------------------------------------------- downloads
api() { curl -fsSL --retry 3 -H "Accept: application/vnd.github+json" -H "User-Agent: recipewarden-install" "$API/$1"; }

# One "tag_name/draft/prerelease value" line per field, whether the API answers pretty-printed or on one line.
release_fields() { grep -oE '"(tag_name|draft|prerelease)": *("[^"]*"|true|false)' | sed -E 's/^"([a-z_]+)": *"?([^"]*)"?$/\1 \2/'; }

latest_release() {
  local t
  t=$(api releases/latest 2>/dev/null | release_fields | awk '$1 == "tag_name" { print $2; exit }') || true
  [[ $t =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]] && echo "$t"
}

latest_dev() {
  local t
  t=$(api "releases?per_page=30" | release_fields | awk '
    $1 == "tag_name" { tag = $2; draft = "" }
    $1 == "draft" { draft = $2 }
    $1 == "prerelease" { if ($2 == "true" && draft == "false" && tag ~ /^v[0-9]+\.[0-9]+\.[0-9]+-dev\.[0-9]+$/) { print tag; exit } }') || true
  [[ -n $t ]] && echo "$t"
}

resolve_tag() {
  [[ -z $tag && -z $package ]] || return 0
  case "$channel" in
    release) tag=$(latest_release) || die $EXIT_FAILED "no release found on github.com/$REPO" ;;
    dev)     tag=$(latest_dev) || die $EXIT_FAILED "no development build found on github.com/$REPO" ;;
    auto)
      if ! tag=$(latest_release); then
        tag=$(latest_dev) || die $EXIT_FAILED "no release or development build found on github.com/$REPO"
        warn "there is no release yet: installing the newest DEVELOPMENT build $tag (use --channel release to refuse)"
      fi ;;
  esac
}

fetch() {  # download, refusing empty files and HTML error pages
  curl -fsSL --retry 3 -o "$2" "$1" || die $EXIT_FAILED "download failed: $1"
  [[ -s $2 ]] || die $EXIT_FAILED "empty download: $1"
  if head -c 512 "$2" | grep -qi '<!doctype html\|<html'; then die $EXIT_FAILED "got a web page instead of a file: $1"; fi
}

download_package() {
  if [[ -n $package ]]; then
    gzip -t "$package" 2>/dev/null || die $EXIT_FAILED "$package is not a valid .tar.gz"
    return 0
  fi
  local version=${tag#v} name
  name="$APP-$version-linux-x64.tar.gz"
  step "downloading RecipeWarden $tag from github.com/$REPO"
  fetch "$DOWNLOAD/$tag/SHA256SUMS.txt" "$work/SHA256SUMS.txt"
  fetch "$DOWNLOAD/$tag/$name" "$work/$name"
  step "verifying checksum"
  grep -E "^[0-9a-f]{64}  \*?$name\$" "$work/SHA256SUMS.txt" > "$work/$name.sum" \
    || die $EXIT_FAILED "$name is not listed in SHA256SUMS.txt"
  (cd "$work" && sha256sum -c --quiet "$name.sum") || die $EXIT_FAILED "checksum mismatch for $name - download refused"
  gzip -t "$work/$name" 2>/dev/null || die $EXIT_FAILED "the package is not a valid .tar.gz"
  package="$work/$name"
}

# ---------------------------------------------------------------------------------------------- install steps
install_dependencies() {
  step "checking system packages"
  local need=() icu
  command -v update-ca-certificates >/dev/null || need+=(ca-certificates)
  if ! ldconfig -p 2>/dev/null | grep -q 'libicuuc\.so'; then
    icu=$(apt-cache search --names-only '^libicu[0-9]+$' 2>/dev/null | awk '{print $1}' | sort -V | tail -1)
    if [[ -z $icu ]]; then
      apt-get update -qq
      icu=$(apt-cache search --names-only '^libicu[0-9]+$' | awk '{print $1}' | sort -V | tail -1)
    fi
    [[ -n $icu ]] || die $EXIT_FAILED "could not find the ICU library package (libicuNN)"
    need+=("$icu")
  fi
  if (( ${#need[@]} > 0 )); then
    log "installing: ${need[*]}"
    DEBIAN_FRONTEND=noninteractive apt-get install -y -qq "${need[@]}" >/dev/null 2>&1 || {
      apt-get update -qq
      DEBIAN_FRONTEND=noninteractive apt-get install -y -qq "${need[@]}" >/dev/null
    }
  fi
}

create_user_and_folders() {
  if ! id "$APP" >/dev/null 2>&1; then
    step "creating service user $APP"
    useradd --system --user-group --home-dir "$data" --no-create-home --shell /usr/sbin/nologin "$APP"
  fi
  install -d -m 0755 "$OPT" "$OPT/releases"
  install -d -m 0750 -o root -g "$APP" "$ETC"
  install -d -m 0750 -o "$APP" -g "$APP" "$data" "$data/backups" "$LOG"
}

unpack_release() {
  local version
  version=$({ tar -tzf "$package" || true; } | head -1 | cut -d/ -f1)   # head closes the pipe early; that is fine
  version=${version#"$APP-"}
  [[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[a-z]+\.[0-9]+)?$ ]] || die $EXIT_FAILED "unexpected package layout ($version)"
  release="$OPT/releases/$version-$(date +%Y%m%d-%H%M%S)"
  step "unpacking $version to $release"
  install -d -m 0755 "$release"
  tar -xzf "$package" -C "$release" --strip-components=1 --no-same-owner
  chown -R root:root "$release"; chmod -R go-w "$release"
  "$release/cli/$APP" --version >/dev/null || die $EXIT_FAILED "the new release does not run on this host"
  new_version=$version
}

write_settings() {
  local cfg="$ETC/appsettings.Production.json" folders
  if [[ ! -f $cfg ]]; then
    step "writing $cfg"
    folders="\"$data/backups\""
    [[ -z $backup_dir ]] || folders="$folders, \"$backup_dir\""
    cat > "$cfg" <<JSON
{
  "Data": { "Directory": "$data" },
  "Backup": { "AllowedFolders": [ $folders ] }
}
JSON
    chown root:"$APP" "$cfg"; chmod 0640 "$cfg"
  fi
}

write_network() {
  local proxies="" allowed="" ip IFS=,
  for ip in $trusted_proxy; do proxies+="${proxies:+, }\"$ip\""; done
  # The host's own bind address may always connect (installer and CLI health checks).
  for ip in ${allowed_ips:+$allowed_ips,}$bind; do
    [[ $ip == 127.* || $ip == 0.0.0.0 ]] && continue
    [[ $allowed == *"\"$ip\""* ]] || allowed+="${allowed:+, }\"$ip\""
  done
  unset IFS
  step "writing $ETC/network.json (bind $bind, port $port)"
  cat > "$ETC/network.json.tmp" <<JSON
{
  "_comment": "Written by install.sh. Change these values here (or run install.sh --reconfigure), then: sudo systemctl restart $APP",
  "Kestrel": { "Endpoints": { "Http": { "Url": "http://$bind:$port" } } },
  "Network": {
    "TrustedProxies": [ $proxies ],
    "AllowedSourceIps": [ $allowed ]
  }
}
JSON
  chown root:"$APP" "$ETC/network.json.tmp"; chmod 0640 "$ETC/network.json.tmp"
  mv -f "$ETC/network.json.tmp" "$ETC/network.json"
}

write_command_wrapper() {
  if [[ -e $CMD ]] && ! grep -q "RecipeWarden host-command wrapper" "$CMD" 2>/dev/null; then
    warn "$CMD exists and was not written by this installer; left unchanged (use $OPT/current/cli/$APP)"
    return 0
  fi
  cat > "$CMD.tmp" <<'WRAP'
#!/bin/sh
# RecipeWarden host-command wrapper (written by install.sh). Run with sudo, e.g.:
#   sudo recipewarden health          sudo recipewarden admin create --email you@example.com
#   sudo recipewarden apply-config    (after changing Backup:AllowedFolders)
export RECIPEWARDEN_CONFIG="${RECIPEWARDEN_CONFIG:-/etc/recipewarden/appsettings.Production.json}"
cli=/opt/recipewarden/current/cli/recipewarden
# apply-config writes systemd files and needs root; everything else runs as the service user so new files belong to it.
if [ "$(id -u)" = 0 ] && [ "${1:-}" != apply-config ]; then
  exec runuser -u recipewarden -- env RECIPEWARDEN_CONFIG="$RECIPEWARDEN_CONFIG" "$cli" "$@"
fi
exec "$cli" "$@"
WRAP
  chmod 0755 "$CMD.tmp"; mv -f "$CMD.tmp" "$CMD"
}

install_unit() {
  install -m 0644 "$OPT/current/$APP.service" "$UNIT"
  install -d -m 0755 "$DROPIN_DIR"
  if [[ $data != "$DATA_DEFAULT" ]]; then
    printf '# Written by install.sh (--data-dir)\n[Service]\nReadWritePaths=%s\n' "$data" > "$DROPIN_DIR/data-dir.conf"
  fi
  local reload=--no-restart
  (( no_start == 0 )) || reload=--no-reload
  "$CMD" apply-config "$reload" >/dev/null || die $EXIT_FAILED "apply-config failed: check Backup:AllowedFolders in $ETC/appsettings.Production.json"
}

wait_healthy() {
  local url
  url="$(current_url)/health"
  printf '==> waiting for %s ' "$url"
  for _ in $(seq 1 "$HEALTH_TIMEOUT"); do
    if [[ $(curl -fsS --max-time 2 "$url" 2>/dev/null) == ok ]]; then echo "OK"; return 0; fi
    printf '.'; sleep 1
  done
  echo " FAILED"
  return 1
}

start_service() {
  (( no_start == 0 )) || { log "--no-start: systemd not touched"; return 0; }
  systemctl daemon-reload
  systemctl enable "$APP" >/dev/null 2>&1
  systemctl restart "$APP"
  wait_healthy
}

snapshot_data() {
  local db="$data/db" snap
  [[ -d $db ]] || return 0
  snap="$data/backups/pre-upgrade-$(date +%Y%m%d-%H%M%S)"
  step "stopping the service and copying the database to $snap"
  (( no_start == 1 )) || systemctl stop "$APP" 2>/dev/null || true
  install -d -m 0700 -o "$APP" -g "$APP" "$snap"
  cp -a "$db" "$snap/"
}

# AC-100: the same rule as AccountRules.ValidateEmail in Core (the email is also the user name).
valid_email() {
  local e=$1 local_part domain
  (( ${#e} <= 254 )) || return 1
  [[ $e =~ ^([A-Za-z0-9._+-]+)@([A-Za-z0-9.-]+)$ ]] || return 1
  local_part=${BASH_REMATCH[1]}; domain=${BASH_REMATCH[2]}
  (( ${#local_part} <= 64 )) || return 1
  [[ $local_part != .* && $local_part != *. && $local_part != *..* ]] || return 1
  [[ $domain =~ ^([A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}$ ]]
}

create_admin() {
  [[ $is_upgrade == 0 ]] || return 0
  if [[ -z $admin_email ]]; then
    if (( unattended == 0 )) && [[ -r /dev/tty ]] && { : < /dev/tty; } 2>/dev/null; then
      printf 'Email for the first Admin account (Enter to skip): ' > /dev/tty
      read -r admin_email < /dev/tty || admin_email=""
    fi
    if [[ -z $admin_email ]]; then
      log "No Admin account created. Create one later with:  sudo $APP admin create --email you@example.com"
      return 0
    fi
    valid_email "$admin_email" \
      || { warn "'$admin_email' is not a valid email; create the Admin later with: sudo $APP admin create --email ..."; return 0; }
  fi
  step "creating Admin account $admin_email"
  if [[ -n $admin_password_file ]]; then
    "$CMD" admin create --email "$admin_email" --password-stdin < "$admin_password_file" \
      || warn "Admin not created; retry with: sudo $APP admin create --email $admin_email"
  else
    "$CMD" admin create --email "$admin_email" < /dev/tty \
      || warn "Admin not created; retry with: sudo $APP admin create --email $admin_email"
  fi
}

prune_releases() {
  local keep
  # keep the three newest releases, never the active one
  keep=$(readlink -f "$OPT/current")
  find "$OPT/releases" -mindepth 1 -maxdepth 1 -type d -printf '%T@ %p\n' | sort -rn | awk 'NR > 3 { print $2 }' |
    while read -r old; do [[ $old == "$keep" ]] || rm -rf "$old"; done
}

print_summary() {
  local url
  url=$(current_url)
  cat <<DONE

RecipeWarden $new_version is installed$( (( no_start )) && echo " (not started: --no-start)" ).
  Health:      curl $url/health
  Service:     systemctl status $APP     Logs: journalctl -u $APP -f
  Settings:    $ETC/appsettings.Production.json   (then: sudo systemctl restart $APP)
  Network:     $ETC/network.json   (bind/port/proxy/allowed IPs; then: sudo systemctl restart $APP)
  Commands:    sudo $APP health | admin create --email ... | apply-config
  Data:        $data      Previous releases: $OPT/releases (3 kept)

Cloudflare Tunnel ingress rule for this host:
  - hostname: recipes.example.com
    service: $url
DONE
  if [[ $bind != 127.* ]]; then
    cat <<NOTE
Only the tunnel host ($trusted_proxy) and the addresses in Network:AllowedSourceIps may connect; the app refuses
everyone else. For defence in depth, also limit port $port to the tunnel host in the host firewall.
NOTE
  fi
}

# ---------------------------------------------------------------------------------------------- modes
do_install() {
  is_upgrade=0
  if installed; then
    is_upgrade=1
    [[ -n $port ]] || port=$(current_url | sed -n 's/.*:\([0-9]*\)$/\1/p')
    bind=${bind:-$(current_url | sed -n 's#http://\([^:]*\):.*#\1#p')}
    data=$(sed -n 's/.*"Directory": *"\([^"]*\)".*/\1/p' "$ETC/appsettings.Production.json" | head -1)
    data=${data:-$DATA_DEFAULT}
    [[ -z $trusted_proxy && -z $allowed_ips ]] || warn "network options are ignored on upgrade; use --reconfigure"
    step "upgrading the existing installation"
  else
    port=${port:-5080}; bind=${bind:-127.0.0.1}; data=${data_dir:-$DATA_DEFAULT}
    if [[ -z $trusted_proxy && $bind == 127.* ]]; then trusted_proxy=$bind; fi
    [[ $bind == 127.* || -n $trusted_proxy ]] \
      || die $EXIT_USAGE "--trusted-proxy is required when --bind is not loopback (the cloudflared host's IP)"
    [[ $bind != 0.0.0.0 ]] || warn "binding to all interfaces; Network:AllowedSourceIps is then the only protection"
    check_port
  fi

  resolve_tag
  log "RecipeWarden ${tag:-from $package} -> $( (( is_upgrade )) && echo upgrade || echo "new install on $bind:$port")"
  if (( yes == 0 )); then log "starting in 5 seconds (Ctrl+C to cancel)"; sleep 5; fi

  work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
  download_package
  install_dependencies
  create_user_and_folders
  unpack_release

  local previous=""
  if (( is_upgrade )); then
    previous=$(readlink -f "$OPT/current")
    snapshot_data
  fi

  write_settings
  (( is_upgrade )) || write_network
  ln -sfn "$release" "$OPT/current"
  write_command_wrapper
  install_unit

  if ! start_service; then
    journalctl -u "$APP" -n 40 --no-pager 2>/dev/null || true
    if [[ -n $previous ]]; then
      step "the new release did not become healthy - rolling back to $(basename "$previous")"
      ln -sfn "$previous" "$OPT/current"
      install -m 0644 "$OPT/current/$APP.service" "$UNIT"
      systemctl daemon-reload; systemctl restart "$APP"
      wait_healthy || warn "the previous release is not healthy either; the data snapshot is in $data/backups"
      die $EXIT_FAILED "upgrade to $new_version failed and was rolled back"
    fi
    die $EXIT_FAILED "RecipeWarden did not start; see the log above and $ETC/network.json"
  fi

  create_admin
  prune_releases
  print_summary
}

do_reconfigure() {
  installed || die $EXIT_PREFLIGHT "RecipeWarden is not installed; run the installer without --reconfigure"
  local old_port old_bind
  old_port=$(current_url | sed -n 's/.*:\([0-9]*\)$/\1/p'); old_bind=$(current_url | sed -n 's#http://\([^:]*\):.*#\1#p')
  port=${port:-$old_port}; bind=${bind:-$old_bind}
  # network.json is written by this script with each list on one line
  # (an empty list is normal, e.g. AllowedSourceIps on a loopback install: grep finding nothing is not an error)
  [[ -n $trusted_proxy ]] || trusted_proxy=$(list_from_network TrustedProxies | paste -sd, -)
  [[ -n $allowed_ips ]] || allowed_ips=$(list_from_network AllowedSourceIps | { grep -vx "$old_bind" || true; } | paste -sd, -)
  [[ $bind == 127.* || -n $trusted_proxy ]] || die $EXIT_USAGE "--trusted-proxy is required when --bind is not loopback"
  [[ $port == "$old_port" ]] || check_port
  data=$DATA_DEFAULT
  write_network
  new_version=$(basename "$(readlink -f "$OPT/current")"); new_version=${new_version%-*-*}
  start_service || die $EXIT_FAILED "RecipeWarden did not start with the new network settings; edit $ETC/network.json and restart"
  print_summary
}

do_uninstall() {
  step "removing RecipeWarden"
  if (( no_start == 0 )) && [[ -d /run/systemd/system ]]; then
    systemctl disable --now "$APP" >/dev/null 2>&1 || true
  fi
  rm -f "$UNIT"; rm -rf "$DROPIN_DIR" "$OPT"
  if grep -q "RecipeWarden host-command wrapper" "$CMD" 2>/dev/null; then rm -f "$CMD"; fi
  (( no_start == 1 )) || systemctl daemon-reload 2>/dev/null || true
  if (( purge == 1 )); then
    local d
    d=$(sed -n 's/.*"Directory": *"\([^"]*\)".*/\1/p' "$ETC/appsettings.Production.json" 2>/dev/null | head -1)
    rm -rf "${d:-$DATA_DEFAULT}" "$DATA_DEFAULT" "$ETC" "$LOG"
    if id "$APP" >/dev/null 2>&1; then userdel "$APP" 2>/dev/null || true; fi
    log "Removed the app, its data, settings, logs and the $APP user."
  else
    log "Removed the app. Kept: $DATA_DEFAULT (data), $ETC (settings), $LOG (logs), user $APP."
    log "Delete them too with: sudo bash install.sh --uninstall --purge"
  fi
}

main() {
  parse_args "$@"
  require_root
  check_os
  check_tools
  case "$mode" in
    install)     do_install ;;
    reconfigure) do_reconfigure ;;
    uninstall)   do_uninstall ;;
  esac
}

main "$@"
