# ADR 0001: Render the theme on the server

- Status: Accepted (M0, 2026-11-17)
- Requirements: FR-UI-01, UX-03, AC-61, SEC-23, SEC-31

## Context
UX-03 asks for the theme to be applied before first paint without inline script, suggesting
"a tiny external script reads the cookie". An external script in `<head>` blocks rendering and still
races the stylesheet on slow connections; it also cannot know a signed-in user's profile theme.

## Decision
The server writes `<html data-theme="light|dark|system">` from the profile (signed-in users) or the
`rw-theme` cookie (everyone else). CSS handles `system` with `prefers-color-scheme`. `theme.js`
(external, deferred) only handles the toggle: it sets the cookie client-side (SEC-31) and, for
signed-in users, POSTs to `/account/theme` with the antiforgery header. At sign-in the profile theme
is copied into the cookie so pages after sign-out match.

## Consequences
- No flash of the wrong theme, no inline script, no render-blocking script.
- AC-61 can be checked in integration tests (server HTML) as well as in Playwright.
- Spec wording for UX-03 should say "server-rendered attribute; external script for the toggle"
  at the next spec revision.
