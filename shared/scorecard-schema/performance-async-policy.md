# Async/Blocking Usage classification

Evaluates asynchronous operations and blocking calls for avoidable hazards, accounting
for documented intent and supported usage patterns. Scores reflect code usage, not
runtime speed or throughput. I/O latency, external rate limits and deliberate
throttling do not inherently indicate misuse. The evidence key remains
`performanceAsync`; this display-name clarification does not change scoring.

The .NET 2.3.1 release uses ruleset `dotnet-2026-09-15-declared-async-boundaries`
and policy `dotnet/performanceAsync/context-classification-v4`. This changes which
observations qualify as scored signals. The numeric 0/2/4/6/8/10 ladder, thresholds,
source-site counting and maximum-severity aggregation are unchanged. Population and
severity calibration is deliberately deferred pending corpus review.

## Deterministic classifications

| Observation | Treatment |
|---|---|
| Task/ValueTask result under a proven completion guard | No blocking finding |
| Result after `if (!task.IsCompleted[Successfully]) return/throw;` | No blocking finding when the same local/parameter is unchanged |
| Result after a successful void Task.Wait | No additional blocking finding; timed waits and swallowed failures do not prove completion |
| Completed Task factory or resolved helper returning completed factories on every normal return | No task-blocking finding; preceding synchronous work may still block |
| Completion guard in a ternary expression | No finding in the proven completed branch; the fallback is assessed independently |
| Blocking in a synchronous interface implementation or override | Informational review lead for implicit/explicit methods, properties and indexers |
| Blocking in a private helper/property reached only through recognized contracts | Review lead with `synchronousContractCallChain`; bounded to four caller edges |
| Blocking in a recognized synchronous callback API | Review lead with `synchronousCallbackContract`; see the bounded API catalog below |
| Blocking with an associated local prose explanation | Informational review lead; accept declared intent without judging the business decision |
| Other recognized synchronous waits | Existing scored error signal |
| Thread sleep | Existing scored warning, or review lead at a recognized synchronous/documented boundary |
| SaveChanges inside a loop | Review lead; identify an iteration-scoped using declaration when present |
| Missing cancellation parameter | Recognize direct tokens and forwarded token-bearing contexts; otherwise review lead |
| Materialization before query shaping | Review lead |
| Sequential awaited I/O | Review lead |
| Unbounded task projection | Review lead |
| Demonstrated shared-state mutation in fan-out | Existing scored error signal |

Review leads retain their CMAI codes, locations, confidence and framework observations.
`observations.classification` is `reviewLead` or `actionableSignal`;
`classificationReason` records the recognized context. Review leads have severity
`info`, `scoreDisposition: excludedReviewLead` and an `excluded` finding effect.
They do not contribute to error/warning counts or the SaveChanges ladder trigger.
An actionable signal warrants action or investigation, but is not proof of a runtime
incident. Confidence describes the observation, not the safety of a proposed rewrite.

Completed-task recognition is shared with Error Handling. Synchronous contract and
local rationale classification is also shared; informational waits cannot activate
its existing cap of 4. Other waits still participate in both dimensions under their
existing policies. No cross-dimension weighting change is introduced.

## Bounds and interpretation

Completion proofs use semantic Task/ValueTask properties and the same local/parameter.
Receiver writes and ref/out arguments invalidate earlier evidence. Guards outside
deferred functions are not execution-time proof. Negative guards must end in an
unconditional return or throw. This is not general interprocedural flow analysis:
arbitrary wrappers, aliases and hidden mutation remain limitations. A try ending in
void Task.Wait qualifies only when every catch exits through return, throw or the
framework ExceptionDispatchInfo.Throw method. Receiver writes in catches/finally
invalidate the proof; timed waits returning bool do not establish it.

Completed-return helpers must resolve to a source method in the current compilation that is not async and cannot
dispatch to an unknown override. Every normal return must directly use framework
Task.CompletedTask or Task.FromResult; nested-function returns do not count. Arbitrary
forwarding helpers and task-cache invariants are not inferred. The helper can still do
synchronous I/O before returning: this proof concerns the returned task only.

Synchronous contracts require non-async, non-awaitable-returning members. Task-like
returns and custom instance GetAwaiter patterns do not receive a contract exemption.
Extension-only custom awaitables remain a limitation. Property/indexer accessors and
implicit/explicit interface implementations are recognized alongside overrides.

Private method/property context requires all resolved source uses in the enclosing type,
including partial and nested declarations, to lead to recognized contracts within four
caller edges. Recursion, unknown delegate escapes, uncalled helpers, and mixed async or
unclassified callers prevent propagation. This bounded analysis does not infer reflective
uses, arbitrary call graphs, or general method-name intent. Deferred functions do not
inherit their enclosing function's context.

