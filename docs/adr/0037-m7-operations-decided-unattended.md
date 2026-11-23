# ADR 0037: M7 Operations — scope and details decided while the owner was away (for review)

- Status: Proposed — **owner review requested** (2026-11-23). Built as described; each point is easy to change.
- Requirements: FR-BKP-01 to 15, FR-ADM-01, FR-OPS-02 to 05, FR-AUTH-03, SEC-12, SEC-15, SEC-60, BR-60 to 66,
  BR-80 to 85; AC-72, AC-74, AC-80 to 89, AC-98, AC-99; ADR 0038 to 0041

## Context
The owner asked for M7 and M8 to be built without stopping for questions (2026-11-23) and for every choice to be
recorded. ADR 0038 to 0041 hold the larger decisions; this ADR holds the smaller ones.

## Decisions
1. **M7 scope** = section 9.10 item 9 plus everything 7.2 lists under `/admin` that did not exist yet: the dashboard,
   `/admin/users`, `/admin/settings`, `/admin/audit`, `/admin/backups`, `/admin/health`, `/admin/nutrition-data`, and
   `/setup`, `/health/ready`. `/admin/ai`, TOTP (`/account/2fa`), `/account/password`, the common-password list and the
   sign-in rate limit stay in M8 (the security pass).
2. **Default retention** for a new schedule: keep the newest 3, plus 7 daily, 4 weekly and 6 monthly. Tiers count the
   last N days, ISO weeks and months that *have* an archive (restic-style), so a gap does not shorten the history.
   Schedule times are in the server's local time zone; retention day boundaries too.
3. **Schedules** are stored as a cron expression; the form offers Daily or Weekly at a time, or a raw cron
   (Cronos, MIT). Retention is four integer columns rather than `RetentionJson`, so it can be queried and validated.
4. **Built-in folder marker**: `<data>/backups` is always allowed (FR-BKP-08) and lives inside the data directory,
   so the app (and the installer) create its `.recipewarden-backup-target` marker. Every other folder's marker is
   created by the server owner, as FR-BKP-12 says.
5. **Off-server backups must be encrypted**: until the key exists, backups can only go to the built-in folder
   (unencrypted, 0600, on the server). Saving a schedule to any other folder needs the key first. This reads
   FR-BKP-15 ("encrypted before leaving the server") literally and keeps the installer's pre-upgrade backup possible
   before an Admin has made a key.
6. **Name collisions**: two runs in the same second get `-1`, `-2` … before the extension.
7. **Upload size**: 100 MB through the browser (Cloudflare's free-plan limit; `Backup:MaxUploadBytes` changes it).
   Bigger archives go through `sudo recipewarden restore <file>` or `install.sh --restore`. Same for the nutrient
   dataset page.
8. **Restore in the browser is two requests**: the archive is checked in a quarantine folder and a report is shown
   (date, version, counts); typing `RESTORE` then applies it. An abandoned check is removed after 2 hours.
   The restore runs inside the request with maintenance mode on (BR-63); every other request gets a 503 page,
   `/health` still answers.
9. **Backup history survives a restore**: run records that the archive does not have (the pre-restore backup among
   them) are copied into the restored database, so every archive on disk stays listed and downloadable.
10. **Settings page** edits what people change in the app: site name, disk thresholds (BR-81) and the webhook.
    Network, trusted proxies, rate limits and backup folders stay in the server owner's files and are shown
    read-only with how to change them (they need a restart and, for folders, `apply-config`).
11. **Webhook formats**: plain text (ntfy), Discord (`{"content"}`) or JSON. Posted once per status change, never
    on the first evaluation. A LAN address (e.g. ntfy at 192.168.1.x) needs the "on my home network" box, which
    permits exactly that host and port (ADR 0041).
12. **Health monitor**: evaluates every 15 minutes (`Health:IntervalMinutes`), one storage snapshot a day, quick_check
    daily and integrity_check weekly (a full check counts as the day's quick check). Results kept 90 days,
    snapshots 400 days. Opening `/admin` or `/admin/health` evaluates on the spot.
13. **BR-84 with no backup yet**: the age counts from when the app first measured health, so a fresh install is not
    flagged until 48 hours (or 2x the schedule) have passed; Critical after 7 days, as for an old backup.
14. **`recipewarden health` exit codes** follow `/health/ready`: 0 for OK or Warning, 2 for Critical. It prints the
    database basics (as before) and every check.
15. **Users page**: Admins create accounts with a first password (no email is sent; there is no mail server) and a
    role of Admin, Editor or None (a signed-in reader). Lock, unlock, set password and delete are there too.
    Role changes, locks and password resets update the security stamp, so other sessions end within 5 minutes.
16. **Admin banners** (FR-OPS-03, FR-BKP-14) show on every page for Admins only, from the latest evaluation.

## Consequences
If the owner changes any of these, the change is local: one Core rule, service or page, each with tests.
