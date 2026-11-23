# ADR 0028: Anyone may tick shopping items; a finished list goes away but its name is remembered

- Status: Accepted (2026-11-23, owner)
- Requirements: D-10, SEC-10, SEC-30, SEC-63, FR-SHOP-03, AC-51; section 7.2 `/shopping`

## Context
D-10 lets anyone on the site view lists and check items off. SEC-10 allows anonymous access only to read-only pages.
AC-51 needs checked state to persist across devices, so ticks must be stored on the server. The owner also wants a
list to go away once everything is bought, with its name remembered so the next trip can start again quickly.

## Decision
1. **One documented anonymous write:** POST `/shopping/{id}?handler=Check` (tick or untick one item). It needs the
   antiforgery token (SEC-30) and has its own rate limit, 120 a minute per client IP. It changes only the checked state
   of one item on an existing, unfinished list. Cloudflare Access already limits the whole site to the household (D-02).
   Creating lists, adding recipes or items, removing them and deleting lists stay Editor-only.
2. **Finished lists:** when the last item on a list is ticked, the list is marked finished and leaves the lists page
   at once. Its page shows "All done — this list will be removed" with **Undo** (untick an item). A finished list
   is deleted for good 15 minutes later, by the next shopping-page request or by the purge service, whichever comes
   first. Pantry items ("You probably have") do not count towards finishing.
3. **Remembered names:** every list name is kept in a small table (name, last used). The new-list page and the lists
   page offer the most recent names as one-tap "Start again" buttons (Editor). The names hold no items.

## Consequences
- An anonymous visitor can finish a list by ticking everything, which removes it after the undo window. That is the
  owner's intended behaviour, and only household members reach the site.
- Section 7.2 gains the check-off row with its access note; the AC-70 matrix covers the exception with its own test.
