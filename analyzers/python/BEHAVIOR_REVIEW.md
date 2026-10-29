# Python behavior evidence before scoring

Two standalone reviewers extend the Python analyzer: handler disposition paths and bounded local async helper paths. Both **Error Handling and Async/Blocking Usage remain unscored**. These extensions are not yet in the packaged adapter or scorecard skill; their existing evidence contracts remain unchanged.

The later [standalone async scoring study](ASYNC_SCORING.md) consumes and verifies
these helper paths without changing this review-only contract. It calculates a
separate, limited-catalogue owner-rate score; packaged scores and Error Handling
remain unchanged.

The subsequent [continuation extension](ERROR_HANDLING_CONTINUATIONS.md) follows bounded post-handler returns/raises through enclosing branches and records retained-default syntax, preserving this first disposition policy unchanged.

## Run

Python 3.12, standard library only, from the repository root:

```powershell
py -3.12 analyzers/python/error_handling_dispositions.py E:/repos/project --output TestResults/dispositions.json
py -3.12 analyzers/python/async_helper_paths.py E:/repos/project --output TestResults/helper-paths.json
py -3.12 analyzers/python/behavior_accuracy.py --output TestResults/behavior-accuracy.json
```

The source reviewers accept the existing selection options and optional UUID `--run-id`/`--audit-id`. Target code is parsed, never imported or executed. Outputs require new `.json` files and existing parents. Complete reviews exit 0; incomplete reviews or invalid invocations exit 2. Empty source populations are complete, not evidence of excellent behavior.

The repeatable corpus command uses the pinned selections and a saved installed-package evidence directory:

```powershell
py -3.12 analyzers/python/behavior_review_corpus.py --repos E:/repos --baseline TestResults/python-async-adapters-package-2026-10-24/final --output TestResults/behavior-corpus-new
```

It verifies clean pinned checkouts, exact source facts, unchanged prior exception and async reports (apart from fresh invocation IDs/timestamps), and input immutability. It records implementation hashes, per-project row digests, summaries, and examples. No score is calculated or modified.

## Handler dispositions

Policy: `python/exception-disposition-paths-review-v1`.

The pass follows statement sequences and both sides of `if`/`elif`/`else`, retaining terminal paths while evaluating subsequent statements only for fallthrough paths. It unions finite outcome states instead of enumerating exponentially many paths. Conditions are not evaluated and infeasible branches are not pruned. All observations are conditional on expression evaluation completing normally; called code can raise, exit, block, or mutate state.

| Classification | Meaning |
| --- | --- |
| `propagation` | All supported paths reach an explicit raise; bare rethrows and raise expressions remain distinguishable |
| `explicit-fallback` | Supported paths return explicitly, or match a prior guarded-import/default/adjacent-literal-return shape |
| `recovery-action` | All supported fallthrough paths contain an explicit call/await or state update; its effectiveness is unknown |
| `loop-transfer` | Supported paths explicitly break or continue |
| `mixed-explicit-disposition` | Supported paths combine the preceding explicit actions |
| `documented-suppression` | An inert handler has qualifying local prose under the existing documentation policy |
| `unexplained-swallowing-candidate` | An inert handler has no observed local rationale or supported fallback and no identified contextual blocker |
| `unresolved` | The reviewer cannot establish a supported disposition for the whole suite or its surrounding context |

`recovery-action` is an action observation, not resolved recovery. A return does not prove a correct fallback, and a raise does not prove outward propagation. No logging-name whitelist or business-correctness judgment is introduced. Developer prose is trusted as a documented choice without assessing its adequacy; directives alone do not count. Documentation in surrounding code is retained as context and withholds a swallowing candidate rather than automatically approving the handler.

Exception groups, enclosing finalizers/context managers, nested handlers, and ambiguous import guards preserve earlier uncertainty. Internal loops, `with`, nested `try`, `match`, declarations, generator suspension, and unsupported expressions remain unresolved. A later `raise` does not erase an earlier unresolved path. Calls inside nested definitions cannot credit the parent.

