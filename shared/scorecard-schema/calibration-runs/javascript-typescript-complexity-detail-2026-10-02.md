# JS/TS complexity explanations

This development pass adds diagnostic evidence to the existing
`javascript-typescript-2026-10-02-type-erasure` ruleset. It changes no scoring
policy, measurement, scope, ruleset or raw CSV format. The npm package remains
unpublished version 0.3.0; all changes remain local.

## Evidence contract

Every measured function now carries `complexityBreakdown` version 1. Its `counts`
object separates if statements, non-default case labels, loops, catch clauses,
ternaries, logical AND, logical OR and nullish coalescing. `decisionIncrements`
is their sum. Adding `baseline: 1` gives `total`, equal to `ownComplexity`.
The scoring measurement derives CC from these same counts, avoiding a second,
potentially divergent explanation algorithm.

The detail is available on all function contribution rows, sampled hotspots and
high-complexity finding observations. Packaged inspection preserves it for the
scorecard skill and other consumers. Missing detail in historical evidence means
unavailable, not an incompatible score. Unknown diagnostic versions should not
be interpreted. See the [consumer guidance](../../../docs/evidence-workflows.md#jsts-complexity-explanations).

This does not measure nesting or automatically label designs. Case labels can
share a body; logical operators can compute values outside branch conditions.
Documented protocol and ordering requirements still matter when interpreting
hotspots. These counts add no penalty and do not establish bugs.

## Verified corpus explanations

The same four pinned selections from the [hotspot review](javascript-typescript-hotspot-review-2026-10-02.md)
produce the following explanations. “Logical” combines the separate operator
counts for this display; evidence retains each count.

| Function | Recorded explanation |
| --- | --- |
| Express `send` | 23 = 1 + 14 if + 4 case + 4 logical |
| Zod `convertBaseSchema` | 122 = 1 + 76 if + 7 case + 3 loop + 14 ternary + 21 logical |
| Query Core `createResult` | 29 = 1 + 10 if + 1 catch + 2 ternary + 15 logical |
| React Query hydration callback | 13 = 1 + 6 if + 1 loop + 5 logical |

All 2,560 function explanations sum to their recorded CC. All 124 complexity
findings carry matching explanations. The independent hotspot audit also verifies
the individual count categories against source for the four functions above.

## Invariance and validation

- 114 tests pass, including six new cases covering every supported category,
  shared case labels, all loop forms, zero-decision bodies, nested ownership,
  type-erasure boundaries and consistent propagation without extra deductions.
- All six pinned calibration fixtures pass without updating their baseline.
- Installed-package tests verify sums, finding detail, typed/untyped MI
  equivalence, inspection preservation, schema validation, comparisons, gates
  and SARIF generation.
- Before/after runs use identical pinned source/configurations and distinct run
  IDs. Raw CSV is byte-identical in every pair. Removing only the new
  `complexityBreakdown` properties makes the entire dimension evidence identical
  to the pre-change output: scores, measurements, decisions, findings, fingerprints,
  dispositions and scope all match.
- Actual before/after comparisons remain compatible, report no new/resolved
  findings, and pass warning and zero-score-drop gates for all four selections.

Method complexity remains 7.0 / 5.7 / 6.7 / 8.1 and MI remains
5.9 / 6.6 / 7.2 / 7.2 for Express, Zod v4, Query Core and React Query respectively.
No threshold was adjusted to preserve these values; the new information is
diagnostic only.

The [recorded verification artifact](javascript-typescript-complexity-detail-2026-10-02.json)
contains pinned revisions, analyzer bundle hashes, run IDs and audited explanations.
Full evidence, CSV, configurations, the saved pre-change bundle and logs are under
`E:/repos/CodeMetrics.AI/TestResults/js-ts-complexity-detail/`. The existing
`function-policy-corpus.mjs` and `hotspot-audit.mjs` scripts reproduce the paired
runs and source checks using that saved bundle and the pinned clean checkouts.
The external repositories remain unchanged; no corpus code was executed.
