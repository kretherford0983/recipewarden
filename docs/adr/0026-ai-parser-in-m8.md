# ADR 0026: The optional AI parser is built in M8

- Status: Accepted (2026-11-22, owner)
- Requirements: FR-IMP-09, FR-IMP-10, FR-NUT-06, SEC-44 to SEC-47, AC-17, AC-18

## Context
FR-IMP-09/10 are Musts, but no milestone in 9.10 names them.

## Decision
The AI parser is built in **M8 Hardening**, after the rules-based importers (M4), the review page and Export for AI
(M5) are settled. M4 keeps an `IRecipeExtractor` seam so the AI parser can plug into the same pipeline and review.
FR-NUT-06 (AI mapping suggestions) moves with it.

## Consequences
- Section 9.10 M8 lists the AI parser. Until then AC-17's first half holds trivially, because no AI code exists.
