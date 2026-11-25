# ADR 0022: Images are stored as WebP with a thumbnail; draft-only images need an editor

- Status: Accepted (2026-11-22, owner)
- Requirements: FR-REC-01, FR-IMP-02, BR-12, SEC-50, SEC-51, SEC-52, AC-11; section 7.2 `/media/{id}`

## Context
SEC-52 requires every uploaded or copied image to be decoded and re-encoded (EXIF and GPS removed), at most 2400 px,
stored under a random name outside the web root and served with `nosniff`. The spec does not name a format or a
thumbnail size. Section 7.2 lists `/media/{id}` as Anonymous, but drafts are not public (ADR 0009).

## Decision
1. **Format:** every image is re-encoded with SkiaSharp to **WebP (quality 82)**, longest side at most 2400 px,
   plus a **640 px thumbnail** for lists. The original bytes are never kept.
2. **Ids and storage:** each image gets a random 128-bit id (32 hex characters). Files are written to
   `<data>/media/<first 2 chars>/<id>.webp` and `<id>.thumb.webp`. The database stores the metadata (8.2 Image).
3. **Access:**
   - `/media/{id}` and `/media/{id}/thumb` are anonymous only for images attached to a published recipe (its finish
     image or a step image).
   - Images that are only on drafts, or not attached yet, need the Editor policy; anyone else gets 404.
   - Responses carry `Content-Type: image/webp`, `nosniff` and `Cache-Control: private, max-age=86400`.
4. **Clean-up:** unattached images older than 24 hours, and images of purged recipes, are deleted by the existing
   purge service.

## Consequences
- Every current browser (Safari 14+, Chrome, Firefox, Edge) shows WebP. Print (AC-24) uses the same files.
- SkiaSharp needs its native library: `SkiaSharp.NativeAssets.Linux.NoDependencies` (MIT) ships it inside the
  self-contained package, with no OS packages needed.
- Section 7.2 now reads "Anonymous for published recipes' images".
