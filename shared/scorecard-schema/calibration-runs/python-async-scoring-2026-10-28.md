# Python Async/Blocking Usage scoring study: 2026-10-28

Implemented standalone policy `python/async-blocking-owner-rate-v1`, using the
accepted JS/TS affected-owner ladder. Selected production async implementations
form the denominator. Each owner with a non-excluded direct or eager-helper
blocking-catalogue observation counts once in the numerator. See the
[policy and CLI](../../../analyzers/python/ASYNC_SCORING.md) and
[machine-readable validation record](python-async-scoring-2026-10-28.json).

## Pinned corpus results

| Project | Eligible async owners | Affected owners | Standalone score | Owners with unresolved paths |
| --- | ---: | ---: | ---: | ---: |
| Requests | 0 | 0 | unavailable | 0 |
| Starlette | 135 | 0 | 10 | 129 |
| HTTPie | 0 | 0 | unavailable | 0 |
| Django | 266 | 0 | 10 | 257 |

These scores describe a seven-spelling blocking catalogue and bounded same-file
helper analysis. They are not broad async-safety grades or runtime performance
measurements. Unresolved paths include receiver calls, ambiguous bindings, and
unsupported helper relationships. Unresolved owner counts are not coverage
percentages; other imported non-catalogue calls can also have unknown behavior.
Unknowns are neither deductions nor proof of nonblocking behavior.

The zero-match corpus does not establish recall. Positive regression fixtures
exercise all seven catalogue spellings, eager helper calls, mistaken eager
offload arguments, partial invocation, and comprehensions. Negative cases cover
thread callbacks, wrapper references, deferred work, rebinding, documented choices,
and nested ownership. The source-contract labels are curated, not an independent
holdout or an estimate of field accuracy.

## Product decisions

- Exact owner-rate bands: below 1% scores 10, below 2% scores 9, below 5% scores 8,
  through 10% scores 6, through 15% scores 4, through 20% scores 2, above 20% scores 0.
  Zero eligible owners receives no score. Display rounding never selects a band.
- Synchronous, test, support, and generated functions provide no denominator
  credit. Repeated paths affect an async owner once; a shared helper may affect
  multiple distinct async roots. Distinct blocking source calls are reported separately.
- Narrow, adjacent declarations of intentional blocking remain visible without
  deductions. Actual comment tokens, explicit intent vocabulary, and statement
  boundaries distinguish this convention from general prose or warning markers.
  The policy documents its conservative limits; it does not judge the business choice.
- Recognized offload callbacks and deferred/reference contexts remain excluded.
  Unresolved paths stay visible on each owner and in summary counts.
- A failed analysis, stale input, tampered evidence, or global budget exhaustion
  produces no score or contribution rows. A bounded helper-depth stop stays unresolved.

## Verification and delivery boundary

- **243 Python tests passed**, no skips, including 13 new scoring tests with 18
  exact ladder boundary cases, execution and intent matrices, population separation,
  shared-helper attribution, source drift, tampering, budget failures, fresh IDs,
  overwrite protection, and no target-code execution.
- All four corpus repositories were clean and matched their pinned revisions
  before and after analysis. Source facts and earlier async/helper evidence matched
  their saved baselines exactly, apart from invocation IDs/timestamps. Inputs were
  unchanged. Final implementation hashes were verified before saving this record.
- Final reports: `TestResults/python-async-scoring-final-2026-10-28/`.
  Test log: `TestResults/python-async-scoring-tests-final-2026-10-28.log`.
  The earlier run predates the final declared-intent rule and is exploratory.
- No package/skill contract, existing Method Complexity score, overall aggregate,
  baseline gate, or release changed. Error Handling remains unscored. Changes are
  local; no commit or publication was performed for this study.

The next integration must version the packaged evidence and canonical validator,
recompute owner contributions rather than trusting supplied scores, and teach the
skill to show B/N and unresolved evidence beside the partial score. Broader
blocking detection and independently reviewed real positives remain useful
calibration work; these corpus tens must not be promoted to full async assurance.
