# ADR 0024: URL import uses a browser-like agent, runs in the request, and offers a paste fallback

- Status: Accepted (2026-11-22, owner)
- Requirements: FR-IMP-01, FR-IMP-06, SEC-40 to SEC-43, UX-10, AC-10, AC-13, W3

## Context
Many recipe sites refuse requests that do not look like a browser. SEC-42 caps a fetch at 10 s. UX-10 wants step
progress. The spec is silent on the User-Agent, and on what happens when a site refuses.

## Decision
1. **User-Agent:** SafeFetcher sends a current desktop-browser User-Agent, `Accept` and `Accept-Language` headers
   (owner's choice). It sends no cookies, runs no scripts and fetches one page per user request.
   Every SSRF rule (SEC-40 to SEC-42) still applies: DNS vetted first, connect to the vetted IP, re-check each
   redirect (max 3), 10 s, 5 MB, and the content-type allow-list. The User-Agent is configurable
   (`Import:UserAgent`).
2. **Paste fallback:** when a site refuses (403, 429, a challenge page, or no recipe found), the import page offers
   "Paste the page text or saved HTML". The pasted text goes through the same extractors (JSON-LD, microdata,
   heuristics) and the same review. The URL is kept as the source (BR-10).
3. **In the request:** fetch, extract and parse run during the POST, with an htmx progress indicator ("Fetching…",
   "Reading the recipe…"). The result is stored as an ImportJob, and the browser goes to its review page.
   No background worker.
4. The raw payload is kept 30 days under `<data>/imports/` (FR-IMP-08). The purge service removes it.

## Consequences
- A browser-like agent gets through more sites. It still cannot pass JavaScript challenges; the paste fallback
  covers those.
- A slow site holds one request for up to about 10 s; rate limits (SEC-43: 20 an hour per user) keep this bounded.
