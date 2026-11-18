# ADR 0002: Content-Security-Policy without a script nonce

- Status: Accepted (M0, 2026-11-17)
- Requirements: SEC-23, UX-03

## Context
SEC-23 lists `script-src 'self' 'nonce-…'`. A nonce exists only to allow inline scripts, and UX-03
and SEC-23 both forbid inline script and inline event handlers.

## Decision
Ship `script-src 'self'` with no nonce. htmx is served locally and configured through a
`<meta name="htmx-config">` tag with `includeIndicatorStyles:false`, `allowEval:false` and
`allowScriptTags:false`, so `style-src 'self'` holds and htmx cannot evaluate script.
A test asserts pages contain no inline `<script>`, no `on*=` attributes and no `style=` attributes.

## Consequences
- Strictly tighter than the spec. If an inline script is ever genuinely needed, re-introduce a
  per-request nonce through middleware and update this ADR.
- Spec SEC-23 should drop `'nonce-…'` at the next spec revision.
