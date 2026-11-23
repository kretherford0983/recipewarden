# ADR 0042: The optional AI parser (M8)

- Status: Proposed — **owner review requested** (2026-11-23). Built unattended; each point is easy to change.
- Requirements: D-08, FR-IMP-09, FR-IMP-10, FR-NUT-06, BR-90, SEC-44 to 47, AC-17, AC-18; ADR 0026, 0041

## Decisions
1. **Off by default.** Nothing reaches any AI host until an Admin turns the parser on at `/admin/ai`. A test
   asserts that no request is made while it is off (AC-17).
2. **Providers**: the Claude API (Messages API, host fixed by name, `anthropic-version: 2023-06-01`) or any
   OpenAI-compatible chat-completions endpoint (Ollama, LM Studio, a hosted service). Plain `HttpClient` through
   `SafeFetcher.SendAsync`; no SDK package. A LAN endpoint is allowed only when the Admin ticks "on my home network",
   and then only that scheme, host and port (ADR 0041).
3. **API key**: encrypted with ASP.NET Core Data Protection (keys in `<data>/keys`, so they move with backups, ADR
   0038), write-only in the UI ("set" / "replace" / "remove"), never logged, audited only as "AI settings changed".
4. **When it runs** (FR-IMP-09): automatically during an import when the rules found no recipe, no ingredients or no
   steps, or when 30% or more of at least three ingredient lines parse with low confidence; and on demand with
   "Re-parse with AI" on the review page, which reads the kept raw text again. The AI result replaces the draft only
   on review; nothing is saved without a person pressing Save.
5. **What is sent** (SEC-46): only the extracted recipe text, cleaned of control characters and cut to 24,000
   characters; for nutrition mapping, only the ingredient name and up to 12 local candidate food names.
6. **What comes back** (SEC-47): at most 512 KB and 4,096 output tokens, 60-second timeout. The reply must be
   recipe-md v1; it is read by the same reader as any pasted recipe-md (HTML stripped, sanitized), and fields are
   marked "filled by the AI parser" on review. A reply that is not recipe-md falls back to the rules-based result
   with a notice. The model has no tools and nothing it returns is executed or rendered as HTML.
7. **Usage** (FR-IMP-10): every call is logged (purpose, provider, model, tokens in/out, duration, result, user) and
   shown at `/admin/ai`; the dashboard shows this month's tokens. An optional monthly token cap pauses AI parsing
   (the rules-based parser is used, with a notice) until the next calendar month (UTC). "Test connection" sends a
   ten-token request with the saved settings.
8. **Nutrition mapping suggestions** (FR-NUT-06): on `/manage/nutrition`, "Suggest a match with AI" offers the
   model the local search results (all words), or, when that finds nothing, foods matching any word ranked by how
   many words match. The model may only pick one of them by number; an Editor confirms with "Use this suggestion".
   Nutrition data itself stays local (no online lookups).

## Consequences
- No new package. Tests use a fake endpoint (FakeWeb), so CI never calls a real AI service.
- Prompts are in `AiParserRules` (Core) and can be tuned without touching the plumbing.
