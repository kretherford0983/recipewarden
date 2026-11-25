# ADR 0007: One-command installer, pulled forward from M7

- Status: Accepted (2026-11-20)
- Requirements: FR-INST-01, FR-INST-02, FR-INST-03 (partly), FR-INST-04, SEC-60, SEC-70, SEC-71, AC-90, AC-91, AC-92

## Context
The first test install of `v0.1.0-dev.1` followed a manual page of commands and failed: they had to run as root and
in one shell, and nothing checked either. The product owner wants installs like PennyWarden's: one `curl` command
that downloads and runs the installer, with settings edited afterwards if needed.

## Decision
- `deploy/install.sh` is attached to every release and is the whole installer:
  `curl -fsSL …/releases/latest/download/install.sh | sudo bash` (or `…/download/<tag>/install.sh | sudo bash -s -- --version <tag>`).
  It resolves the release through the public GitHub API, downloads the package and `SHA256SUMS.txt`, verifies the
  checksum, installs, and upgrades in place when an installation exists.
- Upgrades copy the database first, keep the three newest releases, and roll back automatically when the new release
  fails its health check.
- Network settings live in installer-owned `/etc/recipewarden/network.json` beside the user-owned
  `appsettings.Production.json`; the web app and CLI load both. `--reconfigure` rewrites network.json; editing it and
  restarting the service works too. The host's own bind IP is always allowed (installer and CLI health checks).
- `/usr/local/bin/recipewarden` is a wrapper that runs CLI commands as the service user (root only for `apply-config`).
- The first Admin is created at install (prompt through `/dev/tty`, or `--admin-email` + `--admin-password-file`).
- Tests: `tests/installer/test-install.sh` (Debian 12/13, Ubuntu 22.04/24.04/26.04 containers, `--no-start`) and
  `tests/installer/test-install-systemd.sh` (real systemd on Ubuntu 24.04 and 26.04 CI runners: start, health, upgrade, rollback,
  reconfigure, uninstall). CI builds one package per run for both.

## Still in M7
`--restore` and the `/setup` page (FR-BKP-06), full backups instead of the database copy, host-firewall rules for a
LAN bind (SEC-60's firewall part), `--framework-dependent`.
