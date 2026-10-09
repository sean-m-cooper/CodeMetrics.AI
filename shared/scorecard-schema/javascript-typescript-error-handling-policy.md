# JS/TS Error Handling: failure disposition v2

Version 0.5.0 ruleset: `javascript-typescript-2026-10-09-handler-contracts`.
Version 0.4.0 retains its historical unscored handler evidence. Fresh baselines
are required when adopting the new ruleset, including from the unpublished v1 prototype.
Schema v3 and the other dimension policies are unchanged.

## Product decision

Measure unexplained swallowing, not the business correctness of recovery or the
existence of bugs. Honor attached developer intent without assessing its merit.
Do not equate a nonempty handler with successful handling. Do not infer intent
from names such as `noop`, `tryParse`, or `ignore`.

| Disposition | Meaning within the recognized scope | Issue weight |
| --- | --- | --- |
| `propagated` | A throw or caught-error payload forwarded to a caller-supplied callback | 0 |
| `failureResult` | An explicit value/container, or explicit outcomes on both branches | 0 |
| `reported` | Standard console reporting or a resolved source-backed logging wrapper | 0 |
| `fallback` | A recognized outcome in the continuation after an empty catch | 0 |
| `documented` | Attached developer intent | 0 |
| `unexplainedSwallowing` | A recognized silent discard without declared intent | 1 |
| `unknown` | The bounded analysis cannot establish the disposition | Unavailable |

These are source-level signals. Reporting does not establish that a human will
see a log; a literal result does not establish that callers interpret it correctly.
No claim is made about runtime mutation of built-in APIs, runtime performance,
the suitability of a fallback, or the absence of exception-handling defects.

## Counting and formula

Count each resolved implementation body once across its linked use sites. Keep
catch clauses distinct. Do not count unresolved callback uses as distinct bodies:
they may refer to existing implementations. Classification uses implementation
context plus bounded use context, never a favorable call site borrowed to excuse all uses. Conflicting
resolved classifications produce `unknown`.

For a complete analysis with at least one body, no unknown dispositions, and no
unresolved standard-Promise rejection callbacks:

```text
population = 10 - 10 * unexplainedSwallowing / assessedHandlers
cap = unexplainedSwallowing > 0 ? 9 : 10
score = round-half-up(min(population, cap), 1 decimal)
```

Arithmetic uses exact rational counts. Examples: 2/2 = 0; 2/100 = 9 after the cap;
20/100 = 8; 50/100 = 5; 5/16 = 6.9; 0/100 = 10. These are product-policy scores,
not estimated defect probabilities. JS/TS scores remain uncalibrated against .NET.

Any unknown body or unresolved callback use withholds the entire dimension score.
Zero handlers is unavailable, not 10. Incomplete source yields `failed` with
diagnostic evidence only. Other dimensions can still be scored on a complete run;
the overall must disclose its partial scope.

## Bounded recognition

- Preserve the established empty-body syntax classification and meaningful body
  comments. Body comments in empty handlers establish documented intent. Nested
  function comments are excluded from the enclosing handler's rationale.
- Additional intent is recognized in attached declaration/overload, single-binding
  declaration, catch, and try comments with explicit intent vocabulary: ignore,
  swallow, discard, optional, best effort, does nothing, no-op, or intentional.
  The same vocabulary recognizes rationale inside nonempty bodies. This is a
  bounded English-text recognizer, not semantic validation. Unrecognized rationale
  does not imply that a developer's decision is wrong. For portable recognition,
  put a meaningful explanation directly inside an empty handler, or use an
  explicit phrase such as `Intentional: ...` in the attached comment.
- Recognize literal results, throws, standard console reporting, and explicit
  outcomes on both branches. Constructed objects/arrays/instances and bounded
  immutable aliases also establish explicit results. Suitability and caller
  interpretation are not inferred. Arbitrary calls and mutable return values
  remain unknown.
- Forwarding the caught error, or its properties in a plain first-argument payload,
  to an unchanged caller-supplied callback is delegation of failure. It is not proof
  that the consumer handles it correctly. Local functions with callback-like names,
  defaulted/reassigned callbacks, and reassigned error bindings do not qualify.
- Logging wrappers must resolve to repository-owned implementations and forward
  parameter data through at most three implementation hops to standard console
  output. Ordinary wrappers require a direct unconditional call. A documented
  logging contract may have level/configuration filters; a reachable payload-bearing
  console sink still must be present. Names alone, no-op lookalikes, external
  declarations, optional calls, and reassigned implementations do not qualify.
  This observes a logging contract, not guaranteed delivery under every runtime
  configuration, dynamic dispatch, or monkey patch.
  Already-loaded local helper source outside the selected metrics population may
  supply this contract; tests, dependencies, declarations, generated files, and
  source outside the repository are excluded. Helpers add no metric/body credit.
- Recognize an empty catch followed by an explicit outcome. Also recognize the
  bounded pattern of an immediately preceding uninitialized `let` local, a single try
  assignment, and a strict `local === undefined` guard with an explicit failure
  outcome. Shadowed `undefined`, different state, prior initialization, and direct
  self-reference on the assignment's right side do not establish that pattern.
- A try returning a value followed by an empty/undefined catch remains unknown:
  the undefined result might be a caller contract. A private named helper is
  recognized when every reference is a selected-source call whose result is
  immediately truthiness-guarded and all result reads are guarded. Exported or
  escaping helpers, unguarded reads, and unexpected assignments remain unknown.
  Outer continuation and finally
  blocks remain unknown where they can change the outcome. A terminal empty catch
  and an empty Promise rejection callback whose result is discarded can establish
  unexplained swallowing. Assigned, returned, awaited, or chained Promise results
  require caller/continuation context and remain unknown. A shared callback with
  mixed use contexts remains unknown and still counts only once.

No comment syntax or CMAI suppression code is introduced by this policy.

## Evidence and consumers

`handlerEvidence` remains version 2 with its distinct-body syntax measurements.
Each handler gains `disposition` with a classification, reason, and recognized
intent comments when present. `dispositionEvidence` version 1 reports disposition
counts, assessed/unknown bodies, assessed percentage, and unresolved callback
uses. The percentage describes resolved bodies only; unresolved uses are separate
and always block scoring.

Scored dimensions include a `scoringDecision` showing inputs, formula, rounded
population result, cap, and finding effects. `unexplainedSwallowing` is a warning
only when the population supports scoring. Unknown dispositions and observations
from unavailable populations are informational, excluded from scoring. This keeps
an unsupported conclusion from becoming a quality-gate failure.

Consumers must read dimension status and scope. Never reconstruct a score from
`unexplainedEmptyHandlers`, use counts, CSV, or reviewed examples. Surface unknown
counts and distinguish reported intent from independently verified recovery.

The [earlier calibration decision](javascript-typescript-context-policy-decision.md)
rejected scoring empty syntax alone. This policy replaces that candidate with
contextual classification and explicit abstention; it does not reclassify manual
review labels as automatic exemptions.
