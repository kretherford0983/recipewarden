# Changelog

Each release has a `## <version> — unreleased` section until it ships; the GitHub release carries the publish date.
The first feature branch of a new release bumps `<VersionPrefix>` in `Directory.Build.props` and adds its section.

## 1.0.0 — unreleased

### Ounces on liquids (BR-115, ADR 0049)
- "24 oz chicken broth" now converts and scales as fluid ounces (24 fl oz, 710 ml), not 1½ lb or grams. This applies
  to broth, milk, cream, wine, juice and other water-like liquids; honey, syrups, solids and unmatched lines keep
  weight. A line shown at 1x as written is unchanged. The container yield estimate uses the same rule.

### Reader mode, try 2 (UX-14, ADR 0048 amendment 1)
- Each ingredient line is now a paragraph, and recipe pages say they are articles (`og:type`, title and a
  plain-text description in the page head), like recipe sites where Safari Reader works. Nothing changes on screen.
- Recipe pages carry the recipe as schema.org JSON-LD data, as recipe sites do (ADR 0050, for owner review).
- The JSON-LD now has what Safari's HomeView and Google ask for: servings ("4 servings"), the whole description, full
  image addresses (step photos too), named step sections, category and cuisine from tags, a page address, the date
  last changed, a total time (prep + cook when no total was typed), and calories when the estimate is complete
  (ADR 0050 amendment 1).

### UI polish (owner feedback on the dev build)
- The scale box's "More scaling options" is closed on every page load; its summary names the options in use
  (for example "Units: US").
- A converted ingredient line now says what was written under it ("Written: 24 oz. chicken broth") with an accent
  edge, instead of a dot that looked like a second bullet and a tooltip phones cannot show (UX-06).
- The recipe editor explains ingredient groups with an example.
- Browser reader mode (Safari Reader, Chrome Reading mode, Firefox Reader View) now works on a recipe and shows the
  description, ingredients, steps and notes without the page's buttons (UX-14, ADR 0048). Recipes are also marked
  as schema.org Recipes. Test-only addition: Mozilla Readability 0.6.0 (Apache-2.0).

### UI alignment with PennyWarden (ADR 0047)
- New look: one design system (colour tokens in both themes, one control height, cards on a tinted background, filled
  pills, original line icons) written down in `docs/ui-standards.md` (UX-13).
- App shell: side navigation with icons on desktop, an icon rail on tablets, a bottom tab bar on phones; Manage and
  Admin get section tabs. Controls are 36 px with a mouse and 44 px on touch screens (UX-02).
- Home, Browse and Search: photo-first recipe cards (compact rows on phones); card summaries are plain text, not
  Markdown. Manage overview as tiles; Admin overview as cards; sign-in in a centred card.
- Recipe page: photo beside the description and stat tiles; Cook mode, Print, an Export menu and I made this in one
  row. The scale box shows 0.5x, 1x, 2x, 3x and "Make this my default"; "More scaling options" opens the rest, one
  per line (owner feedback).
- Ingredients typed or imported with their own bullet no longer show two bullets; "((…))" shows as "(…)".
- Checkboxes are normal size; the recipe editor's tags are a dropdown multi-select showing the chosen tags as chips;
  Save draft and Publish stay in view while editing.
- Fixed two pages wider than a phone screen (Manage → Tags, Admin → Settings). New browser test checks the main
  screens fit a phone and a desktop in both themes and can save screenshots for review.

### M8 Hardening and v1.0
- Fixed: the ingredient preview on the import review page failed (500) because it could not find the editor's
  partial; the units list on a recipe now submits reliably right after scaling (it could be ignored until htmx had
  processed the swapped-in toolbar, which also made a browser test flaky in CI).
- Installer fix (found on the test host with dev.17): the SEC-60 nftables rule dropped this host's own connections
  to its LAN bind address, so the installer's health check failed and every upgrade on a LAN-bound host rolled back.
  The rule now accepts traffic arriving on `lo` before the drop; a systemd installer test covers a LAN-bound upgrade.
- Version bumped to 1.0.0 (ADR 0046).
- Installer upgrade fixes (found on the test host): an older release's CLI without `backup` is now detected (its
  `backup --help` exits 0 with the general help), so the pre-upgrade step no longer prints an error; upgrades read
  the tunnel proxy and allowed IPs from `network.json`, so the summary names the proxy and the SEC-60 firewall rule
  is added for hosts installed before M7; an earlier `--no-firewall` is remembered.
