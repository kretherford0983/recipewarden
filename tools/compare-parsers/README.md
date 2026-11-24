# Parser oracle (FR-IMP-11, ADR 0051, plan 1 item 4)

The .NET `IngredientLineParser` is the parser of record. The NLP parser in the Python worker reads the same lines
independently, and every line where the two disagree (BR-118) is a lead: either a .NET parser bug worth a corpus line
and a fix, or a known difference in convention.

The list for the 574-line corpus is `tests/contracts/corpus-disagreements.txt`, produced by
`FR_IMP_11_CorpusDisagreementTests` against the real worker. It runs in every build, so a change in either parser
shows up as a diff in review.

```bash
build/python.sh dev
dotnet test --project tests/RecipeWarden.Infrastructure.Tests --filter-class "*FR_IMP_11_Corpus*"
# after reviewing a changed list (the test writes it next to the file as .received):
RW_ACCEPT_SNAPSHOT=1 dotnet test --project tests/RecipeWarden.Infrastructure.Tests --filter-class "*FR_IMP_11_Corpus*"
```

To check other lines (for example a recipe that imported badly), add them to the corpus file with their expected
reading, then run the same command.

When the list was first made (2026-11-24) it had 15 lines, and none was a .NET bug: the corpus lines carry their
expected reading and the .NET parser passes them. They are NLP-side misreadings (`1-1/2 cups` as ½–1, `2 T` and
`1 tbl`, amounts written as words such as `an egg`) and conventions (alternatives such as `chicken or vegetable broth`).
On review these show as "Second reading" notes that the Editor can ignore; real imports are where new leads will come
from.
