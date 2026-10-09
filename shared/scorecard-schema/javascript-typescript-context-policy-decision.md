# JS/TS context scoring decision, 2026-10-05

Historical decision. The subsequent [2026-10-06 contextual handler policy](javascript-typescript-error-handling-policy.md)
implements the agreed formula with bounded classifications and explicit unknowns.

**Decision: retain statement decomposition and error handling as unscored evidence.**
The calibration pass is complete for the candidates below. Neither is approved for
production deductions. This is a deliberate product boundary, not an invitation for
consumers to invent scores. Architecture also remains unscored.

The goal is to identify maintainability work without penalizing rational,
documented engineering choices. Additional scores need useful information beyond
existing scores and must survive representative counterexamples. We do not need
nine numbers to make the tool useful.

## Decomposition

Owned executable statements remain the primary size measure. We evaluated the
existing experimental anchors 10/20/40/80/160 statements -> 10/8/6/4/0, with 40%
one worst function and 60% remaining mean, across six pinned selections and 4,062
functions using the released package.

All 45 functions above 40 statements also had MI below 65; 42 also had CC above
10. Those are explicit review thresholds for this experiment, not a new production
rule. No additional candidate crossed the size threshold while remaining outside
both comparison groups. Statement/MI-risk rank correlations were 0.790-0.953.
That overlap does not prove size is universally redundant, but this corpus does
not establish a benefit sufficient to justify another aggregate deduction.

The three large, low-CC exceptions were Uptime Kuma startup, socket registration,
and server construction. Source review shows composition/registration work. This
does not prove optimal decomposition, but count alone cannot establish which
responsibility should move or whether moving it would improve the code.

Keep statement totals, rankings, percentiles, concentration, literal context, and
owned-body separation. Do not blend the preview into Method Complexity or Overall.
Reopen numerical scoring only with labeled examples where a structural signal
adds useful review information beyond CC/MI and distinguishes legitimate
registration/orchestration from a demonstrated decomposition problem.

## Error handling

We tested a narrow population candidate adapted from the .NET population approach:

`population = 10 - 10 * unexplainedEmptyBodies / distinctInspectedBodies`

Use a cap of 9 when an unexplained empty body exists; round half up once to one
decimal. Documented empty bodies and bodies containing code contribute no issue
weight. Count one shared implementation once, retaining all uses. An empty
population or unresolved callback uses makes this candidate unavailable; unknown
uses are neither credited nor invented as distinct handler bodies. General
exception flow and unknown/nonstandard Promise APIs are still outside scope.

The arithmetic passes the intended population examples: two of two -> 0, two of
100 -> 9, 20 of 100 -> 8, 50 of 100 -> 5. **The formula is not the blocker; the
definition of a penalized body is.**

We reviewed all 18 unexplained-empty bodies across 426 distinct inspected bodies,
not just the most convenient examples. Nine have fallback or failure-reporting
paths outside the body; two have explicit intent documented outside the body.
The remaining seven are unresolved review questions, not confirmed defects. The
five bodies already classified as documented empty remain acknowledged.

Examples include Express returning an absent-file result to a caller that checks
it, Zod converting failed coercion into validation issues, and Uptime Kuma
reporting `ok: false` after the catch. Query Core's shared no-op has an explicit
declaration comment outside the implementation body. Penalizing these solely for
empty syntax would violate the product's intent policy.

Therefore this body-only candidate is rejected for production. Keep the measured body
classifications and informational review leads, with their scope limitations.
Manual review labels are calibration evidence, never automatic exclusions or
modifications of deterministic findings. A nonempty body does not establish
correct recovery. Names such as `noop` or `tryParse` alone are not exemptions.

Before enabling deductions, a future policy must handle bounded continuation and
comment-attachment cases, preserve unknown-flow gaps, and pass both genuine-issue
controls and legitimate fallback/declared-intent controls. The current corpus has
no context-verified defective empty handlers, so it cannot establish sensitivity
or precision of a defect detector. Do not tune weights to library reputations.

## Evidence and workflow

Subsequent work: the approved [2026-10-06 contextual policy](javascript-typescript-error-handling-policy.md)
implements the population formula with explicit unknowns and bounded failure-path
recognition. The historical decision and results below describe the earlier pass.

The [calibration report](calibration-runs/javascript-typescript-context-calibration-2026-10-05.md)
contains results and reproduction commands. The
[pinned review labels](../calibration/corpus/javascript-typescript-context-review.json)
retain run/audit IDs, source revisions, source hashes, and supporting ranges.
The [application scorecard](calibration-runs/uptime-kuma-released-scorecard-2026-10-05.md)
uses npm 0.4.0 through the released skill workflow and reports only three scored
dimensions. No analyzer policy, ruleset, package version, or score was changed by
this calibration pass.
