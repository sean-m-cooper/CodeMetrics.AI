# Python async call-context study: 2026-10-17

The standalone policy `python/async-call-context-review-v1` adds an unresolved-call inventory, conservative local callback/callee links, and simple stored-wrapper links. **Async/Blocking Usage and Error Handling remain unscored.** Method Complexity remains Python's only scored dimension. Packaged adapter 0.12.0, its evidence contract, and the companion skill are unchanged in this milestone.

## Pinned observations

| Production selection | Async functions | Direct call expressions | Unresolved import-call expressions | Local callee links | Local callback references / distinct definitions | Stored-wrapper invocation links | Method Complexity |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Starlette | 135 | 717 | 585 | 10 | 1 / 1 | 0 | 7.1 |
| Django | 266 | 818 | 644 | 35 | 2 / 1 | 0 | 5.6 |

The two Django callback observations describe the same function at immediate wrapper construction and invocation. They are not two independently offloaded operations. Repeated stored-wrapper invocations similarly point to a shared construction ID. All counts describe syntax, not execution frequency, blocking duration, or safe usage.

Local links do not reduce the original unresolved import counts: a known source declaration can still have unknown runtime behavior. Deferred observations remain separate: Starlette has two calls, both unresolved; Django has six calls, five unresolved, and one local callee link. Synchronous owners remain excluded, not counted as safe async code.

Neither pinned selection exercises the supported stored-wrapper pattern. Its positive behavior is verified by synthetic fixtures, including repeated uses, same-line construction/use, Unicode names, and callback bindings captured before use-site comprehension shadowing. These projects do not establish stored-wrapper detection recall.

## What the inventory tells us

| Unresolved direct callee shape | Starlette | Django |
| --- | ---: | ---: |
| Attribute path | 333 | 303 |
| Bare name | 213 | 257 |
| Attribute on an expression | 30 | 30 |
| Call result | 6 | 54 |
| Subscript | 3 | 0 |

The largest joint reason/shape groups are non-import or ambiguous attribute paths: 330 in Starlette and 276 in Django. Those are candidates for future context work, not defects. Bare names also contain builtins and classes. The report does not infer callable type from shape or infer I/O from names such as `read` and `wait`.

Each joint group retains up to three deterministic owner/call IDs for source review. Full inventories separate execution context and source population. Reflection and cross-scope vetoes still withhold matching; they are not silently bypassed to improve the counts.

## Reviewed examples

The [selection manifest](../../calibration/corpus/python-selection.json) pins Starlette `96d5023c55b29224edb8d684868c49f681d079a9` and Django `dab0a5c47f159eb58472110dcbc10bd4a9b01adb`.

- Starlette `starlette/concurrency.py:57`, `iterate_in_threadpool`: the `_next` callback now links to its same-file selected function definition at line 41. Its body is not added to the async owner's calls.
- Starlette `starlette/concurrency.py:32`, `run_in_threadpool`: `func` is a parameter rebound through `functools.partial`. It remains unresolved, with no invented callback identity or exemption.
- Django `django/contrib/auth/hashers.py:91`, `acheck_password`: both immediate `sync_to_async` expressions reference the same `verify_password` definition. The parameter callback `setter` remains unresolved.
- Starlette's local `receive` functions in `TestClient.wait_startup` and `wait_shutdown` link to their own selected async definitions. This locates source without inferring scheduling or successful suspension.

## Verification

- All 182 Python tests passed with no skips using the existing coverage-enabled Python 3.12 interpreter, including 18 new context tests. An earlier run with the default Python 3.12 environment passed with four coverage-collection skips; the full run supersedes it.
- Seven shared source-facts conformance tests passed.
- Rebinding, conditional/annotated/chained assignments, parameter shadowing, deletion, walrus bindings, exception targets, pattern captures, comprehension shadowing, reflection, import mutation, cross-owner wrappers, late definitions, decorators, eager callback arguments, and deferred bodies are covered by positive/negative fixtures.
- Stale/tampered source facts and prior async input, changed selections, parse failures, budgets, fresh invocation IDs, deterministic rows, and output overwrite refusal are checked. Failures withhold all context rows and summaries.
- Both corpus runs check pinned clean repositories before and after, exact source/function/declaration populations, original async row hashes and summaries, unchanged Method Complexity, and exact existing Error Handling handler/context/transfer/operation rows and summaries. No target code was executed or changed.

The [machine-readable record](python-async-context-2026-10-17.json) contains policies, invocation IDs, source/implementation/row hashes, full summaries, representative links, validation results, and artifact SHA-256 digests. Full reports and source facts are under `TestResults/python-async-context-2026-10-17/`.

Reproduce with:

```powershell
py -3.12 analyzers/python/async_context_corpus.py --repos E:/repos --output TestResults/python-async-context-new
```

The [implementation guide](../../../analyzers/python/ASYNC_CONTEXT.md) defines the narrow binding rules and reporting limits. The next delivery step is packaging this context report and teaching the skill to preserve its unscored boundaries.