An inert handler also remains unresolved when surrounding structure suggests a probe, selective fallback, loop continuation, or generator protocol: enclosing loops, protected yields, multiple sibling handlers, nearby prose, a try `else`, or following statements outside the recognized literal-return pattern. These are structural guards, not exception-name exemptions. Conditional no-action paths remain unresolved instead of being counted as known swallowing.

The pass permits at most 32 nested conditional levels and 100,000 visited statements across the report. A depth limit retains an unresolved path; exhausting the global statement budget fails the report. Each original handler is reported once with its stable identity, source hash, population, and outcome set. Summary totals count handlers, not paths or runtime failures.

## Async helper paths

Policy: `python/async-local-helper-paths-review-v1`.

Starting at each selected async owner, follow at most three edges into selected, same-file synchronous function bodies. Links use the existing unique, direct, source-order-valid lexical definition rules. Parameter shadowing, rebinding, decorated ambiguity, conditional definitions, receiver methods, cross-file aliases, reflection, and module mutation remain unsupported or veto matching. The catalogue remains the seven supported blocking import spellings; it is not a general I/O detector.

Each trace carries its root call and helper call extents, owner IDs, referenced definition where linked, context, and reason. The contexts distinguish:

- `async-body`: direct calls and eagerly invoked synchronous helpers.
- `thread-helper-callback`: callback bodies referenced by supported thread helpers, invoked wrappers, or supported decorators. This is helper syntax, not proof a thread was used or the awaitable ran.
- `wrapper-callback-reference`: the callback of a wrapper construction; no invocation is inferred.
- `deferred-generator-body`: deferred generator-expression work; consumption is not inferred.

`to_thread(work())` evaluates the helper eagerly, while `to_thread(work)` supplies a callback. A stored/inline partial construction alone does not execute its callback. Invocation through a supported partial is followed; its bound arguments keep their original eager context. Supported stored `sync_to_async` wrappers and decorators retain callback context. Creating a coroutine or synchronous generator does not traverse its body as eager synchronous work.

Unresolved paths retain explicit reasons, including the helper depth limit and deferred generator bodies. Local source links do not reduce the original unresolved import-call counts. Declaration expressions, nested function/class/lambda bodies, unknown callback protocols, and transitive async execution remain outside scope. Arguments and conditions are not interpreted; unreachable blocking syntax can remain a candidate.

One function with several candidate paths is counted once in `functionsWithDirectOrHelperCandidates`. Repeated paths to the same blocking expression are also summarized as distinct source-call identities. Callback/deferred/reference paths are recorded separately and do not inflate that owner count. Synchronous functions are never added as clean async owners. With no async owners, availability and the candidate-owner count remain unavailable/null.

A shared 100,000-step budget bounds measured calls and traversal expansion. Exceeding it discards the extension's rows and summary. Function measurements are cached within each file. Cycles/repeated owners are not expanded; existing source-order restrictions can withhold a recursive link before the cycle guard is reached.

## Integrity and validation boundary

Both reviewers regenerate their supplied prior evidence and verify current source facts/selection and source digests. New reports bind the prior row hashes and policy configuration. Drift, incomplete or altered inputs, parser/read errors, and budget failures produce incomplete diagnostic reports with no new rows or summary. Inputs and old classifications remain unchanged. These checks do not authenticate externally edited reports or lock a concurrently changing filesystem.

`behavior_accuracy.py` records expected versus actual classifications and candidate-detection confusion counts for 27 curated cases: 15 handler cases and 12 async cases. The suite includes deliberately introduced blocking and silent swallowing, documented choices, coroutine/generator boundaries, and corpus-inspired route, iterator, and selective-fallback counterexamples. These are regression labels, not an independent holdout, a representative accuracy estimate, or a claim of runtime correctness. Additional unit tests cover tampering, budgets, CLI output preservation, nested ownership, source drift, and provenance vetoes.

No formula should use recognized-shape rates as handling-success rates or count unresolved observations as defects. Before scoring, review remaining candidates, expand independently labeled cases, and decide the explicit coverage/availability policy. Packaging and skill integration are a separate versioned milestone.
