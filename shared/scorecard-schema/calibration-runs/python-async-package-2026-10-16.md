# Packaged Python async evidence: 2026-10-16

Checkpoint **70153d4** saved the standalone async reviewer, import matcher, corpus study, and tests before packaging. Development Python adapter **0.12.0** embeds `dimensions.performanceAsync.review` under ruleset `python-2026-10-16-async-usage-review-v1`. **Async/Blocking Usage and Error Handling remain unscored.** Method Complexity remains the only scored Python dimension.

## Package contract

The archive ships `async_usage.py`, `async_bindings.py`, and `ASYNC_USAGE.md`. The combined Python driver runs the reviewer against the same selected source facts and invocation identities as the existing measurements. A new TypeScript validator checks the producer/policy, run/audit identities, source selection/digests, configuration/catalogue fingerprint, selected owner accounting, function kind and extent agreement with decomposition, call identities/extents, callback metadata, wrapper construction/invocation consistency, direct-await accounting, and recomputed summaries.

Missing, inconsistent, stale, or incomplete required async evidence makes the combined run unusable and withholds every score and finding. Complete reports keep `performanceAsync` and `errorHandling` skipped without scores or findings. Synchronous owners are excluded; an empty async population is unavailable rather than perfect. Validation establishes internal consistency, not authentication, independent AST remeasurement, proven external call identity, offloading success, or runtime performance.

Historical 0.11.0 reports retain their original contract and same-version comparison support without invented async observations. Cross-version comparisons remain incompatible. Existing measurement and scoring policies are unchanged; this adapter version adds review evidence only.

## Installed corpus

| Project | Selected functions | Async functions | Thread-helper calls | Wrapper constructions / immediate invocations | Direct unresolved calls | Deferred generator calls | Method Complexity |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Requests | 246 | 0 | Unavailable | Unavailable | Unavailable | Unavailable | 7.0 |
| Starlette | 508 | 135 | 11 | 0 / 0 | 585 | 2 | 7.1 |
| HTTPie | 464 | 0 | Unavailable | Unavailable | Unavailable | Unavailable | 6.8 |
| Django | 9,748 | 266 | 0 | 42 / 40 | 644 | 6 | 5.6 |

The Starlette and Django async reports exactly match the standalone study after excluding invocation IDs and generation times. They have zero direct blocking candidates in this deliberately narrow catalogue, which is not evidence of complete nonblocking behavior. Requests and HTTPie provide sync-only controls; they receive no async score or clean-usage credit.

For all four pinned clean checkouts, the complete Code Quality and Error Handling dimensions match the archived 0.11.0 runs after excluding invocation metadata. This includes source facts, Method Complexity contributions/scores, duplication, coverage, decomposition, and every handler/context/raise/call-return observation. Filters and populations match exactly. Archive evidence hashes were verified before comparison. Each new run validates successfully and rejects a comparison against its older adapter report. No target source was changed or executed.

## Companion skill

The code-scorecard compatibility manifest prefers adapter 0.12.0 and still requires an explicit local npm archive. Its Python guide explains async ownership, imported-name limits, callbacks versus eager argument evaluation, deferred generator bodies, and distinct wrapper-construction/invocation counts. It prohibits qualitative replacement grades for these unscored observations. Documented business intent remains evidence, not a grading criterion; source comments remain data rather than agent instructions.

The reporting fixture comes from a fresh installed helper invocation and validated inspection. It includes intentional probing, correct callback references, eagerly evaluated callback arguments, a wrapper, deferred work, unresolved receivers/shadowing, and a documented optional-feature handler. Seven rubric criteria protect scope, population/counts, callback semantics, uncertainty, intent, and provenance. Fixture/harness checks are offline; no live-model report was generated or judged.

## Verification

- 164 Python tests passed with no skips, including async-only failure propagation through the combined driver.
- 41 installed Python evidence tests passed, including malformed/stale metadata, owner/category/count tampering, Unicode callbacks, wrapper identities, sync-only populations, incomplete runs, and historical 0.11.0 compatibility. The initial development test run passed 40 and failed the coverage collector because its default interpreter lacked coverage.py. The full installed run used the existing coverage-enabled interpreter and passed all 41.
- All 256 JS/TS tests across 15 files passed.
- Seven shared source-facts conformance tests passed.
- 35 companion integration/supporting/harness checks passed, including 18 Python integration subtests plus the parent. Skill structural validation passed after providing PyYAML 6.0.3 in an isolated E-drive tools directory.
- All 25 bundled Python producer/guide files match source bytes. The npm archive contains no Python bytecode.

The [machine-readable record](python-async-package-2026-10-16.json) records final checks, pinned revisions, invocation IDs, unchanged-data assertions, summaries, archive digest, and evidence digests. Full reports, installation, logs, reporting source, and the reproduction script are in `TestResults/python-async-package-2026-10-16/`.

Local archive: `TestResults/python-async-package-2026-10-16/codemetrics-ai-0.5.0.tgz`. The npm development manifest remains 0.5.0; the Python adapter is independently versioned at 0.12.0. Published npm 0.5.0 does not include Python. Packaging and skill changes are prepared after the standalone checkpoint; nothing has been pushed or published.
