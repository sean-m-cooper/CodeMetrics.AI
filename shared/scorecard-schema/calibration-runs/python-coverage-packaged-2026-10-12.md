# Packaged Python coverage validation — 2026-10-12

The installed development npm archive now embeds optional coverage/CRAP review in Python evidence v3 and ships a separate, explicit collection command. The scorecard skill forwards supplied artifacts, validates their identities, and distinguishes analysis freshness from coverage source binding. All nine Python dimension scores remain unavailable; CRAP is review evidence only.

The preceding source-provenance work was checkpointed as `eb9bb1e` before packaging began. The adapter is `0.5.0-experimental`, the coverage-review producer is `0.3.0-experimental`, and the source-facts producer remains `0.4.0-experimental`. Keeping the source-facts contract unchanged allows existing collection receipts to remain usable with the same source selection and content.

## Package and checks

The local archive is `TestResults/python-coverage-package-2026-10-12/codemetrics-ai-0.5.0.tgz`, SHA-256 `d3dc223b73ff3f93b1ae8dd090e07264c2c595630bfc397b0c9c599895c1e33d`. Its npm version remains the development manifest's `0.5.0`; the published archive with that version does not contain this workflow. The skill still requires an explicit local archive. Nothing was published or pushed.

| Check | Passed |
| --- | ---: |
| JavaScript/TypeScript regression tests | 256 |
| Python unit and integration tests | 63 |
| Shared source-facts conformance/calibration tests | 9 |
| Installed Python package integration tests | 14 |
| Skill Python/runtime/catalog/offline-harness checks | 28 |

The skill's structural validator also passed. The 28 skill checks include one parent test and 11 Python integration subtests. No live-model report evaluation was run.

Installed-package cases cover missing and unbound coverage, matching collection receipts, Unicode source paths, stale run/audit IDs, invalid arithmetic and populations, source/report substitution, failed provenance, output reuse, and historical import. A Windows root-casing mismatch discovered by these checks was fixed in the offline receipt validator.

## Requests comparison

The installed CLI imported the original coverage report and receipt from the [Requests provenance validation](python-coverage-provenance-2026-10-12.md), at revision `611c6162cbc4ac2020a2f91c7cfa4f3abf9bbb60`, with source selection `src/requests/**`. It did not rerun the Requests tests. The existing collection covered three test modules with 255 passed and one skipped; it is not full-suite coverage.

All 246 function rows and the full coverage summary match the earlier offline review exactly:

- 238 source-bound estimates, zero conditional estimates, and eight unavailable functions.
- Unavailable reasons: four nested definitions, three functions in files absent from the report, and one body sharing its definition line.
- Experimental complexity preview remains 7.0; exact-body duplication remains zero groups under its configured exclusions.
- All nine dimension scores remain unavailable, with no overall score or score gates.

The new evidence run is `130fcc01-f81e-4cdb-b9b4-82ab65ee74f4`; the original collection UUID is preserved as `45404943-bcab-4092-8a28-12dd2f2244fb`. Full evidence and comparison output are under `TestResults/python-coverage-package-2026-10-12/`. The Requests checkout remained clean.

## Interpretation boundaries

Normal analysis parses source and imports evidence without executing target code. `codemetrics-python-collect` executes the caller-selected test command and is a separate workflow requiring an existing Python environment. The skill does not launch it just because coverage is missing.

An unsigned collection receipt binds matching selected-source snapshots and the report hash. It does not authenticate test quality, freeze dependencies, detect reverted edits during execution, or measure subprocess coverage. Offline evidence validation checks recorded consistency; historical import does not recheck today's source. A new analysis with supplied provenance does recheck the selected source. Invalid provenance makes the combined run incomplete rather than silently degrading to an unbound report.
