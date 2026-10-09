# JS/TS handler disposition calibration, 2026-10-06

The agreed population formula is implemented with bounded contextual
classification. It can score fully assessed populations. **All six public-source
selections remain unavailable for Error Handling:** five contain unresolved
dispositions, and React Query has no observed handlers. This is coverage evidence,
not a reason to invent scores or mark unknown handlers as successfully handled.

The [policy](../javascript-typescript-error-handling-policy.md) records the product
decision, classifications, formula, comment attachment, and recognition limits.
The [machine-readable report](javascript-typescript-handler-disposition-2026-10-06.json)
records source pins, development source hash, fresh run/audit IDs, configuration
fingerprints, evidence hashes, and every previously reviewed case.

## Observed population

| Selected source | Distinct bodies | Assessed | Unknown | Unresolved callback uses | Error Handling score |
| --- | ---: | ---: | ---: | ---: | --- |
| Express | 2 | 0 | 2 | 0 | Unavailable |
| Zod v4 | 22 | 8 | 14 | 0 | Unavailable |
| TanStack Query Core | 16 | 2 | 14 | 0 | Unavailable |
| TanStack React Query | 0 | 0 | 0 | 0 | Unavailable: no population |
| Excalidraw app | 45 | 32 | 13 | 1 | Unavailable |
| Uptime Kuma server | 341 | 45 | 296 | 0 | Unavailable |
| Total | 426 | 87 | 339 | 1 | Not aggregated |

The one unresolved callback is a use site, not an additional distinct body. It
cannot be added to or removed from the body denominator by assumption.

The first classifier recognizes 20.4% of resolved bodies in these selections.
Custom reporting APIs, nonliteral return contracts, callbacks, conditional paths,
and broader continuation logic account for remaining unknowns. Recognition of
standard console reporting explains much of Excalidraw's higher assessed share.
These percentages describe classifier coverage, not library quality.

## Previously reviewed examples

All 18 labels were checked against the pinned revisions, handler identities, and
full-source/supporting-range hashes. Manual labels were not imported as exemptions.

- All 11 known fallback/declared-intent counterexamples avoid deductions.
  TanStack's overloaded `noop` declaration is recognized as documented intent;
  its 12 uses still count as one body. The other ten remain unknown, including
  Express's undefined-result caller contract and Zod's post-catch validation.
  Avoiding a deduction by abstaining is not proof that those flows are recognized.
- Of seven cases previously marked `requiresReview`, five remain unknown. The
  Excalidraw clipboard catch and Uptime Kuma domain-expiry catch match terminal
  unexplained swallowing. Their surrounding populations remain unavailable, so
  both observations are informational and excluded from scoring. Neither is
  labeled a confirmed defect or an incorrect business decision.
- Assigned, returned, awaited, and chained Promise results require contextual
  review. A shared callback with mixed use contexts remains unknown, avoiding an
  exemption or deduction borrowed from just one use.

## Positive and negative controls

Repository-authored fixtures verify propagated failures, explicit literal
results, standard console reporting, declared intent, terminal silent discards,
and a guarded parse fallback. Negative controls cover arbitrary nonempty code,
unused nested functions, incomplete branches, local console lookalikes, finally
overrides, unrelated comments, comment-like regular expressions, initialized or
hoisted state, a guard on the wrong variable, shadowed `undefined`, mutation in
the assignment expression, and Promise continuation contracts.

Formula examples are 2/2 = 0, 2/100 = 9 after the cap, 20/100 = 8, 50/100 = 5,
5/16 = 6.9, and 0/100 = 10. Absent, unknown, unresolved, and incomplete populations
cannot receive a score. Findings carry the executed policy's input/exclusion
effects. Historical rulesets fail comparison compatibility checks.

## Reproduction and invariants

Validation passed: 205 tests with one worker, followed by targeted attachment
checks after the final comment-boundary adjustment. The installed-package smoke
test passed analysis, schema inspection, stable-baseline gates, a new-warning and
score-drop rejection, SARIF export, and invalid-input handling. An earlier
two-worker run hit test/RPC timeouts; assertions and timeout limits were not
relaxed for the successful single-worker run.

From `analyzers/javascript-typescript`, after building, run:

```powershell
. E:/tools/Use-CalibrationDotNet.ps1
npm run build
node scripts/handler-disposition-corpus.mjs E:/repos E:/repos/CodeMetrics.AI/TestResults/js-ts-context-calibration/corpus E:/repos/CodeMetrics.AI/TestResults/js-ts-handler-disposition
npx vitest run --maxWorkers=1
npm run test:package
```

The input is the saved released 0.4.0 corpus produced by
`released-context-corpus.mjs`; that script and the pinned selection manifest
reproduce its configuration. The new runner checks clean tracked sample source
and exact revisions, reuses the same source selection, and does not install,
build, or execute any sample application. It asserts byte-identical raw CSV and
deep equality of every dimension other than Error Handling against that baseline.

The development version is labeled `0.4.0-dev` in these source runs; no package
was released. Ruleset `javascript-typescript-2026-10-06-handler-disposition` requires
fresh baselines. Architecture and decomposition remain unscored. The next useful
extension is classification coverage for concrete custom reporting and
continuation contracts, validated against these same counterexamples.
