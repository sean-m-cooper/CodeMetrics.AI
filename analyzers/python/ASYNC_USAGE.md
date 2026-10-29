# Python Async/Blocking Usage evidence

The standalone `async_usage.py` reviewer implements `python/async-owned-calls-review-v1`, producer `0.1.0-experimental`. It records selected async-function populations, call syntax, conservative import matches, and explicit thread-helper syntax. **Async/Blocking Usage and Error Handling remain unscored.** Method Complexity is still Python's only scored dimension. Development adapter 0.13.0 embeds this report at `dimensions.performanceAsync.review` under ruleset `python-2026-10-18-async-context-review-v1`. The npm archive ships both producers and this guide. The scorecard skill reports the evidence without deductions or an async score. Historical 0.11.0 reports retain their original contract without invented async observations.

## Run

```powershell
py -3.12 analyzers/python/async_usage.py E:/repos/example --output E:/temp/async-review.json
py -3.12 analyzers/python/async_usage_corpus.py --repos E:/repos --output TestResults/python-async-corpus-new
```

The reviewer accepts the existing selection flags (`--include`, `--exclude`, `--test-path`, `--production-path`, and test/generated/support inclusion flags) plus `--run-id` and `--audit-id`. Output must be a new `.json` file in an existing directory. The corpus command requires a new output directory and the pinned clean Starlette/Django checkouts in the selection manifest. Neither command imports, installs, builds, or executes target code.

Complete evidence exits 0. Parse/read failures, stale source facts, inconsistent selection, or exhausted budgets exit 2 with incomplete diagnostic evidence, empty function rows, and a null summary. Caller-supplied invocation IDs must be UUIDs; otherwise fresh IDs are generated. Function IDs and source digests come from source facts. Call IDs include the owning function and complete start/end extent, so nested calls sharing a starting position remain distinct. Columns use Python AST's zero-based UTF-8 byte offsets; lines are one-based.

## Population and ownership

Every selected implementation owner appears once. Only `AsyncFunctionDef` owners are observed; synchronous functions and lambdas are explicitly excluded rather than counted as clean async implementations. Coroutine and async-generator functions are distinguished. Production, test, support, and generated populations retain separate summaries. An empty async population is unavailable, with null call measurements.

A function owns its body, excluding nested function/lambda/class definitions, their headers/defaults/decorators, annotations, type aliases, and class/module initializers. Those exclusions are measurement boundaries, not claims that declaration expressions never execute. Nested async functions are separate owners. No call graph is followed into synchronous helpers or callbacks. `await`, async iteration/context managers, and yielding do not prove suspension, safe blocking behavior, or runtime execution.

List/set/dict comprehensions are evaluated within the enclosing context. Generator expressions have separate deferred-body observations; their first iterable expression retains the enclosing execution context. This remains conservative when a generator is immediately consumed: consumption is not inferred. Comprehension iteration bindings shadow import names locally; assignment expressions can affect the enclosing scope. Nested owners never add calls to their parent.

## Import matching

`async_bindings.py` recognizes an unambiguous direct-suite import and `as` aliases, followed by a name/attribute path. A matched target is **import spelling**, not proven external runtime identity. Relative imports, conditional imports, repeated/rebound bindings, parameters, assignments, exception targets, pattern captures, and local definitions remain unresolved. An import must precede the use in source order; there is no reaching-definition analysis. Class namespaces are not lexical closures for methods.

For this first policy, `global`/`nonlocal` names are vetoed across the file. Direct `exec`, `eval`, `globals`, `locals`, `setattr`, `delattr`, or wildcard imports veto matching throughout the file. Recognizable writes/deletes to an imported module attribute veto that module across aliases. These deliberately broad vetoes can withhold valid matches. Reasons are retained on each call.

Assignments of imported objects to new aliases, re-exports, receiver types, dynamic imports, monkeypatching, local modules that shadow installed packages, arbitrary reflective behavior, and cross-file mutation are not resolved. The reviewer cannot authenticate an import's implementation. An unresolved call is neither a defect nor evidence of nonblocking behavior.

