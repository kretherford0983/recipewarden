# ADR 0041: Audit log through EF Core, and the single LAN exception for outbound calls

- Status: Proposed — **owner review requested** (2026-11-23).
- Requirements: FR-ADM-01, AC-74, SEC-72; FR-OPS-05, SEC-41, SEC-44

## Audit log
1. **Every write** that goes through EF Core is recorded by a SaveChanges interceptor: who (user id and email, or
   "anonymous" for shopping ticks, "system" for background work, "cli:&lt;os user&gt;" for commands), action
   (`Recipe.Create`, `UserRole.Delete`, …), entity and id, time (UTC) and client IP (CF-Connecting-IP when it came
   through the tunnel, SEC-61). Updates list the **names** of changed properties; a title, name or email is kept to
   make the log readable. **No values are stored**, so password hashes, keys and tokens cannot reach it (SEC-72).
2. **Not audited as entity writes**: seed data and migrations, the audit table itself, and bookkeeping rows written
   by the app (backup records, storage snapshots, health results, the allergen index, inventory snapshots, Identity
   tokens). A user update that only touches sign-in counters is the sign-in itself, recorded instead as
   `Account.SignIn` / `Account.SignInFailed` (with the address typed, never the password).
3. **Explicit events**: backup runs and failures, retention, restore, key creation, settings changes, maintenance
   actions, setup, and `apply-config` (recorded by the service user through a hidden CLI command, so no database file
   is ever written as root).
4. Append-only; no purge in v1. `/admin/audit` filters by action prefix, user, entity and dates, 50 a page.

## Outbound calls other than imports
`SafeFetcher.SendAsync` (POST/GET with headers, no redirects, a timeout and a response cap) serves the health
webhook now and the AI parser in M8. Addresses are checked like imports (SEC-40/41). The **one** exception is an
endpoint an Admin explicitly marks as being on the home network: exactly that scheme, host and port may then be
reached even though it is private or on a non-standard port (SEC-44's rule, applied to the webhook as well). Nothing
else on the LAN opens up, and the connection still goes to the vetted address only.
