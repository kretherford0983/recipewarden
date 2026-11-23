# ADR 0023: Step photos are added in a panel and follow their step across edits

- Status: Accepted (2026-11-22, owner)
- Requirements: FR-REC-01, FR-REC-02 (W2 step 4), 8.2 Step.ImageId

## Context
Steps are edited as one text box and replaced on every save (ADR 0015), so a photo cannot hang off a step row id.

## Decision
1. Below the steps box, the editor lists the saved steps, each with "Add photo", "Replace" and "Remove". Each photo
   upload is its own POST (antiforgery, UploadValidator, write rate limit) and attaches to that step.
2. On save, each old step's photo moves to the new step with the **same text** (after trimming). If no step has the
   same text, it moves to the step in the **same position**. If neither exists, the photo is detached and later
   cleaned up (ADR 0022).
3. The finish image has its own field (upload, replace, remove) on the same form.
4. Imported step images are not copied in M4. Only the finish image is offered (FR-IMP-02, BR-12).

## Consequences
- Editors add photos after the first save, because a step needs to exist before a photo can attach to it.
- Rewording a step and reordering steps in the same save can move a photo to a neighbouring step. The panel shows
  each photo next to its step, so this is visible.
