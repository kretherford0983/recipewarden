# Changelog

Each release has a `## <version> — unreleased` section until it ships; the GitHub release carries the publish date.
The first feature branch of a new release bumps `<VersionPrefix>` in `Directory.Build.props` and adds its section.

## 0.1.0 — unreleased

### M0 Foundations
- Solution layout, central package management, analyzers with warnings as errors, banned raw-SQL/`Html.Raw` APIs.
- Web app: global layout, light/dark/system theme (FR-UI-01), `/health`, security headers and CSP (SEC-23, SEC-62),
  Identity with Admin/Editor roles and a deny-by-default fallback policy (SEC-10, SEC-11), endpoint auth matrix (AC-70),
  trusted-proxy and source-IP checks for the Cloudflare Tunnel (SEC-60, SEC-61).
- CLI: `admin create`, `health`, `apply-config` (FR-BKP-09, AC-86).
- Installer skeleton with preflight checks (AC-91) and container tests; hardened systemd unit.
- CI with coverage, vulnerable-package and secret-scan gates; Dependabot.
- Branch flow `feature/*` → `develop` → `main` with development builds as GitHub pre-releases (ADR 0004).
- Tests run in parallel (projects at once, classes on every core, database migrated once per test process);
  CI jobs side by side behind one required check, `test` (ADR 0006, docs/testing.md).
- One-command install and upgrade: `curl … | sudo bash` with checksum check, automatic rollback, settings in
  `/etc/recipewarden` and network settings in `network.json` (ADR 0007, docs/install.md).
- Ubuntu 26.04 LTS supported by the installer and tested in CI (NFR-02).
- Public repository; server downloads and docs use plain `curl`, never the GitHub CLI (ADR 0005).
