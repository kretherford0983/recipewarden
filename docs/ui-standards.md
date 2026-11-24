# UI standards

How RecipeWarden screens look and behave, so a new screen matches the others (UX-13, ADR 0047). The rules live once,
in `src/RecipeWarden.Web/wwwroot/css/site.css` and a few shared partials; a screen that uses them gets them for free.
**Don't restyle one page to work around a rule:** change the rule here and in the shared CSS.

`UX_ScreenshotTests` (Playwright) opens the main screens on a desktop (1280 px) and a phone (390 px) in both themes and
fails when anything is wider than the window, naming the elements that stick out. With `RW_SCREENSHOTS=<folder>` it
saves a screenshot of each screen for review. `AC_96_AccessibilityTests` checks WCAG AA contrast in both themes. Add
a new main screen to the screenshot test's list.

## 1. Tokens
Colours, radii and sizes are CSS custom properties on `:root`, with dark values for `data-theme="dark"` and for
`system` in a dark OS. Use the tokens, never a hex value in a rule.

| Token | Use |
|---|---|
| `--bg` / `--surface` / `--surface-2` / `--surface-3` | page background / cards, bars / quiet fills (stat tiles, table headers) / pills |
| `--text` / `--muted` | body text / labels, hints, secondary text |
| `--border` / `--border-strong` | dividers, card edges / control edges |
| `--accent`, `--accent-text`, `--accent-hover` | primary buttons, current nav item, chosen scale. Terracotta: RecipeWarden's own colour |
| `--accent-soft`, `--accent-soft-text` | selected backgrounds (current nav, tag chips, step numbers) |
| `--link`, `--focus` | links; the focus ring (never removed) |
| `--ok-*`, `--warn-*`, `--err-*`, `--info-*` (`-bg`, `-fg`, `-border`) | status pills, notices, banners. Always use the pair |

## 2. One control height (UX-02)
Text boxes, selects and buttons are `--control-h` tall: **36 px with a mouse or trackpad, 44 px on a touch screen**
(`pointer: coarse`). Small buttons (`.small`) use `--control-h-sm`. Checkboxes are 18 px (22 px on touch). Cook mode
and shopping check-off stay large on every device. Never set a height or vertical padding on one control to make it
fit. The base control rules use `:where()` so a component (the header search, the scale box) can restyle a control
without `!important`.

## 3. App shell (section 7.1)
- **Top bar:** logo and site name, search, theme toggle (icon), user menu.
- **Primary nav:** `_Layout.cshtml`. A side bar with icons and names on desktop, an icon rail from 761 to 1100 px,
  and a **bottom tab bar on phones** (≤ 760 px), so the main places are one thumb away. The current page has
  `aria-current="page"`. Admin is in the side bar for admins and in the user menu on phones.
- **Section tabs:** Manage and Admin pages show their section's tabs under the top bar (`_SectionNav.cshtml`,
  `Layout/SectionNav.cs`). Each tab is a link with its own address. The recipe editor has no tabs.

## 4. Content
- **Page title** is the first `h1`; a `.page-head` holds a title, a one-line `.lead` and page actions.
- **Cards:** content blocks sit in cards on the tinted background. Top-level `section`s, forms (`.form`, `.editor`,
  `.stacked-form`) and tables in `main` get the card look automatically; use `.card` elsewhere. A section that is a
  row of recipe cards uses `.recipe-row` (no card around it).
- **Toolbars:** a row of controls directly in `main` (`.inline-form`, `.filters`) is a light bar.
- **Tiles** (`.tiles` / `.tile`): an icon, a title and one line of text. Used for the Manage overview and collections.
- **Stat tiles** (`.stats` / `.stat`): a small label over a value (servings, times).

## 5. Fields and forms
- `.field`: the label above, the control below, the hint (`.hint`) under the control. `.field-row` puts fields side
  by side and lines them up on their labels.
- Errors: `.field-error` under the field; `.error-summary` / `.form-errors` at the top of the form (UX-08).
- **Action bars** (`.actions`): the buttons that finish a form, **primary action last**. A long editor uses
  `.actions.sticky-actions` so Save stays in view (the recipe editor).
- **Multi-select** (`.multiselect`): a `details` dropdown whose closed box shows the chosen values as chips. Used for
  tags in the recipe editor; works without JavaScript, and `editor.js` adds the filter and live chips.

## 6. Pills, notices, tables
- `.badge` / `.chip` are filled pills. Status variants: `.warn`, `.ok`, `.error`, `.ai`. Allergens use the warning pair.
- `.notice` (info), `.error-summary` (error) and the admin banner use the status pairs with a stronger left edge.
- Tables (`table.list` and friends) have a header row on `--surface-2`, sit in a card, and **become stacked rows on a
  phone** so nothing is wider than the screen.
- Destructive actions are a `details.confirm` ("Delete…") that opens its own confirmation (UX-07).

## 7. Recipe pages (UX-04)
- Reading type: **18 px / 1.6** on the recipe page and in cook mode; app pages are 15 px.
- Recipe cards are photo first (no photo: a placeholder); the whole card is the link. On phones they become compact
  rows with the thumbnail on the left. The summary is plain text (`CardSummary`), never raw Markdown.
- The recipe page: title with Edit and Nutrition (editors), tags and allergens, the photo beside the description and
  stat tiles, then actions (Cook mode is primary; exports are in the **Export** menu), then the scale box.
- **Scale box** (owner feedback, 2026-11-23): collapsed, it shows only 0.5x · 1x · 2x · 3x
  (`ScaleRules.QuickPresets`) and "Make this my default". "More scaling options" opens the rest, **one per line**:
  other sizes (1.5x), custom, servings, units, scale from what I have, fit to container, how many will this make. It
  is **closed on every page load** (`recipe.js` keeps it open across in-place updates), and its summary names the
  options in use ("Units: US", "Serves 6").
- Ingredients sit in a card (sticky beside the steps on a wide screen); steps have numbered circles. A typed or imported
  bullet is never shown as a second bullet, and doubled brackets show once (`IngredientText`). A converted line
  (UX-06) has an accent edge and "Written: …" under it; never a dot, which reads as a bullet.

## 8. Reader mode (UX-14, ADR 0048)
- The recipe page is one `<article itemscope itemtype="https://schema.org/Recipe">`. **Every tool on it goes inside
  an `<aside>`** (reader modes drop asides); the recipe's own text stays outside asides.
- Text a reader should show is in paragraphs (step text is a `<p>` in its list item).
- Don't use class names reader modes treat as page chrome (`header`, `menu`, `sidebar`, `footer`, `comment`, …).
- `UX_14_ReaderModeTests` checks the page with Mozilla Readability (Firefox Reader View).

## 9. Icons
`_Icon.cshtml`: original 24 px line icons (no icon library, no licence to track). They are decorative
(`aria-hidden`); the link or button around them carries the name. Add a new icon to the switch there.

## 10. Motion
Only small movements (a card lifting on hover, a chevron turning). No colour transitions (the theme switch must not
animate through low-contrast colours), and none at all with `prefers-reduced-motion`.
