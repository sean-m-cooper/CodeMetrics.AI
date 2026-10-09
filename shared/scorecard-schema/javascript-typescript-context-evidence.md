# JS/TS decomposition and handler context

The [2026-10-06 failure-disposition policy](javascript-typescript-error-handling-policy.md)
adds contextual handler classifications and scoring when the entire observed
population is assessable. Unknown bodies and unresolved uses withhold the score.

The [2026-10-05 policy decision](javascript-typescript-context-policy-decision.md)
completed the first numerical calibration pass and retained both dimensions as
unscored evidence. The historical measurement contracts below remain unchanged.

Development ruleset `javascript-typescript-2026-10-03-owner-population` adds
descriptive evidence without new decomposition or error-handling scores. Schema
v3 is unchanged. These are optional extensions for consumers of historical runs.

## Decomposition v1 (historical)

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

## Error handling v1 (historical)

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

## Error handling v2: local referenced implementations

Ruleset `javascript-typescript-2026-10-04-local-handlers` uses scope
`javascript-typescript/errorHandling/handler-evidence-v2`. Handler evidence version 2
changes the counting unit to `distinctHandlerBody`. Catch clauses each have their
own body; Promise rejection handlers are deduplicated by implementation, including
across `.catch` and `.then` calls and across selected files in the same package.

The resolver follows inline functions, named function declarations with bodies,
const function expressions/arrows, const identifier aliases, and compiler-resolved
imports/re-exports whose implementation is in syntactically valid selected source.
Aliases are bounded to 32 links with cycle detection. Bodies from excluded tests,
external packages, unselected workspace packages, or unparsable files do not enter
the population. No additional source is selected as a side effect of resolution.

Mutable bindings, observed binding assignments (including destructuring and loop
assignments), property/method expressions, parameter callbacks, and other value
flow remain uninspected. `uninspectedCallbacks` records file, line, call kind, and
reason; `uninspectedRejectionCallbacks` is its length. This is a conservative static
lookup, not a proof against runtime monkey-patching, eval, or external mutation.

Each handler has `kind: catchClause | promiseRejection`, its body location, and
`uses`, containing every observed call/catch location, original kind
(`catchClause | promiseCatch | promiseThenRejection`), and resolution
(`inline | reference`). `handlerUseSites` sums those uses;
`referencedCallbackUseSites` counts reference uses. Repeated calls do not inflate
`totalHandlers`, classifications, or findings. Nested catches still have separate
bodies. Counts are per selected package, not a claim of runtime invocation counts.

IDs use project, repository-relative file, body kind, and source ordinal among
implemented functions or catches. They survive checkout and line movement but can
change when functions/catches are inserted or reordered. V1 identities and counts
must not be silently compared to v2; the changed ruleset/scope requires a new
baseline. Inspection preserves the complete optional v2 structure.

Comment treatment and unscored/info-only disposition are unchanged. A resolved
shared no-op remains one contextual review lead, not one violation per caller.
Absent or unsupported callback bodies never count as successfully handled errors.

The [application report](calibration-runs/javascript-typescript-application-context-2026-10-04.md)
records the paired six-selection run and the separate decomposition experiment.

## Decomposition v2: executable statements are primary

Version 2 uses measurement `owned-executable-statements-v2` and
`primaryMeasure: ownedStatements`. Statement totals, nearest-rank median/p90,
`largestFunctions`, and `largestFunctionContainers` all use owned executable
statements. Module shares use `shareOfOwnedFunctionStatements`. Existing line
observations remain context; they no longer drive the rankings or score preview.
Empty function populations retain null percentiles and no ranked containers.

A block body counts its executable statement nodes, excluding blocks, empty
statements, labels themselves, type/import/export declarations, nested function
or class declarations, and variable statements with no initializers. An initialized
variable statement counts once, even with several bindings or a multiline literal.
Control statements count once plus their owned body statements; their expressions
and for-loop header declarations do not become extra statement nodes. This is a
syntactic work-unit convention, not a count of machine instructions or side effects.

A concise expression-bodied function counts as one implicit return. For example,
`() => value` and `() => { return value; }` each count one statement. Nested
implemented functions retain separate rows; returning another arrow counts one
return for the outer function and independently measures the inner body. Statements
inside callbacks never inflate their enclosing function's body count. Initializer
and literal context remain separate, and the earlier class/enum/parameter exclusions
are unchanged.

Statements are the agreed primary size measure, while responsibility remains a
review question. Source formatting and large literals do not themselves imply a
need to split code. The offline numerical ladder is still a calibration preview;
no decomposition findings, penalties, or Code Quality blending are introduced.

The profile version identifies the measurement change. Existing scores, decisions,
findings, and dimension scopes are unchanged, so the development ruleset remains
`javascript-typescript-2026-10-04-local-handlers` and score baselines remain compatible.
Consumers comparing decomposition measurements must require the same measurement
version; never present the v1-to-v2 count/ranking change as source improvement.
See the [statement calibration report](calibration-runs/javascript-typescript-statements-2026-10-04.md).
