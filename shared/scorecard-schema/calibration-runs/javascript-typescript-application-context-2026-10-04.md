# JS/TS applications, referenced handlers, and decomposition preview

The development ruleset is `javascript-typescript-2026-10-04-local-handlers`; the unpublished package remains 0.3.0. This pass extends unscored handler inspection, adds two pinned application selections, and evaluates two decomposition hypotheses outside production scoring.

## Corpus expansion

[Excalidraw](https://github.com/excalidraw/excalidraw) adds a React application with collaboration and browser-state orchestration. [Uptime Kuma](https://github.com/louislam/uptime-kuma) adds a Node monitoring service with request/socket handlers and external integrations. Checkouts are under `E:/repos`; exact revisions and selected globs are recorded in the accompanying JSON. No public-project dependency installation, build, or application execution was performed.

| Selection | Production files | Functions | Complexity | MI | Async/Blocking |
|---|---:|---:|---:|---:|---:|
| Express | 7 | 109 | 7 | 5.9 | Unscored |
| Zod v4 | 107 | 1948 | 5.7 | 6.6 | 10 |
| TanStack Query Core | 23 | 427 | 6.7 | 7.2 | 10 |
| TanStack React Query | 23 | 76 | 8.1 | 7.2 | 10 |
| Excalidraw app | 37 | 360 | 7.3 | 5.6 | 10 |
| Uptime Kuma server | 213 | 1142 | 5.5 | 4.7 | 10 |

The application selections add 1,502 functions, bringing the corpus to 4,062. Excalidraw selects its app package, not its editor or shared packages; Uptime Kuma selects the server tree, not its Vue client. These are selected-source assessments, not complete product scores.

All scored dimensions, decomposition profiles, architecture, and raw CSV are identical before/after the handler extension. Architecture, decomposition, and error handling remain unscored. No overall score or cross-ecosystem comparison is produced.

Neither new sample contains a scored async hazard under the current bounded rules: Excalidraw has 0/82 affected owners and Uptime Kuma 0/505. The corpus therefore still does not empirically exercise nonzero async bands. Boundary tests exercise the formula, not its real-world sensitivity.

The source-only mode also has resolution limits. Excalidraw resolves 258/306 literal dependency occurrences (84.3%), with 169 outside the selected scope and 48 unresolved. Uptime Kuma resolves 668/975 (68.5%), with 117 outside scope and 307 unresolved. A complete parse does not establish complete semantic or runtime coverage.

## Referenced handler inspection

V2 follows selected-source function declarations, const aliases, and imports. Each body is counted once; uses are retained separately. Mutable, reassigned, unsupported, and out-of-scope references remain reasoned gaps. See the [v2 contract](../javascript-typescript-context-evidence.md#error-handling-v2-local-referenced-implementations).

| Selection | Unique bodies | Unexplained empty | Documented empty | Reference uses | Remaining gaps |
|---|---:|---:|---:|---:|---:|
| Express | 2 | 1 | 0 | 0 | 0 |
| Zod v4 | 22 | 7 | 0 | 0 | 0 |
| TanStack Query Core | 16 | 1 | 0 | 12 | 0 |
| TanStack React Query | 0 | 0 | 0 | 0 | 0 |
| Excalidraw app | 45 | 1 | 2 | 0 | 1 |
| Uptime Kuma server | 341 | 8 | 3 | 0 | 0 |

Query Core resolves all twelve former gaps to one shared `noop` implementation in `src/utils.ts`. Its inspected population grows from 15 to 16 bodies, with 27 total use sites. The empty implementation produces one informational review lead, not twelve findings or deductions. Its name is useful review context, not a new name-based exemption.

Excalidraw provides two real documented-empty bodies, and Uptime Kuma three. Their comments count as developer intent without a semantic assessment of the decision. Excalidraw retains one gap at `App.tsx:293`: `.catch(reject)` forwards to a Promise executor parameter. Parameter value flow remains outside the resolver; it is neither called empty nor credited as verified handling.

## Experimental decomposition ladders

Two deliberately provisional candidates test sensitivity. Owned-line anchors are 20/40/80/160/320 → 10/8/6/4/0. Owned-statement anchors are 10/20/40/80/160 → 10/8/6/4/0. Values interpolate linearly; one worst function receives 40% and the mean of the remaining functions 60%, with one half-up rounding to one decimal. These are proposed experimental parameters, not an accepted product standard.

| Selection | Line candidate | Statement candidate | Size/MI-risk rank correlation |
|---|---:|---:|---:|
| Express | 8.8 | 8.1 | 0.989 |
| Zod v4 | 6.5 | 5.9 | 0.964 |
| TanStack Query Core | 8 | 8 | 0.977 |
| TanStack React Query | 8.8 | 8.9 | 0.951 |
| Excalidraw app | 5.9 | 8.4 | 0.988 |
| Uptime Kuma server | 5.8 | 5.8 | 0.993 |

The correlation is per-function Spearman correlation between owned size and `100 - owned MI`, with averaged tied ranks. Values of 0.951–0.993 show substantial overlap in these selections. This is unsurprising because MI includes size; it is not independent validation that size predicts defects.

Representative source review provides context beyond the score previews:

| Example | Owned lines | Statements | CC | Interpretation |
|---|---:|---:|---:|---|
| Excalidraw `ExcalidrawWrapper` | 319 | 37 | 16 | Application state, configuration, and JSX composition; line and statement policies diverge substantially. |
| Uptime Kuma `Notification.init` | 119 | 9 | 4 | Provider registration list with 109 literal entries; much of the size is declarative registration. |
| Zod Lithuanian locale `error` | 118 | 4 | 1 | Structured translation data with 91 literal entries. |
| Uptime Kuma socket connection callback | 87 | 57 | 5 | Session setup and handler registration; genuine orchestration with many statements but limited branching. |
| Uptime Kuma `start/beat` | 442 | 290 | 126 | Branch-heavy monitoring orchestration already visible in complexity and MI. |
| Zod `convertBaseSchema` | 255 | 222 | 122 | Conversion branching already visible in complexity and MI. |

These interpretations are review notes tied to the pinned source, not automated exemptions. Literal entries may execute code; a provider-registration array is not proof of inert data. Statement count also omits work in concise expressions and JSX, so it is not a complete responsibility measure.

**Recommendation:** keep decomposition unscored for now. The statement candidate is less sensitive to multiline data/markup, but still penalizes orchestration and overlaps with CC/MI. Do not add an independent line-size penalty. The next policy experiment should establish what structural signal adds information beyond existing scores, then test specific data, registration, UI composition, and orchestration examples before adopting weights.

## Verification and reproduction

- 182 unit/integration tests pass. New cases verify shared-handler deduplication, selected-module imports, unresolved mutation/alias/property cases, excluded source, stable identities, and preview arithmetic/correlation.
- The initial concurrent test/corpus run hit two five-second integration timeouts; a standalone rerun passed without changing timeouts or assertions.
- Installed-package inspection preserves v2 counts and reference sites. Six calibration fixtures retain their scores and finding counts; only the ruleset changes.
- The paired runner validates schema, fresh run IDs, equal populations/configuration/filters, byte-identical raw CSV, and deep equality of every dimension except error handling.
- Ruleset/scope changes reject old baselines; exploratory comparisons cannot enable gates.

```powershell
. E:/tools/Use-CalibrationDotNet.ps1
$env:PYTHONUTF8="1"
npm test --prefix analyzers/javascript-typescript
npm run test:package --prefix analyzers/javascript-typescript
python shared/calibration/run.py --ecosystem javascript-typescript
node analyzers/javascript-typescript/scripts/function-policy-corpus.mjs TestResults/js-ts-application-context/before E:/repos TestResults/js-ts-application-context/corpus --application-context
node analyzers/javascript-typescript/scripts/decomposition-preview.mjs TestResults/js-ts-application-context/corpus TestResults/js-ts-application-context/decomposition-preview.json
```

The before bundle is saved from `4a68403` (or can be rebuilt from that revision). The application-context mode requires the six clean, pinned checkouts; it never fetches, installs, or executes their code. The preview script reads evidence and writes a separate experimental report. It is excluded from the published npm package and cannot alter the analyzer scorecard.

The [recorded JSON](javascript-typescript-application-context-2026-10-04.json) preserves pins, bundle/config/CSV/evidence hashes, run IDs, populations, graph gaps, handler observations, and preview calculations. Full local artifacts are under `TestResults/js-ts-application-context/`.
