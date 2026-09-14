# Factory coupling review: preserve measurements, defer a scoring exemption

Recorded 2026-09-11. This is a source review and a set of labeled regression cases, not a new scoring policy or a full scorecard run.

## Question and decision

Correct partial-type aggregation lowered Polly's architecture score from 9.3 to 5.6 because the non-generic `Policy` exposes 43 structural dependencies. The question was whether broad factory APIs deserve different interpretation from methods coordinating many dependencies.

The reviewed source supports making that distinction visible. It does **not** yet support an automatic factory exemption, a class-name rule, or replacing class coupling with maximum direct method coupling. Keep raw and structural coupling, thresholds, scores and current findings unchanged. A high-coupling finding remains a review lead, not an established responsibility defect.

## Reproducible observations

- Polly source commit: `1a80392b1f093f40e59c515f4aeb989bea5db857`; tracked source unchanged during review.
- Analyzer commit: `63aeb46`; existing coupling and CC calculators, without modified exclusions.
- Scope: `src/Polly/Polly.csproj`, `net6.0`, Release. Roslyn compilation completed with no compiler errors. No target tests or package checks were run for this diagnostic review.
- Non-generic `Polly.Policy`: 43 structural dependencies, 76 raw dependencies and 261 metric members. Its 258 method declarations comprise 228 static and 30 instance methods.
- Direct structural method coupling: 109 methods at zero, 126 at one, 23 at two. Maximum method CC is 7. These measurements do not include the transitive behavior of same-type helpers, callbacks or virtual overrides.
- `CacheEngine.Implementation` and `TimeoutEngine.Implementation` each have three direct structural dependencies in this compilation. Their execution behavior must be read alongside that small count.

The local Roslyn diagnostic project, execution log, all method signatures/locations, creation sites and invocation targets are under `E:/repos/CodeMetrics.AI/TestResults/factory-review/`. `method-trace.json` is the underlying trace; `dependency-usage.json` maps each of the 43 dependencies to directly observed construction/invocation methods; `method-trace.md` is the readable census. Usage categories overlap and do not exhaust declaration/type-argument references. Syntax inside callbacks is included; its execution timing is not inferred.

## Source labels and boundaries

| Reviewed source at the pinned commit | Label | Evidence and limit |
|---|---|---|
| `src/Polly/Bulkhead/AsyncBulkheadSyntax.cs:58` | Validating factory | Checks three arguments, then constructs `AsyncBulkheadPolicy`. This method does not execute the policy. |
| `src/Polly/Caching/CacheSyntax.cs:331` | Validating factory | Checks callback/provider arguments and passes them into `CachePolicy`. Passing a callback is not proof that it executes here. |
| `src/Polly/Policy.ExecuteOverloads.cs:232` | Execution and result handling within the same public type | Calls `Execute`, produces success/failure results, catches exceptions and classifies them. The whole type cannot be labeled a pure factory from the preceding two samples. |
| `src/Polly/Policy.SyncNonGenericImplementation.cs:12` | Virtual and callback delegation | Adapts an action into a generic virtual/abstract implementation. A direct coupling count does not describe the downstream implementation. |
| `src/Polly/Caching/CacheEngine.cs:6` | Runtime orchestration | Reads cache state, invokes the action and notifications, calculates TTL, writes the result and handles failures. Small direct fan-out does not mean trivial behavior. |
| `src/Polly/Timeout/TimeoutEngine.cs:7` | Runtime orchestration | Coordinates cancellation, execution, waiting and exception translation. This label describes observable behavior, not a defect judgment. |

Graph tools assisted discovery and tracing. The counts above come from the analyzer's Roslyn compilation/calculators, not the graph's complexity values or collapsed overload nodes. Source samples were rechecked in this review; no conclusion of comprehensive correctness or test adequacy is claimed.

## Labeled regression cases

`FactoryCouplingReviewTests` compiles four synthetic examples against the actual collectors and architecture probe. All use behavior-bearing dependency classes and keep the class-wide structural union at 12.

| Case | Maximum direct method coupling | Expected interpretation |
|---|---:|---|
| Twelve independent construction methods | 1 | Broad creation surface with narrow individual methods; retain the census and review context. |
| One method calling all twelve dependencies | 12 | Concentrated orchestration; a `Factory` name must not exempt it. |
| A factory returning a product after consulting eleven other dependencies | 12 | Product construction and decision logic coexist; returning an object is not an exemption. |
| The orchestration operation extracted into twelve local helpers | 1 | Same coordination behavior, lower direct maximum. The public entry method itself measures zero. Helper extraction must not earn an automatic factory exemption. |

All four still emit the existing high-coupling observation. The tests label the distinction and protect against unjustified future discounts; they do not certify the current score as a calibrated judgment of each example's design. Four focused regression cases pass. Production analyzer code, package version and scoring policy are unchanged.

## Gate for a future scoring change

Expose class breadth and method-level evidence together before considering any discount. A scoring proposal must survive local-helper extraction, forwarding factories, callbacks, virtual dispatch, mixed factory/behavior types, and contrasting repositories. It must retain explicit unresolved behavior rather than classifying it as safe construction. Continue to treat direct method coupling as supplemental evidence until those cases are labeled and evaluated. This review supplies counterexamples and a decision boundary; it does not establish general calibration.
