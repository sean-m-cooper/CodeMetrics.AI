# Packaged Python async call context: 2026-10-18

Verification completed October 19. Checkpoint **90121e1** saved the standalone context reviewer, provenance matcher, pinned study, and tests before packaging. Development Python adapter **0.13.0** embeds `dimensions.performanceAsync.contextReview` under ruleset `python-2026-10-18-async-context-review-v1`. **Async/Blocking Usage and Error Handling remain unscored.** Method Complexity is still the sole scored Python dimension.

## Package contract

The archive ships `async_context.py`, `async_provenance.py`, and `ASYNC_CONTEXT.md`. The combined driver produces context from the same source facts and original async report, preserving run/audit IDs. Any required context failure makes the combined analysis incomplete and withholds every score and finding.

The new validator checks producer/policy, source and invocation identities, original async-row hash, configuration, ordered owner/call populations, local definition identities/extents, callback expression metadata, conservative binding vetoes, and stored-wrapper construction links. It recomputes separate direct/deferred and population summaries plus bounded reason/shape inventories. A callback on a stored invocation must agree with its construction; immediate wrapper references also agree. These are internal consistency checks, not authentication, independent AST remeasurement, runtime identity, or proof of successful offloading.

Historical 0.12.0 reports retain their original contract and same-version comparison support without fabricated context observations. Cross-version comparisons are rejected. The original async report, all previous review evidence, and Method Complexity policy remain unchanged.

## Installed corpus

| Project | Async functions | Local callee links (direct) | Local callback references (direct) | Stored-wrapper invocation links | Method Complexity |
| --- | ---: | ---: | ---: | ---: | ---: |
| Requests | 0 | Unavailable | Unavailable | Unavailable | 7.0 |
| Starlette | 135 | 10 | 1 | 0 | 7.1 |
| HTTPie | 0 | Unavailable | Unavailable | Unavailable | 6.8 |
| Django | 266 | 35 | 2 | 0 | 5.6 |

Django's two callback observations reference one definition at wrapper construction and invocation. They are not two offloaded operations. No pinned selection exercises the narrow stored-wrapper pattern; positive fixtures validate it without claiming corpus detection recall. The original unresolved direct import-call totals remain 585 for Starlette and 644 for Django. Local source links do not establish blocking behavior or reduce uncertainty about runtime behavior.

All four installed runs verify clean pinned checkouts, source populations, unchanged complete Code Quality and Error Handling dimensions, and unchanged original async reports after removing invocation metadata. Starlette and Django context reports exactly match the standalone study after the same normalization. Requests and HTTPie retain unavailable async populations. Every new report validates, and comparisons against its archived 0.12.0 predecessor are rejected.

## Companion skill

The compatibility manifest prefers adapter 0.13.0 and still requires an explicit local npm archive. Python guidance explains inventory shapes, local source links, captured callbacks, deferred limitations, historical absence, and the distinction between references and executions. It prohibits qualitative replacement grades for Async/Blocking Usage and Error Handling and preserves documented intent.

A fresh final-archive helper run supplies the reporting fixture and validated inspection. It exercises two stored invocations linked to one construction, four callback references to one definition, an eager argument call, a deferred wrapper, a shadowed constructor, an unresolved receiver, and a documented optional-feature handler. Seven rubric criteria protect scope, counts, ownership, uncertainty, intent, and provenance. Fixture/harness checks are offline; no live-model report was generated or judged.

## Verification

- 183 Python tests passed without skips, including context-only failure propagation.
- 45 evidence tests passed against the installed final archive, including corrupted links/inventories, stale metadata, failure propagation, unchanged scoring boundaries, and frozen historical 0.12.0 validation/comparison.
- The JS/TS suite passed all 256 tests across 15 files on rerun. The initial concurrent run had one 5-second test timeout and a worker reporting timeout; no JS/TS source change or test-timeout change was made.
- Seven shared source-facts conformance tests passed.
- 36 companion checks passed: 20 Python integration checks (19 subtests plus their parent) and 16 runtime/catalog/offline-harness checks. Skill structural validation passed using the isolated PyYAML environment.
- All 28 bundled Python scripts/guides match source bytes. The final archive contains the corrected historical ruleset in `METHOD_COMPLEXITY.md`; all other packaged files are byte-identical to the archive used for corpus and companion integration runs. A fresh helper run and all 45 installed evidence tests also validate the final archive.

The [machine-readable record](python-async-context-package-2026-10-18.json) records archive hashes, validation, pinned revisions, invocation identities, evidence digests, unchanged-data assertions, and corpus summaries. Logs, installations, full reports, source fixture, and reproduction scripts are under `TestResults/python-async-context-package-2026-10-18/`.

The local archive is `TestResults/python-async-context-package-2026-10-18/codemetrics-ai-0.5.0.tgz`. The enclosing npm development manifest remains 0.5.0, independently versioned from Python adapter 0.13.0. Published npm 0.5.0 does not include Python. Nothing was pushed or published.
