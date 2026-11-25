# ADR 0016: Tag picker as grouped checkboxes

- Status: Accepted (2026-11-21, owner)
- Requirements: FR-ORG-01, NFR-03, UX standards (6.1); ADR 0011

## Context
The editor's comma-separated tag box works poorly with the built-in typed tags (ADR 0011). Options: grouped
checkboxes, an htmx type-ahead combobox, or a native multi-select.

## Decision
1. The editor shows all tags as checkboxes in one collapsible `<details>` group per tag type (Meal, Course, Season,
   Method, Diet, Cuisine, then untyped), with checked tags listed in the group summary.
2. A "New tags" text box (comma-separated) creates tags on save, with the existing name rules; a name that matches
   an existing tag (normalized) selects it instead of creating a duplicate. The box keeps the M1 field name
   (`Input.Tags`), so forms and tests that post tag names keep working; ticked tags post as `Input.TagIds`.
3. With JS on, a filter box hides non-matching checkboxes. Without JS everything still works.
4. Server-rendered, native inputs: keyboard and screen-reader support come from the platform (NFR-03).

## Consequences
- The posted form carries tag ids plus new names; overposting rules (SEC-13) apply as for other editor fields.
