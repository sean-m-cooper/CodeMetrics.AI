# Explicit ESM and CommonJS forwarding

The unpublished JS/TS analyzer now recognizes bounded import-then-export and
CommonJS forwarding in `dependencyViews` version 2, classification
`explicit-forwarding-v2`. Architecture remains unscored. The ruleset, full source
graph, measurements, existing findings and scores are unchanged.

## What changed

Version 1 classified only direct export-from declarations as public re-exports.
Version 2 also recognizes local ESM import/import-equals bindings exposed through
named/default exports, and direct require calls or simple require bindings exposed
through unshadowed CommonJS export assignments. Static property selection is
supported. Each dependency occurrence records its role array.

The classifier tracks local symbols instead of matching identifier text. A
binding forwarded and used locally appears in both views, while reassigned
bindings, wrappers, alias chains and unsupported patterns remain implementation
references. Explicit types stay outside both value views. See the
[contract](../javascript-typescript-module-graph.md#implementation-reference-and-public-re-export-views)
for supported syntax and limitations.

The TypeScript checker can return synthetic CommonJS export symbols for property
accesses even where a local parameter shadows `exports`. Lexical value/alias
scope checks protect these cases. The tests cover shadowed `exports` and `module`
names as well as local symbols that share an imported binding's spelling.

## Pinned corpus results

These are distinct internal source/target pairs, not occurrence totals or scores.
The source revisions and selected scopes are unchanged from the previous pass.

| Selection | Implementation edges v1 → v2 | Re-export edges v1 → v2 | Pairs shared in v2 | Overall edges |
| --- | ---: | ---: | ---: | ---: |
| Express | 7 → 6 | 0 → 4 | 3 | 7 |
| Zod v4 | 144 → 141 | 114 → 115 | 4 | 252 |
| Query Core | 54 → 54 | 19 → 19 | 0 | 73 |
| React Query | 24 → 24 | 18 → 18 | 0 | 42 |

Express's root `module.exports = require('./lib/express')` is now correctly
identified as forwarding. Its application, request and response dependencies
remain implementation references and also appear as public exports: they are
used to construct application behavior and exposed through the public API.
The three shared edges are counted once in the overall graph.

Express also forwards `body-parser`, `router` and `serve-static`. Their roles are
recorded, but these references remain unresolved in the no-install checkout and
do not become invented internal targets. Resolution coverage remains unchanged.

Zod's classic, v4 and mini entry modules forward imported namespaces. Three import
occurrences move to the public re-export view; two target pairs were already
present there through direct re-exports. This explains the three-edge decrease
in implementation and one-edge increase in public re-exports. Both TanStack
selections are unchanged.

Maximum implementation fan-out is still 3 / 7 / 7 / 5 for Express, Zod, Query Core
and React Query. All complete-graph cycles and their finding identities remain
unchanged. These classification changes are improved descriptions of the same
source, not code improvements or score changes.

## Forwarding is not a safety exemption

The authored CommonJS partial-exports example still captures `undefined` during
initialization. Its two references are now recognized as forwarding, but the
cycle remains in the full graph and retains its informational finding. A public
export assignment can read a value before it is populated; source forwarding
does not establish safe loading behavior.

## Verification

- All 149 unit/integration tests pass, including 35 graph tests. New coverage
  includes named/default/namespace/import-equals forwarding, mixed local use,
  object shorthand, CommonJS property forwarding, reassignment/destructuring,
  loop targets, initialized variable redeclarations, alias chains, wrappers,
  lexical shadowing and explicit type exports.
- Eleven authored architecture cases pass, with ten observed runtime cases and
  one static type-only case. The real offline installed-package/workspace check
  also passes.
- Installed-analyzer checks verify forwarding role metadata survives inspection,
  schema validation, comparisons and export tooling.
- All six shared calibration fixtures pass without another baseline change.
- Fresh paired runs of all four public selections preserve raw CSV, populations,
  filters, configuration fingerprints, scores, graph coverage and original
  topology. Removing only dependency views and role metadata makes all other
  dimension evidence identical to the saved pre-change bundle.
- Every pair remains comparison-compatible, with no new/resolved/changed findings
  or severity increases; warning and zero-score-drop gates pass.

The [verification artifact](javascript-typescript-forwarding-2026-10-03.json)
records bundle hashes, source revisions, run IDs, evidence hashes, changed
reference classifications and invariants. Full evidence, context results, logs
and the saved baseline bundle are under
`E:/repos/CodeMetrics.AI/TestResults/js-ts-forwarding/`.

Reproduce the paired runs and audit after building the analyzer:

```powershell
. E:/tools/Use-CalibrationDotNet.ps1
node analyzers/javascript-typescript/scripts/function-policy-corpus.mjs `
  TestResults/js-ts-forwarding/before E:/repos TestResults/js-ts-forwarding/corpus
node analyzers/javascript-typescript/scripts/dependency-view-audit.mjs `
  TestResults/js-ts-forwarding/corpus TestResults/js-ts-forwarding/verified.json
```

The audit permits changes only to dependency views and occurrence role metadata;
it does not waive score, finding or compatibility changes. No public-corpus code
was executed and the external checkouts remain clean. This evidence does not
establish a numeric Architecture ladder or cross-ecosystem comparability.
