# ADR 0051: Optional Python adapters for specialised parsing

- Status: Accepted (owner, 2026-11-23: D1, D4, D5; go-ahead for M9 on 2026-11-24).
- Requirements: FR-NUT-07, FR-IMP-11, FR-IMP-12, FR-OPS-06, BR-116 to BR-119, SEC-90 to SEC-95, NFR-10
- Plan: Project doc `claude/python-adapters-plan.md` (spike findings F1 to F12).

## Context
Some parsing jobs have mature, permissively licensed Python libraries with no .NET equivalent:
ingredient-parser-nlp (a trained ingredient parser with USDA FoodData Central matching) and recipe-scrapers
(750 site-specific recipe parsers). Rebuilding them in C# would take far more work than any feature in v1.
The stack line in CLAUDE.md allowed .NET only, so adding Python needs an ADR. A full rewrite of the app in Python was
discussed and not pursued.

## Decision
1. Python is allowed **only as an adapter behind an interface defined in Core**, for parsing and matching work
   where a Python library is clearly better. Business rules stay in Core (C#). Any new use of Python needs its own ADR.
2. **Runtime (D1):** a standalone CPython 3.13 (python-build-standalone, PSF licence) and locked wheels ship
   inside the Linux package under `/opt/recipewarden/<ver>/python`. The installer still installs OS packages only (D-05).
3. **Process model:** one short-lived process per request (`python -I -B -m rw_worker <command>`), JSON over
   stdin and stdout, at most two at a time. No resident service, so idle memory doesn't change. A warm worker
   may follow by ADR if NFR-10 is missed.
4. **Fallback (BR-119):** every Python feature has a .NET behaviour (the v1 behaviour) that is used when the
   worker is off, missing, failing or late.
5. **Role of the NLP parser (D4):** a second opinion at import and the source of mapping suggestions.
   `IngredientLineParser` stays the parser of record everywhere.
6. **Default (D5):** on, with an Admin switch `Python:Enabled`.
7. **Security:** spec section 5.10 (SEC-90 to 95). No network, no shell, strict limits, untrusted output,
   read-only files.

## Consequences
- The package grows by an estimated 60 to 80 MB compressed (budget 90 MB, NFR-10). CI gains a `python` job, and
  `build-test` and `package` each gain a setup step.
- Two toolchains to maintain. Dependabot and pip-audit cover the lock file.
- The same runtime is reused for v2 OCR.

## Alternatives considered
- **Distro Python plus a venv at install:** older LTS releases ship Python below 3.11, which the libraries need, and
  the install would need internet access.
- **Python embedded in .NET (CSnakes or pythonnet):** a crash in Python takes down the web app, and it fits
  poorly with the self-contained build.
- **Long-running sidecar service:** a second unit to install and monitor, which isn't needed at the measured
  cold start of under 1 s.
- **Porting the models to ONNX:** that covers the tagger model only, not the FDC matcher or the site parsers.
