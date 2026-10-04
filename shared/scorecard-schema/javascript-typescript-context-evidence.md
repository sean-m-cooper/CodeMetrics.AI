# JS/TS decomposition and handler context

Development ruleset `javascript-typescript-2026-10-03-owner-population` adds
descriptive evidence without new decomposition or error-handling scores. Schema
v3 is unchanged. These are optional extensions for consumers of historical runs.

## Decomposition

`dimensions.codeQuality.componentDetails.decomposition` version 1 uses measurement
`owned-function-size-and-module-distribution-v1`. Its status is `unscored` on a
complete run and `failed` on incomplete analysis; its score is null. Code Quality
continues to use only the established method-complexity policy.

Each module exposes implemented functions and their owned source lines, statement
counts, and object/array literal entry counts. Function source lines reuse the
owned-body measurement used by complexity/MI: signatures, nested function bodies,
type syntax, and comments do not increase the enclosing function's size. Nested
implemented functions have their own rows. Statement counts exclude blocks,
empty statements, and declarations of nested functions/classes/types/enums; an
expression-bodied arrow can therefore have zero statements and nonzero lines.

The population reports function count, summed owned function lines, and nearest-rank
median/p90 function lines. Empty populations have null percentiles. Largest-function
and module-container lists sample ten entries each. Module concentration is the
module's sum of owned function lines divided by the whole population's sum.
These sums are not unique physical file LOC: separate functions on one physical
line each own tokens on that line. No finding threshold or responsibility inference
is attached to any of these measurements.

Module initializer statements and literal entries are separate from function
bodies. Top-level interface/type-alias, enum, and direct export-from declaration
counts provide context. Class/enum initialization and parameter defaults remain
outside the size profile; see its explicit exclusions. Literal counts describe
syntax, not proof of passive data or safe initialization. Direct re-export counts
are declaration occurrences, not the dependency graph's unique edge counts.

Present large bodies as review candidates with their function/module context.
Do not infer an SRP violation from a large module, a data table, or a controller's
orchestration role. Do not average this null component into Code Quality or claim
that an unchanged score means decomposition was scored.

## Error handling

`dimensions.errorHandling.handlerEvidence` version 1 records handler occurrences:
catch clauses and inline rejection callbacks on resolved standard Promise/PromiseLike
`.catch` and the second argument to `.then`. Custom APIs named `catch`, `.finally`,
and callbacks whose bodies are referenced rather than inline are not inspected as
handlers. Missing/non-inline rejection arguments increment
`uninspectedRejectionCallbacks`, so the inspected population is not comprehensive.

Each inspected occurrence has one classification:

| Classification | Deterministic observation |
|---|---|
| `unexplainedEmpty` | Only empty statements or no-value/undefined returns, with no substantive body comment |
| `documentedEmpty` | The same empty syntax, with a body comment containing at least one letter or number |
| `containsCode` | Other syntax; no assertion that it recovers or correctly handles the error |

An unshadowed `undefined` and `void` of a numeric literal are recognized undefined
values. Returning a fallback value, throwing, logging, and side-effect expressions
are not classified as empty. Comments inside the body, or attached immediately
before its opening brace or concise callback expression, document intent.
Unrelated comments outside that body attachment do not.
The analyzer records these comments without judging their rationale or demanding
that a particular business choice be changed.

Counts partition the inspected handlers. `unexplainedEmptyPercent` uses that
inspected population only, and is null when there are no handlers. It is not a
score. Empty handlers can be valid fallback paths whose handling occurs elsewhere;
even the word "unexplained" refers only to the body-comment observation.

Unexplained empty bodies produce informational review leads with
`scoreDisposition: excludedUncalibrated`. They do not lower any score or fail a
warning/error gate. Handler IDs use repository-relative file, project, kind, and
occurrence ordinal; line movement is stable, but inserting/reordering handlers of
the same kind can change their identities. General exception/rejection flow and
referenced callback recovery are outside this first pass.

The dimension remains `skipped` with no score/scoring decision because its scoring
policy is uncalibrated. On incomplete source it is `failed`; surviving rows are
diagnostic only. Zero handlers is not evidence for a perfect score. Consumers must
not reconstruct a score from the percentage, findings, or CSV.

## Verification

The tests cover nested ownership, data-only modules, imported/shadowed APIs,
documented swallowing, referenced callbacks, zero populations, and parse failures.
The [paired public-corpus report](calibration-runs/javascript-typescript-owner-context-2026-10-03.md)
records unchanged complexity, MI, raw CSV, and architecture, along with the newly
observed size and handler populations. This is an evidence-development corpus,
not proof of universal scoring calibration.
