# codemetrics-ai

Deterministic source analysis for JavaScript, TypeScript, JSX and TSX. Version 0.3.0 implements source metrics and React hook/effect checks and emits schema-v3 evidence, including executed scoring decisions. Scores are **uncalibrated across ecosystems**; compare them only with compatible runs of this analyzer.

Each scored dimension includes `scoringDecision`: policy inputs, executed steps, rounding and finding attribution. These are aggregate policy decisions, not independent finding deductions. See [the decision contract](../../shared/scorecard-schema/scoring-decisions.md).

The unpublished development ruleset `javascript-typescript-2026-10-02-module-graph` replaces the published 0.3.0 maximum-CC/median-MI ladders with owned-function population scores and extends React-only checks with bounded promise-usage analysis. It also corrects erased TypeScript syntax in owned-body measurements and adds unscored module dependency evidence. Generate new baselines for this ruleset; package version alone does not identify development scoring behavior.

```sh
npx codemetrics-ai --project package.json
npx codemetrics-ai --project package.json --tsconfig tsconfig.json
npx codemetrics-ai --output results/metrics.csv --scorecard-output results/evidence.json
```

Default output paths are `.scorecard/javascript-typescript/metrics.csv` and `.scorecard/javascript-typescript/evidence.json`, relative to the current directory. Node 20 or later is required. The TypeScript compiler API is a pinned runtime dependency, so an installed package can analyze source without a development checkout.

## Implemented scope

| Dimension | Implementation |
|---|---|
| codeQuality (Method Complexity) | 40% one worst function score + 60% mean remaining; owned CC findings above 10. Decomposition is unmeasured. |
| maintainability | 40% mean weakest ceil(N/5) function scores + 60% mean remaining |
| performanceAsync (Async/Blocking Usage) | React hook/effect checks, standard Promise async executors, and informational async Array.forEach callbacks |
| architecture | Module dependencies, explicit type-only separation, internal cycle groups and fan-in/out rankings; score remains unmeasured |
| Other dimensions | Explicitly skipped |

React checks run only when a source file imports React. Aliased named imports and namespace imports are recognized; local functions shadowing imports are excluded. Missing dependency arrays are advisory because running after every render can be intentional. The implementation does not claim exhaustive Rules of Hooks, dependency-array correctness, or general concurrency analysis. Source syntax errors make the run incomplete and source dimensions unscored. Semantic type errors are outside this AST-focused version's completeness check.

## Async/Blocking Usage

The metric checks avoidable usage hazards, not execution speed or throughput. I/O latency, throttling and sequential awaits do not inherently indicate misuse.

- Inline async executors passed to the resolved standard `Promise` constructor are scored warnings: their returned Promise is ignored by the constructor. Custom/shadowed constructors are excluded.
- Inline async callbacks passed to resolved standard `Array.forEach` or `ReadonlyArray.forEach` are informational review leads. These APIs ignore returned Promises, but detached work can be intentional; no concurrency rewrite is prescribed.
- React hooks in condition expressions, logical left operands and once-evaluated loop initializers/iterables are not penalized merely for that surrounding syntax. Conditional branches, logical right operands and repeated loop positions remain findings. Outer conditional contexts still apply. The special React `use` API is not treated as an ordinary conditional hook.
- Effects without dependency arrays are informational, excluded from scoring and warning/error quality gates. They remain visible in evidence and SARIF.

An applicable async/React scope retains the provisional 6/10 policy: any scored signal selects 6; otherwise 10. A synchronous-only or type-only scope is skipped rather than awarded 10. Module-level promise operations and top-level await are inspected even without function metrics. Findings, scope and scoring dispositions distinguish observed usage from demonstrated runtime defects.

This is bounded source analysis, not promise-flow verification. Referenced executor/callback functions, constructor aliases, runtime monkey-patching, arbitrary libraries, floating promises and exhaustive hook control flow are outside scope. See the [policy and supporting references](../../shared/scorecard-schema/javascript-typescript-async-policy.md).

## Module architecture evidence

`dimensions.architecture.dependencyGraph` version 1 records selected modules and dependency occurrences. It handles ESM imports/re-exports, TypeScript import-equals and import types, unshadowed CommonJS `require`/`module.require`, and dynamic `import()`. Explicit `import type`/`export type` and type-only named bindings are separated; mixed bindings retain a value dependency. Ordinary imports are value-capable source syntax: their presence does not prove they survive TypeScript emission or execute at runtime.

Resolution uses the package's TypeScript configuration, including paths, module mode and export conditions. Default source scans use Node10 resolution. If normal resolution fails, a read-only virtual node_modules view lets TypeScript resolve uniquely named selected workspace packages through their existing manifests. It never installs packages, changes source, or guesses a missing `dist` entry from `src`. Duplicate workspace names are not guessed. An explicit package selection does not automatically add sibling packages to graph scope.

