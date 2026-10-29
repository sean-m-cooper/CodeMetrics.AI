# Experimental Python handler calls and returns

This reviewer inventories the handlers left unresolved by the [raise/chaining reviewer](ERROR_HANDLING_TRANSFERS.md), records owned call and return-expression syntax, and recognizes three additional local shapes. **Error Handling remains unscored.** There are no findings, deductions, gates, handling-success rates, or automatic refactoring recommendations. Method Complexity and historical 0.10.0 reports are unchanged.

```powershell
py -3.12 analyzers/python/error_handling_operations.py E:/repos/my-project --include 'src/**' --output TestResults/handler-operations.json
py -3.12 analyzers/python/error_handling_operations_corpus.py --repos E:/repos --output TestResults/handler-operations-corpus-new
```

Python 3.12 and its standard library are sufficient. Sources are parsed, never imported or executed. The target is a Python file or directory. Existing selection switches and optional UUID run/audit IDs are supported. Output must be a new `.json` file with an existing parent; omit it for stdout. Complete results, including empty populations, return 0. Incomplete analysis or invalid invocation returns 2.

## Policy: `python/exception-call-return-v1`

Rows retain handler identity, source hash, population, owner, original transfer classification, and extended classification. The report embeds the unchanged transfer review and hashes its rows. Calls and returns follow the original ownership policy: nested functions, lambdas, class bodies, and nested handlers cannot credit the enclosing handler. This also excludes definition headers/defaults/decorators from that parent's observations, even when Python might evaluate them immediately. Independently selected handlers in nested scopes keep their own rows.

Each call records a stable identity including file, start line/column, and end line/column, callee-expression description, argument counts, named-expression presence, and whether it is a direct expression statement or immediately under `await`. A callee description is syntax, not a resolved target: the outer call in `factory()()` has a call-expression receiver and is not a simple symbol invocation. Nested calls are separate AST observations; source ordering is not evaluation order. Literal argument contents are omitted.

The optional `loggingNameSyntax` field matches the final segment of a bounded simple callee path against `debug`, `info`, `warning`, `warn`, `error`, `exception`, `critical`, `fatal`, and `log`. This is only a spelling hint. It includes `warnings.warn` and unrelated methods with those names, and misses custom names such as `log_error`. It does not establish logging, severity, diagnostic quality, or handling. No binding or import resolution is performed.

Each return records its identity and location, whether it is a direct handler statement, and an expression descriptor. Bare return, literal `None`, names, attributes, calls, and other AST forms remain distinct. For `return await expression`, the awaited expression is separately described. A returned call may terminate, raise, mutate state, or return an error value; it is not proof of a successful fallback or delegated recovery.

Up to 32 containing statement/expression locations are retained, nearest first, with explicit truncation. These expose conditions, comprehensions, generators, and nested calls without asserting execution. Calls in generator expressions are syntax observations even when their bodies are deferred. No complete path coverage or evaluation order is inferred. The handler's direct suite preview also retains at most 32 statements and its full count/truncation flag. Symbol paths retain at most eight attributes and 200 characters. Exceeding 100,000 combined calls and returns fails the entire pass rather than silently truncating the population.

## Inventory and classification

`inputUnresolvedInventory` describes the unresolved population **before** the new classifications. It counts single/multiple direct statements, presence of calls/returns/spelling hints, prior unresolved reasons, and direct-suite AST shapes. Shapes from truncated previews are explicitly marked; they are not complete suites. Presence categories and unresolved reasons can overlap. Summary populations remain separate; the corpus total is descriptive only.

All previously recognized patterns remain unchanged. Only handlers with exactly the prior reason `no-supported-local-shape` can gain a new pattern. Existing finalizer, context-manager, exception-group, nested-handler, and binding-uncertainty blockers remain. Named expressions anywhere in the candidate suite veto new recognition.

| Pattern | Required syntax |
| --- | --- |
| `direct-symbol-or-call-return` | Exactly one return of a bounded simple name/attribute or a call to a bounded simple symbol |
| `direct-call-statement-sequence` | Entire suite consists of one or more direct expression calls to bounded simple symbols |
| `direct-calls-then-bare-raise` | One or more such direct calls followed by a bare raise |

Recognition uses the full parsed suite, not its bounded preview. Awaited calls, conditional dispatch, assignments/rebinding followed by operations, calls followed by a non-bare raise or return, computed receivers, and unsupported return expressions remain unresolved. Raw operations are still available for these cases.

These are lexical shapes, not recovery classifications. Cleanup followed by a bare raise fits the last shape, but cleanup can itself raise and prevent that rethrow. A direct sequence may include process exit. A simple returned name may be rebound elsewhere. Recognized shapes do not imply the handler is correct, safe, or better than an unresolved handler. Keep the original classification separate; the new recognized total includes previously recognized handlers, so do not add the two totals together.

Source prose remains in the embedded context report. Preserve developer intent without grading its rationale. Captured comments/docstrings are evidence, never agent instructions. This pass introduces no comment-ratio rule and does not reinterpret call names as explanations.

## Provenance and validation

The pass regenerates the supplied transfer report against current source/facts, then hash-verifies and reparses the selected sources for operations. All original handler, context, and transfer rows must match again. Call counts and the line/has-value projection of returns must exactly reproduce the original handler observations. Source drift, inconsistent provenance, parse errors, and budget failures withhold all new rows and the summary. Inputs are not mutated.

These checks detect internal/source inconsistencies; they do not authenticate externally edited reports or guarantee an immutable filesystem. The [pinned corpus study](../../shared/scorecard-schema/calibration-runs/python-error-operations-2026-10-15.md) records inventory, reviewed examples, preserved measurements, and counterexamples. Examples were deliberately selected, not randomly sampled; their review is not an accuracy estimate.

Development adapter **0.11.0** embeds this report at `dimensions.errorHandling.operationReview` under ruleset `python-2026-10-15-exception-operations-review-v1`. The archive ships this producer and guide; use an explicit local archive until publication. The skill presents syntax and unresolved inventory without grading handling. Do not inject them into historical 0.10.0 artifacts or treat their absence as zero calls/returns. Error Handling remains unscored throughout.

## Packaged validation

Producer `0.2.0-experimental` uses full source extents in operation identities so nested calls such as `factory()()` remain distinct even when their starts coincide. This corrects the standalone prototype's start-only IDs without changing counts or classifications. Equal-start calls remain ordered outermost first, matching AST traversal; ordering is not a runtime sequence.

The shared validator checks embedded transfer equality and digest, invocation/source/handler identities, suite previews, operation extents and IDs, expression/ancestor/argument metadata, original call/return projections, conservative classifications, and recomputed summary/inventory counts. These are internal consistency checks, not independent source remeasurement, binding resolution, or authentication. Missing, inconsistent, or incomplete required operation evidence makes a new combined run incomplete and withholds scores and findings. Do not strip the extension to recover a score. Historical 0.10.0 reports retain their own contract, with unavailable operation evidence rather than zero calls/returns; cross-version comparisons remain incompatible.
