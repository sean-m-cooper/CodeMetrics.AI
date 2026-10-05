# JS/TS function-policy corpus comparison

Local development comparison of `javascript-typescript-2026-10-02-async-usage`
(legacy maximum-CC/median-MI scoring) against
`javascript-typescript-2026-10-02-function-policy`. Both builds report npm version
0.3.0; ruleset and bundle hashes distinguish them. This is not a published release.

The [function policy](../javascript-typescript-function-policy.md) implements the
accepted individual anchors and population weights. Thresholds were not changed
after seeing these corpus results. Each pair analyzes identical pinned source,
configuration and file selections. Raw CSV is byte-identical in every pair.

## Results

| Source selection | Files | Functions | Method complexity before → after | Maintainability before → after |
| --- | ---: | ---: | ---: | ---: |
| Express `index.js` + `lib/**/*.js` | 7 | 109 | 4 → **7.0** | 8 → **5.9** |
| Zod `packages/zod/src/v4/**/*.ts` | 107 | 1,948 | 2 → **5.7** | 8 → **6.6** |
| TanStack Query `packages/query-core/src/**/*.ts` | 23 | 427 | 4 → **6.7** | 8 → **7.2** |
| TanStack Query `packages/react-query/src/**/*.{ts,tsx}` | 23 | 76 | 6 → **8.1** | 8 → **7.2** |

These are source-only package selections, not complete repository scorecards.
Tests, fixtures and generated source are filtered by analyzer discovery. Examples,
benchmarks, build tooling and Zod v3 are outside the explicit selections. The TS
scopes extend each package's existing tsconfig and override only source selection
and allowJs/noEmit. No dependencies were installed and no corpus code, build,
tests or lifecycle scripts were executed. Completeness here means source parsing,
not semantic compilation or exhaustive symbol resolution. Decomposition remains
unmeasured; these scores cannot be called combined C&D scores.

## What changed and why

Complexity now includes the rest of the function population while retaining a
strong worst-function weight. Express's `send` is CC23 (individual score 3.4),
with remaining functions averaging 9.381: `0.4*3.4 + 0.6*9.381 ≈ 7.0`.
Zod's `convertBaseSchema` is CC122 (individual score 0), with remaining functions
averaging 9.464: its result is 5.7. A large number of simple functions cannot hide
that hotspot. TanStack Query Core's `createResult` is CC29; React Query's worst
measured function is the `HydrationBoundary` useMemo callback at CC13.

MI falls because the old median obscured a weaker minority. This comparison
includes both the new population weights and the owned-body MI measurement;
it is not a weighting-only experiment. The new group means explain the result:

| Scope | Weak group size | Weak group mean individual score | Remaining mean individual score |
| --- | ---: | ---: | ---: |
| Express | 22 | 2.318 | 8.332 |
| Zod v4 | 390 | 2.337 | 9.476 |
| Query Core | 86 | 3.703 | 9.575 |
| React Query | 16 | 3.710 | 9.558 |

Apply 40%/60% to these means, then round once. No function is counted in both
groups. Express's weakest owned MI is `send` at 34.71, with 57 owned source lines.
Zod's `convertBaseSchema` is MI0.96 with 255 owned source lines. These observations
identify review targets; they do not establish defects or mandate decomposition.

The bounded async dimension is unchanged for these pairs: Express is unmeasured;
the other three scopes score 10 under the provisional async usage policy. That
does not establish runtime performance or exhaustive promise correctness.

## Pinned sources and reproduction

All clones are under `E:/repos`; no corpus checkout or dependency cache was placed
on C:. Checkouts were clean before analysis.

| Repository | Commit |
| --- | --- |
| https://github.com/expressjs/express | `7ef98448f8b38099ab1ded55e458538ad47a51e7` |
| https://github.com/colinhacks/zod | `0b216ef674e297ebe41d8bf902262e56f8755822` |
| https://github.com/TanStack/query | `29859ae60c8dca0a5cdbf8abccc775b655cf43e2` |

The checked-in [summary](javascript-typescript-function-policy-2026-10-02.json)
records pins, package versions, analyzer bundle hashes, configuration/CSV hashes,
run IDs, populations, scores and sampled offenders. Full evidence, configurations,
CSV and logs are preserved locally in
`E:/repos/CodeMetrics.AI/TestResults/js-ts-function-policy/`. The pre-change compiled
development package was preserved in its `before/` subdirectory with the same
dependency installation. Its bundle is local test evidence, not a public package.

From the repository root, with the saved pre-change bundle and pinned clean
checkouts available:

```powershell
. E:/tools/Use-CalibrationDotNet.ps1
npm run build --prefix analyzers/javascript-typescript
node analyzers/javascript-typescript/scripts/function-policy-corpus.mjs `
  TestResults/js-ts-function-policy/before E:/repos `
  TestResults/js-ts-function-policy/corpus
```

The runner rejects changed commit pins or dirty checkouts, validates every
evidence file, compares populations/filters/configuration, checks distinct run
IDs and byte-identical CSV, and verifies unchanged async evidence. Source-only
configurations are written beside run artifacts, leaving corpus checkouts clean.

## Verification

- 91 JS/TS tests pass, including 33 new function-policy cases: anchors, clamp and
  interpolation, exact half-up ties, singleton/empty populations, tied worst
  functions, disjoint MI groups, monotonicity, nested ownership, enum/type/signature
  neutrality, multiline literals, callbacks on one line and schema-valid evidence.
- The existing raw metric/React snapshot passes unchanged by this function-policy
  work; the new measurements have separate regression coverage.
- All six pinned calibration fixtures pass after reviewing the policy change.
  Only `decisions` changes scores: complexity 6 → 5.4 and MI 8 → 10. It has one
  CC13 function; owned-body MI is above the new 75 anchor. Finding counts,
  labeled accuracy and populations are unchanged. The refreshed baseline passes
  again without record mode.
- The isolated installed npm package passes analysis, function decision/component
  validation, comparison/gates, SARIF and invalid-input checks.
- All eight external complexity/MI scores were independently recomputed from the
  contribution populations using Python `Fraction` arithmetic and matched.
- Self-analysis emits complete, schema-valid evidence. Whitespace checks pass.

This establishes a reproducible first corpus observation, not cross-language
calibration. Follow-up should inspect owned-function measurement and representative
hotspots before considering any further threshold changes.

That follow-up is recorded in the [hotspot review and TypeScript measurement correction](javascript-typescript-hotspot-review-2026-10-02.md).
The figures above preserve the original function-policy checkpoint.
