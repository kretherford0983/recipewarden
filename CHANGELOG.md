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

### M1 Recipes
- Traceability synced with the project copy: M0 marked done with its deployment check; AC-72 overposting carried into M1.
- Scope decisions: recipe slugs follow the title with redirects from old slugs (ADR 0008); M1 is text only, images move to M4 and the "free of" filter to M6 (ADR 0009). Spec v0.8.
- Core rules for recipes (publish/draft, slugs with redirects, servings, trash, edit-conflict diff, source credit), tag and collection names, and safe search queries (SEC-21). Search input with unpaired UTF-16 surrogates no longer causes an error.
- Ingredient lines are split into amount (fractions, ranges, ½), unit, ingredient, preparation and notes, keeping the original text; doubtful lines are flagged for review (FR-REC-03).
- Recipes can be added, edited, published or kept as drafts, deleted to a 30-day trash and restored (FR-REC-01/02/04).
- Reading view with ingredient groups, step sections, fractions as ½ and source credit; old links redirect after a rename (FR-VIEW-01, BR-02).
- Full-text search and browsing with tag, collection, time and rating filters (FR-SRCH-01/02).
- Tags and collections, with management pages for editors (FR-ORG-01/02).
- Security: search and write rate limits (SEC-63); safe Markdown (SEC-22); every form carries an antiforgery token, including sign-out (SEC-30).
- Browser tests for adding and reading a recipe run in CI (Playwright).
- Servings and times accept ranges ("6-8", "20-25 min") and hours ("1 hr 15 min", "1-1.5 hours"); "Ready in" filters on the longer end (ADR 0010).
- Built-in tags for meals, courses, seasons, methods and diets, including "Homemade Ingredient"; your own tags with the same names are kept (ADR 0011).

### Fixes
- `recipewarden admin create` and the installer check the email before asking for a password: an invalid address
  (e.g. `name@#example.com`) or one that already has an account is refused straight away with the reason (AC-100).
