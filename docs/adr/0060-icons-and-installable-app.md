# ADR 0060: Icons and an installable web app

- Status: Accepted (owner asked for it, 2026-11-25).
- Requirements: new UX-15, AC-134.

## Context
The browser tab showed a blank icon, and "Save page as app" in Chrome used a screenshot-like tile. The owner asked
for icons everywhere one is needed and whether the site could be installed as an app without that manual step.

## Decision
- One artwork, the header logo (white line icon on the terracotta rounded square), drawn in `build/icons/*.svg`:
  `favicon.svg` (tighter crop and heavier line so it reads at 16 px), `icon.svg` (app icons, rounded), and
  `maskable.svg` (full-bleed background with the glyph inside the 80% safe zone, for Android masks and the Apple touch
  icon, which iOS rounds itself). `build/icons.sh` renders favicon.ico (16/32/48), apple-touch-icon.png (180) and
  icons/icon-192/512 and maskable-192/512; the PNGs are committed so builds need no image tools.
- Every page links them through `_AppIcons` (main and print layouts); the maintenance page carries the SVG inline as a
  data URI because it answers every request itself (BR-63).
- `/manifest.webmanifest` is served by the app (anonymous) so the installed app takes the **site name** setting:
  standalone display, start and scope `/`, theme colour #a33f19 (light) with the dark page colour for dark mode, and
  shortcuts to Search, Shopping lists and Browse.
- **No service worker.** Chrome has not required one to install since version 112 on desktop (108 on Android), and a
  cache would risk showing stale or signed-in pages. The installed app needs the server, like the browser does.
  Chrome's own check (DevTools `Page.getInstallabilityErrors`) runs in the browser tests.

## Consequences
Chrome and Edge show an install button in the address bar and "Install RecipeWarden…" in the menu; Android offers "Add
to home screen"; Safari on iOS and macOS uses "Add to Home Screen" / "Add to Dock" with the touch icon. Firefox on
desktop does not install web apps. Changing the artwork means editing the SVGs and running `build/icons.sh`.
