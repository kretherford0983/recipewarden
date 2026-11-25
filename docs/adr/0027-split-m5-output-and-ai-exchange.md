# ADR 0027: M5 is split into M5 Output and M5b AI exchange

- Status: Accepted (2026-11-23, owner)
- Requirements: section 9.10 M5; FR-EXP-01 to 08, FR-VIEW-02, FR-SHOP-01 to 04

## Context
Section 9.10 M5 covers two unrelated halves: output (print, Markdown export, cook mode, shopping lists), which is
mostly Musts, and the AI exchange (Export for AI, prompt builder, goal templates, prompt history, AI-response import
with diff and variants, the recipe-md JSON Schema), which is mostly Coulds and Shoulds. One PR for both would be
hard to review.

## Decision
- **M5 Output** (branch `feature/m5-output`, 0.5.0): FR-EXP-01, 02, 03, FR-VIEW-02, FR-SHOP-01 to 04, BR-40 to
  BR-42, AC-12 (print and export parts), AC-23 (cook mode), AC-24, AC-25, AC-35 (shopping half), AC-50, AC-51, W6.
- **M5b AI exchange** (its own branch and PR right after): FR-EXP-04 to 08, BR-90 to BR-97, AC-26 to AC-29, W10, and
  the AI-response variant (FR-REC-05 as far as it needs).

## Consequences
Section 9.10 lists M5b. The two share recipe-md (ADR 0025), so M5b builds on M5's export.
