# Python callback adapter context

`async_adapters.py` implements `python/async-callback-adapters-review-v1` as an experimental report embedded at `dimensions.performanceAsync.adapterReview` in development adapter 0.14.0, ruleset `python-2026-10-24-async-adapters-review-v1`. It supplements the [async context report](ASYNC_CONTEXT.md) without changing its original observations. The archive ships this guide and the producer; the companion skill reports the evidence without runtime claims. Historical 0.13.0 reports retain their contract without invented adapter observations. **Async/Blocking Usage and Error Handling remain unscored.** Method Complexity remains Python's only scored dimension.

```powershell
py -3.12 analyzers/python/async_adapters.py E:/repos/example --output E:/temp/adapters.json
py -3.12 analyzers/python/async_adapters_corpus.py --repos E:/repos --output TestResults/python-adapters-new
```

The same source-selection flags and optional run/audit UUIDs as the original async reviewer are supported. Output must be a new `.json` file with an existing parent; the corpus directory must be new. Target code is parsed, never imported or executed.

## Partial construction

Import-matched `functools.partial` calls record the construction extent, first positional callback expression, and bound positional/keyword expression shapes and extents. Literal argument values are not copied. `*args` and `**kwargs` are explicit syntax, not known argument counts. With a missing or starred first positional argument, callback layout is unresolved; the tool does not interpret `func=` as a callback. Callback references use the existing conservative local-definition/import-spelling rules. Calls, lambdas, receiver methods, and nested partial results are not resolved to callback implementations.

These are construction observations, not proof that a partial was created, callable, executed, or offloaded successfully. Eager callback/argument calls retain their original owner. Callback and lambda bodies are never traversed to assign behavior to the async caller.

An inline partial passed to a recognized helper or used as a callee links to its original owned construction call ID. A stored partial supports a direct-suite, single-name assignment in the same async owner, ending before use, with exactly one binding and a first positional callback. Reassignment, parameters, aliases, conditional/annotated/chained assignments, deletion, reflective operations, cross-scope writes, and comprehension shadowing withhold that link. Stored links in deferred generator bodies are withheld. Inline constructions in deferred bodies remain separately labeled syntax.

For example, Starlette's `func = functools.partial(func, *args, **kwargs)` is a visible construction, but the parameter and assignment make `func` ambiguous. The subsequent helper callback remains unresolved. We do not infer the incoming callable's identity.

Supported stored `sync_to_async` wrappers also expose partial callback context evaluated at construction. Repeated callback references link to the same construction. Construction, callback references, and invocation expressions must never be added together as distinct offloaded operations.

## Decorator links

A bare-name callee or helper callback can link to a unique direct lexical `def` with exactly one import-matched `asgiref.sync.sync_to_async` decorator. Supported syntax is `@sync_to_async`, `@sync_to_async()`, or a factory with one literal boolean `thread_sensitive` keyword. Import aliases are supported. The decorator is resolved in the definition's enclosing scope, not the caller's scope.

The definition must end before the reference, have a selected source-fact owner, and have no competing binding. Stacked or unknown decorators, unsupported arguments, starred arguments, async definitions, conditional definitions, late definitions, receiver methods, and ambiguous/mutated imports are unresolved. Class namespaces are not lexical closures for methods. A positive link records a synchronous source definition and decorator syntax; it does not reclassify that owner's body as async or validate passing a decorated callable to a thread helper. Decorator headers are not added to the original async call population.

## Accounting and validation

Every original async call keeps its ID and direct/deferred execution context. Construction and reference counts are separate, as are production, test, support, and generated populations. Sync-only and empty populations are unavailable, with null measurements. Zero matches are not a clean async verdict. Existing unresolved import counts are unchanged.

The report carries source selection, source-facts producer/policy, original async-row hash, configuration fingerprint, and matching run/audit IDs. It regenerates the input report before collection and verifies source hashes and exact owner/call measurements while collecting. Stale, inconsistent, changed, over-budget, or failed inputs produce exit 2, diagnostics, empty rows, and a null summary. These checks are not report authentication, filesystem locking, control-flow proof, or runtime identity analysis.

No score, finding, deduction, gate, automatic concurrency recommendation, or judgment of developer intent is emitted.

The package validator checks internal consistency against validated async/context reports: invocation and source metadata, original-row hash, ordered owners/calls, extents, construction links and repeated references, selected local definitions, decorator options, and recomputed context/population summaries. It is not independent AST remeasurement or report authentication. Incomplete required evidence makes the combined scorecard unusable; observations cannot restore a score. See the [installed-package study](../../shared/scorecard-schema/calibration-runs/python-async-adapters-package-2026-10-24.md).
