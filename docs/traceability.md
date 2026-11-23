# RecipeWarden — requirements traceability

Seeded from docs/spec.md section 10 (v0.2, 2026-11-17); in step with spec v0.18. Keep in step with the spec and the test suite.

Test classes are named after requirement IDs (e.g. `FR_SCL_01_ScalingTests`) and tagged with `[Trait("Req", "FR-SCL-01")]`, so CI can print coverage per requirement and fail if any Must requirement has no test.

| Requirement | Rules | Design elements | Test suites | Acceptance |
| --- | --- | --- | --- | --- |
| FR-REC-01/02 | BR-01, BR-02, BR-06 | Recipe aggregate (8.2); `/manage/recipes/*` | Core.Tests RecipeRules; Web.Tests RecipeEditor; E2E W2 | AC-01, AC-02 |
| FR-REC-03 | BR-04 | IngredientLineParser (9.3) | Importers.Tests IngredientLine (corpus) | AC-03 |
| FR-REC-04 | BR-05 | Recipe.DeletedAt; `/manage/trash` | Web.Tests Trash | AC-04 |
| FR-IMP-01/02/03 | BR-10, BR-12, BR-13; SEC-40–43 | SafeFetcher, HtmlRecipeExtractor, ImportService, ImportJob, ImageStore; `/manage/import` | Core `AC_13_UrlRulesTests`; Importers `AC_10_HtmlImportTests`; Infra `AC_13_SafeFetcherTests`, `FR_IMP_01_ImportServiceTests`; Web `FR_IMP_01_ImportPageTests`; E2E `W3_W4_ImportTests` | AC-10, AC-11, AC-12, AC-13 |
| FR-IMP-04/05 | BR-13; SEC-50–53 | RecipeMarkdown (Core), Text/Docx/Pdf extractors, UploadValidator | Core `AC_14_RecipeMarkdownTests`, `SEC_50_UploadRulesTests`; Importers `AC_15_FileImportTests`; Infra `SEC_51_UploadValidatorTests`; Web `FR_IMP_01_ImportPageTests`; E2E W4 | AC-14, AC-15 |
| FR-IMP-06/07 | BR-04 | Review page (editor form), ImportRules duplicates | Core `AC_16_DuplicateRulesTests`; Infra `FR_IMP_01_ImportServiceTests`; Web `FR_IMP_01_ImportPageTests`; E2E W3/W4 | AC-16 |
| FR-ORG-01/02 | — | Tag, Collection tables; `/collections` | Web.Tests Tags, Collections | AC-20 |
| FR-SRCH-01/02 | SEC-21; BR-53 in M6 (ADR 0009) | RecipeSearch FTS5; `/search` | Infra.Tests Search; Web.Tests SearchInjection | AC-21, AC-22 |
| FR-VIEW-01/02 | BR-10 | Recipe page anatomy (7.3); cook mode `/recipes/{slug}/cook`, `TimerRules` | E2E W1, `W6_ShoppingPrintCookTests`; Core `FR_VIEW_02_TimerRulesTests`; Web `FR_EXP_01_PrintCookExportPageTests` | AC-23 |
| FR-EXP-01 | BR-10, BR-28 | Print layout and `print.css`; `/print` | E2E W6 one-page PDF; Web `FR_EXP_01_PrintCookExportPageTests` | AC-24 |
| FR-EXP-02/03 | BR-10 | recipe-md v1 (9.7); `RecipeExporter`; `export.md`, `export.zip`, bulk zips | Core `FR_EXP_02_RecipeMarkdownWriteTests`; Infra `FR_EXP_02_RecipeExportTests`; Web `FR_EXP_01_PrintCookExportPageTests` | AC-25 |
| FR-SCL-01/02/03 | BR-20, BR-29 | ScalingService | Core.Tests Scaling (property-based) | AC-30, AC-31 |
| FR-MEAS-01/02 | BR-21–24, BR-28 | DisplayPlanner, Unit, Food.Density | Core.Tests DisplayPlanner | AC-32, AC-33 |
| FR-MEAS-03 | BR-25 | Vague units | Core.Tests VagueQuantities | AC-34 |
| FR-MEAS-04 | BR-26 | YieldConversion | Core.Tests YieldConversion | AC-35 |
| FR-MEAS-06 | BR-27 | Count rounding | Core.Tests CountItems | AC-36 |
| FR-TOOL-01/02/03 | BR-30, BR-33 | KitchenRules, KitchenTools (Core); KitchenService, InventoryVersion, InventorySnapshot; `/manage/kitchen` | Core `FR_TOOL_01_KitchenRulesTests`; Infra `FR_TOOL_01_KitchenStoreTests`; Web `FR_TOOL_01_KitchenPageTests`; E2E W5 | AC-40 |
| FR-TOOL-04/05 | BR-31, BR-32 | SuggestionEngine (Core), SuggestionService, RecipeSuggestionState; `/manage/recipes/{id}/suggestions` | Core `FR_TOOL_04_SuggestionEngineTests`; Infra `FR_TOOL_04_SuggestionStoreTests`; Web `FR_TOOL_04_SuggestionPageTests`; E2E `W5_KitchenTests` | AC-41, AC-42 |
| FR-SHOP-01–04 | BR-40–42 | `ShoppingRules`, `ShoppingService`, shopping tables; `/shopping` | Core `FR_SHOP_02_ShoppingRulesTests`; Infra `FR_SHOP_01_ShoppingServiceTests`; Web `FR_SHOP_01_ShoppingPageTests`; E2E W6 | AC-50, AC-51 |
| FR-NUT-01/02 | BR-50, BR-51 | `NutritionRules` (Core), `NutritionService`, NutritionFacts, NutrientRef; recipe panel, `/manage/recipes/{id}/nutrition` | Core `FR_NUT_01_NutritionRulesTests`, `FR_NUT_01_NutritionTextTests`; Infra `FR_NUT_02_NutritionServiceTests`; Web `FR_NUT_01_NutritionPageTests`; E2E `M6_NutritionTests` | AC-55 |
| FR-ALG-01/02/03 | BR-52, BR-53 | `AllergenRules` (Core), RecipeAllergen, `RecipeAllergenIndex`; badges, "free of" filter | Core `FR_ALG_01_AllergenRulesTests`; Infra `FR_NUT_02_NutritionServiceTests`; Web `FR_NUT_01_NutritionPageTests`; E2E `M6_NutritionTests` | AC-56 |
| FR-HOME-01/02 | — | Home page; Recommendation rules | Web.Tests Home | AC-60 |
| FR-UI-01/02 | UX-03 | Theme script, UserPreference | E2E Theme | AC-61 |
| FR-AUTH-01/02/03 | SEC-01–14 | Policies; route access table (7.2) | Web.Tests AuthMatrix (every endpoint) | AC-70, AC-71, AC-72 |
| FR-AUTH-04 | SEC-04 | Identity TOTP | Web.Tests TwoFactor | AC-73 |
| FR-ADM-01 | SEC-72 | `AuditRules` (Core), `AuditInterceptor`, `AuditLog`, AuditEntry; `/admin/audit` | Core `FR_ADM_01_AuditRulesTests`; Infra `FR_ADM_01_AuditTests`; Web `FR_ADM_01_AuditPageTests`, `SEC_12_UsersPageTests` | AC-74 |
| NFR-05 | SEC-20–84 | Middleware, headers, CI checks | Web.Tests Headers, Csrf, Uploads, Ssrf; CI gates | AC-75, AC-76, AC-77 |
| FR-BKP-01–05 | BR-60–63; SEC-54 | `RetentionRules`, `BackupScheduleRules`, `ArchiveRules` (Core); `BackupService`, `BackupScheduleService`, `RestoreService`, BackupRecord, BackupSchedule; `/admin/backups` | Core `FR_BKP_03_RetentionRulesTests`, `FR_BKP_02_ScheduleRulesTests`, `SEC_54_ArchiveRulesTests`; Infra `FR_BKP_01_BackupServiceTests`, `FR_BKP_02_ScheduleServiceTests`; Web `FR_BKP_01_BackupPageTests`; E2E `W7_W9_OperationsTests` | AC-80, AC-81, AC-82 |
| FR-INST-01–04 | SEC-60, SEC-70, SEC-71 | install.sh, unit file | Installer container tests | AC-90, AC-91, AC-92, AC-93 |
| NFR-01/03/06 | — | — | Perf test (5,000 recipes); Playwright axe scan; coverage gate | AC-95, AC-96, AC-97 |
| FR-EXP-04 to 08 | BR-90–97; BR-10 | `AiPromptBuilder`, `AiReplyReader`, `RecipeMarkdownValidator`, `AiVariantRules` (Core); `AiExchangeService`; `/export-ai`, `/manage/import/ai-response`, `/docs/recipe-md.schema.json` | Core `FR_EXP_07_AiPromptBuilderTests`, `FR_EXP_05_AiReplyReaderTests`; Infra `FR_EXP_05_AiExchangeServiceTests`; Web `FR_EXP_04_AiExchangePageTests`; E2E `W10_AiExchangeTests` | AC-26 to AC-29 |
| FR-BKP-06/07 | BR-62, BR-64–66; SEC-15, SEC-54 | install.sh --restore, `SetupService`, `/setup`, CLI `restore` (`RestoreOrchestrator`), `setup-token` | Installer `test-install.sh` (AC-83), `test-install-systemd.sh` (AC-85); Infra `SEC_15_SetupTokenTests`; Web `SEC_15_SetupPageTests`; Cli `FR_BKP_07_CliOperationsTests`, `AC_85_RestoreOrchestratorTests` | AC-83, AC-84, AC-85 |
| FR-OPS-02–05 | BR-80–85 | `HealthRules` (Core); `HealthService`, `HealthMonitorService`, `HealthState`, StorageSnapshot, HealthCheckResult; /admin/health, /health/ready, Admin banner, webhook | Core `FR_OPS_02_HealthRulesTests`; Infra `FR_OPS_02_HealthServiceTests`; Web `FR_OPS_02_HealthPageTests`; E2E `W7_W9_OperationsTests` | AC-98, AC-99 |
| FR-SCL-04/05/06 | BR-110–114; BR-20, BR-21, BR-27 | ContainerRules (Core); Recipe.Yield\*; KitchenTool (Container); recipe page fit and how-many; `/manage/recipes/{id}/cooked` | Core `FR_SCL_04_ContainerRulesTests`; Infra `FR_SCL_06_YieldAndPanStoreTests`; Web `FR_SCL_04_ContainerPageTests` | AC-37, AC-38, AC-39 |
| FR-IMP-09/10 | BR-13, BR-93; SEC-44–47 | IAiRecipeParser, AI settings, AiCallLog; /admin/ai | Importers.Tests AiParser (stubbed provider); Web.Tests AiSettings, AiSsrfException | AC-17, AC-18 |
| FR-NUT-03 to 06 | BR-50, BR-54 | Unmatched report, mapping, NutrientRef (Custom), `NutrientDatasetImporter`, CLI `nutrition-import`; `/manage/nutrition` (`/admin/nutrition-data` in M7; FR-NUT-06 in M8) | Core `FR_NUT_01_NutritionRulesTests`; Infra `FR_NUT_02_NutritionServiceTests`; Cli `FR_NUT_05_NutritionImportCommandTests`; Web `FR_NUT_01_NutritionPageTests` | AC-57, AC-58 |
| FR-BKP-08 to 15 | BR-60–62; SEC-70, SEC-74, SEC-75 | Backup:AllowedFolders, apply-config, systemd drop-in, `BackupFolders`, `BackupDestinationRules` (Core), marker check, `BackupCrypto`, `BackupKeyService` (9.6.1, ADR 0039) | Core `FR_BKP_10_DestinationRulesTests`; Cli `FR_BKP_09_ApplyConfigTests`; Infra `FR_BKP_01_BackupServiceTests`, `FR_BKP_02_ScheduleServiceTests`; Web `FR_BKP_01_BackupPageTests`; installer container tests | AC-86, AC-87, AC-88, AC-89 |

