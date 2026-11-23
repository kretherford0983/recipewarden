# ADR 0043: M8 security pass — account features and the rules that lacked tests

- Status: Proposed — **owner review requested** (2026-11-23).
- Requirements: FR-AUTH-04, SEC-01 to 04, SEC-20, SEC-22, SEC-24, SEC-31, SEC-53, SEC-62, SEC-63, SEC-70,
  SEC-80 to 84; AC-73

## Account features finished in M8
1. **Two-factor sign-in** (SEC-04): `/account/2fa` sets up an authenticator app from a QR code (QRCoder 1.9.0, MIT,
   rendered as an SVG data URI so the CSP needs no change) or the key typed in. Ten recovery codes are shown once and
   can be replaced. Turning it off needs a current code, so a borrowed session cannot remove it. `/account/login-2fa`
   is the second step; failures count toward lockout (SEC-02) and the sign-in rate limit, with one generic message.
   Admins without two-factor see a prompt on `/admin`. Every change updates the security stamp.
   Recovery when someone loses both the app and the codes (the spec is silent): another Admin can **turn two-factor
   off** for them at `/admin/users` (not for themselves; it clears the key and codes, ends sessions and is audited).
   A lone Admin uses `recipewarden admin create` on the server to make a second Admin.
2. **Common passwords** (SEC-01): a bundled list of 1,188 common passwords of 12 or more characters (from SecLists,
   MIT) plus "not your own email"; applied to new accounts, Admin resets and `/account/password`.
3. **Remember me** (SEC-03): a tick box on sign-in gives a persistent cookie; every session, remembered or not, ends
   30 days after sign-in at the latest.
4. **Sign-in rate limit** (SEC-63): 10 POSTs to `/account/login` per minute per client IP (the Cloudflare client IP
   behind the tunnel); the form itself is never limited. Configurable as `RateLimits:SignInsPerMinute`.
5. **`/account/password`**: change your own password with the current one; other sessions end within 5 minutes.

## Rules that now have tests (they were in place, but untested)
- **SEC-31** — a crawl of 30 GET and HEAD routes as an Admin and anonymously must leave SQLite's `data_version`
  unchanged. This found one real issue, fixed: the first `/admin` view stored "health tracking since"; now only the
  health monitor writes it.
- **SEC-53** — a PDF with an OpenAction script, a form field and an embedded file yields its page text only.
- **SEC-20/22** — no `FromSqlRaw`, `ExecuteSqlRaw`, `SqlQueryRaw`, `Html.Raw` or `new HtmlString` in the source,
  except the sanitized Markdown renderer.
- **SEC-24** — the web app, Infrastructure, Core and Importers never start a process; the CLI never uses a shell.
- **SEC-62** — security headers on pages, error pages and static files.
- **SEC-70, SEC-80 to 84** — the service unit keeps its hardening; Dependabot, analyzers, warnings-as-errors, the
  vulnerable-package and gitleaks gates, SHA-pinned actions and read-only default permissions stay in place.

## Not testable in the repository
- **SEC-64** (Cloudflare Access) is configured in the Cloudflare dashboard; `docs/install.md` describes it.
