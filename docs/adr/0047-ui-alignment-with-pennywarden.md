# ADR 0047: UI alignment with PennyWarden

- Status: Accepted (owner, 2026-11-23).
- Requirements: UX-01, UX-02, UX-03, UX-04, UX-11, UX-13 (new); section 7.1, 7.3
- Review: Project doc `claude/ui-alignment-review.md` (2026-11-23).

## Context
The owner finds the app's appearance its biggest issue. RecipeWarden's stylesheet grew one milestone block at a time
(M1 to M8 appended to `site.css`): no app frame, content directly on the page background, only text inputs styled
(selects and text areas browser-default), 44 px controls on every device, emoji icons, two names for the same colour
variable and duplicate rules. PennyWarden, by the same owner, has a small written design system
(`docs/ui-standards.md` there): one token set, one control height, an app shell with a side navigation, content in
cards, labelled fields that line up, row action menus and filled status pills.

A rewrite in Python was considered and rejected for this problem: the look comes from CSS and markup patterns, not the
server language.

## Decision
Bring PennyWarden's UI elements into RecipeWarden where they fit a recipe app; **not** a one-to-one port.

1. **Adopt:** the token set (`--bg`, `--surface`, `--surface-2`, `--text`, `--muted`, `--border`, status bg/fg
   pairs), one control height for inputs, selects and buttons, labelled fields (label above, hint below), cards,
   page headers, filled pills, table styling, action bars with the primary action last, original line icons in
   place of emoji, and a written `docs/ui-standards.md`.
2. **Adapt:** the app shell keeps the top bar with search (search is the main way in). Navigation is a side bar on
   desktop (icons and names), an icon rail on tablets, and a bottom tab bar on phones (one-thumb reach, UX-02).
   Admin and Manage get their own section navigation.
3. **Keep RecipeWarden's own:** the warm accent colour (terracotta, not PennyWarden's blue), the photo-first recipe
   grid, the recipe page layout and cook mode, shopping check-off rows and the print stylesheet.
4. **Control size (UX-02):** 36 px controls with a mouse or trackpad (`pointer: fine`), 44 px on touch screens
   (`pointer: coarse`). Cook mode and shopping check-off stay large on every device.
5. **Type (UX-04):** app pages use a 15 px base; the recipe page and cook mode keep the 18 px / 1.6 reading type.
6. **Licence:** PennyWarden's frontend is AGPL-3.0. Nothing is copied from it: tokens, rules and icons here are
   written for RecipeWarden (the icons are original line drawings). No new packages.

## Consequences
- One stylesheet organised by layer (tokens, base, controls, shell, components, pages) replaces the per-milestone
  blocks; class names used by tests and scripts are kept.
- `UX_ScreenshotTests` (Playwright) checks the main screens fit their window in both themes, on a desktop and a
  phone, and writes screenshots for review when `RW_SCREENSHOTS` is set. The AC-96 accessibility scan keeps the new
  colours at WCAG AA contrast.
- Later screens follow `docs/ui-standards.md`; a rule changes there and in the shared CSS, never on one page.