Coverage distinguishes `internal`, `external`, `builtin`, `outOfScope`, `unavailableSource`, `unresolved` and `dynamic` occurrences. Literal resolution percentage is separate from topology coverage: a resolved but excluded target or a computed import still leaves a gap. A zero-literal population reports null, not 100%. Declaration targets are marked because TypeScript resolution is not runtime-loader verification. The graph includes no transitive source outside the selected files.

Cycles are strongly connected groups of resolved internal value/mixed dependencies; repeated imports count once toward fan-in/out. Cycles are informational review leads and carry no score penalty. Dynamic-literal imports and CommonJS calls can be conditional or deferred; a cycle does not establish an initialization failure. Top-ten fan-in/out lists are observations, with no invented defect cutoff. Re-export-only modules are marked for barrel-file context.

Architecture retains `status: skipped` and no score until calibration. Other measured dimensions remain usable when this diagnostic graph has resolution gaps. Source parse failures retain partial graph evidence with `status: failed` and the existing incomplete-analysis exit. Schema v3 is unchanged; the new scope/ruleset requires fresh baselines. See the [graph contract](../../shared/scorecard-schema/javascript-typescript-module-graph.md).

## Discovery

The CLI discovers files from each package's tsconfig when present, otherwise scans source extensions. Positive npm workspace globs (including object-form `workspaces.packages`) are supported. Each child package owns its files and exclusions; the root cannot re-include a child's excluded files. External tsconfig references are not recursively analyzed as separate packages: include them in the workspace manifest. Explicit missing manifests/configs and unsupported negative workspace patterns are errors.

Build output, node_modules, metadata, declarations, test/fixture directories, `.test`/`.spec` source, conventional generated filenames, and `@generated`/`<auto-generated` headers are excluded. Discovery does not follow directory symlinks. Skipped candidate files appear in filters; pruned directories are not counted as candidate files.

## Scored function policy

Code quality measures **method complexity only**. Individual owned CC anchors are 3/5/10/20/40 → 10/8/6/4/0. MI anchors are 40/52/58/65/70/75 → 0/2/4/6/8/10. Both use linear interpolation and clamp to 0–10. A single function uses its own score; a scope with no implemented functions is skipped. Exactly one worst function receives 40% of complexity weight, even when several tie. For MI, the weakest `ceil(N/5)` functions receive 40% through their group mean. The remaining functions receive 60%; no function is in both groups.

Measurements use each function's **own body**, excluding its signature, nested functions, type declarations, classes and enums. Nested implemented functions are measured separately. Multiline literals contribute every occupied source line; comments and brace/semicolon-only lines contribute none. Implemented empty bodies receive MI 100. Owned MI retains its fractional value before scoring. Parameter defaults, module-level code, class static blocks and non-function field initializers are outside these function scores and are disclosed in scope. This is not full runtime-code coverage.

Measurement `owned-function-body-v2-type-erasure` also excludes `as`, `satisfies`, angle-bracket assertions, non-null assertions, annotation delimiters, definite-assignment assertions and generic type-argument delimiters/commas. Their runtime expressions remain in scope, including functions and decisions inside `factory<T>` instantiation expressions. TypeScript's AST classifies those expressions as type nodes, so they require explicit executable traversal. Runtime negation, comparisons and object-property colons remain measured. The [hotspot audit](../../shared/scorecard-schema/calibration-runs/javascript-typescript-hotspot-review-2026-10-02.md) explains the correction and its corpus impact.

Interpolation and weighting use exact rational arithmetic on decimal measurement representations, with one half-up rounding to one decimal at the end (8.65 → 8.7). The logarithmic MI measurement itself uses JavaScript floating-point arithmetic. `scoring.observations.functionContributions` records every function, measurements, group, individual score, rational weight and weighted loss. `topOffenders` samples five functions; it is not the scoring population. `componentDetails.methodComplexity` exposes the same complexity score; decomposition has a null score and unsupported status.

Function rows and complexity findings also carry optional `complexityBreakdown` version 1: baseline 1, counts of `ifStatements`, `switchCases`, `loops`, `catchClauses`, `ternaryExpressions`, `logicalAnd`, `logicalOr` and `nullishCoalescing`, plus `decisionIncrements` and `total`. `total = baseline + sum(counts) = ownComplexity`. The same owned traversal supplies the count and explanation. Counts include each non-default case label, even when labels share a body. They do not measure nesting, prove defects or add penalties. Older evidence can omit this diagnostic detail without becoming incompatible.

The [corpus verification](../../shared/scorecard-schema/calibration-runs/javascript-typescript-complexity-detail-2026-10-02.md) checks all 2,560 function explanations and demonstrates unchanged scores, raw CSV, finding identities and baseline gates.

The anchors express the accepted product standard, not empirical equivalence between JS/TS and .NET. Keep ecosystems separate and do not restore these scores from historical CSV MI. See the [full policy](../../shared/scorecard-schema/javascript-typescript-function-policy.md) and [initial corpus comparison](../../shared/scorecard-schema/calibration-runs/javascript-typescript-function-policy-2026-10-02.md).

## Historical raw metric policy

