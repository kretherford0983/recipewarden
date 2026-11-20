# RecipeWarden — requirements traceability

Seeded from docs/spec.md section 10 (v0.2, 2026-11-17). Keep in step with the spec and the test suite.

Test classes are named after requirement IDs (e.g. `FR_SCL_01_ScalingTests`) and tagged with `[Trait("Req", "FR-SCL-01")]`, so CI can print coverage per requirement and fail if any Must requirement has no test.

| Requirement | Rules | Design elements | Test suites | Acceptance |
| --- | --- | --- | --- | --- |
| FR-REC-01/02 | BR-01, BR-02, BR-06 | Recipe aggregate (8.2); `/manage/recipes/*` | Core.Tests RecipeRules; Web.Tests RecipeEditor; E2E W2 | AC-01, AC-02 |
| FR-REC-03 | BR-04 | IngredientLineParser (9.3) | Importers.Tests IngredientLine (corpus) | AC-03 |
| FR-REC-04 | BR-05 | Recipe.DeletedAt; `/manage/trash` | Web.Tests Trash | AC-04 |
| FR-IMP-01/02/03 | BR-10, BR-12, BR-13; SEC-40–43 | SafeFetcher, JsonLdExtractor, ImportJob; `/manage/import` | Importers.Tests UrlImport (snapshots); Infra.Tests SafeFetcher; E2E W3 | AC-10, AC-11, AC-12, AC-13 |
| FR-IMP-04/05 | BR-13; SEC-50–53 | Markdown, Docx, Pdf extractors | Importers.Tests FileImport; Web.Tests UploadValidation | AC-14, AC-15 |
| FR-IMP-06/07 | BR-04 | Review page, duplicate detector | E2E W3/W4; Core.Tests Duplicates | AC-16 |
| FR-ORG-01/02 | — | Tag, Collection tables; `/collections` | Web.Tests Tags, Collections | AC-20 |
| FR-SRCH-01/02 | BR-53; SEC-21 | RecipeSearch FTS5; `/search` | Infra.Tests Search; Web.Tests SearchInjection | AC-21, AC-22 |
| FR-VIEW-01/02 | BR-10 | Recipe page anatomy (7.3); cook mode | E2E W1 | AC-23 |
| FR-EXP-01 | BR-10, BR-28 | Print stylesheet; `/print` | E2E PrintLayout (PDF snapshot) | AC-24 |
| FR-EXP-02/03 | BR-10 | recipe-md v1 (9.7) | Importers.Tests MarkdownRoundTrip | AC-25 |
| FR-SCL-01/02/03 | BR-20, BR-29 | ScalingService | Core.Tests Scaling (property-based) | AC-30, AC-31 |
| FR-MEAS-01/02 | BR-21–24, BR-28 | DisplayPlanner, Unit, Food.Density | Core.Tests DisplayPlanner | AC-32, AC-33 |
| FR-MEAS-03 | BR-25 | Vague units | Core.Tests VagueQuantities | AC-34 |
| FR-MEAS-04 | BR-26 | YieldConversion | Core.Tests YieldConversion | AC-35 |
| FR-MEAS-06 | BR-27 | Count rounding | Core.Tests CountItems | AC-36 |
| FR-TOOL-01/02/03 | BR-30, BR-33 | KitchenTool, InventoryVersion; `/manage/kitchen` | Core.Tests Inventory; E2E W5 | AC-40 |
| FR-TOOL-04/05 | BR-31, BR-32 | Suggestion engine, RecipeSuggestionState | Core.Tests Suggestions; E2E W5 | AC-41, AC-42 |
| FR-SHOP-01–04 | BR-40–42 | ShoppingList tables; `/shopping` | Core.Tests ListMerge; E2E W6 | AC-50, AC-51 |
| FR-NUT-01/02 | BR-50, BR-51 | NutritionFacts, NutrientRef | Core.Tests Nutrition | AC-55 |
| FR-ALG-01/02/03 | BR-52, BR-53 | Allergen, FoodAllergen, RecipeAllergen | Core.Tests Allergens | AC-56 |
| FR-HOME-01/02 | — | Home page; Recommendation rules | Web.Tests Home | AC-60 |
| FR-UI-01/02 | UX-03 | Theme script, UserPreference | E2E Theme | AC-61 |
| FR-AUTH-01/02/03 | SEC-01–14 | Policies; route access table (7.2) | Web.Tests AuthMatrix (every endpoint) | AC-70, AC-71, AC-72 |
| FR-AUTH-04 | SEC-04 | Identity TOTP | Web.Tests TwoFactor | AC-73 |
| FR-ADM-01 | SEC-72 | AuditEntry | Web.Tests Audit | AC-74 |
| NFR-05 | SEC-20–84 | Middleware, headers, CI checks | Web.Tests Headers, Csrf, Uploads, Ssrf; CI gates | AC-75, AC-76, AC-77 |
| FR-BKP-01–05 | BR-60–63; SEC-54 | BackupService, BackupRecord; `/admin/backups` | Infra.Tests Backup, Retention, Restore; E2E W7 | AC-80, AC-81, AC-82 |
| FR-INST-01–04 | SEC-60, SEC-70, SEC-71 | install.sh, unit file | Installer container tests | AC-90, AC-91, AC-92, AC-93 |
| NFR-01/03/06 | — | — | Perf test (5,000 recipes); Playwright axe scan; coverage gate | AC-95, AC-96, AC-97 |
| FR-EXP-04 to 08 | BR-90–97; BR-10 | AiExportBuilder, AI-response importer, recipe-md schema; /export-ai, /manage/import/ai-response | Importers.Tests AiExport, AiResponseImport; E2E W10 | AC-26 to AC-29 |
| FR-BKP-06/07 | BR-62, BR-64–66; SEC-15, SEC-54 | install.sh --restore, /setup, CLI restore | Installer container tests (migration A to B); Web.Tests Setup; Cli.Tests Restore | AC-83, AC-84, AC-85 |
| FR-OPS-02–05 | BR-80–85 | HealthMonitor service, StorageSnapshot, HealthCheckResult; /admin/health, /health/ready | Core.Tests HealthRules, Projection; Infra.Tests StorageProbe; E2E W9 | AC-98, AC-99 |
| FR-SCL-04/05/06 | BR-110–114; BR-20, BR-21, BR-27 | ContainerScaler; Recipe.Yield\*; KitchenTool (Container); recipe toolbar | Core.Tests ContainerScaling, YieldEstimate; E2E W1 | AC-37, AC-38, AC-39 |
| FR-IMP-09/10 | BR-13, BR-93; SEC-44–47 | IAiRecipeParser, AI settings, AiCallLog; /admin/ai | Importers.Tests AiParser (stubbed provider); Web.Tests AiSettings, AiSsrfException | AC-17, AC-18 |
| FR-NUT-03 to 06 | BR-50, BR-54 | Unmatched report, NutrientRef (Custom), dataset importer; /manage/nutrition, /admin/nutrition-data | Core.Tests NutritionMapping; Infra.Tests DatasetRefresh | AC-57, AC-58 |
| FR-BKP-08 to 15 | BR-60–62; SEC-70, SEC-74, SEC-75 | Backup:AllowedFolders, apply-config, systemd drop-in, BackupSchedule, marker check, encryption (9.6.1) | Cli.Tests ApplyConfig; Infra.Tests BackupDestination, Retention, Encryption; installer container tests | AC-86, AC-87, AC-88, AC-89 |

