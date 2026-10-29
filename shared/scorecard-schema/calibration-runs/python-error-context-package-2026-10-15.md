# Packaged Python exception-context evidence: 2026-10-15

Checkpoint **fa70917** saved the standalone context producer, tests, and corpus study before integration. Development adapter **0.9.0** embeds context at `dimensions.errorHandling.contextReview` under ruleset `python-2026-10-15-exception-context-review-v1`. Error Handling remains unscored; Method Complexity is still the only scored dimension.

The archive ships the context producer and `dist/python/ERROR_HANDLING_CONTEXT.md`. The validator checks the embedded original handler report, invocation IDs, handler-row digest, configuration, one-to-one handler identities, bounded suite accounting, classification constraints, and summaries. A missing, inconsistent, or incomplete required context report makes the entire run incomplete and withholds scores and findings. These are internal consistency checks, not authentication or independent syntax remeasurement.

Historical 0.8.0 evidence retains its original contract and same-version comparison support without fabricated context. Earlier supported contracts also remain valid. Cross-version comparisons remain incompatible. The companion skill requires the local archive and reports patterns as syntax, unresolved cases as unknown behavior, and surrounding prose as context rather than automatic handler justification. It adds no Error Handling score, deduction, gate, or defect claim.

## Installed corpus

| Project | Selected implementations | Handlers | Recognized shapes | Unresolved | Method Complexity |
| --- | ---: | ---: | ---: | ---: | ---: |
| Requests | 246 | 66 | 17 | 49 | 7.0 |
| Starlette | 508 | 62 | 5 | 57 | 7.1 |
| HTTPie | 464 | 69 | 9 | 60 | 6.8 |
| Django | 9,748 | 1,267 | 162 | 1,105 | 5.6 |

All **1,464 contexts** match the standalone reports exactly after excluding invocation IDs and timestamps, including the embedded base review. There are 193 recognized shapes and 1,271 unresolved cases; these are neither handling-success rates nor defect counts. Existing source facts, selections, Method Complexity contributions/scores, decomposition, duplication, coverage, and original handler observations match the previous installed 0.8.0 runs. All four checkouts stayed clean at their pinned revisions.

The [machine-readable record](python-error-context-package-2026-10-15.json) retains revisions, run/audit IDs, evidence hashes, counts, and checks. The [standalone study](python-error-context-2026-10-15.md) documents reviewed examples and conservative boundaries.

## Verification

- 110 Python tests, 9 shared contract/calibration tests, and 256 JS/TS tests passed.
- 27 installed Python evidence tests passed, including all six patterns, Unicode documentation, truncation, altered/stale identities and summaries, context-only failure, historical 0.8.0 compatibility, and prior score/coverage/decomposition behavior.
- 32 companion skill integration/supporting/harness checks passed, including 15 Python integration subtests. Skill structural validation passed.
- Installed Python producers and guides match source byte-for-byte; the archive excludes Python bytecode.
- The new reporting fixture was generated and validated through the installed analyzer/inspector. Seven authored rubric criteria and the offline harness protect interpretation boundaries. No live-model report was generated or judged.

## Local artifact

Archive: `TestResults/python-error-context-package-2026-10-15/codemetrics-ai-0.5.0.tgz`

SHA-256: `fec3f1869c4533de273fe3af338a084638570dcfbe6c08a73631718f3bc0b1a0`

Full corpus evidence, logs, installed package, and reproduction script are in the same directory. The npm development manifest still reads 0.5.0; published npm 0.5.0 lacks the Python workflow. Adapter 0.9.0 is the independent evidence version. This work has not been pushed or published.
