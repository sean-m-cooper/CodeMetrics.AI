# JS/TS module graph: first corpus pass

The development ruleset `javascript-typescript-2026-10-02-module-graph` adds
module dependency evidence without an Architecture score. The package remains
unpublished version 0.3.0. Existing complexity, MI and async policies are unchanged.
The [graph contract](../javascript-typescript-module-graph.md) describes supported
syntax, resolution, coverage and interpretation limits.

## Observed graph

These are the same four pinned, source-only selections used in the previous
[complexity explanation pass](javascript-typescript-complexity-detail-2026-10-02.md).
They are not whole-repository scorecards. No corpus dependencies were installed,
and no corpus code or lifecycle scripts were executed.

| Selection | Modules | Resolved literal occurrences | Explicit type-only occurrences | Observed cyclic groups | Coverage gaps |
| --- | ---: | ---: | ---: | ---: | --- |
| Express | 7 | 26/58 (44.8%) | 0 | 0 | 32 unresolved package references; 1 computed require |
| Zod v4 | 107 | 447/447 (100%) | 182 | 1 | None within supported observations |
| TanStack Query Core | 23 | 140/140 (100%) | 67 | 0 | None within supported observations |
| TanStack React Query | 23 | 68/113 (60.2%) | 46 | 0 | 35 query-core references; 10 react references unresolved |

All 160 selected modules were analyzed. Literal resolution includes explicit
type-only references, and the cycle graph excludes those references. Zero cycles
means none observed among resolved internal value-capable edges; it is not a
guarantee about runtime behavior or unavailable dependencies. Even 100% refers
only to the supported syntax and declared source scope.

Express's 32 unresolved occurrences name external packages unavailable to the
resolver in the source-only checkout. Its computed call is `require(mod)` at
`lib/view.js:81`, where the extension selects a view engine. This is a legitimate
plugin pattern and remains an explicit unknown target, with no guessed edge or
penalty. React Query's selection does not include its sibling Query Core package,
and neither its package name nor React resolves in this no-install run.

## What deserves review

**Re-export files dominate fan-out.** Zod's locale index has 63 distinct targets,
its core index has 18, and its classic external exports have 13. All three are
recognized as re-export-only modules. Query Core and React Query also have
re-export-only index files at the top, with 19 and 18 targets respectively.
These results argue against treating a raw dependency count as a decomposition
defect. The graph preserves this context without inventing a score exemption.

**The Zod cycle is a real source dependency relationship.** `core/core.ts:4`
imports `members` from `util.ts`; `core/util.ts:2` imports `globalConfig` from
`core.ts`. Source inspection confirms uses at `core.ts:99` and `util.ts:519`.
Those imports establish the two-node group, but this pass does not establish
initialization order or a runtime failure. It produces one informational review
lead, not two penalties or an asserted defect.

**Fan-in is useful context, not a penalty.** Zod's core utility module has 79
distinct importing/re-exporting value-capable modules. Query Core's utility module
has 14. This identifies dependencies with a broad potential change impact; it
does not establish poor cohesion, unsafe changes or excessive responsibility.

The next calibration work should label intended barrels, ordinary service
dependencies, deferred cycles and demonstrated initialization hazards separately.
It should also exercise installed dependency/workspace resolution before making
claims about full package boundaries. This pass does not authorize a numeric
Architecture ladder.

## Invariance and validation

- 129 tests pass, including 15 graph cases: ESM/CommonJS, explicit types, shadowed
  Node names, aliases, workspace export conditions, missing build artifacts,
  ambiguous workspace names, graph gaps, parse failures, stable identities and
  iterative traversal of a 20,000-module chain.
- Installed-package checks pass for analysis, schema validation, inspection,
  baseline gates and SARIF. New informational cycles do not fail a warning gate.
- All six shared calibration fixtures pass. Their baseline diff changes only
  the ruleset; scores, findings and accuracy labels are unchanged.
- All four corpus pairs have identical raw CSV, populations, filters and
  configuration fingerprints, with distinct run IDs. All eight non-Architecture
  dimensions match after removing only the additive `complexityBreakdown`
  diagnostic field missing from the saved comparison bundle.
- Method complexity remains 7.0 / 5.7 / 6.7 / 8.1; MI remains
  5.9 / 6.6 / 7.2 / 7.2. Async remains skipped / 10 / 10 / 10.
- Architecture remains `skipped` with no score or scoring decision. A fresh
  comparison baseline is required because its scope and the ruleset changed.

The saved baseline bundle uses the previous type-erasure ruleset and predates
complexity explanation metadata. It is not an exact build of checkpoint commit
`677028c`. The [recorded artifact](javascript-typescript-module-graph-2026-10-02.json)
contains both bundle hashes, pinned revisions, run IDs, evidence hashes, coverage,
cycle edges, rankings and invariant-check results.

Full evidence, CSV, scope configurations and logs are under
`E:/repos/CodeMetrics.AI/TestResults/js-ts-module-graph/`. Reproduce the paired runs
after building the current analyzer:

```powershell
. E:/tools/Use-CalibrationDotNet.ps1
node analyzers/javascript-typescript/scripts/function-policy-corpus.mjs `
  TestResults/js-ts-complexity-detail/before E:/repos `
  TestResults/js-ts-module-graph/corpus
```

The saved baseline bundle is local; preserve it or construct an equivalent
pre-graph bundle before repeating the comparison. The runner verifies clean
checkouts and pinned revisions and installs nothing in them.
