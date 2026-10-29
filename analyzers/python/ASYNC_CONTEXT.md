# Python async call context

`async_context.py` implements the policy `python/async-call-context-review-v1`. It supplements the existing [async usage report](ASYNC_USAGE.md) with an unresolved-call inventory and bounded source links. **Async/Blocking Usage and Error Handling remain unscored.** Method Complexity remains the sole scored Python dimension.

Development adapter 0.13.0 embeds this report at `dimensions.performanceAsync.contextReview` under ruleset `python-2026-10-18-async-context-review-v1`. The npm archive ships both producers and this guide; the companion skill presents the context without scores. Historical 0.12.0 evidence retains its original contract without invented context observations. The original async rows, categories, counts, ownership, and configuration remain unchanged.

## Run

```powershell
py -3.12 analyzers/python/async_context.py E:/repos/example --output E:/temp/async-context.json
py -3.12 analyzers/python/async_context_corpus.py --repos E:/repos --output TestResults/python-async-context-new
```

The same source-selection flags and optional run/audit UUIDs as the async reviewer are supported. Output must be a new `.json` file in an existing directory; the corpus runner requires a new output directory. Target source is parsed, never imported, installed, built, or executed.

## Inventory and accounting

Each call retains its original full-extent call ID. The report records its callee shape: bare name, attribute path, attribute on an expression, call result, subscript, lambda, or other expression. Unresolved import-match reasons come unchanged from the original report. A joint reason/shape inventory includes expression counts and up to three deterministic owner/call references per group.

Direct async-body and deferred-generator-body observations remain separate, as do production, test, support, and generated populations. Synchronous functions and lambdas remain excluded. A population with no async owners is unavailable, with null context measurements.

Unresolved import counts are not reduced by local source links. For example, finding a local function definition does not establish that its implementation is nonblocking. Names such as `read`, `wait`, or `get` do not imply blocking. Bare-name groups also contain builtins and classes; shape is not inferred callable type.

## Local callback and callee links

A bare name may link to a selected function definition in its lexical scope or an enclosing lexical scope in the same file. The definition must be a unique direct-suite binding, undecorated, and end before the reference in source order. Parameter shadowing, duplicate/reassigned names, conditional definitions, comprehension-local bindings, reflection, and cross-scope writes withhold links. Class namespaces are not treated as lexical closures for methods.

Links identify source-fact owner IDs, source extents, member names, and `FunctionDef` versus `AsyncFunctionDef`. No callback body is traversed or charged to the caller. A link to an async callback is not a claim that passing it to a thread helper is valid. Imported callback matches remain import spellings, not authenticated external implementations.

Helper callbacks distinguish reference expressions from eager call expressions. `to_thread(work())` retains the eager `work()` call under its real async owner; it does not gain a callback-definition link for the returned value. Lambdas retain their separate synchronous owner. Receiver methods, callable objects, arbitrary aliases, re-exports, partial application, and external modules are not resolved.

## Stored wrappers

The narrow supported pattern is a same-async-owner, direct-suite, single-name assignment of an import-matched `asgiref.sync.sync_to_async` call, followed by a call through that name:

```python
async def run(value):
    def work(item):
        return item + 1
    wrapped = sync_to_async(work)
    return await wrapped(value)
```

The wrapper name must have exactly one binding across the owner. Construction must end before use, with a non-starred first positional callback. Conditional, annotated, chained, destructured, reassigned, deleted, parameter-shadowed, module-level, and enclosing-owner wrapper bindings are withheld. Deferred generator-body invocations are also withheld. An eager first generator iterable retains the enclosing context. Existing imported-module mutation and reflective/cross-scope vetoes apply.

Each supported invocation links to the original construction call ID and assignment extent. The callback reference is interpreted at construction, not in a use-site comprehension's local namespace. Repeated uses link to the same construction; construction and invocation must not be added together as two offloaded operations. Local-callback summary counts are reference observations and can include the same callback on construction and invocation rows; they are neither distinct callbacks nor execution counts.

Only `sync_to_async` stored wrapper syntax is followed. Thread-helper callback references are described, but returned awaitables, stored `to_thread` calls, decorated wrappers, `functools.partial`, and `loop.run_in_executor` receiver types are not inferred.

## Provenance and failure behavior

The report shares the source async run/audit IDs and records a hash of the original async rows, source selection, and source-facts producer/policy. Before collection it regenerates and compares the original report, ignoring only its generation timestamp. During collection it rereads source digests and checks that every original owner and owned async call measurement is unchanged. The existing 100,000-owner and 100,000-call budgets apply.

Incomplete, stale, tampered, or over-budget inputs exit 2 with diagnostics, empty rows, and a null summary. These checks do not authenticate reports, lock a changing filesystem, prove control flow, or establish runtime identity. Unique lexical syntax can still be affected by runtime mutation outside the analysis boundary.

No score, finding, deduction, gate, automatic concurrency recommendation, or judgment of documented business intent is emitted. Source links provide review context only.

The [pinned study](../../shared/scorecard-schema/calibration-runs/python-async-context-2026-10-17.md) records real-project observations, reviewed examples, unchanged prior evidence, and test results.
