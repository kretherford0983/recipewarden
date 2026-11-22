# ADR 0015: Steps get an optional heading; step text stays plain

- Status: Accepted (2026-11-21, owner) — to be revisited after the M phases for refinement
- Requirements: FR-REC-02, SEC-22, section 8.2 (Step)

## Context
Found while testing M1: pasting `1. **Preheat oven**` followed by an indented `Preheat oven to **375°F**.` gives two
steps per item with literal `**`, because `StepTextRules` makes one step per line and step text is plain text.
Options: (a) a `Step.Title` column, (b) restricted Markdown in step text, (c) strip `**` only.

## Decision
1. `RecipeStep` gets a nullable `Title` (max 120 characters). It is separate from `SectionName` (the group heading
   such as "For the sauce").
2. Paste rules (`StepTextRules`): a numbered item starts a step; following non-numbered lines (indented or not)
   continue that step until the next numbered item or section heading; after a blank line only indented lines
   continue it. If the item's
   first line is wholly emphasised (`**…**` or `__…__`) and more lines follow, it becomes the step title.
   Unnumbered pastes keep today's one-step-per-line behaviour.
3. Emphasis markers (`**`, `__`) are stripped from step text; step text stays plain and HTML-encoded (SEC-22).
4. Headings are edited in the steps box: a recipe whose steps have headings is shown there as a numbered list,
   `1. **Heading**` followed by the text indented on the next line, which the same paste rules read back. (A
   separate field per step was considered; it would replace the steps box, which the paste flow and the reorder
   control rely on, so it waits for the revisit.) The reading view shows the heading in bold above the text.
5. A bold lead-in that ends in "." or ":" ("**Sear.** Brown the meat") is also a heading; bold words inside a
   sentence ("**Do not** overmix") are not.

## Consequences
- Temperature and timer detection keep working on plain text.
- Revisit after the M phases (e.g. whether limited emphasis in step text is wanted).
