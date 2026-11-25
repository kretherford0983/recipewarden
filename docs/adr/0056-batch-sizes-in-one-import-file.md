# ADR 0056: Batch sizes and variations in one import file

- Status: Accepted for D1 and D2 (owner, 2026-11-24); D3 accepted as amended (owner, 2026-11-25, amendment 1).
- Requirements: FR-IMP-04, FR-IMP-06; new BR-124 to BR-127, AC-117 to AC-120.

## Context
The owner imported three Markdown files: five puddings (variations) in one file, one casserole as a single and a
double batch, and a side dish with sub-headed steps and two versions of its sauce. The importer split every `# Title`
into its own draft and mis-read dividers, bulleted servings and times, bold step headings and italics.

## Decision
- **D1, same recipe at another batch size → import only the base (BR-124, BR-125).** Two recipes from one file are
  batch siblings when their titles match once a batch word is removed ("(Single Batch)", "Double", "x2", "for 12")
  and their ingredients overlap by more than 80% with one consistent ratio (at least 70% of matched amounts within
  ±15%). The base is the "single" one, else the one without a batch word. The other is not imported; its note in the
  base's notes holds only what scaling cannot reproduce: off-ratio or one-sided ingredient lines, servings, pan and
  times that differ, the steps if they differ, and note lines the base does not have. The review warns and offers
  "Import it as its own recipe", which makes it a draft again and removes its note.
- **D2, variations → separate recipes (BR-126).** Titles that still differ without a batch word are never folded.
- **D3, two versions of one ingredient group (proposed).** "Sauce (Side Dish)" and "Sauce (Taco Topping)" are kept as
  imported, with a review warning that shopping lists and nutrition count both, so the reviewer keeps one and moves the
  other to notes. Folding automatically was rejected: the amounts were 1¼× but the cheese changed too, and the
  importer cannot know which version the cook makes most.
- **BR-127, Markdown clean-up.** Dividers and in-file links are dropped; servings and times are read from bullets and
  "Bake:" lines, with a trailing bracket ignored; a pan in the Yield section prefills the pan; a bold line before a
  numbered item is its heading; `###` sub-headings that each hold one step are step headings; indented bullets stay in
  their step; single-mark italics are removed; a Nutrition table or label lines become the imported nutrition panel.

## Consequences
Pure Core rules (`BatchRules`, `NutritionText.FromLabelLines`, `StepTextRules`); the owner's three files are test
fixtures. A plain bullet in a numbered step list ("- " directly under "2.") now needs whitespace after it to count as
a bullet ("-Item" is text).

## Amendment 1 (2026-11-25): D3 choices (BR-132)
The owner kept the warning and asked for three choices on the review page, after picking the version to keep: keep it
and drop the others (no notes); keep it and move the others to the notes, as a bulleted list under their heading; or
split into separate drafts in the same batch, one per version, titled "Title (Version)", each keeping the groups
shared by all versions. The choice is applied to the parsed import, so edits made in the review form before it are
not kept (the page says so). Any choice removes the warning.
