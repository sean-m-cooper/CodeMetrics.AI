# JS/TS hotspot review and type-erasure correction

This follow-up inspects the highest-complexity function in each selection from
the [initial function-policy corpus](javascript-typescript-function-policy-2026-10-02.md).
The corpus source and scoring anchors/weights are unchanged. The review found
one measurement defect and one related traversal defect, now covered by tests.

## Direct source audit

Counts below are independently enumerated from each function's syntax, excluding
nested function/type bodies. Add the baseline of one to each row's decisions.
They reproduce the analyzer's own CC exactly. These are counts of syntax under
the documented policy, not counts of bugs or a cognitive-complexity assessment.

| Function | CC | If | Case | Loop | Catch | Ternary | Logical operators | Owned lines |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Express `send` | 23 | 14 | 4 | 0 | 0 | 0 | 4 | 57 |
| Zod `convertBaseSchema` | 122 | 76 | 7 | 3 | 0 | 14 | 21 | 255 |
| Query Core `createResult` | 29 | 10 | 0 | 0 | 1 | 2 | 15 | 116 |
| React Query `HydrationBoundary` useMemo callback | 13 | 6 | 0 | 1 | 0 | 0 | 5 | 27 |

Source locations at the recorded pins:

- Express `lib/response.js:126–222`, commit `7ef98448f8b38099ab1ded55e458538ad47a51e7`.
- Zod `packages/zod/src/v4/classic/from-json-schema.ts:390–806`, commit `0b216ef674e297ebe41d8bf902262e56f8755822`.
- TanStack `packages/query-core/src/queryObserver.ts:558–726` and
  `packages/react-query/src/HydrationBoundary.tsx:115–162`, commit
  `29859ae60c8dca0a5cdbf8abccc775b655cf43e2`.

Zod's eight directly nested functions contribute six additional decision
increments in their own bodies; those increments are correctly absent from
the parent's CC122. The callback nested inside one of those functions is also
separate. The other three audited functions contain no nested function bodies.

### Interpretation from source review

**Zod:** The function handles unsupported keywords, references, enum/const schemas,
type unions, and string/number/object/array conversion. It contains a large flat
format-dispatch chain alongside more involved object and tuple handling. This is
a substantial amount of logic in one function, but CC122 does not mean 122 nested
conditions. Type-specific conversion helpers are a reasonable review candidate;
the metric cannot establish that extraction would improve the implementation.

**Express:** `send` combines payload normalization, content type/encoding, content
length, ETag generation, freshness, no-body status codes and HEAD handling. Several
branches implement explicit protocol behavior or documented optimization choices.
The count is reproducible and the function merits careful reading. It does not
justify deleting those branches or imposing a responsibility split without checking
header ordering and shared state. Three consecutive case labels also share a body;
this policy counts labels, not distinct branch implementations.

**Query Core:** `createResult` combines optimistic state, placeholder data,
memoized selection, select-error state and result flags. Its 15 logical operators
include both control conditions and boolean field computations. The latter still
count under the accepted CC policy; they are not evidence of deep nesting.

**React Query:** The hydration callback distinguishes new and existing queries,
checks freshness, and delays updates that would otherwise affect the current UI
during transitions. The surrounding comments explain those ordering requirements.
CC13 remains a review signal; it does not establish an incorrect design or justify
removing the safeguards. The callback is measured independently of its component.

The review supports keeping the accepted CC policy. It also supports showing
decision composition as context rather than inferring a defect from the headline.

## Measurement defects corrected

The owned-body MI walker excluded TypeScript type nodes but still counted some
erased syntax around them: annotation colons, definite-assignment assertions,
`as`, `satisfies`, angle assertions, non-null assertions and generic argument
brackets/commas. A one-line function with `const result = value; return result;`
had Halstead volume 30 and MI89.5227. Adding a local `: string` annotation alone
raised volume to 34.8692 and lowered MI to 89.0653. Runtime behavior was identical.

Measurement `owned-function-body-v2-type-erasure` removes that syntax and retains
the executable operand. Negation, runtime comparisons, object-property colons and
operand decisions remain measured. Type-only continuation lines no longer add
owned lines.

The review also identified TypeScript's classification of standalone instantiation
expressions (`factory<T>`) as TypeNodes. Skipping the whole node could lose decisions
in its operand or fail to discover a nested function. Both measurement and function
discovery now retain the executable expression. A regression case verifies that
an inline generic function inside an instantiation is counted once, separately
from its enclosing function; a type-query-only expression adds no function.

Ruleset `javascript-typescript-2026-10-02-type-erasure` distinguishes the corrected
measurement. The aggregate policy IDs, anchors and weights are unchanged. Schema
v3 and raw metric formulas are unchanged; a previously undiscovered function can
now add a legitimate raw member row. Every audited corpus CSV remains byte-identical.
The package remains unpublished version 0.3.0 in local development.

## Effect on the same pinned corpus

| Scope | Functions with corrected own MI | Unrounded aggregate MI score before → after | Rounded MI | Method complexity |
| --- | ---: | ---: | ---: | ---: |
| Express | 0 | 5.926482 → 5.926482 | 5.9 | 7.0 |
| Zod v4 | 614 | 6.620562 → 6.632681 | 6.6 | 5.7 |
| Query Core | 66 | 7.226348 → 7.243145 | 7.2 | 6.7 |
| React Query | 16 | 7.218617 → 7.232477 | 7.2 | 8.1 |

All eight rounded scores remain unchanged. In total, 696 functions receive
corrected MI measurements. Zod's audited hotspot changes from MI0.957842 to
MI0.993821, Query Core's from MI25.739662 to MI25.810855, and React Query's from
MI46.006438 to MI46.040606. The correction removes a language-syntax bias but does
not explain away the large hotspots. The full corpus populations, discovery
filters, raw CSV and async evidence are unchanged.

## Verification and reproduction

- 108 tests pass, including 17 new type-erasure/traversal cases. Two integration
  tests exceeded their existing five-second timeout while calibration ran
  concurrently; the full suite passed when rerun without that competing workload.
  No timeout or expectation was relaxed.
- All six pinned calibration fixtures pass again without record mode. The
  reviewed baseline diff changes only the ruleset; scores, findings, populations
  and labeled accuracy are unchanged.
- Installed-package smoke checks cover typed/untyped MI equivalence, measurement
  metadata, evidence validation, comparisons, gates, SARIF and invalid input.
- The corpus runner validates both evidence sets, pins, clean source checkouts,
  matching populations/configuration, fresh run IDs, identical raw CSV and
  unchanged async evidence. The hotspot audit separately checks each CC sum.

The [audit artifact](javascript-typescript-hotspot-review-2026-10-02.json) includes
pins, bundle hashes, scores and decision source lines. Full evidence and the saved
pre-correction bundle are local under
`E:/repos/CodeMetrics.AI/TestResults/js-ts-hotspot-review/`. No corpus source was
modified and no corpus code or lifecycle script was executed.

After building, with the saved before bundle and the existing pinned checkouts:

```powershell
. E:/tools/Use-CalibrationDotNet.ps1
node analyzers/javascript-typescript/scripts/function-policy-corpus.mjs `
  TestResults/js-ts-hotspot-review/before E:/repos `
  TestResults/js-ts-hotspot-review/corpus
node analyzers/javascript-typescript/scripts/hotspot-audit.mjs `
  E:/repos TestResults/js-ts-hotspot-review/corpus `
  TestResults/js-ts-hotspot-review/hotspots.json
```

All prior source-only scope limits still apply. This review does not establish
cross-ecosystem calibration or comprehensive JS/TS analysis coverage.
