# JS/TS context calibration and released workflow

This pass completes the decomposition experiment, evaluates contextual
error-handling scoring, and exercises the released skill on real applications.
**Neither candidate warrants production deductions.** The
[product decision](../javascript-typescript-context-policy-decision.md) records why
and the evidence required to reopen scoring. No analyzer score or ruleset changes.

## Released evidence

All six pinned selections were run through the merged `code-scorecard` skill and
registry-installed `codemetrics-ai@0.4.0`, with fresh invocation run/audit IDs and
validated inspections. The corpus covers 410 production files and 4,062 functions.
The recorded JSON includes source pins, skill hash/revision, configuration and
evidence hashes, artifact paths, IDs, and candidate details. Checkouts remain under
`E:/repos`; no sample dependencies were installed and no sample application was
built or executed. Runtime performance, complete security/testing coverage, and
whole-product health are outside this run.

| Selection | Functions | Method Complexity | MI | Async/Blocking | Statement preview, unscored | >40 statements | Also CC>10 | Also MI<65 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Express | 109 | 7.0 | 5.9 | N/A | 8.2 | 1 | 1 | 1 |
| Zod v4 | 1,948 | 5.7 | 6.6 | 10 | 5.9 | 19 | 19 | 19 |
| Query Core | 427 | 6.7 | 7.2 | 10 | 8.0 | 2 | 2 | 2 |
| React Query | 76 | 8.1 | 7.2 | 10 | 8.9 | 0 | 0 | 0 |
| Excalidraw app | 360 | 7.3 | 5.6 | 10 | 8.4 | 0 | 0 | 0 |
| Uptime Kuma server | 1,142 | 5.5 | 4.7 | 10 | 5.8 | 23 | 20 | 23 |

Every large function in this threshold experiment is already in the low-MI
comparison group; 42/45 are also above the existing CC warning threshold. Counts
overlap and must not be added. Statement/MI-risk correlations range from 0.790 to
0.953. This is evidence against adding a third overlapping aggregate penalty in
this corpus, not a universal claim that function size is useless.

The three large, low-CC cases are Uptime Kuma's connection callback
(`server/server.js:373`, 56 statements, CC 5), server constructor
(`server/uptime-kuma-server.js:73`, 44, CC 5), and startup callback
(`server/server.js:208`, 43, CC 4). Current source shows session setup, handler and
monitor-type registration, and application initialization. These remain review
candidates; statement count cannot establish an unnecessary responsibility.
Their MI is already low (approximately 32.7, 35.4, and 36.5).

## Error-handling candidate

The offline experiment scores distinct bodies, honors documented intent, retains
reference uses, and refuses a number for zero bodies or unresolved callback uses.
Its proposed formula is `10 - 10*unexplained/total`, capped at 9 when any
unexplained body exists. This formula is **rejected for production with the current
body classifier**. Values below are sensitivity results, not scorecard dimensions.

| Selection | Distinct bodies | Unexplained | Documented empty | Unknown callback uses | Rejected candidate |
|---|---:|---:|---:|---:|---:|
| Express | 2 | 1 | 0 | 0 | 5.0 |
| Zod v4 | 22 | 7 | 0 | 0 | 6.8 |
| Query Core | 16 | 1 | 0 | 0 | 9.0 |
| React Query | 0 | 0 | 0 | 0 | N/A |
| Excalidraw app | 45 | 1 | 2 | 1 | N/A |
| Uptime Kuma server | 341 | 8 | 3 | 0 | 9.0 |

All 18 unexplained bodies were inspected, with source hashes and supporting ranges
in the [review manifest](../../calibration/corpus/javascript-typescript-context-review.json).
Nine have fallback/error reporting outside the body; two have intent documented
outside the body; seven remain unresolved questions. None is a context-verified
defect in this review. Unknowns must not be relabeled as true positives, and these
labels do not rewrite deterministic findings or prove runtime correctness.

Important counterexamples:

- Express `tryStat` returns `undefined` and its caller tests the result before
  checking the file or trying another path. Empty-body syntax is not a missing
  failure contract.
- Zod's failed coercions proceed to validation issues; another synchronous attempt
  falls back to async validation. Its recursion probe explicitly explains a
  conservative fallback before the try and throws afterward.
- Query Core documents its shared no-op at the declaration/overload group, outside
  the implementation body. Twelve uses still count as one body.
- Uptime Kuma's `getPushExample` catch is followed by a callback reporting
  `ok: false`. Optional maintenance dates have an explanation before the try.

Synthetic regression tests demonstrate that the rejected formula would assign
zero to a function whose empty catch leads to an explicit failed result. Other
tests check two-of-two versus two-of-100, half-up rounding, declared bodies, shared
uses, absent populations, unresolved callbacks, partial evidence, duplicate rows,
and complete source-review coverage. The experiment never mutates evidence.

## Configuration and application verification

The historical Uptime Kuma selection omitted its repository tsconfig. The fresh
scope extends that configuration, retaining ES2020 libraries; Express uses an
explicit ES2022 analysis target because it has no repository tsconfig. The other
four selections retain their repository configurations. These selected-source
programs do not necessarily reproduce their complete build environments.

Uptime Kuma's eligible async population grows from 505 to 524, still with zero
scored owners. Literal graph resolution changes from 668/975 to 669/975. Its
handler population remains 341. All six selections preserve byte-identical raw
CSV, function/decomposition/handler evidence, populations, filters, and scores
against the prior statement corpus. Version/configuration differences are not
source improvements and cannot be treated as compatible historical baselines.

The [full Uptime Kuma scorecard](uptime-kuma-released-scorecard-2026-10-05.md)
records a second released-helper run with a compatible fresh baseline. Both run
and audit IDs change; all score deltas are zero, no findings are new/resolved/
changed, and warning/score-drop gates pass. The report preserves six unavailable
dimensions, 0/524 scoped async usage, 341 unique handler bodies, and unresolved
graph coverage. Source inspection supports concrete review steps without claiming
that every hotspot is defective or safe to split.

## Verification and reproduction

All 190 tests pass. The six new checks exercise the offline candidate, real
fallback behavior, fail-closed populations, deduplication, immutability, and source
review provenance. The released package itself was exercised in six real-source
helper runs and one compatible application rerun.

```powershell
. E:/tools/Use-CalibrationDotNet.ps1
npm run build --prefix analyzers/javascript-typescript
node analyzers/javascript-typescript/scripts/released-context-corpus.mjs E:/repos/ai_tools-js-0.4-release/skills/code-scorecard E:/repos TestResults/js-ts-context-calibration/corpus
node analyzers/javascript-typescript/scripts/context-policy-preview.mjs TestResults/js-ts-context-calibration/corpus TestResults/js-ts-context-calibration/preview.json E:/repos
npm test --prefix analyzers/javascript-typescript
```

The runner requires the explicit 0.4.0 compatibility pin and the
[pinned corpus](../../calibration/corpus/javascript-typescript-context.json).
Original review IDs remain historical provenance; reuse on a fresh run requires
the same revision, handler identity/location, and matching source hashes. The
verification rejects missing/duplicate reviews or changed source. Do not change
review labels merely to fit candidate scores.

See the [recorded results](javascript-typescript-context-calibration-2026-10-05.json).
Local logs and full evidence are under `TestResults/js-ts-context-calibration/`.
