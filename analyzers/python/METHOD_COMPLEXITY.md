# Python Method Complexity policy

Accepted product baseline, 2026-10-13. Adapter **0.7.0** scores `dimensions.codeQuality`, displayed as **Method Complexity**, using policy `python/method-complexity-worst-40-remainder-60/v1` and ruleset `python-2026-10-13-method-complexity-v1`. Evidence uses `calibration: baseline`. The other eight dimensions remain unavailable. Adapter **0.8.0** retains this scoring policy and measurements, adding unscored exception-handler evidence under ruleset `python-2026-10-15-error-handling-review-v1`. Adapter **0.9.0** adds unscored context evidence under ruleset `python-2026-10-15-exception-context-review-v1`, with the same Method Complexity policy and measurements. Adapter **0.10.0** adds unscored raise/chaining evidence under ruleset `python-2026-10-15-exception-transfers-review-v1`, with unchanged Method Complexity. Adapter **0.11.0** adds unscored call/return evidence under ruleset `python-2026-10-15-exception-operations-review-v1`, with unchanged Method Complexity. Historical 0.7.0, 0.8.0, 0.9.0, and 0.10.0 evidence retain their original contracts; cross-version comparisons are not enabled.

## What is scored

Each selected **production implementation** contributes once, identified by its source-facts function ID. Methods, ordinary functions, async functions, nested functions, and lambdas use their own decisions under `python/owned-function-complexity-v2`. Nested bodies do not also contribute to their parent's CC. Declaration-only exclusions retain their source-selection provenance. An ordinary no-op implementation remains measured.

Only `population: production` enters the score. Include switches can add test, support, and generated functions to review evidence without changing the scored population. Explicit production-path overrides reclassify eligible source and change selection identity. Path classifications are inspectable conventions, not proof of installed-package membership or runtime reachability. No eligible production implementations means unavailable, not 10.

## Ladder and aggregation

| Function CC | Individual score |
| ---: | ---: |
| 1–3 | 10 |
| 5 | 8 |
| 10 | 6 |
| 20 | 4 |
| 40 or more | 0 |

Interpolate linearly between anchors. For two or more eligible functions, the score is **40% of the worst individual score plus 60% of the arithmetic mean of all remaining individual scores**. Ties for worst CC are ordered by stable function ID; choosing among tied scores does not affect the result. A singleton receives its individual score with 100% weight. The worst function is excluded from the remainder and receives credit or loss only once.

The canonical shared engine calculates with exact decimal/rational arithmetic and rounds the final result to one decimal, with midpoint ties up. Individual and group values are not rounded before aggregation. For example, a CC 20 function and sixteen remaining functions with mean individual score 8.25 produce 6.55, displayed as **6.6**.

These are deliberate coaching/product decisions: 10 is exceptional and 8 is a practical target. A difficult function should constrain excellence even among many simple functions; additional hotspots increase aggregate loss. The result describes control-flow complexity, not the presence of bugs, business correctness, runtime speed, cohesion, or availability of a safe refactor. Membership-style simple OR patterns count as one decision; guards, structural alternatives, comprehensions, and exception branches retain the recorded measurement policy.

## Evidence and boundaries

`codeQuality.methodComplexity` records policy, selected/eligible counts, excluded population counts, the aggregate, and contributions with function IDs, individual scores, rational weights, and weighted losses. `codeQuality.scoringDecision` records the selected policy and finding attribution. CC ≥ 10 findings remain informational review leads; production complexity findings reference aggregate inputs and are not additional deductions. Included nonproduction findings do not contribute to the score.

Owned statements, block nesting, exact-body duplication, coverage, CRAP, and exception-handler observations stay separate **unscored review evidence**. A lambda can have measured CC while decomposition or coverage is unavailable. Missing optional coverage does not lower the score. Malformed or stale supplied review evidence, or any incomplete combined analysis, withholds the score and findings rather than returning partial success.

The scorecard has **1/9 dimensions scored** when a nonempty production population is available. Any reported aggregate equals Method Complexity and must be labeled **partial assessment, 1/9 dimensions scored**. It is not an overall codebase-health grade. No Python/.NET/JS score equivalence is established.

## Compatibility and acceptance evidence

Adapters through 0.6.0-experimental retain their historical `experimentalPreview` contract and have no scored dimensions. Those previews could include explicitly selected nonproduction bodies; they must not be promoted or rewritten. Current evidence omits `experimentalPreview` and uses `methodComplexity` instead. Raw source-facts and review producer versions remain unchanged.

Comparisons and score-drop gates require matching supported versions, rulesets, effective configuration/selection, dimension scopes, and available scores. Historical previews, empty populations, and incomplete results cannot gate. Explicit finding-severity gates operate on findings and remain separate from score deductions. Saved imports retain their original IDs and remain historical.

The [initial calibration study](../../shared/scorecard-schema/python-complexity-calibration.md), [implemented match adjustment](../../shared/scorecard-schema/calibration-runs/python-match-v2-2026-10-10.md), authored population/rounding cases, and four pinned corpus selections support this baseline. They do not establish statistical defect prediction or represent all Python software. The accepted policy preserves those anchors and weights rather than tuning them to improve corpus scores. Parser support remains Python 3.12.