The callback catalog resolves parameter/delegate and consumer symbols for
Microsoft.Extensions.Options.OptionsBuilder<T>.Configure action callbacks,
Microsoft.AspNetCore.DataProtection.StackExchangeRedis.RedisXmlRepository's synchronous
IDatabase factory, and System.Threading.CancellationToken.Register lifecycle callbacks.
Async lambdas, awaitable-returning delegates and arbitrary Action/Func consumers are
not included. Named private callback methods must satisfy the same all-uses check.
These API contracts explain a synchronous boundary, not runtime safety or the absence
of an opportunity for async initialization, caching, or interface redesign.

Paired synchronous/asynchronous scripting APIs and documented cache invariants remain
review cases; having an async counterpart does not automatically exempt a wait. Existing
CMAI annotations can express reviewed rationale without the analyzer judging it.

## Declared boundaries update

The unpublished `dotnet-2026-09-15-declared-async-boundaries` ruleset uses
`dotnet/performanceAsync/context-classification-v4`. The numeric ladder is unchanged.
The earlier descriptions above remain applicable except for these two bounded additions:

- An explanatory comment on a single initialized Task/ValueTask local declaration can
  apply to that same local in the immediately following statement in the same block.
  The access must stay within the function, with no receiver writes/ref escapes or
  unrelated invocations in that statement. Branch/deferred boundaries, intervening
  statements, other receivers, task-marker comments and unrelated prose do not qualify.
  The reason is `documentedTaskLocalChoice`. It records intent, not proven completion.
- The synchronous script delegate directly returned from the expression-bodied factory
  assigned to `OrchardCore.Scripting.GlobalMethod.Method` in an object initializer is a
  cataloged boundary. Recognition resolves the property and its
  `IServiceProvider -> Delegate` factory signature and requires the returned delegate to
  be synchronous and non-awaitable. The reason is `synchronousScriptingContract`.
  `AsyncMethod`, arbitrary delegate properties, similar names in other namespaces,
  async/task-returning delegates, deeper deferred lambdas and statement-bodied factories
  do not qualify. Having a paired async member alone grants no exemption.

Both classifications retain the original finding, code, source span and confidence as
informational review leads excluded from penalties in Async/Blocking Usage and Error
Handling. Existing CMAI8001/CMAI5005 rationale-bearing annotations remain available for
explicit reviewed intent outside these shapes; see the catalog shipped with the package.
Internal helper propagation, cache completion inference, obsolete APIs and hidden base
members are unchanged. Previous rulesets are incompatible baseline gates. This does not
establish throughput, latency or safety of a synchronous script's execution.

The base local rationale rule checks the operation's statement or enclosing branch,
before crossing a function boundary; the adjacent same-task extension is described above.
Prose must mention synchronous execution,
sync-over-async, blocking or cancellability. TODO/FIXME/HACK markers, commented-out
statements, unrelated prose and general method documentation do not qualify. This is
an intent declaration, not a correctness proof. Existing CMAI directives remain the
precise way to declare a rule-specific exclusion with rationale; supported annotations
and their scope are described by the packaged rule catalog.

Cancellation contexts require an accessible instance CancellationToken member and
forwarding of the context or that member to a resolved task-returning call. This
recognizes a channel, not end-to-end cancellation correctness. Missing signatures do
not establish a dropped token; overload and lifecycle review is still needed.

A loop save may already persist one batch or transaction. Changing session lifetime,
event publication, ordering or failure durability is a design change, not an automatic
optimization. Sequential I/O may be required by shared DbContext use or dependent
operations. Messages request context review instead of prescribing Task.WhenAll or
moving saves outside loops.

This remains a partial static assessment. A 10 means no scored signals were observed;
it is not a throughput, latency, allocation or scalability measurement. Old/new scores
across these rulesets are classification comparisons, not compatible baseline gates
or evidence of improvements to unchanged corpus source.

The unpublished `dotnet-2026-09-14-short-circuit-completion` ruleset recognizes accesses in the right operand of built-in boolean `&&` when the left proves completion of the same Task/ValueTask local or parameter. A negated completion proof on the left of `||` also qualifies. Parentheses and nested boolean guards are supported; wrong receivers, mixed unproven alternatives, eager `&`/`|`, writes/ref/out uses within the guarded expression, user-defined operators and deferred functions do not establish this proof. Write detection is deliberately conservative across the whole expression; general interprocedural mutation is outside scope. Completion proves nonblocking access, not successful completion or safe repeated ValueTask consumption. Both Async/Blocking Usage and Error Handling use the shared classifier. Numerical ladders and raw metrics are unchanged; prior rulesets are incompatible baseline gates.