A member is a function with a body: declarations, expressions, arrows, methods, constructors and accessors. Nested functions are separate members and do not inflate their parent's complexity. Named JSX-producing PascalCase functions are components; `useX` functions are hooks. Types group class members or standalone functions and their callbacks within a module. TypeScript interfaces, type aliases and overload signatures without bodies are not runtime members.

Cyclomatic complexity starts at 1 per member and adds 1 for `if`, loops, `catch`, non-default switch cases, ternaries, `&&`, `||` and `??`. JSX markup itself contributes no decisions; expressions inside JSX do. Type-only conditions contribute none. Source lines count distinct lines with non-brace/non-semicolon tokens owned by the member, excluding nested functions and type nodes. Executable counts count owned statements (excluding blocks/empty statements), with one for expression-bodied functions.

Halstead volume is `N * log2(n)`: token count times vocabulary size. Identifiers/literals are operands; other runtime tokens are operators. Nested function and type tokens are excluded. MI is rounded to the nearest integer using:

```text
max(0, (171 - 5.2*ln(max(V,1)) - 0.23*CC - 16.2*ln(max(LOC,1))) * 100/171)
```

For hand-checking, V=64, CC=4, LOC=10 produces raw MI=65. Raw source lines include signature tokens and count token start lines; raw MI is integer-rounded. These raw metrics remain compatible with earlier CSV output, but the current scores use the separate owned-body measurements described above. The earlier maximum-CC and median-MI bands apply only to historical rulesets.

CSV preserves the shared header. Namespace is the repository-relative module path; Project is the package name. Coupling currently counts distinct imported module specifiers per file, and is repeated on its member/type rows; it is not .NET class-coupling parity. Inheritance follows compiler-resolved class base types. Type rows sum owned member complexity/lines and average member MI; these are documented JS/TS aggregates, not Visual Studio type metrics.

## Baselines and SARIF

```sh
npx codemetrics-ai --baseline quality-baselines/javascript-typescript.json \
  --comparison-output results/comparison.json --fail-on-new warning \
  --max-score-drop 0 --sarif results/analysis.sarif

# Works with either .NET or JS/TS evidence:
npx --package codemetrics-ai codemetrics-evidence --input results/evidence.json \
  --baseline quality-baselines/javascript-typescript.json --output results/comparison.json
```

Exit codes: 0 success, 1 requested quality gate failed, 2 invalid input or incomplete/incompatible evidence. Without gate flags, findings do not fail an otherwise complete analysis. See [evidence workflows](../../docs/evidence-workflows.md) and [v3 migration](../../shared/scorecard-schema/migration-v3.md).

## Development

Architecture evidence includes separate import/load and direct public re-export
rankings under `dependencyGraph.dependencyViews`. Version 2 recognizes explicit
ESM and CommonJS forwarding; bindings also used locally appear in both views.
Reassigned bindings and unsupported alias/value flow remain in implementation
evidence. Overall dependencies and cycle findings remain intact, including mixed
files and cycles crossing the views. Architecture is still unscored. See the
[dependency view contract](../../shared/scorecard-schema/javascript-typescript-module-graph.md#implementation-reference-and-public-re-export-views).

`metrics.ts` owns source traversal, raw member metrics and finding identities. `function-measurements.ts` collects the separate owned-body measurements; `function-scoring.ts` applies the two population policies with exact interpolation/rounding from `score-arithmetic.ts`. `react-probe.ts` checks calls owned by each function for React hook/effect observations. `async-probe.ts` checks each visited node once for bounded standard-library promise patterns, including module-level operations. Neither probe traverses nested bodies or calculates metrics. `async-scoring.ts` owns applicability, scope and the provisional async score. Regression fixtures preserve raw metrics, source locations and existing finding fingerprints; advisory severity/disposition changes are intentional.

```sh
npm ci
npm test
npm run test:package
npm run test:architecture
```

Tests include parser/React/workspace fixtures, formula assertions, schema validation, comparison compatibility, gate behavior, and compiled CLI execution. The package smoke test packs and installs the package with production dependencies in an isolated directory and exercises analysis, comparison and SARIF. The shared corpus adds pinned accuracy and score-distribution checks.

The test command limits Vitest to two workers to avoid contention between concurrent TypeScript compiler instances; integration assertions and timeouts are unchanged.

`test:architecture` verifies eleven labeled graph examples against their observed loading behavior and tests a real, offline npm installation alongside a competing workspace version. It executes only repository-authored fixture code. Safe deferred cycles, eager initialization failures, an immediately invoked function and partial CommonJS exports demonstrate why cycle counts alone are not defect labels. Results are written to `TestResults/js-ts-architecture-context/` at the repository root. CI and npm release verification both run this check; Architecture remains unscored.

The packaged `codemetrics-evidence --inspect-output <path>` command reads v2/v3 and validates optional expected provenance; historical v2 cannot be compared, gated or exported as SARIF. Structured dimension scope identifies implemented coverage. See [consumer integration](../../docs/evidence-workflows.md#skill-and-other-evidence-consumers).
