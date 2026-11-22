# ADR 0019: Finished yield is saved by editors; readers can try a yield for one view

- Status: Accepted (2026-11-22, owner)
- Requirements: FR-SCL-04, FR-SCL-05, FR-SCL-06, BR-110 to BR-114, AC-37, AC-38, AC-39; SEC-10, SEC-31; ADR 0014

## Context
Container scaling needs a finished yield. FR-SCL-06 and AC-39 have the cook record a measured yield after cooking.
That is a write, so it needs an account (SEC-10).

## Decision
1. **Editors** record yields two ways:
   - An "I made this" form on the recipe page sets `LastCookedAt` and, optionally, a measured yield
     (`YieldSource` = Measured).
   - A yield field in the recipe editor (`YieldSource` = Entered).
   Precedence is measured, then entered, then estimated (BR-111).
2. **Readers** can type a yield on the "Fit to container" form. It is used for that view only (query string) and
   never saved.
3. **Estimates** (BR-111) are computed on each view from the ingredients that convert to volume, listing the ones
   that could not be included and labelled "estimate". They are never stored.
4. Fit to container is part of the recipe toolbar: pick a container from My Kitchen or type a capacity, with count
   and fill %. It is a GET, like any scale (BR-114, ADR 0014).

## Consequences
- AC-39's "after marking it cooked and entering the measured yield" is an Editor action; tested as such.
