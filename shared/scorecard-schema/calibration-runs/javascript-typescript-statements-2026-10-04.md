# Statement-based JS/TS decomposition calibration

Executable statements are the agreed primary decomposition size measure. Source lines remain diagnostic context. Evidence profile v2 uses `owned-executable-statements-v2` and ranks functions/modules by owned statements. This is a measurement decision; the numerical ladder and its weights remain an experiment, with no production decomposition score.

## Counting rules

- A concise expression body counts one implicit return, just like an explicit return statement.
- Nested implemented functions own their bodies separately; the enclosing function receives no duplicate body credit or penalty.
- Blocks, empty statements, label wrappers, type/import/export declarations, nested function/class declarations, and uninitialized variable statements add no executable statements.
- Initialized variable statements count once. Multiline data literals and formatting do not multiply that count.
- Control statements and their owned body statements count individually. This is a syntactic convention, not a machine-instruction or side-effect count.

The same convention supplies statement totals, nearest-rank median/p90, top functions, and module concentration. Existing line and literal measurements remain available for context. See the [v2 contract](../javascript-typescript-context-evidence.md#decomposition-v2-executable-statements-are-primary).

## Updated preview

The candidate uses 10/20/40/80/160 owned statements → 10/8/6/4/0, linearly interpolated. One worst function contributes 40%; the mean remaining functions contributes 60%. A single function receives its individual result, and an empty population is unscored. Final rounding is half-up to one decimal. These numerical parameters are provisional.

| Selection | Median statements | P90 statements | Candidate score | Largest function by statements |
|---|---:|---:|---:|---|
| Express | 4 | 15 | 8.2 | `send`: 47 |
| Zod v4 | 1 | 9 | 5.9 | `convertBaseSchema`: 220 |
| TanStack Query Core | 1 | 7 | 8.0 | `createResult`: 56 |
| TanStack React Query | 1 | 6 | 8.9 | `useBaseQuery`: 26 |
| Excalidraw app | 2 | 9 | 8.4 | `AIComponents/anonymous`: 39 |
| Uptime Kuma server | 4 | 19 | 5.8 | `start/beat`: 289 |

The rankings now answer the statement-size question directly. Query Core is led by `createResult` (56 statements), and Excalidraw by the AI component callback (39), rather than the functions with the most owned source lines. Express `send` drops from 50 syntactic statement nodes to 47 executable statements after excluding declaration-only statements.

Formatting, literals, and concise bodies no longer distort the primary count. Statement count still does not establish responsibility: registration and orchestration can require many statements. CC and MI remain separate existing scores; no automatic exemptions or extra deductions were inferred.

## Compatibility and verification

All six pinned selections were analyzed before and after with fresh run IDs. The runner verifies identical raw CSV, populations, filters, and configuration fingerprints; every dimension except the decomposition extension is deeply equal. Within Code Quality, scores, decisions, findings, scope, and method-complexity details are unchanged.

The production ruleset remains `javascript-typescript-2026-10-04-local-handlers`. Score-baseline comparisons remain compatible, with no new/resolved findings or warning/score-drop gate failures. Comparisons of decomposition counts/rankings must use matching measurement versions: the v1-to-v2 change is an analyzer measurement change, not source improvement.

184 unit/integration tests pass, including concise/explicit-return parity, nested ownership, declaration exclusion, label wrappers, data-versus-work ranking, and empty populations. Installed-package inspection checks profile v2 and the primary measure. Six shared calibration fixtures retain their existing baseline.

```powershell
. E:/tools/Use-CalibrationDotNet.ps1
$env:PYTHONUTF8="1"
npm test --prefix analyzers/javascript-typescript
npm run test:package --prefix analyzers/javascript-typescript
python shared/calibration/run.py --ecosystem javascript-typescript
node analyzers/javascript-typescript/scripts/function-policy-corpus.mjs TestResults/js-ts-statements/before E:/repos TestResults/js-ts-statements/corpus --statement-decomposition
node analyzers/javascript-typescript/scripts/decomposition-preview.mjs TestResults/js-ts-statements/corpus TestResults/js-ts-statements/preview.json
```

The before directory contains the saved local-handler-v2/decomposition-v1 bundle from immediately before this change. The [recorded JSON](javascript-typescript-statements-2026-10-04.json) contains its bundle hash, sample pins, evidence/config/CSV hashes, fresh run IDs, count-change totals, and preview results. The same source-only selection limits from the [application report](javascript-typescript-application-context-2026-10-04.md) apply. No public sample code was installed, built, or executed.
