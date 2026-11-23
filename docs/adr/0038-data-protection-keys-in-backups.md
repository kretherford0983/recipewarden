# ADR 0038: The Data Protection key ring is included in backups

- Status: Accepted as the recommended option (option a, noted in traceability 2026-11-20); **owner review requested**.
- Requirements: FR-BKP-01, FR-BKP-06, SEC-45, SEC-71

## Context
FR-BKP-01 defines a backup as database + images + config. The ASP.NET Core Data Protection keys in `<data>/keys`
(SEC-71) protect sign-in cookies, antiforgery tokens and, from M8, AI API keys (SEC-45). Without them a restore on a
new host signs everyone out and leaves stored secrets undecryptable.

## Options
a. Include `keys/` in every archive. Backups that leave the server are encrypted (ADR 0039), so this adds no exposure.
b. Leave the keys out and document that a host move needs everyone to sign in again and AI keys re-entered.
c. Export the key ring separately, e.g. with the recovery kit.

## Decision
Option a. `keys/*.xml` are in every archive and listed in the manifest. On restore they are **added** to the key
ring (an existing file of the same name is kept), so both the archive's and this host's cookies and secrets keep
working. Test: restore round trip; M8 adds a test that a restored AI key still decrypts.
