# ADR 0053: Unmodified MPL-2.0 dependencies in the Python worker

- Status: Accepted (owner, 2026-11-24: D3). Narrow exception to "permissive licences only".
- Requirements: SEC-95, AC-108

## Context
The full Python dependency tree is MIT, BSD, Apache-2.0, PSF, CNRI-Python and W3C, except for two packages:
**certifi** (MPL-2.0), pulled in via requests ← mf2py/pyrdfa3 ← extruct ← recipe-scrapers, and **tqdm**
(MPL-2.0 AND MIT), pulled in via nltk ← ingredient-parser-nlp. Neither can be removed without dropping the
library that needs it, so without this exception neither M9 nor M10 can ship. ADR 0045 refused axe-core (MPL-2.0).

## Decision
MPL-2.0 is allowed **only** for packages that are named in `python/licences-allowed.txt` (initially certifi and
tqdm), are transitive, and are used unmodified. MPL-2.0 is file-level copyleft: its obligations apply only to
changes to those files, and we make none. The licence texts ship in the package with the rest of each wheel's
metadata. Any new MPL-2.0 package needs this ADR amended. All other copyleft licences stay refused.

## Why this differs from ADR 0045
axe-core would have been a direct, top-level tool, and an in-page checker met the need without it. Here the
MPL-2.0 code is two small, unmodified transitive dependencies of permissive libraries that have no alternative.

## Consequences
`build/check-python-licences.sh` enforces the allow-list in CI (AC-108).
