# JS/TS owner population and contextual evidence

This pass replaces the provisional async 6/10 cliff and adds unscored decomposition and handler evidence. The development package remains 0.3.0; ruleset `javascript-typescript-2026-10-03-owner-population` requires a fresh baseline.

The comparison uses the saved compiled bundle from architecture checkpoint `250864d`. Pinned, clean checkouts remain under `E:/repos`. These are selected source scopes, not whole-repository scorecards; no public-project build, dependency installation, or code execution was performed.

## Async owner population

| Selection | Before | After | Affected / eligible owners |
|---|---:|---:|---:|
| Express | Unscored | Unscored | 0 / 0 |
| Zod v4 | 10 | 10 | 0 / 46 |
| TanStack Query Core | 10 | 10 | 0 / 38 |
| TanStack React Query | 10 | 10 | 0 / 12 |

Public scores remain unchanged because no scored async hazards were observed in these selections. This corpus does not exercise the nonzero rate bands. Fourteen explicit boundary cases check those bands; the shared async-effect and conditional-hook fixtures intentionally move from 6 to 0 because their affected-owner rates exceed 20%. The intentional-effect fixture remains 10, and labeled finding accuracy is unchanged.

The [policy](../javascript-typescript-async-policy.md) records eligibility, single-owner counting, module buckets, and callback ownership. Small populations can score 0 for a single affected owner. A rate below 1% can score 10 despite findings. No runtime-performance or cross-ecosystem calibration claim follows.

## Decomposition profile

| Selection | Functions | Median owned lines | P90 owned lines | Largest function |
|---|---:|---:|---:|---|
| Express | 109 | 5 | 19 | `send`: 57 lines |
| Zod v4 | 1948 | 2 | 18 | `convertBaseSchema`: 255 lines |
| TanStack Query Core | 427 | 3 | 11 | `execute`: 120 lines |
| TanStack React Query | 76 | 2 | 13 | `useBaseQuery`: 58 lines |

These are owned-body sizes with nested functions measured separately. Module distribution, initializer/literal context, and declaration/export counts are retained in evidence. A large converter or orchestration method is a review candidate, not a proven decomposition defect. Code Quality and MI scores are unchanged.

## Handler observations

| Selection | Inspected handlers | Unexplained empty | Documented empty | Contains code | Uninspected rejection callbacks |
|---|---:|---:|---:|---:|---:|
| Express | 2 | 1 | 0 | 1 | 0 |
| Zod v4 | 22 | 7 | 0 | 15 | 0 |
| TanStack Query Core | 15 | 0 | 0 | 15 | 12 |
| TanStack React Query | 0 | 0 | 0 | 0 | 0 |

A source review of all eight unexplained-empty observations shows why they remain informational:

- Express `lib/view.js:202` returns `undefined` when the stat operation throws, consistent with a lookup fallback.
- Zod `core/compile.ts:242` preserves a conservative recursion flag before reporting unsupported compilation.
- Zod `core/schemas.ts:353` falls through to asynchronous validation; a following comment explains that path outside the handler body.
- The remaining five Zod observations surround coercion attempts; subsequent validation handles the resulting value.

These are contextual interpretations of the pinned source, not new automatic exemptions or defect findings. Body comments document intent without semantic approval. No documented-empty examples occur in this public subset; positive/negative fixtures verify that recognition. Query Core has twelve uninspected rejection arguments, so its inspected ratio is not comprehensive. React Query has no inspected handlers and receives no fabricated perfect score.

## Verification and reproduction

- 175 unit/integration tests pass, including 26 new tests for owner boundaries, size context, handler classification, and incomplete analysis.
- Six shared calibration fixtures pass after accepting only the ruleset and two intentional async score changes.
- Installed-package analysis, inspection, baseline gates, and SARIF pass with new evidence assertions.
- Eleven architecture context cases and offline installed-package precedence pass.
- All four paired corpus runs preserve raw CSV, population, filters, configuration fingerprints, complete architecture and MI dimensions, complexity inputs/findings/scores, and async finding identities.
- Old-to-new baseline comparison is rejected; exploratory comparison cannot enable gates. Fresh run identities are verified.

```powershell
. E:/tools/Use-CalibrationDotNet.ps1
npm test --prefix analyzers/javascript-typescript
npm run test:package --prefix analyzers/javascript-typescript
npm run test:architecture --prefix analyzers/javascript-typescript
python shared/calibration/run.py --ecosystem javascript-typescript
node analyzers/javascript-typescript/scripts/function-policy-corpus.mjs TestResults/js-ts-next-dimensions/before E:/repos TestResults/js-ts-next-dimensions/corpus --owner-population-transition
```

The paired command requires the saved checkpoint bundle (or a rebuilt package from `250864d`). Full local artifacts are in `TestResults/js-ts-next-dimensions/`. The [recorded report](javascript-typescript-owner-context-2026-10-03.json) retains checkout pins, bundle/config/CSV hashes, run IDs, counts, and ranked evidence. See the [context evidence contract](../javascript-typescript-context-evidence.md) for limitations and consumer behavior.
