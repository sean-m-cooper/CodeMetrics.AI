# JS/TS Error Handling: failure disposition v1

Development ruleset: `javascript-typescript-2026-10-06-handler-disposition`.
This policy is implemented locally; npm 0.4.0 retains its historical unscored
handler evidence. Fresh baselines are required when adopting the new ruleset.
Schema v3 and the other dimension policies are unchanged.

## Product decision

Measure unexplained swallowing, not the business correctness of recovery or the
existence of bugs. Honor attached developer intent without assessing its merit.
Do not equate a nonempty handler with successful handling. Do not infer intent
from names such as `noop`, `tryParse`, or `ignore`.

| Disposition | Meaning within the recognized scope | Issue weight |
| --- | --- | --- |
| `propagated` | An unconditional throw on the failure path | 0 |
| `failureResult` | An explicit literal result, or explicit outcomes on both branches | 0 |
| `reported` | Standard `console.error` or `console.warn` reporting | 0 |
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
  outcomes on both branches. Arbitrary calls, nonliteral returns, partial branches,
  custom logging APIs, and unsupported control flow remain unknown.
- Recognize an empty catch followed by an explicit outcome. Also recognize the
  bounded pattern of an immediately preceding uninitialized `let` local, a single try
  assignment, and a strict `local === undefined` guard with an explicit failure
  outcome. Shadowed `undefined`, different state, prior initialization, and direct
  self-reference on the assignment's right side do not establish that pattern.
- A try returning a value followed by an empty/undefined catch remains unknown:
  the undefined result might be a caller contract. Outer continuation and finally
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
