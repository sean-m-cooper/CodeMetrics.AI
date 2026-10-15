# Packaged Python call and return evidence: 2026-10-15

Checkpoint **54ca6f0** saved the standalone producer, inventory, corpus study, and tests before packaging. Development adapter **0.11.0** embeds `dimensions.errorHandling.operationReview` under ruleset `python-2026-10-15-exception-operations-review-v1`. **Error Handling remains unscored.** Method Complexity remains the only scored Python dimension.

The archive ships the producer and `dist/python/ERROR_HANDLING_OPERATIONS.md`. Validation checks embedded transfer equality and digest, invocation/source/handler identities, suite previews, operation identities/extents, expression and ancestor metadata, argument accounting, original call/return projections, conservative classification, and recomputed summary/inventory counts. Missing, inconsistent, or incomplete required evidence makes the combined run incomplete and withholds scores and findings. These checks establish internal consistency, not authentication, independent AST measurement, resolved logging/delegation, or runtime correctness.

Historical 0.10.0 reports retain their own contract and same-version comparison support without invented operation evidence. Earlier supported contracts remain valid; cross-version comparisons remain incompatible. The companion skill keeps input unresolved inventory separate from remaining unresolved counts and never adds prior recognized totals to extended totals. Call names, awaited syntax, and recognition are not proof of recovery or execution. Documented intent remains evidence rather than a grading criterion.

## Identity correction found during packaging

Nested calls such as `factory()()` can share their starting line/column. The standalone prototype's start-only operation IDs therefore collided: one extra occurrence in Starlette and 26 in Django. Producer `0.2.0-experimental` now includes end line/column in each operation ID. Synthetic tests cover repeated starts, and the entire standalone corpus was regenerated in `TestResults/python-error-operations-2026-10-15-extents/`.

The corrected reports have identical counts, classifications, and other operation metadata after excluding IDs and the added ending-column field. Prior handler/context/raise identities and reports are unchanged. The original standalone study remains a historical record; its old operation-row hashes must not be substituted for the corrected report hashes.

## Installed corpus

| Project | Handlers | Owned calls | Owned returns | Recognized shapes | Newly recognized | Unresolved | Method Complexity |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| requests | 66 | 42 | 13 | 43 | 7 | 23 | 7.0 |
| starlette | 62 | 59 | 10 | 18 | 1 | 44 | 7.1 |
| httpie | 69 | 44 | 10 | 31 | 9 | 38 | 6.8 |
| django | 1,267 | 1,179 | 295 | 550 | 137 | 717 | 5.6 |

All **1,464 handler records, 1,324 calls, and 328 returns** match the corrected standalone reports after excluding invocation IDs/timestamps. The 642 recognized shapes include 488 prior shapes and 154 additional classifications; 822 remain unresolved. Input unresolved inventory still describes the original 976 handlers. These counts are neither success rates nor defects.

Existing source facts, selections, Method Complexity contributions/scores, duplication, decomposition, coverage, and original handler/context/transfer reports match the installed 0.10.0 runs. All four checkouts remained clean at pinned revisions. The [machine-readable record](python-error-operations-package-2026-10-15.json) preserves revisions, invocation IDs, evidence hashes, inventory, counts, identity corrections, and checks. The [standalone study](python-error-operations-2026-10-15.md) contains 15 reviewed source examples and conservative interpretation boundaries.

## Verification

- 140 Python tests passed with no skips, including 14 focused operation tests and combined-driver failure propagation. Seven shared conformance tests passed.
- All 35 installed Python evidence tests passed: syntax/ownership/extents, Unicode, awaited/nested calls, truncation, altered/stale imports and inventories, operation-only failure, and historical 0.10.0 support.
- All 256 distinct JS/TS tests passed across the full run and one isolated retry. The initial run passed 255; an unchanged async test exceeded five seconds under concurrent load. Its isolated retry passed in 3.54 seconds with the existing timeout.
- 34 distinct companion integration/supporting/harness checks passed, including 17 Python integration subtests plus the parent. The initial coverage-collection check lacked the coverage-enabled interpreter; all 18 Python checks passed after selecting the existing coverage environment. Runtime/catalog checks had already passed. Skill structural validation passed.
- All 22 packaged Python producer/guide files match source bytes; no Python bytecode is included.
- The reporting fixture was generated and validated by the installed analyzer/inspector. Seven rubric criteria and offline harness validation cover scope, counts, inventory, semantics, names, intent, and limits. No live-model report was generated or judged.

## Local artifact

Archive: `TestResults/python-error-operations-package-2026-10-15/codemetrics-ai-0.5.0.tgz`

SHA-256: `8b6d852487ba0221b9b881703a079156d70f2bc0c48919f42dcefce64ae795f0`

Full installed corpus evidence, logs, reporting source, archive, installation, and reproduction script are in the same directory. The npm development manifest remains 0.5.0; published npm 0.5.0 lacks Python. Adapter 0.11.0 is the independent evidence version. Packaging and skill changes are prepared after the standalone checkpoint; nothing was pushed or published.
