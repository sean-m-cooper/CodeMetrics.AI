# JS/TS owned-function scoring

Development ruleset `javascript-typescript-2026-10-02-function-policy` introduces
`javascript-typescript/codeQuality/owned-functions-v2` and
`javascript-typescript/maintainability/owned-functions-v2`. Schema v3, raw metrics
and CSV are unchanged. The npm version remains 0.3.0 during local development;
these policies are not yet published. Older scores are not compatible baselines.

The follow-up ruleset `javascript-typescript-2026-10-02-type-erasure` keeps those
anchors and weights, correcting measurement to `owned-function-body-v2-type-erasure`.
Type-only assertion wrappers (`as`, `satisfies`, angle assertions, non-null `!`)
contribute their runtime expression but not erased syntax. Type annotation colons,
definite-assignment `!`, and generic argument delimiters/commas are excluded.
Standalone instantiation expressions (`factory<T>`) retain executable operands,
including nested functions and decisions, despite TypeScript classifying the node
as a TypeNode. Runtime object colons, comparisons and boolean negation remain.
Raw metric formulas are unchanged; previously undiscovered functions inside
instantiation expressions can now add legitimate member rows. All four audited
corpus CSV files remain byte-identical. See the [hotspot audit](calibration-runs/javascript-typescript-hotspot-review-2026-10-02.md).

## Product calibration

| Measurement | Individual score anchors |
| --- | --- |
| Own cyclomatic complexity | 3 → 10; 5 → 8; 10 → 6; 20 → 4; 40 → 0 |
| Own maintainability index | 40 → 0; 52 → 2; 58 → 4; 65 → 6; 70 → 8; 75 → 10 |

Interpolate linearly between anchors and clamp to 0–10 outside them. Complexity
is 40% of exactly one worst individual score plus 60% of the remaining mean.
Maintainability is 40% of the mean of the weakest `ceil(N/5)` individual scores
plus 60% of the remaining mean. A singleton uses its individual score. Zero
implemented functions is unmeasured, never a measured 10. Every function belongs
to one group only. Ties use the source identity for deterministic ordering.

These are the product expectations accepted for .NET, applied to an explicitly
bounded JS/TS population. A severe hotspot prevents an excellent complexity
score even in a large codebase. A CC40+ function caps that aggregate at 6 before
the remaining population contributes any additional shortfall. Scores measure
static maintainability signals, not confirmed bugs. Language measurements are
not empirically calibrated against each other; do not average ecosystem scores.

## Measurement and ownership

Implemented declarations, expressions, arrows, methods, constructors and accessors
count as functions. Bodyless overloads, declarations, interfaces and enums provide
no population credit. Discovery assigns each included file to one package in
the supported npm workspace model. A function ID hashes its repository-relative
file, syntax start offset and kind. Different callbacks on the same line remain
distinct. This is an identity within a run; it is not a stable cross-edit finding
fingerprint. Existing rule/member finding fingerprints retain their contract.

Only the function body contributes. Exclude nested function, class, enum and
type declarations from the enclosing body's measurements; separately visited
implemented functions retain their own measurements. Signatures and type-only
nodes contribute none. Parameter defaults, top-level statements, static blocks
and non-function field initializers are outside this first policy. A lambda in
an initializer is still an implemented function. These exclusions are scope
limits, not findings of clean code. No source/compiler variants are aggregated
by this analyzer beyond the existing single-owner file discovery.

Own CC starts at one and adds a decision for each `if`, loop, `catch`, non-default
switch case, ternary, `&&`, `||` and `??`. JSX markup alone adds no decisions.
Own source lines count all occupied lines of non-brace/non-semicolon body tokens,
including multiline literals. Comments and blank lines add none. Halstead volume
uses token count times log2(vocabulary), treating identifiers/literals as operands
and other body tokens as operators. JSX text is excluded from Halstead and line
measurement. Nested and type-only tokens are excluded.

Own MI uses the unrounded value of
`clamp((171 - 5.2*ln(max(V,1)) - .23*CC - 16.2*ln(max(LOC,1))) * 100/171, 0, 100)`.
A body with no owned source lines receives 100. This distinguishes an implemented
empty function from declarations that never enter the population. Raw CSV MI
retains its historical signature-inclusive, token-start-line, integer-rounded
measurement; it cannot reconstruct this score.

## Arithmetic and evidence

Logarithms use JavaScript floating point. Each resulting finite MI is converted
from its decimal string representation to an exact rational before interpolation
and weighting. CC uses integer/rational arithmetic. No individual score is rounded
before aggregation. Final scores round once to one decimal, half up: 8.65 → 8.7.
Number-valued diagnostic contributions are display approximations; rational
weights and the executed final decision identify the aggregation.

The existing `deductions` operation records equivalent weighted shortfalls from
10 for the `weakest` and `remainder` groups. These step scores are deduction
amounts, not component quality scores or additional penalties. Inputs record
anchors, counts, aggregation and rounding. `scoring.observations` records all
`functionContributions`; `topOffenders` is only a five-function sample. Hotspot
findings above own CC10 link to their one contributing group as `policyInput`.
They never introduce a separate penalty. Failure withholds every source score.

Optional `complexityBreakdown` version 1 appears on function contribution/sample
rows and high-complexity finding observations. Its eight `counts` fields are
`ifStatements`, `switchCases`, `loops`, `catchClauses`, `ternaryExpressions`,
`logicalAnd`, `logicalOr` and `nullishCoalescing`. Their sum is `decisionIncrements`;
`total = baseline (1) + decisionIncrements = ownComplexity`. These counts come from
the scoring measurement's owned traversal. They preserve nested-body and type-erasure
boundaries and count each non-default switch label, including shared-body labels.
This is additive diagnostic evidence, not another metric or a nesting assessment.
Its introduction changes no scores, measurement policy, finding identities, ruleset,
scope or comparison compatibility. Absence in older evidence means unavailable.

`codeQuality` remains the evidence key. Its display name is **Method Complexity**.
`componentDetails.methodComplexity` exposes its score and population, while
`componentDetails.decomposition` is explicitly unsupported with a null score.
Consumers must not infer a JS/TS decomposition score or apply the .NET combined
C&D weighting. `maintainability` remains the MI-based dimension.

See the [initial corpus comparison](calibration-runs/javascript-typescript-function-policy-2026-10-02.md)
for pinned inputs and observed effects. This corpus informs follow-up work;
it does not justify moving thresholds to favor particular libraries.
