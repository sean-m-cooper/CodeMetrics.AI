# Python handler calls, returns, and unresolved-shape inventory: 2026-10-15

Policy: `python/exception-call-return-v1`. This standalone pass records owned call/return syntax and three additional lexical shapes. **Error Handling remains unscored.** The original handler, context, and transfer reports remain unchanged, as does the packaged 0.10.0 contract.

## What remains unresolved before this pass

Of the **976** unresolved handlers, **696 have one direct statement** and **280 have multiple statements**. The largest complete suite shapes are one assignment (185), one pass (166), one return (119), one expression statement (67), and one if statement (59). This is not primarily a population of large multi-statement handlers. Assignment/pass patterns should not be treated as defective just because they remain outside the recognizer.

Within that input population, 476 handlers contain owned calls, 198 contain returns, and 54 contain a callee spelling matching a logging-name hint. These presence categories overlap. Name hints neither establish logging nor identify every diagnostic operation. The machine-readable inventory preserves all suite shapes, explicit preview truncation, prior reason counts, and separate project/population results.

## Results

| Project | Handlers | Owned calls | Owned returns | Prior recognized | Newly recognized | Recognized total | Still unresolved |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| requests | 66 | 42 | 13 | 36 | 7 | 43 | 23 |
| starlette | 62 | 59 | 10 | 17 | 1 | 18 | 44 |
| httpie | 69 | 44 | 10 | 22 | 9 | 31 | 38 |
| django | 1,267 | 1,179 | 295 | 413 | 137 | 550 | 717 |
| Total | 1,464 | 1,324 | 328 | 488 | 154 | 642 | 822 |

The 154 additional shapes are 76 direct call-statement sequences, 69 direct returns of symbols/calls, and nine call sequences followed by a bare raise. The recognized total includes the original 488; it is not an additional count. There are 65 calls with matching logging-name syntax and 27 immediately awaited calls across the full selected population. These are syntax counts, not handling-success rates, runtime invocations, coverage, or defects.

## Source review

Fifteen deliberately chosen examples cover all four projects and positive/excluded boundaries. They are not a random sample and do not establish precision or recall.

- **requests, `src/requests/utils.py:336`:** Cleanup call followed by bare raise is a syntax shape; os.remove may itself fail, so no guaranteed original propagation.
- **requests, `src/requests/sessions.py:213`:** Direct read call preserves the alternative read path as syntax, not proven recovery.
- **requests, `src/requests/__init__.py:118`:** warnings.warn matches the name hint but is not proof of a logging-framework receiver.
- **starlette, `starlette/formparsers.py:52`:** Returned decode call is a fallback-shaped source observation; the reviewer does not verify the business choice or result.
- **starlette, `starlette/_exception_handler.py:43`:** Conditional lookup, rebinding, awaited dispatch, and multiple raises remain unresolved; calls are still recorded.
- **httpie, `httpie/cli/argtypes.py:138`:** Direct stderr write plus sys.exit fits a call sequence, not a successful-continuation claim.
- **httpie, `httpie/internal/daemons.py:70`:** os._exit is a simple symbol call; recognized call syntax does not establish recovery or normal return.
- **httpie, `httpie/manager/tasks/plugins.py:36`:** Diagnostic write then bare raise is recognized without proving the write completes or original error escapes.
- **httpie, `httpie/manager/tasks/plugins.py:151`:** return self.fail(...) is a call return; the name fail does not determine outcome or correctness.
- **django, `django/contrib/auth/forms.py:418`:** logger.exception matches spelling and direct-call syntax; neither receiver identity nor successful email handling is established.
- **django, `django/contrib/admin/checks.py:227`:** Returned refer_to_missing_field call records diagnostic delegation syntax without grading it.
- **django, `django/core/mail/backends/base.py:112`:** self.close then bare raise is a cleanup-shaped sequence; close can fail and the caller context remains unknown.
- **django, `django/contrib/sessions/backends/base.py:141`:** Logger assignment followed by warning remains unresolved; visible logging does not remove rebinding uncertainty.
- **django, `django/db/backends/base/creation.py:230`:** Conditional return and calls remain unresolved under an enclosing context manager; keep the blocker.
- **django, `django/http/response.py:523`:** Warning, nested async function, and conversion logic remain unresolved. Nested definition calls cannot credit this handler.

## Validation and interpretation

All 1,324 calls and 328 returns reproduce the original per-handler call counts and line/has-value return projections. The runner verifies pinned clean checkouts before and after analysis, source and selected-function digests, and exact original handler/context/transfer row digests. Inputs are unchanged, every original recognized classification is preserved, and no observation is attributed to both parent and nested handler.

The underlying Method Complexity facts and scoring implementation are unchanged. The established Requests/Starlette/HTTPie/Django scores remain 7.0/7.1/6.8/5.6 on these inputs; this milestone did not rerun packaged scorecards or introduce a new dimension score.

**138 Python tests passed**, including 13 focused call/return tests; **seven shared source-facts conformance tests passed**. The final Python run used the existing coverage-enabled interpreter so all coverage integration tests executed without skips. Tests cover shape boundaries, misleading/shadowed names, rebinding and named expressions, computed receivers, conditional/deferred calls, awaited returns, finalizers/context managers/groups/nested handlers, class-body versus method ownership, Unicode, truncation, argument-value omission, empty/excluded populations, stale input/source, local/global budgets, stable IDs, CLI status, output preservation, and non-execution.

The [policy guide](../../../analyzers/python/ERROR_HANDLING_OPERATIONS.md) defines ownership, budgets, exclusions, and interpretation. A call sequence can include process exit or failing cleanup; a return expression can call a function named `fail`. Those real examples justify keeping shape recognition separate from handling quality. Existing uncertainty remains visible, without judging developer intent or recommending automatic decomposition.

The [machine-readable study](python-error-operations-2026-10-15.json) retains pinned revisions, invocation IDs, implementation/source/row digests, full inventory, summaries, and reviewed decisions. Full reports and logs are in `TestResults/python-error-operations-2026-10-15/`. This milestone is standalone; packaging and skill integration require a separate versioned change. Error Handling remains unscored.
