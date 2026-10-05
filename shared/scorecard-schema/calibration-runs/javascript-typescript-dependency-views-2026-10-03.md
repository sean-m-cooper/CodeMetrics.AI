# JS/TS implementation-reference and public re-export views

This additive pass separates imports/module-loading calls from direct re-exports
in module dependency evidence. It assigns no Architecture score, penalty or
runtime-safety label. The unpublished package remains 0.3.0 with ruleset
`javascript-typescript-2026-10-02-module-graph`.

`dependencyGraph.dependencyViews` version 1 uses classification
`direct-reexport-syntax-v1`. See the [contract](../javascript-typescript-module-graph.md#implementation-reference-and-public-re-export-views)
for counting, overlap, scope and compatibility rules.

## Corpus results

All four pinned, source-only selections were rerun against a saved bundle from
immediately before this change. The original overall graph is preserved; the new
views expose different subsets of its references.

| Selection | Largest overall fan-out | Largest import/load fan-out | Largest direct re-export fan-out |
| --- | ---: | ---: | ---: |
| Express | 3 | 3 (`lib/express.js`) | 0 |
| Zod v4 | 63 | 7 (`classic/schemas.ts`, `core/schemas.ts`) | 63 (`locales/index.ts`) |
| TanStack Query Core | 19 | 7 (`queryObserver.ts`) | 19 (`index.ts`) |
| TanStack React Query | 18 | 5 (`useBaseQuery.ts`, `useQueries.ts`) | 18 (`index.ts`) |

These columns rank different reference sets; they are not score improvements or
claims of fewer dependencies in the source. Zod's locale export surface remains
visible, while its schema implementations now lead the import/load ranking.
Query Core's observer and client dependencies become easier to see below its
public API surface. React Query's hooks similarly emerge below its index file.

| Selection | Distinct overall edges | Import/load edges | Re-export edges | Pairs present in both |
| --- | ---: | ---: | ---: | ---: |
| Express | 7 | 7 | 0 | 0 |
| Zod v4 | 252 | 144 | 114 | 6 |
| TanStack Query Core | 73 | 54 | 19 | 0 |
| TanStack React Query | 42 | 24 | 18 | 0 |

Zod demonstrates why these counts cannot simply be added: six source/target
pairs are both imported and re-exported. Each pair counts once overall. Repeated
occurrences are also deduplicated within each view.

Fan-in remains contextual. Zod's `core/util.ts` has 78 importing modules, compared
with 79 in the overall graph. Query Core's `utils.ts` has 13 importers, compared
with 14 overall. These counts identify broad dependencies without establishing
poor cohesion or a design defect.

## Limits preserved

- This is a source-syntax partition. Import-then-export forwarding, unused
  imports, side-effect imports and CommonJS forwarding remain in the conservative
  import/load view. Express's zero direct re-exports does not mean it exposes no
  public API. Alias/value-flow classification remains outside this pass.
- Individual references are classified, not whole files. A barrel may still
  contain implementation imports or initialization side effects; its
  `reExportOnly` flag does not erase those edges.
- Explicit type-only references stay outside both value-capable views. Mixed
  imports/exports retain their value edge.
- The overall graph still reports Zod's two-module source cycle. Cycles crossing
  re-export/import boundaries remain visible; no separate view-based cycle
  penalties or exemptions are introduced.
- Resolution coverage is unchanged: Express 44.8%, Zod and Query Core 100%,
  React Query 60.2% of observed literal occurrences. Express also has a computed
  require. Missing external dependencies and selected-source boundaries still
  limit interpretation; these are not complete runtime dependency inventories.

## Verification

- All 138 unit/integration tests pass. Six new graph regressions cover export
  forms, overlapping targets, side-effect imports, cycles crossing the views,
  resolution/scope gaps and conservative forwarding behavior.
- The nine authored architecture cases now assert separate view counts while
  preserving their previous graph and runtime expectations. The offline installed
  package/workspace check passes as well.
- Installed-package checks verify both views and confirm inspection preserves
  them. Schema validation, comparison, gates and SARIF checks pass.
- All six shared calibration fixtures pass without a baseline change.
- Removing only `dependencyViews` makes every dimension's evidence identical to
  the saved pre-change bundle on all four public selections. Raw CSV, populations,
  filters and configuration fingerprints are identical within each paired run.
- All four comparisons remain compatible, have no new/resolved/changed findings
  or severity increases, and pass warning and zero-score-drop gates. All numerical
  scores and existing cycle identities are unchanged.

The [verification artifact](javascript-typescript-dependency-views-2026-10-03.json)
records source revisions, bundle hashes, run IDs, evidence hashes, coverage,
rankings, counts and invariant checks. Full evidence, the saved baseline bundle
and logs are under `E:/repos/CodeMetrics.AI/TestResults/js-ts-dependency-roles/`.

Reproduce the paired comparison after building the analyzer and preserving the
saved pre-change bundle:

```powershell
. E:/tools/Use-CalibrationDotNet.ps1
node analyzers/javascript-typescript/scripts/function-policy-corpus.mjs `
  TestResults/js-ts-dependency-roles/before E:/repos `
  TestResults/js-ts-dependency-roles/corpus
node analyzers/javascript-typescript/scripts/dependency-view-audit.mjs `
  TestResults/js-ts-dependency-roles/corpus `
  TestResults/js-ts-dependency-roles/verified.json
```

The audit requires exact pre-change dimension evidence after removing the single
additive field. It does not discard changed findings, relax compatibility or
accept a score difference. External checkouts remain clean, and no public-corpus
code was executed.