## Status by milestone

### M0 Foundations (2026-11-17)

| ID | Status | Implementation | Tests |
| --- | --- | --- | --- |
| FR-UI-01 | Done (cookie + profile); E2E theme check in a later milestone | `ThemeRules` (Core), `ThemeResolver`, `_Layout`, `wwwroot/js/theme.js`, `/account/theme`, `UserPreference` | Core.Tests `FR_UI_01_ThemeRulesTests`; Web.Tests `FR_UI_01_ThemeTests`; Infra.Tests `SEC_05_UserAdminServiceTests.Theme_preference_is_saved_and_updated` |
| UX-03 | Done (ADR 0001) | server-rendered `data-theme`; external toggle script | Web.Tests `FR_UI_01_ThemeTests`, `SEC_62_SecurityHeadersTests.Pages_contain_no_inline_script_or_event_handlers` |
| AC-61 | Server part done; Playwright check of the first paint later | — | Web.Tests `FR_UI_01_ThemeTests` |
| FR-OPS-01 | `/health` done; journald via stdout and rolling files via Serilog (Production config) | `RecipeWardenHost`, `appsettings.Production.json` | Web.Tests `FR_OPS_01_HealthTests`; Cli.Tests `CliCommandTests.Health_*` |
| SEC-23 | Done (ADR 0002: no nonce, no inline script) | `SecurityHeadersMiddleware` | Web.Tests `SEC_62_SecurityHeadersTests` |
| SEC-62 | Done (+ `X-Frame-Options: DENY`, no `Server` header) | `SecurityHeadersMiddleware` | Web.Tests `SEC_62_SecurityHeadersTests` |
| SEC-10 | Done (fallback policy; also covers unmatched URLs) | `RecipeWardenHost.AddIdentityAndAuth`, Razor conventions | Web.Tests `AC_70_AuthMatrixTests` |
| SEC-11 | Done | `AppRoles`, `AppPolicies` (Core); policies in Web | Core.Tests `SEC_11_PolicyRulesTests`; Web.Tests `AC_70_AuthMatrixTests` |
| AC-70 | Done for every M0 endpoint; the table grows with each route | — | Web.Tests `AC_70_AuthMatrixTests` |
| AC-71 | Editor→`/admin` 403 done; overposting part with first write forms (M1) | — | Web.Tests `AC_70_AuthMatrixTests.Editor_gets_403_on_admin_routes` |
| SEC-01 | Length 12 done; common-password list later (M8) | `ServiceCollectionExtensions.ConfigureIdentity` | Infra.Tests, Cli.Tests (short password refused) |
| SEC-02 / AC-77 | Done | Identity lockout; generic message | Web.Tests `SEC_03_SignInTests` |
| SEC-03 | Cookie flags, 8 h sliding, 5-minute stamp check done; "remember me" (30 days) not yet | `ConfigureApplicationCookie` | Web.Tests `SEC_03_SignInTests` |
| SEC-05 | Done (no registration endpoint) | Login only; CLI `admin create` | Web.Tests `AC_70_AuthMatrixTests.There_is_no_registration_endpoint`; Infra/Cli tests |
| SEC-30 / AC-75 | Done for login, logout and theme | Razor Pages antiforgery; htmx header in `app.js` | Web.Tests `SEC_03_SignInTests`, `FR_UI_01_ThemeTests` |
| SEC-60 / SEC-61 | App side done (allow-list, trusted proxies, CF-Connecting-IP); installer firewall in M7 | `SourceIpAllowListMiddleware`, `CloudflareClientIpMiddleware`, forwarded headers | Web.Tests `SEC_60_NetworkTrustTests`, `CloudflareClientIpMiddlewareTests` |
| SEC-73 | Done | `/error` page with correlation ID | Web.Tests `SEC_62_SecurityHeadersTests.Errors_show_a_generic_page_with_a_reference` |
| SEC-20 / SEC-22 | Banned-API analyzer in place (FromSqlRaw, ExecuteSqlRaw, Html.Raw) | `BannedSymbols.txt` | build |
| SEC-71 | Data Protection keys in `<data>/keys` | `RecipeWardenHost` | — (installer permissions in M7) |
| SEC-80–84 | Done | `ci.yml`, `build.yml`, `dependabot.yml`, `.githooks/pre-commit`, `.gitleaks.toml`, analyzers | CI |
| NFR-06 | Gates in place (ADR 0003); parallel runs (ADR 0006) | `build/test.sh`, `tests/xunit.runner.json`, `tests/Shared/TestDatabaseTemplate.cs` | CI |
| NFR-07 (WAL part) | Done | `DatabaseInitializer`, `SqlitePragmaInterceptor` | Infra.Tests `NFR_07_SqliteConventionsTests` |
| NFR-08 | Done | `Directory.Build.props`, `Directory.Packages.props` | build |
| FR-INST-04 | CLI `admin create` done; installer prompt in M7 | `UserAdminService`, `AdminCreateCommand` | Infra.Tests `SEC_05_UserAdminServiceTests`; Cli.Tests `CliCommandTests` |
| FR-BKP-08 | Validation rules done | `BackupFolderPolicy` (Core) | Core.Tests `FR_BKP_08_BackupFolderPolicyTests` |
| FR-BKP-09 / AC-86 | Done except audit to `AuditEntry` (prints an audit line until FR-ADM-01, M7) | `ApplyConfigRunner`, `BackupFoldersDropIn` | Core.Tests `FR_BKP_09_DropInTests`; Cli.Tests `FR_BKP_09_ApplyConfigTests`, `HostFileSystemTests`, `HostAdapterTests` |
| FR-INST-01/02/04, AC-90, AC-91 | One-command install done (ADR 0007): download + checksum, user/dirs, unit, network.json, first Admin; firewall rules and `/setup` token in M7 | `deploy/install.sh`, `deploy/recipewarden.service`, `/usr/local/bin/recipewarden` wrapper | `tests/installer/test-install.sh` (Debian 12/13, Ubuntu 22.04/24.04), `tests/installer/test-install-systemd.sh` |
| FR-INST-03, AC-92 | Upgrade in place with database copy and automatic rollback done; full pre-upgrade backup in M7 | `deploy/install.sh` | `test-install-systemd.sh` (broken release rolled back) |

### Notes and open issues

- `/error` (SEC-73) and `/account/theme` (FR-UI-01) are not listed in section 7.2. They are in the
  auth-matrix table as Anonymous and Signed in respectively; add them to 7.2 at the next spec revision.
- Requests that match no route are denied by the fallback policy (redirect to sign-in for anonymous
  visitors) instead of returning 404, so probing cannot map routes. Signed-in users get the 404 page.
- **Open (needs an ADR before M5):** D-10 lets anyone check shopping-list items off, which is an
  anonymous write; SEC-10 allows anonymous access to read-only pages only. Options: (a) a narrow
  documented exception for the check-off endpoint with its own rate limit and antiforgery,
  (b) require sign-in to check off, (c) keep check-off state client-side only.
