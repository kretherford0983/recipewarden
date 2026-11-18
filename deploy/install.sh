#!/usr/bin/env bash
# RecipeWarden installer (FR-INST-01..04, section 9.5).
#
# M0 skeleton: argument parsing and preflight checks (root, supported OS, free port - AC-91).
# The install, upgrade, uninstall, reconfigure and restore steps arrive in M7; until then the
# script stops after preflight and changes nothing on the host.
#
# SEC-24: no user input is ever evaluated or interpolated into a command string.
set -euo pipefail
umask 077

readonly APP=recipewarden
readonly EXIT_OK=0 EXIT_USAGE=1 EXIT_PREFLIGHT=2 EXIT_NOT_IMPLEMENTED=3

# Test hook: lets the container tests simulate other distributions. Root-only script; read, never executed.
readonly OS_RELEASE_FILE="${OS_RELEASE_FILE:-/etc/os-release}"

port=5080
bind=127.0.0.1
data_dir=/var/lib/$APP
backup_dir=""
trusted_proxy=""
allowed_ips=""
framework_dependent=0
unattended=0
mode=install
purge=0
restore=""

log()  { printf '%s\n' "$*"; }
warn() { printf 'warning: %s\n' "$*" >&2; }
die()  { local code=$1; shift; printf 'error: %s\n' "$*" >&2; exit "$code"; }

usage() {
  cat <<USAGE
Usage: sudo ./install.sh [options]

Install:
  --port <n>                 Listening port (default 5080)
  --bind <ipv4>              Bind address: 127.0.0.1 (tunnel on this host), this host's LAN IP
                             (tunnel in its own container/VM), or 0.0.0.0 (not recommended)
  --trusted-proxy <ip>[,..]  The cloudflared host(s); defaults to the bind IP when loopback
  --allowed-ips <ip|cidr>[,..]  Extra direct-access sources
  --data-dir <path>          Data directory (default /var/lib/$APP)
  --backup-dir <path>        Add a backup folder to Backup:AllowedFolders
  --framework-dependent      Use the distro ASP.NET Core runtime instead of the self-contained build
  --restore <archive>        Start from a backup instead of empty (FR-BKP-06)
  --unattended               Never prompt; fail if something is missing

Other modes:
  --upgrade                  Upgrade in place (backup first, roll back on failed health)
  --reconfigure              Change port, bind, proxy or allowed IPs on an existing install
  --uninstall [--purge]      Remove the app; --purge also deletes data
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

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --port)            need_value "$@"; port=$2; shift 2 ;;
      --port=*)          port=${1#*=}; shift ;;
      --bind)            need_value "$@"; bind=$2; shift 2 ;;
      --bind=*)          bind=${1#*=}; shift ;;
      --trusted-proxy)   need_value "$@"; trusted_proxy=$2; shift 2 ;;
      --trusted-proxy=*) trusted_proxy=${1#*=}; shift ;;
      --allowed-ips)     need_value "$@"; allowed_ips=$2; shift 2 ;;
      --allowed-ips=*)   allowed_ips=${1#*=}; shift ;;
      --data-dir)        need_value "$@"; data_dir=$2; shift 2 ;;
      --data-dir=*)      data_dir=${1#*=}; shift ;;
      --backup-dir)      need_value "$@"; backup_dir=$2; shift 2 ;;
      --backup-dir=*)    backup_dir=${1#*=}; shift ;;
      --restore)         need_value "$@"; restore=$2; shift 2 ;;
      --restore=*)       restore=${1#*=}; shift ;;
      --framework-dependent) framework_dependent=1; shift ;;
      --unattended)      unattended=1; shift ;;
      --upgrade)         set_mode upgrade; shift ;;
      --reconfigure)     set_mode reconfigure; shift ;;
      --uninstall)       set_mode uninstall; shift ;;
      --purge)           purge=1; shift ;;
      -h|--help)         usage; exit $EXIT_OK ;;
      *)                 usage >&2; die $EXIT_USAGE "unknown option: $1" ;;
    esac
  done

  if ! [[ $port =~ ^[0-9]{1,5}$ ]] || (( 10#$port < 1 || 10#$port > 65535 )); then
    die $EXIT_USAGE "--port must be 1-65535"
  fi
  port=$((10#$port))
  is_ipv4 "$bind" || die $EXIT_USAGE "--bind must be an IPv4 address"
  [[ -z $trusted_proxy ]] || validate_list --trusted-proxy "$trusted_proxy" is_ipv4
  [[ -z $allowed_ips ]] || validate_list --allowed-ips "$allowed_ips" is_ipv4_or_cidr
  [[ $data_dir == /* ]] || die $EXIT_USAGE "--data-dir must be an absolute path"
  [[ -z $backup_dir || $backup_dir == /* ]] || die $EXIT_USAGE "--backup-dir must be an absolute path"
  (( purge == 0 )) || [[ $mode == uninstall ]] || die $EXIT_USAGE "--purge is only valid with --uninstall"
  [[ -z $restore || $mode == install ]] || die $EXIT_USAGE "--restore is only valid for a new install"
  [[ -z $restore || -f $restore ]] || die $EXIT_USAGE "--restore: file not found: $restore"

  if [[ -z $trusted_proxy && $bind == 127.* ]]; then
    trusted_proxy=$bind
  fi
  if [[ $bind == 0.0.0.0 ]]; then
    warn "binding to all interfaces; the firewall rule and Network:AllowedSourceIps are your only protection"
  fi
  if [[ $bind != 127.* && -z $trusted_proxy ]]; then
    die $EXIT_USAGE "--trusted-proxy is required when --bind is not loopback (the cloudflared host's IP)"
  fi
}

set_mode() {
  [[ $mode == install ]] || die $EXIT_USAGE "choose only one of --upgrade, --reconfigure, --uninstall"
  mode=$1
}

require_root() {
  [[ ${EUID:-$(id -u)} -eq 0 ]] || die $EXIT_PREFLIGHT "run as root: sudo ./install.sh ..."
}

check_os() {
  [[ -r $OS_RELEASE_FILE ]] || die $EXIT_PREFLIGHT "cannot read $OS_RELEASE_FILE; unsupported OS"
  local id version
  id=$(sed -n 's/^ID=//p' "$OS_RELEASE_FILE" | tr -d '"')
  version=$(sed -n 's/^VERSION_ID=//p' "$OS_RELEASE_FILE" | tr -d '"')
  case "$id:$version" in
    debian:12|debian:13|ubuntu:22.04|ubuntu:24.04) log "OS: $id $version (supported)" ;;
    *) die $EXIT_PREFLIGHT "unsupported OS '$id $version'. Supported: Debian 12/13, Ubuntu 22.04/24.04 (NFR-02)" ;;
  esac
  [[ $(uname -m) == x86_64 ]] || warn "architecture $(uname -m): only x86_64 is supported in v1 (arm64 is a Should)"
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

main() {
  parse_args "$@"
  require_root
  check_os
  if [[ $mode == install || $mode == reconfigure ]]; then
    check_port
  fi

  log "Preflight passed: mode=$mode bind=$bind port=$port trusted-proxy=${trusted_proxy:-none}" \
      "allowed-ips=${allowed_ips:-none} data-dir=$data_dir backup-dir=${backup_dir:-none}" \
      "restore=${restore:-no} framework-dependent=$framework_dependent unattended=$unattended purge=$purge"
  log "This is the M0 installer skeleton: no packages, users, files or services were changed."
  log "The $mode steps are implemented in milestone M7."
  exit $EXIT_NOT_IMPLEMENTED
}

main "$@"
