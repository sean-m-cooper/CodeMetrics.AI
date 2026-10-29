# Packaged Python raise and chaining evidence: 2026-10-15

Checkpoint **0f97873** saved the standalone producer, tests, and corpus study before integration. Development adapter **0.10.0** embeds evidence at `dimensions.errorHandling.transferReview` under ruleset `python-2026-10-15-exception-transfers-review-v1`. Error Handling remains unscored; Method Complexity is the only scored Python dimension.

The archive ships the producer and `dist/python/ERROR_HANDLING_TRANSFERS.md`. Validation checks the embedded context report and digest, invocation and handler identities, owned raise projection against the original observations, bounded expression/container metadata, conservative classifications, and recomputed summaries. Missing, inconsistent, or incomplete required evidence makes the combined run incomplete and withholds scores and findings. These checks establish internal consistency, not authentication, independent AST remeasurement, resolved bindings, or runtime correctness.

Historical 0.9.0 evidence retains its original contract and same-version comparisons without fabricated raise observations. Earlier supported contracts remain valid. Cross-version comparisons remain incompatible. The companion skill distinguishes bare raises, raised names/calls, explicit causes, and literal `from None`; it does not import a .NET rethrow penalty, demand explicit chaining, or judge documented intent.

## Installed corpus

| Project | Selected implementations | Handlers | Owned raises | Recognized shapes | Newly recognized | Unresolved | Method Complexity |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| requests | 246 | 66 | 30 | 36 | 19 | 30 | 7.0 |
| starlette | 508 | 62 | 36 | 17 | 12 | 45 | 7.1 |
| httpie | 464 | 69 | 26 | 22 | 13 | 47 | 6.8 |
| django | 9,748 | 1,267 | 439 | 413 | 251 | 854 | 5.6 |

All **1,464 handler records and 531 owned raises** match the standalone reports exactly after excluding invocation IDs and timestamps, including embedded context and base reviews. The 488 recognized shapes include 193 prior shapes and 295 additional classifications; 976 remain unresolved. These counts are neither handling-success rates nor defect counts. Existing facts, selections, Method Complexity contributions/scores, duplication, decomposition, coverage, and prior handler/context reports match the installed 0.9.0 runs. All four checkouts stayed clean at pinned revisions.

The [machine-readable record](python-error-transfers-package-2026-10-15.json) preserves revisions, invocation IDs, evidence hashes, counts, and checks. The [standalone study](python-error-transfers-2026-10-15.md) documents source-reviewed examples and limitations.

## Verification

- 125 Python tests, 9 shared contract/calibration tests, and 256 JS/TS tests passed.
- 31 installed Python evidence tests passed, including raise forms, Unicode and long names, ownership and truncated suites, altered/stale reports, transfer-only failure, and historical 0.9.0 compatibility.
- 33 companion integration/supporting/harness checks passed, including 16 Python integration subtests. Skill structural validation passed.
- All 20 packaged Python producer/guide files match source byte-for-byte; the archive excludes Python bytecode.
- The new reporting fixture was generated and validated through the installed analyzer/inspector. Seven rubric criteria cover scope, counts, forms, intent, unresolved patterns, provenance, and limits. Offline fixture/harness validation passed; no live-model report was generated or judged.

## Local artifact

Archive: `TestResults/python-error-transfers-package-2026-10-15/codemetrics-ai-0.5.0.tgz`

SHA-256: `53f6a08225408f86ffced4268dc3e11b9d30db3e8b0deeaefabacf5d0dcd56a9`

Full corpus evidence, logs, installed package, and reproduction script are in the same directory. The npm development manifest remains 0.5.0; published npm 0.5.0 lacks the Python workflow. Adapter 0.10.0 is the independent evidence version. Packaging and companion skill integration follow the standalone checkpoint in separate commits. Nothing was pushed or published for this validation.