## Status by milestone

**Current:** M0 complete and deployed (2026-11-20). **M1 Recipes:** merged into `develop` (PR #7, #8; CLI email check PR #9) and deployed to the test host 2026-11-21, owner testing. **M2 Measurement:** merged into `develop` (PR #10, 2026-11-22; dev build v0.2.0-dev.9). **M3 Kitchen:** merged into `develop` (PR #11, 2026-11-22); host test pending. **M4 Import:** merged into `develop` (PR #12, 2026-11-23); host test pending. **M5 Output and M5b AI exchange:** merged into `develop` (PR #13, #14, 2026-11-23); ADR 0032 and 0033 await owner review. **M6 Nutrition and allergens:** merged into `develop` (PR #15, 2026-11-23); ADRs 0034 to 0036 await owner review. **M7 Operations:** built unattended on `feature/m7-operations` (0.7.0), PR #16 into `develop`; ADRs 0037 to 0041 await owner review. **M8 Hardening (1.0.0):** built unattended on `feature/m8-hardening`, stacked on M7; ADRs 0042 to 0046 await owner review.

### M0 Foundations (built 2026-11-17; deployed and verified 2026-11-20 — DONE)

| ID | Status | Implementation | Tests |
| --- | --- | --- | --- |
| FR-UI-01 | Done (cookie + profile); E2E first-paint check in M8 (`AC_61_ThemeFirstPaintTests`) | `ThemeRules` (Core), `ThemeResolver`, `_Layout`, `wwwroot/js/theme.js`, `/account/theme`, `UserPreference` | Core.Tests `FR_UI_01_ThemeRulesTests`; Web.Tests `FR_UI_01_ThemeTests`; Infra.Tests `SEC_05_UserAdminServiceTests.Theme_preference_is_saved_and_updated` |
| UX-03 | Done (ADR 0001) | server-rendered `data-theme`; external toggle script | Web.Tests `FR_UI_01_ThemeTests`, `SEC_62_SecurityHeadersTests.Pages_contain_no_inline_script_or_event_handlers` |
| AC-61 | Done (M8: E2E with JavaScript off on the return visit) | — | Web.Tests `FR_UI_01_ThemeTests` |
| FR-OPS-01 | `/health` done; journald via stdout and rolling files via Serilog (Production config) | `RecipeWardenHost`, `appsettings.Production.json` | Web.Tests `FR_OPS_01_HealthTests`; Cli.Tests `CliCommandTests.Health_*` |
| SEC-23 | Done (ADR 0002: no nonce, no inline script) | `SecurityHeadersMiddleware` | Web.Tests `SEC_62_SecurityHeadersTests` |
| SEC-62 | Done (+ `X-Frame-Options: DENY`, no `Server` header) | `SecurityHeadersMiddleware` | Web.Tests `SEC_62_SecurityHeadersTests` |
| SEC-10 | Done (fallback policy; also covers unmatched URLs) | `RecipeWardenHost.AddIdentityAndAuth`, Razor conventions | Web.Tests `AC_70_AuthMatrixTests` |
| SEC-11 | Done | `AppRoles`, `AppPolicies` (Core); policies in Web | Core.Tests `SEC_11_PolicyRulesTests`; Web.Tests `AC_70_AuthMatrixTests` |
| AC-70 | Done for every M0 endpoint; the table grows with each route | — | Web.Tests `AC_70_AuthMatrixTests` |
| AC-71 | Done (Editor→`/admin` 403) | — | Web.Tests `AC_70_AuthMatrixTests.Editor_gets_403_on_admin_routes` |
| AC-72 / SEC-13 | Overposting part with the first write forms (M1); last-Admin rule with `/admin/users` (M7) | — | — |
| SEC-01 | Done (length 12; common-password list and "not your email" in M8, ADR 0043) | `ServiceCollectionExtensions.ConfigureIdentity` | Infra.Tests, Cli.Tests (short password refused) |
| SEC-02 / AC-77 | Done | Identity lockout; generic message | Web.Tests `SEC_03_SignInTests` |
| SEC-03 | Done (remember me and the 30-day cap in M8) | `ConfigureApplicationCookie` | Web.Tests `SEC_03_SignInTests` |
| SEC-05 | Done (no registration endpoint) | Login only; CLI `admin create` | Web.Tests `AC_70_AuthMatrixTests.There_is_no_registration_endpoint`; Infra/Cli tests |
| SEC-30 / AC-75 | Done for login, logout and theme | Razor Pages antiforgery; htmx header in `app.js` | Web.Tests `SEC_03_SignInTests`, `FR_UI_01_ThemeTests` |
| SEC-60 / SEC-61 | App side done (allow-list, trusted proxies, CF-Connecting-IP); installer firewall in M7 | `SourceIpAllowListMiddleware`, `CloudflareClientIpMiddleware`, forwarded headers | Web.Tests `SEC_60_NetworkTrustTests`, `CloudflareClientIpMiddlewareTests` |
| SEC-73 | Done | `/error` page with correlation ID | Web.Tests `SEC_62_SecurityHeadersTests.Errors_show_a_generic_page_with_a_reference` |
| SEC-20 / SEC-22 | Banned-API analyzer in place (FromSqlRaw, ExecuteSqlRaw, Html.Raw) | `BannedSymbols.txt` | build |
| SEC-71 | Data Protection keys in `<data>/keys`; verified on the test host 2026-11-20 | `RecipeWardenHost` | — (installer permissions in M7) |
| SEC-80–84 | Done | `ci.yml`, `build.yml`, `dependabot.yml`, `.githooks/pre-commit`, `.gitleaks.toml`, analyzers | CI |
| NFR-06 | Gates in place (ADR 0003); parallel runs (ADR 0006) | `build/test.sh`, `tests/xunit.runner.json`, `tests/Shared/TestDatabaseTemplate.cs` | CI |
| NFR-07 (WAL part) | Done | `DatabaseInitializer`, `SqlitePragmaInterceptor` | Infra.Tests `NFR_07_SqliteConventionsTests` |
| NFR-08 | Done | `Directory.Build.props`, `Directory.Packages.props` | build |
| FR-INST-04 / AC-100 | CLI `admin create` done (email checked before the password prompt, 2026-11-21); installer prompt in M7 | `AccountRules.ValidateEmail` (Core), `UserAdminService.CheckNewAdminEmailAsync`, `AdminCreateCommand`, `valid_email` in install.sh | Core.Tests `FR_INST_04_EmailRulesTests`; Infra.Tests `SEC_05_UserAdminServiceTests`; Cli.Tests `CliCommandTests`; installer `test-install.sh` |
| FR-BKP-08 | Validation rules done | `BackupFolderPolicy` (Core) | Core.Tests `FR_BKP_08_BackupFolderPolicyTests` |
| FR-BKP-09 / AC-86 | Done except audit to `AuditEntry` (prints an audit line until FR-ADM-01, M7) | `ApplyConfigRunner`, `BackupFoldersDropIn` | Core.Tests `FR_BKP_09_DropInTests`; Cli.Tests `FR_BKP_09_ApplyConfigTests`, `HostFileSystemTests`, `HostAdapterTests` |
| FR-INST-01/02/04, AC-90, AC-91 | One-command install done (ADR 0007): download + checksum, user/dirs, unit, network.json, first Admin; firewall rules and `/setup` token in M7 | `deploy/install.sh`, `deploy/recipewarden.service`, `/usr/local/bin/recipewarden` wrapper | `tests/installer/test-install.sh` (Debian 12/13, Ubuntu 22.04/24.04/26.04), `tests/installer/test-install-systemd.sh` |
| FR-INST-03, AC-92 | Upgrade in place with database copy and automatic rollback done; full pre-upgrade backup in M7 | `deploy/install.sh` | `test-install-systemd.sh` (broken release rolled back) |

### M1 Recipes (built 2026-11-21; merged and deployed to the test host 2026-11-21 — DONE)

Scope decisions: ADR 0008 (slugs follow the title, old slugs redirect), ADR 0009 (text only — images in M4;
"free of" filter BR-53 in M6; model gaps filled; drafts not public).

| ID | Status | Implementation | Tests |
| --- | --- | --- | --- |
| FR-REC-01 / BR-01, BR-03, BR-04 | Done (text fields; images M4, nutrition/allergens M6) | Recipe aggregate (8.2), `PublishRules`, `ServingsRules`, `RecipeService` | Core `BR_01_PublishRulesTests`, `BR_03_ServingsRulesTests`; Infra `FR_REC_01_RecipePersistenceTests` |
| FR-REC-02 / AC-01, AC-02 | Done | `/manage/recipes/new`, `/manage/recipes/{id}/edit`, `RecipeEdit`, `StepTextRules`, `editor.js` (reorder) | Core `FR_REC_02_*`; Web `FR_REC_02_RecipeEditorTests`; E2E `W2_AddRecipeTests` |
| BR-02 | Done (ADR 0008) | `SlugRules`, `RecipeSlugAlias`, 301 from old slugs | Core `BR_02_SlugRulesTests`; Infra `BR_02_SlugUniquenessTests`; Web `FR_VIEW_01_ReadingViewTests` |
| BR-06 | Done | `Recipe.Version`, `RecipeDiff`, conflict table in the editor | Core `BR_06_RecipeDiffTests`; Infra `BR_06_ConcurrencyTests`; Web `BR_06_EditConflictTests` |
| FR-REC-03 / AC-03 | Done: corpus 574 lines, 99.7% (misses: "minced beef", "boneless, skinless …"); authored corpus, so the real-world rate is lower until M4 imports add lines | `IngredientLineParser` (Importers), `UnitCatalog` (Core), live chips | Importers `FR_REC_03_*`, `FR_REC_01_IngredientListTests`; Core `FR_REC_03_UnitCatalogTests` |
| FR-REC-04 / BR-05 / AC-04 | Done | soft delete, `/manage/trash` (undo, restore, Admin purge), `TrashPurgeService` | Core `BR_05_TrashRulesTests`; Infra `FR_REC_04_SoftDeleteTests`; Web `FR_REC_04_TrashTests` |
| FR-REC-06 | Column and checkbox only; behaviour in M2 | `Recipe.IsPrecision` | Infra `FR_REC_01_RecipePersistenceTests` |
| BR-10 / BR-11 | Done (any change away from Web needs Admin; source URLs http/https only); BR-11 audit line waits for FR-ADM-01 (M7) | `SourceRules`, credit link in the reading view | Core `BR_10_SourceRulesTests`; Web `FR_REC_02_RecipeEditorTests`, `FR_VIEW_01_ReadingViewTests` |
| FR-ORG-01 / AC-20 | Done | `Tag` (NormalizedName), `NameRules`, `TagService`, `/tags/{slug}`, `/manage/tags` | Core `FR_ORG_01_TagRulesTests`, `SafeTextTests`; Infra `FR_ORG_01_TagStoreTests`; Web `FR_ORG_TagAndCollectionTests` |
| FR-ORG-02 / AC-20 | Done | `RecipeCollection`, `CollectionService`, `/collections`, `/collections/{slug}`, `/manage/collections` | Infra `FR_ORG_02_CollectionStoreTests`; Web `FR_ORG_TagAndCollectionTests` |
| FR-SRCH-01 / AC-21 | Done (matched-term count, then bm25 field weights) | `RecipeSearch` FTS5, `RecipeSearchService`, `/search` | Infra `FR_SRCH_01_SearchTests`; Web `SEC_21_SearchPageTests`; E2E `W1_PhoneReadingTests` |
| FR-SRCH-02 | Done (no allergen filter until M6; no has-image until M4; rating and last-cooked values come with FR-REC-07) | `SearchCriteria`, `/recipes` | Core `FR_SRCH_02_SearchFilterRulesTests`; Infra `FR_SRCH_02_SearchFilterTests` |
| SEC-21 / AC-22 | Done | `FtsQuery`, `SafeText` (Core); expression passed as a parameter | Core `SEC_21_FtsQueryBuilderTests`, `SafeTextTests`; Infra `SEC_21_SearchInjectionTests`; Web `SEC_21_SearchPageTests` |
| FR-VIEW-01 / AC-23 | Done for the reading view; AC-23 cook-mode part (wake lock, step check-off) in M5 | `/recipes/{slug}`, `IngredientFormatter` (UX-05) | Web `FR_VIEW_01_ReadingViewTests`; E2E `W1_PhoneReadingTests` |
| FR-HOME-01 | Done (recommendations FR-HOME-02 in M8, ADR 0044) | `/` | Web `FR_VIEW_01_ReadingViewTests.Home_lists_recent_recipes_collections_and_tags` |
| AC-72 / SEC-13, SEC-14 | Done for M1 forms (last-Admin rule with `/admin/users`, M7) | input DTOs; route ids bind from the route only; purge re-checked in the handler | Web `AC_72_OverpostingTests`, `FR_REC_04_TrashTests` |
| AC-75 / SEC-30 | Done for every M1 handler and form | `asp-antiforgery="true"` on every post form | Web `SEC_30_AntiforgeryTests`, `SEC_30_FormTokenTests` |
| AC-76 / SEC-22 | Done (descriptions, notes, collection descriptions) | `MarkdownRenderer` (Markdig, raw HTML off + HtmlSanitizer) | Web `AC_76_RecipeContentEncodingTests` |
| SEC-63 | Done (sign-in 10/min per IP added in M8) | `RateLimits`, configurable under `RateLimits` | Web `SEC_63_RateLimitTests` |
| SEC-20 | `SqlQueryRaw` added to the banned list | `BannedSymbols.txt` | build |
| BR-03, FR-REC-01, FR-SRCH-02 (ADR 0010) | Done 2026-11-21: servings and time ranges, times in hours; "Ready in" uses the high end | `ServingsRules.ParseText`, `TimeText`, `Recipe.*Max`, migration `M1_RangesAndDefaultTags` | Core `BR_03_ServingsRangeTests`, `FR_REC_01_TimeTextTests`; Infra `ADR_0010_RangeTests`; Web `ADR_0010_EditorRangeTests`; E2E `W2_AddRecipeTests` |
| FR-ORG-01 (ADR 0011) | Done 2026-11-21: 31 built-in typed tags incl. "Homemade Ingredient", seeded once; existing tags kept | `DefaultTags`, migration `M1_RangesAndDefaultTags` | Core `FR_ORG_01_DefaultTagsTests`; Infra `ADR_0011_DefaultTagSeedTests`; Web `ADR_0010_EditorRangeTests.Built_in_tags_are_suggested_in_the_editor` |
| AC-70 | Extended to every M1 route (sample URLs from seeded data) | — | Web `AC_70_AuthMatrixTests` |

### M2 Measurement (built 2026-11-22; merged into `develop` 2026-11-22 — DONE, host test pending)

Scope decisions: ADR 0012 (display planner, container scaling and pan sizes move to M3; egg wording), ADR 0013
(USDA seed, food linking), ADR 0014 (scale and units in the URL), ADR 0015 (step headings), ADR 0016 (tag picker).

| ID | Status | Implementation | Tests |
| --- | --- | --- | --- |
| FR-SCL-01 / BR-20 / AC-30 | Done: presets, custom 0.1x–100x, target servings; bad values fall back to 1x with a message | `ScaleRules` (Core), `RecipeScaler`, `/recipes/{slug}?scale=&servings=` toolbar | Core `FR_SCL_01_ScaleRulesTests`; Infra `FR_SCL_01_RecipeScalerTests`; Web `FR_SCL_01_ScalingPageTests`; E2E `FR_SCL_01_ScaleToolbarTests` |
| FR-SCL-02 / AC-31 | Done (counts, same kind, or volume↔weight with a density) | `IngredientFactor` (Core), `RecipeScaler.FactorFromAsync`, `?from=&have=` | Core `FR_SCL_02_IngredientFactorTests`; Infra/Web scaler and page tests |
| FR-SCL-03 | Done: GET only, nothing stored; old-slug redirects keep the query | `DetailsModel` | Core `Scaling_leaves_the_input_unchanged`; Infra/Web "stored recipe unchanged" tests |
| BR-29 | Done (above 2x) | `ScaleRules.NoteFor` | Core, Web |
| FR-MEAS-01 / BR-23, BR-24, BR-28 | Done in M2 form: unit choice with escalation, fractions, US/metric/weight; no density = no guess | `MeasureDisplay`, `Fractions`, `IngredientScaler` (Core) | Core `FR_MEAS_01_MeasureDisplayTests`; Infra/Web weight-view tests |
| FR-MEAS-02, BR-21 (tools), BR-22 (auto weight), BR-33 / AC-32, AC-33 | **Moved to M3** (ADR 0012). M2 uses BR-21 tolerances for unit choice and shows AC-32's exact amount (1⅛ cups) | — | Core `Three_quarter_cup_at_1_5x_is_one_and_an_eighth_cups` |
| FR-MEAS-03 / BR-25 / AC-34 | Done | `IngredientScaler` (vague) | Core `FR_MEAS_03_VagueAmountTests`; Web `Pinch_at_10x_and_to_taste_unchanged` |
| FR-MEAS-04 / BR-26 / AC-35 | Done; purchase grams kept for shopping lists. AC-35 shopping-list half in M5 | `YieldRules`, `IngredientScaler`, `YieldConversion` | Core `FR_MEAS_04_YieldConversionTests`; Infra, Web |
| FR-MEAS-05 | Done: densities, aliases, household foods, vague values (in tsp), yields (in %) | `FoodRules` (Core), `FoodCatalogService`, `/manage/conversions` | Core `FR_MEAS_05_FoodRulesTests`; Infra `FR_MEAS_05_ConversionEditTests`; Web `FR_MEAS_05_ConversionsPageTests` |
| FR-MEAS-06 / BR-27 / AC-36 | Done (eggs by precision flag, cans, counts, sticks) | `EggRules`, `IngredientScaler` | Core `FR_MEAS_06_CountItemTests`; Infra, Web |
| FR-MEAS-07 | °F/°C done; pan sizes **moved to M3** | `TemperatureText` | Core `FR_MEAS_07_TemperatureTextTests`; Infra, Web, E2E |
| FR-REC-06 | Tolerance (±2%/±8%) and egg wording in M2; planner use in M3 | `MeasureDisplay.Tolerance`, `EggRules` | Core precision tests |
| FR-UI-02 | Done: query > profile > cookie; "Make this my default" (recipe.js) | `DisplayUnitsRules`, `UserPreferenceStore`, `/account/preferences` | Core `FR_UI_02_DisplayUnitsRulesTests`; Infra profile test; Web cookie and profile tests |
| ADR 0013 seed and linking | Done: 166 foods (149 with density), 7 yields, seeded once; lines linked on save and at startup | `tools/seed/`, `MeasurementSeeder`, `FoodMatcher`, `FoodCatalogService.LinkUnmatchedAsync`, migration `M2_Measurement` | Core `FR_MEAS_01_FoodMatcherTests`; Infra `FR_MEAS_01_MeasurementSeedTests`, `FR_MEAS_01_FoodLinkTests` |
| ADR 0015 step headings | Done | `StepTextRules`, `RecipeStep.Title`, reading view | Core `FR_REC_02_StepHeadingTests`; Infra `Step_temperatures_follow…`; Web `ADR_0015_StepHeadingPageTests` |
| ADR 0016 tag picker | Done | `_EditorForm` picker, `RecipeInput.TagIds`, `editor.js` filter | Web `ADR_0016_TagPickerTests` |
| FR-SCL-04/05/06, AC-37–39 | **Moved to M3** (ADR 0012) | — | — |
| AC-70 / SEC-30 | Extended to `/manage/conversions`, `/account/preferences` and the scaled recipe page | — | Web `AC_70_AuthMatrixTests`, `SEC_30_FormTokenTests` |

### M3 Kitchen (built 2026-11-22; merged into `develop` 2026-11-22, PR #11 — DONE, host test pending)

Scope decisions: ADR 0017 (suggestions without writes on view; amendment: only a new scale is an offer), ADR 0018
(planner uses spoons and cups, jugs ignored; weight first by precision or Food.Form), ADR 0019 (yields: editors save,
readers try), ADR 0020 (pan field prefilled from the steps), ADR 0021 (appliances inventory-only; standard set seeded).

| ID | Status | Implementation | Tests |
| --- | --- | --- | --- |
| FR-TOOL-01/02/03 / BR-30, BR-33 / AC-40 | Done: add, presets, edit, retire, restore; each change bumps the version and stores a snapshot; standard set seeded once (`kitchen-v1`) | `KitchenRules`, `KitchenTools` (Core); `KitchenService`, `KitchenSeeder`; `/manage/kitchen` | Core `FR_TOOL_01_KitchenRulesTests`, `FR_TOOL_01_KitchenInputTests`; Infra `FR_TOOL_01_KitchenStoreTests`; Web `FR_TOOL_01_KitchenPageTests` |
| FR-TOOL-04/05 / BR-31, BR-32 / AC-41, AC-42 | Done per ADR 0017: computed on view, no writes; a new scale offers weights (Show me for readers; Dismiss / Save to recipe for editors); a new spoon applies at once with no banner | `SuggestionEngine` (Core); `SuggestionService`; recipe banner; `/manage/recipes/{id}/suggestions` | Core `FR_TOOL_04_SuggestionEngineTests`; Infra `FR_TOOL_04_SuggestionStoreTests`; Web `FR_TOOL_04_SuggestionPageTests`; E2E `W5_KitchenTests` |
| FR-MEAS-02 / BR-21, BR-24 / AC-32 | Done: up to 3 fills of active spoons and cups, near-exact first then the recipe tolerance; "1 cup + 2 Tbsp"; tooltip explains the plan | `DisplayPlanner`, `ToolPlan` (Core); `IngredientScaler` | Core `FR_MEAS_02_DisplayPlannerTests`; Web `FR_TOOL_01_KitchenPageTests` (plan follows the inventory) |
| BR-22 / AC-33 | Done: with a scale, precision recipes and dry foods show grams (scale resolution) with volume second | `IngredientScaler.WeightFirst`, `Food.Form`, `FoodRules.DefaultForm` | Core `BR_22_WeightFirstTests`, `BR_22_FoodFormTests`; Web `Save_to_recipe_makes_weights_the_default`; E2E W5 |
| FR-REC-06 | Done: recipe tolerance used by the planner | `DisplayPlanner.Plan(…, tolerance)` | Core planner tolerance tests |
| FR-SCL-04/05/06 / BR-110–114 / AC-37, AC-38, AC-39 | Done: fit to a container (owned or typed), fill %, count, how many; yield estimated (labelled), entered or measured ("I made this") | `ContainerRules` (Core); `RecipeScaler.ContainersAsync`; `RecipeService.MarkCookedAsync`; recipe page; `/manage/recipes/{id}/cooked` | Core `FR_SCL_04_ContainerRulesTests`, `FR_SCL_06_RecipeYieldAndPanEditTests`; Infra `FR_SCL_06_YieldAndPanStoreTests`; Web `FR_SCL_04_ContainerPageTests` |
| FR-MEAS-07 (pans) / BR-29 | Done: pan field prefilled from the steps; alternatives within ±15% area, owned pans first, bake-time note | `PanRules` (Core); editor form; recipe page | Core `FR_MEAS_07_PanRulesTests`; Web `Doubling_a_9x13_recipe_suggests_pans` |
| AC-70 / SEC-30 | Extended to `/manage/kitchen`, `/manage/recipes/{id}/cooked` and `/suggestions` | — | Web `AC_70_AuthMatrixTests`, `SEC_30_FormTokenTests` |
| Migration | `M3_Kitchen`: KitchenTool, InventoryVersion, InventorySnapshot, RecipeSuggestionState; Recipe pan columns; Food.Form (default `Other`, then `food-form-v1` seed) | `Data/Migrations` | Infra store tests |

### M4 Import (built 2026-11-22; merged into `develop` 2026-11-23, PR #12 — DONE, host test pending)

Scope decisions: ADR 0022 (WebP + thumbnail; `/media` anonymous only for published recipes' images), ADR 0023 (step
photos follow their step), ADR 0024 (browser-like User-Agent, paste fallback, imports in the request), ADR 0025
(review page is the editor form; recipe-md writer in Core for AC-14), ADR 0026 (AI parser moves to M8).

| ID | Status | Implementation | Tests |
| --- | --- | --- | --- |
| FR-IMP-01 / AC-10 | Done: JSON-LD (@graph, arrays, HowToSection/Step, ISO times), microdata, heading fallback | `HtmlRecipeExtractor`, `HtmlText`, `TextRecipeExtractor` (Importers); `ImportRules.Minutes`, `ServingsAndYield` (Core) | Importers `AC_10_HtmlImportTests` (6 saved pages); Core `FR_IMP_01_ImportRulesTests`; Web `FR_IMP_01_ImportPageTests`; E2E W3 |
| FR-IMP-02 / BR-12 / AC-11 | Done: image copied only when ticked; fetched through SafeFetcher, re-encoded WebP, EXIF/GPS removed, source URL stored | `ImportService.SaveAsync`, `ImageStore`, `ImageEncoder` | Infra `FR_IMP_01_ImportServiceTests` (ticked/unticked), `SEC_52_ImageStoreTests`; Web; E2E W3 |
| FR-IMP-03 / BR-10 / AC-12 | Done for the reading view: credit from the page, locked on review and on save. Print and export parts in M5 | `ImportService` (credit lock), `_EditorForm` (locked view) | Importers `The_page_becomes_the_web_source`; Infra `A_web_import_keeps_its_credit…`; Web |
| SEC-40 to 43 / AC-13 | Done: scheme/port/credential checks, DNS vetted (any private answer refuses), connect only to the vetted IP, redirects re-checked (max 3), 10 s, 5 MB after decompression, content types; refusals logged; 20 URL imports an hour per user | `UrlRules` (Core), `SafeFetcher`, `RateLimits.UrlImportPolicy` | Core `AC_13_UrlRulesTests`, `AC_13_AddressRangeTests`; Infra `AC_13_SafeFetcherTests`; Web `A_home_network_address…`, `SEC_43_UrlImportLimitTests` |
| ADR 0024 paste fallback | Done: refused or recipe-less pages open the paste form with the address; pasted text or HTML goes through the same extractors | `ImportReader.ReadPaste`, `/manage/import?handler=Paste` | Importers; Infra; Web `A_blocked_site_opens_the_paste_form…` |
| FR-IMP-04 / AC-14 | Done: recipe-md v1 reader and writer (round trip over a fixture set); loose Markdown and .txt by headings; several recipes per file | `RecipeMarkdown`, `FrontMatter` (Core), `TextRecipeExtractor` | Core `AC_14_RecipeMarkdownTests`, `FR_IMP_04_RecipeMarkdownEdgeTests`; Importers `AC_15_FileImportTests`; E2E W4 |
| FR-IMP-05 / SEC-50, 51, 53 / AC-15 | Done: .docx (headings, lists; macro files refused) and text PDFs (encrypted and scanned refused); 20 MB, zip-bomb and type checks | `DocxRecipeExtractor`, `PdfRecipeExtractor`, `UploadValidator`, `UploadRules` | Core `SEC_50_UploadRulesTests`; Importers `AC_15_FileImportTests`; Infra `SEC_51_UploadValidatorTests`; Web `Files_are_read_and_bad_files_are_reported` |
| FR-IMP-06 / BR-04 / UX-10 | Done: review page is the editor form with warnings (missing parts, lines below 0.8); progress text while importing | `/manage/import/{id}/review`, `ImportReader.Warnings`, `import.js` | Core; Importers; Web; E2E W3/W4 |
| FR-IMP-07 / AC-16 | Done: same address (tracking parameters ignored) or same title with more than 80% ingredient overlap; save needs "Save anyway" | `ImportRules.FindDuplicate`, `ImportService` | Core `AC_16_DuplicateRulesTests`; Infra; Web `A_second_import_of_a_page_needs_a_choice` |
| FR-IMP-08 | Done: jobs and raw payloads removed after 30 days | `ImportService.PurgeAsync`, `TrashPurgeService` | Infra `Jobs_and_raw_payloads_are_purged_after_30_days` |
| FR-REC-01, FR-REC-02 (images) / SEC-52 | Done: finish and step photos on the edit page; photos follow their steps; `/media` access by publish state; orphans deleted | `ImageStore`, `ImageEncoder`, `StepPhotoRules` (Core), `EditModel` photo handlers, `/media` | Core `ADR_0023_StepPhotoRulesTests`; Infra `SEC_52_ImageStoreTests`, `ADR_0023_StepPhotoTests`; Web `FR_REC_01_PhotoPageTests`; E2E W2 photo at phone width |
| FR-SRCH-02 (has image) | Done | `SearchCriteria.HasImage`, list filter and thumbnails | Infra `Has_image_filters…`; Web `The_list_filters_by_photo…` |
| AC-70 / AC-75 / AC-76 | Extended to `/manage/import`, the review page and `/media` | — | Web `AC_70_AuthMatrixTests`, `SEC_30_FormTokenTests`, `Markup_from_a_page_is_shown_as_text` |
| Migration | `M4_Import`: Image, ImportJob, Recipe.FinishImageId, Step.ImageId | `Data/Migrations` | Infra store tests |

### M5 Output (built 2026-11-23 while the owner was away; merged PR #13 — owner review of ADR 0032 pending)

Scope decisions: ADR 0027 (M5 split; AI exchange is M5b), ADR 0028 (anonymous check-off, finished lists removed after
15 minutes, remembered names), ADR 0029 (lines stored per recipe, merged on view), ADR 0030 (seed shopping-v1;
editable sections, pantry and packages), ADR 0031 (cook mode ticks on the device; print and export follow the view;
bulk export), **ADR 0032 (smaller decisions made unattended — for owner review)**.

| ID | Status | Implementation | Tests |
| --- | --- | --- | --- |
| FR-SHOP-01 / BR-42 | Done: named lists; add a recipe at the shown scale from its page (Editor), manual items; each item lists its recipes | `ShoppingService`, `/shopping`, `/shopping/new`, `/shopping/{id}` | Infra `FR_SHOP_01_ShoppingServiceTests`; Web `FR_SHOP_01_ShoppingPageTests`; E2E W6 |
| FR-SHOP-02 / BR-40 / AC-50 | Done: merge by food (else name); volume and mass merge with a density, counts with counts, the rest as separate amounts on one item | `ShoppingRules.Merge`, `ShoppingRules.Amount` (Core) | Core `FR_SHOP_02_ShoppingRulesTests`, `ADR_0030_ShoppingDefaultsTests`; Infra; Web; E2E W6 |
| BR-41 | Done: package counts, whole and half packages as is ("2½ sticks"), else "buy N" | `ShoppingRules.Packages`; Food.PackageSize/PackageUnitId/PackageName | Core; Infra |
| AC-35 (list part) | Done: prepared amounts go on the list as raw purchase weight | `ShoppingRules.Amount` with `PurchaseGrams` | Core; Infra `Cooked_chicken_goes_on_the_list…` |
| FR-SHOP-03 / AC-51 | Done: store sections in walking order; pantry staples under "You probably have"; ticks on the server per item; the last tick finishes the list (Undo for 15 minutes), names remembered | `ShoppingRules`, `ShoppingSeeder` (shopping-v1), `ShoppingService.CheckAsync`/`PurgeFinishedAsync`, `TrashPurgeService` | Core; Infra; Web `An_anonymous_reader_ticks…`; E2E W6 (phone and desk) |
| D-10 / ADR 0028 (anonymous write) | Done: POST `?handler=Check` with antiforgery, only keys of current items, 120 a minute per IP; editing handlers need Editor | `DetailsModel` (Shopping), `RateLimits.ShoppingCheckPolicy` | Web `A_tick_without_a_token…`, `Anonymous_readers_cannot_edit_a_list`, `SEC_63` tick limit; `AC_70` table |
| FR-SHOP-04 | Done: print layout, plain text and Markdown (names escaped) | `ShoppingText` (Core), `?handler=Print`, `?handler=Text` | Core; Web `A_list_prints…` |
| FR-MEAS-05 (shopping fields) | Done: section, pantry and package per food on `/manage/conversions` | `FoodCatalogService.UpdateShoppingAsync`, `ShoppingRules.ValidateFood` | Web `Editors_set_a_foods_section…` |
| FR-EXP-01 / AC-24 / AC-12 | Done: one column, ingredients before steps, no navigation, the view's scale/units/weights, source credit and URL; a 15-ingredient, 10-step recipe at 2x with weights prints on one Letter page | `/recipes/{slug}/print`, `_PrintLayout`, `print.css`, `RecipeViewModel` | Web `FR_EXP_01_PrintCookExportPageTests`; E2E W6 PDF (PdfPig page count) |
| FR-EXP-02 / AC-25 | Done: `export.md` and `export.zip` (images/finish.webp, images/step-NN.webp, relative links); a scaled view writes scaled lines and `scale:` | `RecipeExporter`, `RecipeMarkdown.Write` (scale, images) | Core `FR_EXP_02_RecipeMarkdownWriteTests`; Infra `FR_EXP_02_RecipeExportTests`; Web |
| FR-EXP-03 | Done: `/recipes/export.zip` and `/collections/{slug}/export.zip`, one folder per recipe; readers get published only; search rate limit | `RecipeExporter.BulkIdsAsync`/`WriteBulkZipAsync`, `BulkExportModel` | Infra; Web; `SEC_63` bulk limit |
| FR-VIEW-02 / AC-23 | Done: cook mode with large type, ticks in this browser only, Wake Lock where supported, timers from step text | `/recipes/{slug}/cook`, `TimerRules` (Core), `cook.js` | Core `FR_VIEW_02_TimerRulesTests`; Web; E2E W6 at phone width |
| AC-70 / AC-75 / AC-76 | Extended to the M5 routes, forms and list items | — | Web `AC_70_AuthMatrixTests`, `SEC_30_FormTokenTests`, `Item_text_is_encoded_on_the_page` |
| Migration | `M5_Output`: ShoppingList, ShoppingListRecipe, ShoppingLine, ShoppingCheck, ShoppingListName; Food.PackageName | `Data/Migrations` | Infra store tests |

### M5b AI exchange (built 2026-11-23 while the owner was away; merged PR #14 — owner review of ADR 0033 pending)

Scope decisions: ADR 0027 (split), **ADR 0033 (scope and flow — for owner review)**. No new tables or packages.

| ID | Status | Implementation | Tests |
| --- | --- | --- | --- |
| FR-EXP-04 / AC-26 / BR-90, BR-91 | Done: one recipe, a collection or all recipes; instruction header, goal(s), optional My Kitchen (tool names) and units, recipe-md with source; nothing personal; no AI calls | `AiPromptBuilder.Compose`, `AiExchangeService.RecipesAsync`/`KitchenAsync`, `/recipes/{slug}/export-ai`, `/collections/{slug}/export-ai`, `/recipes/export-ai` | Core `FR_EXP_07_AiPromptBuilderTests`; Infra `FR_EXP_05_AiExchangeServiceTests`; Web `FR_EXP_04_AiExchangePageTests` |
| FR-EXP-07 / AC-28 / BR-94, BR-95, BR-97 | Done: goal picker with details, last time, constraints, ask-first; editable preview; Copy and Download use exactly the shown text; typed text quoted on one line | `AiPromptBuilder.Build`, `ai-export.js` | Core; Web; E2E W10 (edited preview copied to the clipboard) |
| BR-96 / AC-29 | Done: reduce and increase of the same ingredient block the preview until "Keep both"; no goals → general prompt | `AiPromptBuilder.Conflicts` | Core; Web |
| FR-EXP-05 / AC-27 / BR-92, BR-93 | Done: blocks found in chat text and code fences, matched by slug (or the page's recipe), checked, diffed field by field and line by line; saved as a draft variant (or published, or replacing the original after a version check); source kept from the original; Changes and the adapted note in the notes | `AiReplyReader`, `AiVariantRules`, `AiExchangeService.ReviewAsync`/`SaveAsync`, `/manage/import/ai-response`, `RecipeService.SetParentAsync` | Core `FR_EXP_05_AiReplyReaderTests`; Infra; Web; E2E W10 |
| FR-EXP-06 / AC-25 | Done: `/docs/recipe-md.schema.json` (static), and matching Core rules that the export output passes | `wwwroot/docs/recipe-md.schema.json`, `RecipeMarkdownValidator` | Core `The_writers_output_passes_the_schema_rules…`; Infra export test; Web `The_schema_is_public_json` |
| FR-REC-05 (links) | Done for AI variants: "Variant of …" and the variants list on the recipe page (readers see published ones) | `RecipeView.Parent`/`Variants` | Infra; Web |
| FR-EXP-08 | **Deferred** (ADR 0033): templates are built in; no prompt history | — | — |
| AC-70 / AC-75 / AC-76 | Extended to the M5b routes and forms; reply text and typed goals are encoded | — | Web `AC_70_AuthMatrixTests`, `SEC_30_FormTokenTests`, `Typed_text_is_quoted_and_encoded_on_the_page` |

### M6 Nutrition and allergens (built 2026-11-23; PR into `develop` — owner review of ADRs 0034 to 0036 pending)

Scope decisions: owner chose the recommendations (2026-11-23); ADR 0034 (dataset, mappings, label foods; upload page in
M7, FR-NUT-06 in M8), ADR 0035 (calculation on view, completeness, imported and manual panels), ADR 0036 (allergen
rules, stored for search, "free of").

| ID | Status | Implementation | Tests |
| --- | --- | --- | --- |
| D-06 | Done: SR Legacy 2018-04 (7,793 foods, 9 nutrients, portions) bundled as a trimmed FDC zip and applied once; no network | `tools/seed/build-nutrients.py`, `Data/Seed/fdc-sr-legacy-2018-04.zip`, `NutrientDatasetImporter.ApplySeedAsync` | Infra `The_bundled_sr_legacy_dataset…` |
| FR-NUT-01 / BR-50 / AC-55 | Done: panel per serving labelled from source, calculated (with %), or entered by hand; manual > imported > calculated | `NutritionRules.Choose`, `_NutritionPanel`, `/manage/recipes/{id}/nutrition`; imported from JSON-LD and recipe-md | Core; Importers `FR_NUT_01_ImportedNutritionTests`; Infra; Web |
| FR-NUT-02 / BR-51 | Done: lines weighed by weight, density, portions, packages or purchase grams (yields); completeness; "partial" below 70%; low end of a servings range | `NutritionRules.Grams`/`Calculate`, `NutritionService.GetAsync` | Core `FR_NUT_01_NutritionRulesTests`; Infra |
| FR-NUT-03 / BR-54 / AC-57 | Done: unmatched report by recipe count (no food, no data, removed USDA row); mapping by search creates or updates a food; mappings beat automatic matches | `NutritionService.UnmatchedAsync`/`SearchAsync`/`MapAsync`, `/manage/nutrition` | Infra; Web |
| FR-NUT-04 / AC-57 | Done: label foods per 100 g (g or ml with density) | `NutritionRules.FromLabel`, `NutritionService.AddLabelFoodAsync` | Core; Infra; Web |
| FR-NUT-05 / AC-58 | Done except the upload page (M7): any FDC CSV zip (SR Legacy or Foundation) upserted by FDC id, removed foods flagged, mappings kept, version recorded; Admin CLI `recipewarden nutrition-import <zip>` | `NutrientDatasetImporter`, `FdcCsv`, `NutritionImportCommand` | Infra `A_newer_dataset_keeps_mappings…`; Cli `FR_NUT_05_NutritionImportCommandTests` |
| FR-NUT-06 | **Moved to M8** with the AI parser (ADR 0034) | — | — |
| FR-ALG-01 / AC-56 | Done: 9 US majors + gluten by word rules (FDA 2025 tree nut list), May contain for vague lines, suppressed by "gluten-free", "vegan" etc.; badges on the recipe page | `AllergenRules`, `RecipeAllergenIndex` (on save, on linking, backfill `allergens-v1`) | Core `FR_ALG_01_AllergenRulesTests`; Infra; Web; E2E |
| FR-ALG-02 / BR-52 | Done: per-allergen overrides (Contains, May contain, Not present) that survive re-detection | `NutritionService.SetAllergensAsync`, `/manage/recipes/{id}/nutrition` | Infra; Web `Soy_sauce_shows_soy_and_wheat…` |
| FR-ALG-03 | Done: footer line plus a disclaimer on the panel and the filter | `_NutritionPanel`, `_RecipeList` | Web |
| BR-53 / FR-SRCH-02 | Done: "free of" checkboxes on `/recipes` and `/search`; unmatched recipes hidden unless "include unverified" | `SearchCriteria.FreeOf`, `RecipeSearchService` | Infra `Free_of_hides…`; Web; E2E |
| AC-25 (extras) | Exports carry `allergens:` and the `nutrition:` panel; the schema lists them | `RecipeMdExtras`, `recipe-md.schema.json` | Core `FR_NUT_01_NutritionTextTests`; Infra |
| AC-70 / AC-75 | Extended to the M6 pages and forms | — | Web `AC_70_AuthMatrixTests`, `SEC_30_FormTokenTests` |
| Migration | `M6_Nutrition`: NutrientRef, NutrientDataset, NutritionFacts, RecipeAllergen; Food.NutrientRefId | `Data/Migrations` | Infra |

#### M0 deployment check (2026-11-20, test host)

- `recipewarden.service` enabled and active; runs from `/opt/recipewarden/current` (release folder under `releases/`); ~60 MB RAM.
- Kestrel bound to the host's LAN IP on port 5080 (cloudflared in a separate container per SEC-60). `/health` returns 200.
- Data Protection key ring at `/var/lib/recipewarden/keys` (owner `recipewarden`, mode 700). Same key reused after
  `systemctl restart` (no new "No XML encryptor configured" warning), so keys persist outside the release folders.
  That warning is expected: keys are unencrypted at rest, protected by folder permissions.
- `backup-folders.conf` drop-in present, generated by `apply-config`, with `ReadWritePaths="-/var/lib/recipewarden/backups"`.
- Not yet exercised on the host: sign-in via `recipewarden admin create` + browser, Cloudflare Tunnel route.

### M7 Operations (built 2026-11-23 while the owner was away; PR into `develop` — owner review of ADRs 0037 to 0041 pending)

| ID | Status | Implementation | Tests |
| --- | --- | --- | --- |
| FR-BKP-01 / AC-80, BR-60 | Done | `BackupService` (online backup, integrity_check, tar.gz, manifest, SHA-256s, 0600) | Infra `FR_BKP_01_BackupServiceTests.Back_up_now_…`; Web `FR_BKP_01_BackupPageTests.Back_up_now_…`; Cli `FR_BKP_07_CliOperationsTests` |
| FR-BKP-02, FR-BKP-14 / AC-89 | Done (Cronos, server local time) | `BackupScheduleRules` (Core), `BackupScheduleService`, `BackupSchedulerService` (every minute) | Core `FR_BKP_02_ScheduleRulesTests`; Infra `FR_BKP_02_ScheduleServiceTests.A_failed_run_is_retried_…` |
| FR-BKP-03 / AC-81, BR-61 | Done | `RetentionRules` (Core), `BackupService.ApplyRetentionAsync` | Core `FR_BKP_03_RetentionRulesTests` (60 simulated days); Infra `Retention_deletes_only_the_schedules_own_archives…` |
| FR-BKP-04 / AC-82, BR-62, BR-63, SEC-54 | Done | `RestoreService` (quarantine, checks, apply with rollback), `MaintenanceMiddleware`, two-step page | Infra `FR_BKP_01_BackupServiceTests` (restore, `..`, links, caps, checksum, unlisted, newer); Web `FR_BKP_01_BackupPageTests`; E2E W7 |
| FR-BKP-05 | Done | run history with download/delete; schedule changes audited (interceptor) | Web `Schedules_take_only_allowed_folders_and_changes_are_audited` |
| FR-BKP-06 / AC-83, AC-84, BR-64, BR-66, SEC-15 | Done | `install.sh --restore`, `SetupService`, `/setup`, CLI `setup-token` | installer `test-install.sh` (AC-83 block); Web `SEC_15_SetupPageTests`; Infra `SEC_15_SetupTokenTests` |
| FR-BKP-07 / AC-85, BR-65 | Done | CLI `restore` (root orchestration; `--check`/`--apply` as the service user) | Cli `AC_85_RestoreOrchestratorTests`, `FR_BKP_07_CliOperationsTests`; `test-install-systemd.sh` (newer archive refused, restore while running) |
| FR-BKP-09 audit | Done | `internal-audit` as the service user after apply-config | Cli `Internal_audit_records_an_entry` |
| FR-BKP-10, 11, 12, 13 / AC-87, AC-88, SEC-74 | Done | `BackupFolders`, `BackupDestinationRules` (Core); built-in folder marker (ADR 0037) | Core `FR_BKP_10_DestinationRulesTests`; Infra marker, partial, run-time folder and Test tests; Web Test button |
| FR-BKP-15 / AC-89, SEC-75 | Done (ADR 0039) | `BackupCrypto`, `BackupKeyService`, recovery kit download | Infra encrypted round trip, wrong passphrase, other kit, tampered/cut archive; Web kit download, no private key stored |
| FR-ADM-01 / AC-74, SEC-72 | Done (ADR 0041) | `AuditInterceptor`, `AuditLog`, `HttpAuditActor`, `/admin/audit`, sign-in events | Core, Infra, Web audit tests |
| FR-AUTH-03, SEC-12 / AC-72 (last Admin) | Done | `UserAdminRules` (Core), `UserAdminService`, `/admin/users` | Core `SEC_12_UserAdminRulesTests`; Web `SEC_12_UsersPageTests` |
| FR-OPS-02 / AC-98 | Done | `HealthService`, `/admin/health`, `/admin` dashboard, CLI `health` | Infra `Report_shows_…`, `Volume_figures_match_df_within_one_percent`; Web `Health_page_shows_…` |
| FR-OPS-03 / AC-99, BR-81, BR-82, BR-85 | Done | daily `StorageSnapshot`, projection, Admin banner, server-drawn SVG chart | Core projection tests (30 days × 100 MB, 3 GB free ≈ 30 days); Infra and Web low-space tests |
| FR-OPS-04, BR-83 | Done | integrity check, VACUUM, purge, move guide on `/admin/health` | Infra `Integrity_check_and_compact`; Web maintenance actions |
| FR-OPS-05 | Done | `/health/ready` (503 only when Critical); webhook (text/Discord/JSON) through `SafeFetcher.SendAsync` | Web `Low_space_gives_warning_with_banner_then_critical_with_503`, webhook tests |
| FR-NUT-05 page | Done | `/admin/nutrition-data` (upload through `UploadValidator.SaveArchiveAsync`) | Web `Nutrition_data_page_…` |
| FR-INST-03 (full pre-upgrade backup) | Done | `install.sh` runs `recipewarden backup --kind pre-upgrade` when the installed CLI has it | `test-install.sh` |
| SEC-60 (installer firewall) | Done | ufw when active, else nftables with an `ExecStartPre=+` drop-in; `--no-firewall` | shellcheck; manual check on a LAN-bound host is pending |

### M8 Hardening and v1.0.0 (built 2026-11-23 while the owner was away; PR into `develop` after M7 — owner review of ADRs 0042 to 0046 pending)

| ID | Status | Implementation | Tests |
| --- | --- | --- | --- |
| FR-IMP-09 / AC-17, SEC-44, SEC-46, SEC-47 | Done (ADR 0042) | `AiParserRules` (Core), `AiProtocol` (Importers), `AiParserService`, `ImportService.WithAiAsync`, "Re-parse with AI" on review | Core `FR_IMP_09_AiParserRulesTests`; Importers `FR_IMP_09_AiProtocolTests`; Infra `FR_IMP_09_AiParserServiceTests` (off = no request; Claude and OpenAI-compatible; bad reply; cap); Web `FR_IMP_09_AiPageTests` |
| FR-IMP-10 / AC-18, SEC-45 | Done | `/admin/ai` (write-only key protected with Data Protection), `AiCallLog`, monthly total on `/admin`, token cap, Test connection | Infra key encrypted at rest and never in audit; Web settings, test connection, dashboard total |
| FR-NUT-06 | Done | "Suggest a match with AI" on `/manage/nutrition`; candidates from `NutritionService.SuggestionCandidatesAsync` | Infra `FR_NUT_02_NutritionServiceTests` (candidates); Web `Nutrition_mapping_offers_an_AI_suggestion…` |
| FR-AUTH-04, SEC-04 / AC-73 | Done (ADR 0043) | `/account/2fa` (QRCoder, MIT), `/account/login-2fa`, recovery codes, Admin prompt | Web `SEC_04_TwoFactorTests` |
| SEC-01, SEC-03, SEC-63, `/account/password` | Done | `CommonPasswordValidator` (SecLists list, MIT), remember-me cap, `SignInPolicy` | Web `SEC_01_PasswordTests`, `SEC_63_SignInRateLimitTests` |
| FR-HOME-02 / AC-60 | Done (ADR 0044) | `RecommendationRules` (Core), `RecommendationService`, `_RecommendationRow`, `Home:Hemisphere` | Core `FR_HOME_02_RecommendationRulesTests`; Infra `FR_HOME_02_RecommendationServiceTests`; Web `FR_HOME_02_HomeRecommendationTests` |
| NFR-01 / AC-95 | Done (ADR 0045) | search matches through an FTS subquery; ranking only for relevance | Web `AC_95_PerformanceTests` (5,000 recipes; runs alone) |
| NFR-03 / AC-96 | Done (ADR 0045) | in-page checker `a11y-check.js` (axe-core is MPL-2.0) | E2E `AC_96_AccessibilityTests` (23 pages × 2 themes, self-test, focus) |
| AC-61 | Done | — | E2E `AC_61_ThemeFirstPaintTests` |
| SEC-31 | Done (fix: `/admin` no longer writes on view) | `HealthService` writes "tracking since" only from the monitor | Web `M8_SecurityPassTests.Get_requests_never_write_to_the_database` |
| SEC-20, SEC-22, SEC-24, SEC-53, SEC-62, SEC-70, SEC-80 to 84 | Tested in M8 (ADR 0043) | — | Web `M8_SecurityPassTests`; Importers `SEC_53_PdfActiveContentTests` |
| SEC-64 | Manual (Cloudflare dashboard) | `docs/install.md` | — |
| FR-EXP-08 | Still deferred (ADR 0033, 0046) | — | — |

### Notes and open issues

- `/error` (SEC-73) and `/account/theme` (FR-UI-01) are not listed in section 7.2. They are in the
  auth-matrix table as Anonymous and Signed in respectively; add them to 7.2 at the next spec revision.
- Requests that match no route are denied by the fallback policy (redirect to sign-in for anonymous
  visitors) instead of returning 404, so probing cannot map routes. Signed-in users get the 404 page.
- **Resolved (2026-11-23, ADR 0028):** D-10's anonymous check-off is a narrow documented exception — one POST
  handler with antiforgery and its own per-IP rate limit (option a).
- **Resolved (2026-11-23, ADR 0038, option a, for owner review):** Data Protection key ring in backups.
  FR-BKP-01 defines a backup as database + images + config, so `<data>/keys` (SEC-71) is not included.
  Restoring on a new host (FR-BKP-06) would then sign everyone out and leave AI API keys (SEC-45)
  undecryptable. Options: (a) include `keys/` in the archive — backups are already encrypted (9.6.1,
  SEC-75), so this adds no exposure; add a round-trip test that a restored AI key still decrypts;
  (b) exclude keys deliberately and document that a host move needs re-sign-in and AI keys re-entered;
  (c) export the key ring separately, e.g. with the restore kit. Tentative recommendation: (a).
  Not discussed yet — owner wants to revisit later.

- **Done in M2 (2026-11-22):** the two items below (tag picker, ADR 0016; step headings, ADR 0015).
- **Deferred to M2 by owner (2026-11-21), found while testing M1 on the host:**
  - **Tag picker.** Tags are typed as one comma-separated text box, which works poorly with the built-in typed
    tags (ADR 0011). Needs a picker that shows tags grouped by type with multi-select, plus typing a new tag.
  - **Step headings.** A pasted list such as `1. **Preheat oven**` followed by an indented line
    `Preheat oven to **375°F**.` becomes two steps per item, and the `**` shows literally, because the paste
    splitter makes one step per line and step text is plain. Needs: numbered items with continuation lines kept as
    one step, an optional step heading, and a decision on bold in step text (restricted Markdown per SEC-22, or
    strip it). Spec is silent on step headings; raise options (e.g. a `Step.Title` column vs. Markdown) at M2 start.

- **M1 notes (2026-11-21):**
  - Two bugs found by the M1 tests and fixed: MVC binds form values before route values, so a posted `id`
    could retarget a recipe edit (route ids now bind with `[FromRoute]`); and Razor adds no antiforgery token
    to a form with a literal `action`, so post forms (M0's sign-out included) failed with 400 in a browser.
  - BR-11 is applied to any change away from Web (not only Web→Personal), so an Editor cannot hide the credit
    by switching to Manual. Spec v0.8 BR-11 says so.
  - Rate limits are configurable (`RateLimits:SearchPerMinute`, `RateLimits:WritesPerMinute`); the admin
    settings page (M7) will expose them.
  - Not exercised on the host yet: the new pages behind the tunnel. The packaged build was started locally and
    served `/`, `/recipes`, `/search` and `/collections` with the M1 migration applied.
  - Found on the first deployed M1 build (owner, 2026-11-21): servings and times needed ranges ("serves 6-8",
    "20-25 min", "1 hr 15 min"). Fixed by ADR 0010; default tags added with ADR 0011.

- **M2 notes (2026-11-22):**
  - Two M1 editor tests checked the old comma-separated tag box and its datalist; they now check the same thing in
    the picker (tags shown as ticked; built-in tags offered as checkboxes). `Input.Tags` keeps its name for new tags.
  - Two M1 persistence tests compared step text as strings; steps now carry an optional heading (`StepView`), and
    the tests compare the same text with an empty heading.
  - Core branch coverage is 91.9% (gate 85%); M1 ended at 95.4%. The new engine adds many small branches.
  - Seed: the USDA cooking-yield table gives a min–max range for only some cuts, so 7 yield conversions are seeded
    (not ~15); turkey, fish and shrimp have no ranged data. Households can add their own on `/manage/conversions`
    later (adding yields from the page is not in M2; editing is).
  - The display uses BR-21 tolerances to pick a unit, so an ordinary recipe may show "⅔ cup" for 10 Tbsp (6.7% off);
    baking/precision recipes keep 10 Tbsp.
  - Not exercised on the host yet: the seed on a real M1 database (covered by the M1→M2 upgrade test) and the
    toolbar behind the tunnel.

- **M4 notes (2026-11-22):**
  - New packages (all permissive): AngleSharp 1.10.5 (MIT), DocumentFormat.OpenXml 3.5.1 (MIT), PdfPig 0.1.16
    (Apache-2.0), SkiaSharp 4.157.0 and SkiaSharp.NativeAssets.Linux.NoDependencies 4.157.0 (MIT). The package
    contains `libSkiaSharp.so`; no OS packages are added.
  - No existing assertion changed. The access table, the form-token page list and the sample seed were extended for
    the new routes, and the test factories gained a pretend internet (`tests/Shared/FakeWeb.cs`), so no test reaches
    the network.
  - SEC-42 now also accepts `application/xhtml+xml` pages (still HTML), and the size cap applies after decompression.
  - Step photos are added after the first save (a step must exist first). Imported pages' step images are not copied.
  - Not exercised on the host yet: the M3→M4 migration on a real database, real recipe sites through the tunnel, and
    image upload from a phone.

- **M5 notes (2026-11-23, built unattended at the owner's request):**
  - Decisions not taken by the owner are in ADR 0032, marked for review.
  - One existing assertion was extended, not weakened: the M3 seed test listed every seed run and now includes
    `shopping-v1` (each seed still runs once). The access table, the form-token page list and the sample seed (a
    list with id 1) were extended for the new routes.
  - The recipe page model became `RecipeViewModel` (shared by the reading view, print, cook mode and export); the
    reading view's behaviour is unchanged (all M1–M4 page tests pass).
  - No new packages. Bulk and single zips are built in a temporary file that is deleted when the response ends.
  - Not exercised on the host yet: the M4→M5 migration and seed on a real database, printing from a real phone or
    desktop browser, and Wake Lock on the owner's phones.

- **M5b notes (2026-11-23, built unattended):**
  - Open points are in ADR 0033 for owner review; FR-EXP-08 is deferred there.
  - No existing assertion changed; the access table and the form-token page list were extended.
  - A bare (unfenced) recipe-md block runs to the next block or the end of the reply, so chat text after the last
    bare block lands in its last section. Fenced blocks end at the fence. The review page shows what was read.

- **M7 notes (2026-11-23, built unattended):**
  - Decisions are in ADR 0037 to 0041, all marked for owner review.
  - Two existing assertions changed with the behaviour, not weakened: (1) `test-install.sh` expected `--restore` to
    exit 3 "milestone M7"; it now checks that a missing archive is refused and runs a full restore-at-install
    scenario. (2) The installer tests and `build/smoke-test.sh` expected `recipewarden health` to print
    "Status: OK"; the command now also judges the machine's disk (BR-81), which on a build machine can be Warning or
    Critical, so those checks assert the database line (`quick_check: ok`) — the thing they meant. The CLI unit tests
    of the database basics are unchanged.
  - Every web and browser test host now uses a simulated roomy disk and runs the scheduler and monitor by hand
    (`Operations:Background=false`), so tests never depend on the build machine.
  - New package: Cronos 0.13.0 (MIT). Encryption uses .NET's own cryptography.
  - Not exercised on the host yet: the M6→M7 migration on a real database, a NAS share with its marker, the ufw/nftables
    rule on a LAN-bound host, Cloudflare's upload limit on a real restore, and the webhook to the owner's ntfy/Discord.

- **M6 notes (2026-11-23):**
  - Foundation Foods was recommended alongside SR Legacy, but its download is blocked from the build machine. The
    importer reads it as is (`recipewarden nutrition-import`), so it can be added on the host or bundled later.
  - One existing assertion was extended, not weakened: the seed-run list now includes `nutrients-sr-legacy-2018-04`
    and `allergens-v1` (each still runs once). The access table and the form-token page list were extended.
  - Pasted text in recipe-md format is now read with the recipe-md reader (keeping every field), not the loose
    text reader.
  - The seeded test database is larger (7,793 nutrient rows), so test runs take about 20 s longer locally.
  - Not exercised on the host yet: the M5→M6 migration and the 7,793-row seed on a real database, and allergen
    badges on the owner's own recipes (word rules may need tuning — ADR 0036 is for review).
