# Packaged Python exception-handler evidence: 2026-10-15

Checkpoint **48c1697** saved the standalone producer, tests, and first corpus review before integration. Development adapter **0.8.0** embeds the report at `dimensions.errorHandling.review`, under ruleset `python-2026-10-15-error-handling-review-v1`. Error Handling remains unscored. Method Complexity remains the only scored dimension; its measurement and scoring policies are unchanged.

The package ships the producer and `dist/python/ERROR_HANDLING.md`. The validator checks provenance, source/owner identities, annotation and classification consistency, owned-transfer fields, and summary accounting. Missing, malformed, stale, or incomplete handler evidence fails the combined run and withholds its score and findings. Validation checks internal consistency rather than independently remeasuring the AST or authenticating edited evidence.

Historical 0.7.0 evidence retains its Method Complexity score, original fingerprint, and same-version comparison support without fabricated handler observations. Saved 0.5/0.6 preview contracts remain valid. Cross-version comparison remains incompatible. The skill preserves these distinctions and requires an explicit local package; released .NET and JS/TS pins are unchanged.

## Installed corpus

| Project | Selected implementations | Handlers | Method Complexity |
| --- | ---: | ---: | ---: |
| Requests | 246 | 66 | 7.0 |
| Starlette | 508 | 62 | 7.1 |
| HTTPie | 464 | 69 | 6.8 |
| Django | 9,748 | 1,267 | 5.6 |

All 1,464 handler rows, classifications, summaries, selection records, and policy metadata exactly match the standalone reports after excluding invocation IDs and timestamps. Existing function facts, selections, Method Complexity contributions/scores, decomposition, duplication, and coverage reviews match the previous 0.7.0 installed runs. All checkouts stayed clean at their pinned revisions. The [machine-readable record](python-error-handling-package-2026-10-15.json) retains revision, run/audit IDs, evidence hashes, and per-population counts.

## Verification

- 94 Python tests and 9 shared conformance/calibration tests passed.
- 256 JS/TS tests passed.
- 23 installed Python evidence tests passed, covering malformed/stale handlers, owner/source mismatches, category/summary consistency, incomplete reviews, historical compatibility, score gates, and existing coverage/decomposition behavior.
- 31 skill integration/supporting/harness checks passed, including 14 Python integration subtests. The helper rejects stale/altered handler imports and preserves review-only scope. Skill structural validation passed.
- A new analyzer-generated `python-error-handling` reporting fixture and seven rubric criteria cover deliberate probing, documented shutdown, explicit fallbacks/rethrows, unresolved dispatch, and the 1/9 scoring boundary. The fixture was validated by the installed inspector; the harness passed offline checks. No live-model report was generated or judged.

The final archive differs from the full-test archive only in the package README's current version/scope description. Both were installed separately; every other packaged file, including all runtime code and policy documents, is byte-identical. The final installation also generated and validated a fresh handler-bearing smoke report.

## Local artifact

Final archive: `TestResults/python-error-handling-package-2026-10-15-verified/codemetrics-ai-0.5.0.tgz`

SHA-256: `bdd8284608e04ad3b76f4d0f11376e4774329939e1a9ee9c66d4cd57aede07ea`

Full corpus evidence and reproduction script: `TestResults/python-error-handling-package-2026-10-15/`.

The npm development manifest still reads 0.5.0; the published 0.5.0 package does not include this Python workflow. Adapter 0.8.0 is its independent evidence version. This work has not been pushed or published.