- ADRs 0042 to 0046 (decisions made while the owner was away, for review).
- Optional AI parser, off by default (FR-IMP-09/10, ADR 0042): Claude API or an OpenAI-compatible endpoint (e.g. a
  local Ollama); used automatically for hard imports and on demand with "Re-parse with AI"; results always reviewed
  and marked; usage log, monthly total, token cap and Test connection at `/admin/ai` (AC-17, AC-18).
- AI suggestions for unmatched nutrition ingredients, chosen only from local foods and confirmed by an Editor (FR-NUT-06).
- Two-factor sign-in with an authenticator app and recovery codes; Admins are prompted; an Admin can turn it off for
  someone who lost their codes (SEC-04, AC-73).
- `/account/password`; common-password list; "remember me" with a 30-day cap; sign-in limit 10/min per IP
  (SEC-01, SEC-03, SEC-63; ADR 0043).
- Home page recommendations: in season, favorites, not cooked lately (FR-HOME-02, ADR 0044).
- Faster search with many recipes: p95 ≈ 30 ms at 5,000 recipes on 2 vCPU (was 226 ms) (AC-95).
- Security pass: tests for SEC-20/22/24/31/53/62/70/80–84; fixed `/admin` writing to the database on first view.
- Accessibility scan of 23 pages in both themes, focus visibility and the theme's first paint (AC-96, AC-61; ADR 0045).
- New packages: QRCoder 1.9.0 (MIT), Microsoft.AspNetCore.DataProtection 10.0.15 (MIT). Bundled data: common
  passwords from SecLists (MIT). Migration `M8_AiParser`.

## 0.7.0 — unreleased

### M7 Operations
- Version bumped to 0.7.0 (docs/branching.md).
- ADRs 0037 to 0041 (decisions made while the owner was away, for review).
- Backups (FR-BKP-01 to 05): back up now, schedules (daily, weekly or cron) to allowed folders, grandfather-father-son
  retention that only touches the schedule's own archives, run history with download and delete, failure banner and
  retries after 1, 2, 4 … hours (AC-80, AC-81, AC-87 to 89).
- Encrypted backups with a one-time recovery kit (FR-BKP-15, ADR 0039); Data Protection keys included (ADR 0038).
- Restore from a listed or uploaded archive with a validation report and the word RESTORE, maintenance page,
  pre-restore backup and rollback on failure (FR-BKP-04, AC-82); `sudo recipewarden restore` (AC-85);
  `install.sh --restore` for moving hosts (AC-83); first-run `/setup` with a one-time token (AC-84).
- Audit log of every write with user, time and IP, and `/admin/audit` (FR-ADM-01, AC-74).
- `/admin/users` (create, roles, lock, password, delete; the last Admin is protected, AC-72), `/admin/settings`
  (site name, disk thresholds, webhook), `/admin` dashboard, `/admin/nutrition-data` upload.
- Health and capacity (FR-OPS-02 to 05): `/admin/health` with storage by category, 90-day chart, days-until-full,
  integrity check, compact and purge; Admin banners; `/health/ready`; status-change webhook; `recipewarden health`.
- Installer: full pre-upgrade backup, setup token, host firewall rule for LAN binds (SEC-60), `--restore-kit`.
- New CLI commands: `backup`, `restore`, `setup-token`. New package: Cronos (MIT). Migration `M7_Operations`.

## 0.6.0 — unreleased

### M6 Nutrition and allergens
- Version bumped to 0.6.0 (docs/branching.md).
- ADRs 0034 to 0036 (owner chose the recommendations; details for review).
- Bundled USDA SR Legacy nutrient data (7,793 foods), applied once at startup; no network (D-06).
- Nutrition per serving on the recipe page: calculated (with % matched, "partial" below 70%), imported from the
  page or recipe-md, or entered by hand (FR-NUT-01/02, AC-55).
- Allergen badges (9 US majors + gluten) detected from ingredients, with editor overrides that survive edits, and a
  "free of" filter on the recipe list and search (FR-ALG-01 to 03, BR-53, AC-56).
- `/manage/nutrition`: unmatched-ingredient report, mapping by search, foods from package labels (FR-NUT-03/04, AC-57).
- `recipewarden nutrition-import <zip>` applies a newer USDA dataset and keeps mappings (FR-NUT-05, AC-58).
- Exports include allergens and the nutrition panel; pasted recipe-md keeps every field.
- Migration `M6_Nutrition`.

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
