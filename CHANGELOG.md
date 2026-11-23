# Changelog

Each release has a `## <version> — unreleased` section until it ships; the GitHub release carries the publish date.
The first feature branch of a new release bumps `<VersionPrefix>` in `Directory.Build.props` and adds its section.

## 0.5.0 — unreleased

### M5 Output
- Version bumped to 0.5.0 (docs/branching.md).
- ADRs 0027 to 0031 (owner decisions) and 0032 (smaller decisions made while the owner was away, for review).
- Shopping lists (FR-SHOP-01 to 04, AC-50, AC-51): named lists, recipes added at the shown scale, manual items,
  merging with unit conversion and package counts ("1¼ cups (2½ sticks)"), store sections, pantry staples,
  anonymous check-off with antiforgery and a per-IP limit, finished lists removed after 15 minutes with Undo,
  remembered names for "Start again"; print, text and Markdown.
- Seed `shopping-v1`: store sections, starter pantry and package sizes; editable on `/manage/conversions`.
- Print layout (FR-EXP-01, AC-24): one Letter page for a 15-ingredient, 10-step recipe at the view's scale and units.
- Markdown export (FR-EXP-02/03, AC-25): `export.md`, `export.zip` with images, bulk zips for all recipes or a collection.
- Cook mode (FR-VIEW-02, AC-23): ticks kept on the device, screen kept awake, timers from step text.
- Migration `M5_Output`.

### M5b AI exchange
- ADR 0033 (scope and flow, for owner review); FR-EXP-08 deferred.
- Export for AI for a recipe, a collection or all recipes: goal picker, details, constraints, My Kitchen, editable
  preview with Copy and Download (FR-EXP-04, FR-EXP-07, AC-26, AC-28, AC-29).
- Import AI response: paste or upload a reply, review the changes and a diff, save as a draft variant, publish it, or
  replace the original; the original's credit is kept (FR-EXP-05, AC-27, BR-92, BR-93).
- `/docs/recipe-md.schema.json` (FR-EXP-06); recipe pages link variants and their parent.

## 0.4.0 — unreleased

### M4 Import
- Version bumped to 0.4.0 (docs/branching.md).
- Import from a web address: schema.org JSON-LD, microdata or headings; credit kept and locked; optional copy of the finished-dish image (FR-IMP-01..03, AC-10..12; ADR 0024).
- SafeFetcher: DNS vetted before connecting, no home-network or metadata addresses, redirects re-checked, 10 s and 5 MB caps; 20 URL imports an hour (SEC-40..43, AC-13).
- When a site blocks importing, paste the page text or saved HTML instead (ADR 0024).
- Import files: recipe-md v1, Markdown, .txt, Word .docx and text PDFs, several recipes per file; macro, legacy, encrypted, scanned and oversized files refused (FR-IMP-04/05, AC-14, AC-15).
- Review page: the editor form filled from the import, with warnings and a possible-duplicate choice (FR-IMP-06/07, AC-16; ADR 0025). Imports kept 30 days (FR-IMP-08).
- Photos: finished-dish and step photos, re-encoded as WebP without location data, with thumbnails; published recipes' photos are public, drafts' are not (FR-REC-01, SEC-52; ADR 0022, ADR 0023). "With a photo" filter on browse.
- AI parser moved to M8 (ADR 0026). Spec v0.13; ADRs 0022–0026.

## 0.3.0 — unreleased

### M3 Kitchen
- Version bumped to 0.3.0 (docs/branching.md).
- My Kitchen (`/manage/kitchen`): measuring spoons and cups, scales, containers, pans and appliances; presets; retire and restore; versioned inventory with snapshots; standard US set seeded (FR-TOOL-01..03, BR-30, BR-33, AC-40).
- Display planner: amounts shown as fills of your spoons and cups, e.g. "1 cup + 2 Tbsp", with an explanation tooltip (FR-MEAS-02, BR-21, BR-24, AC-32).
- Weight first with a scale for precision recipes and dry foods; new `Food.Form` field on Conversions (BR-22, AC-33).
- Suggestions without writes on view: a new scale offers weights; Show me, Dismiss, Save to recipe (FR-TOOL-04/05, BR-31, BR-32, AC-41, AC-42; ADR 0017).
- Container scaling: fit to a container or several, fill %, how many; recipe yield estimated, entered or measured with "I made this" (FR-SCL-04..06, BR-110..114, AC-37..39).
- Pan field (prefilled from the steps) and pan alternatives when scaling (FR-MEAS-07, BR-29).
- Spec v0.12; ADRs 0017–0021.

## 0.2.0 — unreleased

### M2 Measurement
- Recipe toolbar: scale presets (0.5x–3x), custom factor (0.1x–100x), target servings, scale from what you have
  ("3 lb chicken"), and units (as written, US, metric, weight). Scaling is in the URL, so a scaled view can be shared;
  the saved recipe never changes (FR-SCL-01 to 03, ADR 0014).
- Measurement engine in Core: unit choice with escalation (16 Tbsp → 1 cup), kitchen fractions, volume↔weight with
  densities (no density, no guess), pinch/dash counts, cooked-to-raw estimates as a range with their basis, eggs to
  the nearest half (yolk or beaten egg by the baking/precision flag), cans as "+ about ⅓ more", °F/°C in steps.
- Ingredient catalogue seeded from USDA public-domain data (166 foods, 7 cooking yields); ingredients linked to
  foods on save and at startup (ADR 0013). `/manage/conversions` edits densities, aliases, vague amounts and yields.
- Default units and scale per reader (cookie) or per account (`/account/preferences`) (FR-UI-02).
- Editor: tag picker grouped by type (ADR 0016); numbered steps keep their continuation lines and can have a heading
  (`1. **Preheat oven**`), with no literal asterisks (ADR 0015).
- Moved to M3 with the kitchen inventory: display planner tool combinations, container scaling, pan sizes (ADR 0012).
- Version bumped to 0.2.0 (docs/branching.md).

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
