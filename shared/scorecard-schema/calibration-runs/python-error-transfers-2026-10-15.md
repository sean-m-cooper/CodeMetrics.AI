# Python raise/chaining study: 2026-10-15

Policy: `python/exception-raise-chaining-v1`. This standalone extension records owned raise/cause syntax and adds narrowly defined one-raise handler patterns. **Error Handling remains unscored.** The original context report and all previously recognized patterns are preserved; the packaged 0.9.0 contract is unchanged.

The [machine-readable record](python-error-transfers-2026-10-15.json) contains pinned revisions, run/audit IDs, source/function/handler/context digests, an implementation digest, population counts, and 16 source-reviewed cases. These were purposefully selected across the four projects and pattern boundaries; they are not a random sample or an accuracy estimate. The [policy guide](../../../analyzers/python/ERROR_HANDLING_TRANSFERS.md) defines ownership, precedence, budgets, and limitations.

## Results

| Project | Handlers | Owned raises | Previously recognized | Newly recognized | Recognized total | Still unresolved |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Requests | 66 | 30 | 17 | 19 | 36 | 30 |
| Starlette | 62 | 36 | 5 | 12 | 17 | 45 |
| HTTPie | 69 | 26 | 9 | 13 | 22 | 47 |
| Django | 1,267 | 439 | 162 | 251 | 413 | 854 |
| Total | 1,464 | 531 | 193 | 295 | 488 | 976 |

The 295 additional classifications comprise 239 direct symbol/call raises without an explicit cause, 48 direct raises with a cause matching the handler-name spelling, and eight direct `from None` shapes. The direct handler-name raise pattern has synthetic positive/counterexample tests but no new matches in this corpus under the one-statement and context restrictions. Same-spelling raises do occur inside larger unresolved handlers and remain available as raw syntax.

These are descriptions of syntax, not counts of successful or failed handling. A raise statement can be conditional, unreachable, or intercepted elsewhere. A called symbol is not necessarily an exception constructor. Recognized rows do not establish object identity, business correctness, or a safe refactoring opportunity.

## Source review and decisions

- **Requests, `src/requests/adapters.py:490`:** a one-statement handler raises `InvalidURL(e, request=request)`. Record the call and omitted explicit cause; recognize a direct raise shape without asserting semantic translation correctness. `src/requests/models.py:479` raises the name `UnicodeError`, showing why an explicit constructor call is not required.
- **Requests, `src/requests/adapters.py:713`:** the `MaxRetryError` handler examines different reasons and raises different wrappers. Preserve each owned raise, but leave the handler unresolved rather than inferring exhaustive paths. `src/requests/__init__.py:118` emits a warning and also remains unresolved; lack of a recognized shape is not a failure finding.
- **Starlette, `starlette/templating.py:27`:** direct call-form raise with `from _import_error` matches the handler-name syntax. `starlette/middleware/opentelemetry.py:9` explicitly uses `from None`; it receives a distinct shape with no penalty or claim of erased exception history.
- **Starlette, `starlette/_exception_handler.py:43`:** conditional `raise exc`, explicit chaining, and handler dispatch remain unresolved as a whole. `starlette/testclient.py:34` keeps the nested-handler blocker despite a direct `from None` statement.
- **HTTPie, `httpie/cli/argtypes.py:198`:** raising `argparse.ArgumentTypeError(...)` supports qualified call paths. The absent `from` is not evidence of lost implicit exception context. `httpie/manager/compat.py:56` explicitly raises `PipError(...) from error` and matches the handler-name cause shape.
- **HTTPie, `httpie/cli/argparser.py:132`:** conditional translation of `SystemExit` followed by bare rethrow remains unresolved. `httpie/config.py:70` has a single call-form raise but retains its enclosing-context-manager blocker.
- **Django, `django/contrib/admin/views/autocomplete.py:83`:** `raise PermissionDenied from e` is a name-form raise with matching handler-name cause. `django/core/mail/__init__.py:395` uses explicit `from None`; the observation does not judge the API decision.
- **Django, `django/db/models/sql/compiler.py:1654`:** nested cursor cleanup raises an outer exception name from None. Retain the syntax and nested-handler uncertainty. `django/utils/autoreload.py:636` chains from an incoming parameter, not the current handler's binding; this valid-looking case remains outside the narrow handler-name pattern.

The [Python 3.12 reference](https://docs.python.org/3.12/reference/simple_stmts.html#the-raise-statement) supports the distinction between bare raising, expression raising, explicit causes, implicit context, and explicit suppression of context display. The analyzer does not translate these distinctions into grades. In particular, it does not import a .NET `throw ex` penalty or require explicit `from` everywhere.

## Verification and delivery boundary

- **124 Python tests passed**, including 14 focused transfer tests; nine shared contract/calibration tests passed.
- Tests cover all four new patterns, distinct bare/name/call/cause forms, named-expression/rebinding exclusions, conditional and multistatement suites, context blockers, ownership boundaries, deferred methods versus immediate class bodies, metadata limits, literal omission, empty/test populations, stale provenance/source, budget failures, CLI IDs/exit codes/output preservation, and non-execution.
- The final corpus runner verifies clean pinned checkouts before and after analysis. All frozen source selections, function facts, 1,464 original handler observations, and context rows remain unchanged.
- Every new owned-raise row projects exactly to the prior line/bare/explicit-cause observation, with no newly invented or omitted raises. All 193 previously recognized context classifications remain identical.
- Method Complexity inputs and scoring code are unchanged. The existing Requests/Starlette/HTTPie/Django scores therefore remain 7.0/7.1/6.8/5.6; this study does not introduce a new scored dimension or a new packaged scorecard run.

Full final reports are in `TestResults/python-error-transfers-2026-10-15-verified/`. The earlier directory without `-verified` contains the initial run before the additional name-correspondence summary counters and owned-raise projection checks.

This milestone is standalone. Packaging and skill integration require a subsequent versioned change; current 0.9.0 artifacts must retain their original contract. No Error Handling score, deduction, gate, defect estimate, or automatic refactoring recommendation is introduced.
