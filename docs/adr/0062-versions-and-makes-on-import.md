# ADR 0062: Versions under one title, Makes on import, and the review page's action bar

- Status: Accepted (owner, 2026-11-25: Q1, Q2 and Q3 as recommended; build approved the same day).
- Requirements: FR-IMP-04, FR-IMP-06, FR-IMP-09, FR-SCL-06, UX-13; new BR-133 to BR-136, AC-138 to AC-144.
- Related: ADR 0019 (yields), ADR 0025 (review page), ADR 0056 (batch sizes and variations).

## Context
The owner imported a wing sauce file: one `# Title` with an oil-based and a butter-based version as `## ` sections,
each with its own Yield, Prep Time, Ingredients, Instructions and storage notes, and a `### Ingredients (16oz
Extra-Thick Batch)` inside the oil version. Only `# ` lines split recipes, so everything merged into one draft (28
ingredient lines, 17 steps). Makes stayed empty: "Approximately 1 1/2 cups of sauce" under `### Yield` has no
keyword, and "approximately" and "of sauce" were not accepted. The AI parser could not help: it was told to reply with
exactly one recipe and was never asked for the yield. The owner also asked for Makes to be filled when a recipe does
not state it, and reported that the review page's Save draft / Publish bar stuck out of the form, under an empty
"Import" box, with "Discard this import" floating below.

## Decision
- **Q1, versions → separate drafts (BR-133).** Inside one `# ` recipe, `## ` headings that each hold their own
  Ingredients and Steps sections and either their own facts (yield, servings, times) or a version word in the heading
  ("Oil Based Sauce", "Classic Version") split into drafts titled "Title (Version)" (merged into a title's own bracket:
  "… (Homemade, Oil Based Sauce)"). Parts of one dish ("For the cake", "For the frosting") have no facts of their own
  and stay one recipe. The lines before the first `## ` heading are every draft's description; shared sections before
  the first version ("Choosing the Right Sauce") go into every draft's notes (Q3). A second Ingredients section whose
  heading names a batch ("(16oz Extra-Thick Batch)"), with its Steps, is split off so BR-124/BR-125 fold it into that
  draft's notes.
- **Loose yield words (BR-134).** Under a Yield, Makes or Servings heading, a line without a keyword is that fact; a
  line that is neither ("Enough for 3–4 lb of wings") goes to the notes. "approximately", "approx.", "roughly",
  "around", "about" and "~" read as "about"; words after "of" following the unit are dropped. Time headings accept a
  bare "- 10 minutes" and "~10 minutes".
- **Q2, unstated Makes → the local estimate (BR-135).** When an import states no yield, the review prefills Makes
  with the BR-111 ingredient estimate in US cups ("about 1 1/3 cups"; Tbsp under ¼ cup, quarts from 8 cups) and warns,
  naming what was not counted. No estimate when more than half the lines with an amount cannot be counted (a dish of
  wings and eggs). Saved unchanged it is stored as Estimated, so a typed or measured yield always outranks it (BR-111);
  a changed value is Entered. The estimate is worked out on view, so it follows catalogue fixes. AI estimates were
  rejected: they cost tokens, are not reproducible, and the parser must not invent amounts.
- **AI parser (BR-136).** The instructions ask for `yield:` only when the text states it, and for one recipe-md
  document per recipe or version (a larger batch goes in Notes). Every valid document becomes a draft. "Re-parse with
  AI" on one draft of a batch keeps the document whose title matches the draft. An AI yield that does not read as an
  amount becomes a remark.
- **Review page (AC-144).** The editor form is no longer wrapped (the wrapper stopped it being a card, so the sticky
  bar's negative margins pushed it out); the AI marker is a class on the form. The Import options box renders only
  when it holds an option. "Discard this import" is a `form="discard-import"` button at the end of the sticky bar; its
  form stays outside the editor (forms cannot nest) and keeps its antiforgery token.

## Consequences
Rules stay in Core (`ImportRules.YieldAmount`, `ImportYieldRules`, `AiParserRules.PickForDraft`); the splitting is in
`TextRecipeExtractor`. The owner's file is a fixture. Dishes with servings may also get an estimated Makes when most of
their amounts are volumes (a soup); the reviewer clears it. Components are told apart from versions by facts or
version words, so a file whose components each carry their own yield would split; the review shows the batch and the
drafts can be discarded.