## Initial catalogue

The intentionally narrow blocking-call catalogue contains:

- `time.sleep` ([Python reference](https://docs.python.org/3.12/library/time.html#time.sleep)).
- `subprocess.run`, `call`, `check_call`, and `check_output` ([Python reference](https://docs.python.org/3.12/library/subprocess.html)).
- `os.system` ([Python reference](https://docs.python.org/3.12/library/os.html#os.system)).
- `urllib.request.urlopen` ([Python reference](https://docs.python.org/3.12/library/urllib.request.html#urllib.request.urlopen)).

Matches are review candidates, including calls with documented intent or zero-valued arguments. No duration, reachability, valid arguments, defect, or penalty is inferred. Names such as `.read()`, `.wait()`, `.result()`, `.get()`, and `.sleep()` without a supported import binding are not catalogue matches. This is not complete I/O or CPU-blocking detection.

Thread-helper syntax is recorded for `asyncio.to_thread`, `anyio.to_thread.run_sync`, and `starlette.concurrency.run_in_threadpool`. `asgiref.sync.sync_to_async` construction and an immediate invocation of the resulting wrapper are separate categories. The latter's two AST calls must not be added together as two offloaded operations. A stored or decorated wrapper is not followed through later calls. `loop.run_in_executor` remains unresolved because its receiver type is unknown. See the [Python thread helper](https://docs.python.org/3.12/library/asyncio-task.html#asyncio.to_thread), [AnyIO thread guide](https://anyio.readthedocs.io/en/stable/threads.html), [Starlette thread pool](https://starlette.dev/threadpool/), and [Django adapter guidance](https://docs.djangoproject.com/en/5.2/topics/async/#sync-to-async).

```python
await asyncio.to_thread(time.sleep, 1)      # Helper call; callback reference is not a sleep call.
await asyncio.to_thread(time.sleep(1))      # Inner sleep is evaluated before the helper call.
await asyncio.to_thread(lambda: time.sleep(1))  # Lambda body is a separate synchronous owner.
await sync_to_async(work)(value)            # Wrapper construction plus wrapped invocation syntax.
```

Callback expressions are described without claiming callable identity or successful offloading. Only a non-starred first positional callback is described; other layouts remain unresolved. Calls nested in argument expressions remain in their actual lexical context. A thread helper does not blanket-exempt its arguments. The immediate parent `Await` is recorded as syntax only; creating an awaitable is not proof it runs.

## Evidence and limits

Each observed function contains owned call locations, execution context (`async-body` or `deferred-generator-body`), category, import-match status/reason, immediate-await syntax, and callback expression information where applicable. Summary categories count expressions rather than execution frequency. Deferred calls are separate from the direct async-body totals. Functions with blocking candidates are a review inventory, not a risk rate or scoring denominator. There are no findings, scores, gates, or recommended concurrency changes.

All supplied source facts are regenerated and compared before review. Each file is read again against its digest. This detects changed source, new selected files, altered owners/complexity, and selection drift; it does not lock a concurrently changing filesystem. Source-facts file limits apply, plus 100,000 selected owners and 100,000 observed calls. Budget failures discard all rows, including earlier successful files.

The [pinned corpus study](../../shared/scorecard-schema/calibration-runs/python-async-usage-2026-10-15.md) documents useful thread-helper observations, unresolved receivers, and the limits of zero catalogue matches. Its regression runner checks exact prior handler/context/transfer/call-return rows and unchanged Method Complexity inputs and scores. Error Handling remains review-only.

The separate [async call-context reviewer](ASYNC_CONTEXT.md) adds unresolved reason/shape inventories and bounded local source links, including simple stored wrappers. It leaves this report's import matching, counts, and packaged contract unchanged. Neither report establishes safe blocking behavior or scores Async/Blocking Usage or Error Handling.
