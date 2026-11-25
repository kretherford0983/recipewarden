# ADR 0040: Restore paths, archive layout and the first-run setup page

- Status: Proposed — **owner review requested** (2026-11-23).
- Requirements: FR-BKP-04, 06, 07, BR-62 to 66, SEC-15, SEC-54, AC-82 to 85

## Decision
1. **Archive layout**: `manifest.json` (format, version, app version, schema version = newest EF migration id,
   created at, kind, every file with size and SHA-256), `db/recipewarden.db` (SQLite online backup, checked with
   integrity_check, single file), `media/…`, `keys/…` (ADR 0038), `config/…` (the two config files, for reference only).
2. **Validation before anything changes** (SEC-54, BR-62): every tar entry is checked before it is written — no
   absolute paths, no `..` or empty segments, no links or special files, only the layout above, size and count caps —
   into a quarantine folder under `<data>/tmp`. Then the manifest, every SHA-256, that nothing unlisted came along,
   the version (BR-65: a newer app version or an unknown migration is refused with "Install version X or later, then
   restore"), and the database's integrity_check.
3. **Applying**: pre-restore backup (Kind PreRestore, built-in folder) → the database is copied in with SQLite's
   online backup API (the live database stays a valid WAL database) → the media folder is swapped → the archive's
   Data Protection keys are added → migrations and seeds run → a `Restore.Run` audit entry. Any failure puts the
   previous database and media back.
4. **Never from the archive** (BR-64): `network.json` and `appsettings.Production.json` — they belong to this host and
   are root-owned. The archive's copies are kept in it for reference only.
5. **CLI** `sudo recipewarden restore <archive> [--kit file]`: as root it copies the archive into `<data>/tmp` for the
   service user, runs `restore --check` as that user (nothing changes on failure, the service is not touched), asks
   for `RESTORE` (or `--yes`), stops the service, runs `restore --apply` as the service user, starts the service and
   waits for `/health`. The passphrase goes to the child on standard input, never on the command line. Files are
   never created as root in the data directory. `--check` and `--apply` can also be run directly as the service user.
6. **Installer** `--restore <archive> [--restore-kit f] [--restore-passphrase-file f]`: fresh installs only (an
   installed host uses the CLI). It installs the release and writes this host's settings, then checks the archive
   with the new CLI and restores it with `--no-pre-restore` before the first start; it creates no Admin and issues no
   setup token. A refused archive leaves an empty install and says how to start over.
7. **`/setup`** (SEC-15): available only while no Admin exists, setup has not completed, and a token was issued. The
   installer issues the token (`recipewarden setup-token`) when it created no Admin and printed it with the URL; only
   the token's SHA-256 is stored (`<data>/setup/token.json`, 0600). "Start new" creates the first Admin; "Restore
   from backup" restores an uploaded archive (no confirmation word: there is nothing to lose yet). Either writes
   `<data>/setup/complete`, after which the page is 404 for good (BR-66). The page is rate-limited like search.
