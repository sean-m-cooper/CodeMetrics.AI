# Python exception-handler review: 2026-10-13

Policy: `python/exception-handler-observations-v1`. This first standalone pass records handler syntax without scoring it. No target code was executed. The machine-readable [record](python-error-handling-2026-10-13.json) binds results to pinned revisions, selected source hashes, unchanged function facts, configuration fingerprints, and implementation hash.

| Project | Handlers | Documented no-op | Undocumented no-op | Terminal raise | Terminal return | Terminal loop transfer | Other body |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Requests | 66 | 2 | 4 | 21 | 13 | 1 | 25 |
| Starlette | 62 | 0 | 9 | 26 | 10 | 3 | 14 |
| HTTPie | 69 | 5 | 6 | 16 | 10 | 3 | 29 |
| Django | 1,267 | 41 | 151 | 338 | 255 | 60 | 422 |

All selected handlers belong to the configured production-candidate file population. Counts include module/class-body handlers; they are not counts of defective methods or failed runtime operations. Each handler is classified once. These categories must not become a handling-success percentage.

## Source review

The following observations were checked against local source at the pinned revisions. They explain the classification and its limits, not the overall correctness of each project.

- **Requests, `src/requests/models.py:1110`:** a documented Unicode decode no-op precedes a second JSON decoding attempt through `self.text`. The suite explicitly explains the codec mismatch. Its existence is not an error-handling gap.
- **Requests, `src/requests/compat.py:43`:** an undocumented-in-suite `ImportError` no-op sits inside a loop probing supported character-detection libraries. The function docstring explains the search. A suite-only documentation rule cannot capture all surrounding rationale.
- **Starlette, `starlette/datastructures.py:132`:** a no-op after failed IPv6 parsing leaves the hostname unchanged; the `else` branch adds brackets on success. This is a syntax-probing branch, not evidence that an operational failure was ignored.
- **Starlette, `starlette/middleware/body_limit.py:95`:** the no-op catches `_RequestBodyLimitResponseSent`, and the containing `finally` cleans scope state. The exception name and surrounding response logic require context; this pass makes no resolved-type or protocol-correctness claim.
- **Starlette, `starlette/_exception_handler.py:43`:** `other-body` contains conditional rethrows, handler lookup, sync/async dispatch, and response emission. Treating this category as inadequate handling would misclassify substantive recovery logic.
- **HTTPie, `httpie/config.py:74`:** a missing-file no-op is adjacent to explicit translation of malformed JSON and other OS errors. These branches need separate interpretation; a blanket empty-handler deduction loses the distinction.
- **HTTPie, `httpie/output/formatters/colors.py:184`:** `pass # Nope` is recorded as documented syntax. Surrounding code probes whether a body is JSON before selecting a lexer. The analyzer preserves the terse comment without inventing a prose-quality threshold.
- **HTTPie, `httpie/compat.py:98`:** a terminal `return None` follows missing package metadata. It is a visible fallback, not automatically a swallowed-error defect.
- **Django, `django/apps/config.py:112`:** the no-op has no in-suite comment, but the preceding comment explains an attempted app-module import. Its local category must not be presented as proof of undocumented intent.
- **Django, `django/contrib/admin/options.py:1852`:** the comment explains that invalid action data should fail later validation, so no immediate action is needed. This deliberate decision is preserved as documented evidence.

## Verification and next boundary

The full Python suite passes 93 tests, including 14 focused handler tests. All 9 shared conformance/calibration tests pass. Coverage includes nested-owner isolation, documentation boundaries, Unicode offsets, exception groups, immediate class-body versus deferred-function finally context, source drift, incomplete parses, budgets, empty/nonproduction populations, invocation IDs, non-execution, and output preservation.

The four corpus checkouts stayed clean at their pinned revisions. Source-selection fingerprints, selected-source hashes, function-fact hashes, and implementation counts match the frozen baseline. A second run after correcting class-body finally context retained identical handler rows and summaries on all four selections. Existing Method Complexity measurements and scoring code are unchanged.

The next integration boundary is packaging this review evidence with validation and skill guidance. Before any scoring proposal, distinguish explicit developer rationale and contextual probing/fallback patterns from unexplained no-op behavior, and retain an honest unresolved category. This study does not authorize an Error Handling score or automatic refactoring recommendations.
