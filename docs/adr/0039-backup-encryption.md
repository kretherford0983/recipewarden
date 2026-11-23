# ADR 0039: Backup encryption and the recovery kit

- Status: Proposed — **owner review requested** (2026-11-23).
- Requirements: FR-BKP-15, SEC-75, BR-62, AC-89 (PennyWarden BKP-APP-008)

## Decision
1. **Key pair**: RSA-3072, created on `/admin/backups` by an Admin with a passphrase (12+ characters). The public key
   and its fingerprint (`SHA256:…` of the public key) are stored in AppSetting, so a restore brings the matching key
   with the data. The private key is never stored on the server.
2. **Recovery kit**: a text file the browser downloads once, containing the fingerprint, a short explanation and the
   private key as PKCS#8 encrypted with the passphrase (PBKDF2-HMAC-SHA256, 600,000 iterations, AES-256-CBC). The
   passphrase is used once and never kept or logged.
3. **Archive format** (`.tar.gz.rwenc`): header `RWENC\x01`, the key fingerprint, a random AES-256 key wrapped with
   RSA-OAEP-SHA256, an 8-byte nonce prefix; then the `.tar.gz` in 1 MiB AES-256-GCM chunks. Each chunk's nonce is the
   prefix plus its number, and its associated data binds the header's SHA-256, the chunk number and a last-chunk
   flag, so chunks cannot be changed, cut off, reordered or moved between archives. Streaming: memory use is two
   chunks whatever the archive size. Only .NET's own `System.Security.Cryptography` is used (no new package).
4. **Restore** asks for the kit and passphrase (web, CLI `--kit`, installer `--restore-kit`). A wrong passphrase, a
   kit for another key ("encrypted for a different recovery kit (key …)"), or any changed byte fails before anything
   is written (SEC-75).
5. **Replacing the key** is allowed with an explicit tick; older archives still need the old kit. The run history
   shows which key each archive used.

## Consequences
Losing the kit or the passphrase makes encrypted archives unreadable — by design. The page says so before the key is
created. Unencrypted local backups (before a key exists) remain readable on the server (0600).
