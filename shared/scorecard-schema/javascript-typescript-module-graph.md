# JS/TS module dependency evidence, version 1

Development ruleset `javascript-typescript-2026-10-02-module-graph` adds
`dimensions.architecture.dependencyGraph`. This is a bounded evidence pass, not
an architecture scoring policy. Successful source analysis leaves Architecture
`skipped` with no score or scoring decision. Do not promote graph coverage or the
absence of observed cycles into a score of 10. Existing function/MI/async policies
and raw CSV measurements are unchanged.

## Nodes and observations

Nodes are the selected source files, identified by repository-relative paths and
package names. Physical paths are canonicalized internally to prevent duplicate
graph nodes from aliases. `analyzed` identifies nodes successfully parsed;
`reExportOnly` identifies modules containing at least one re-export and otherwise
only imports, re-exports, type declarations, empty statements or string directives.
It is descriptive barrel-file context, not an exemption or a defect classification.

Every supported dependency occurrence retains its source module, project, line,
source offset, original literal specifier (or null), syntax form, usage class,
resolution outcome and resolver provenance. Repeated references remain distinct
observations but contribute only one edge per ordered module pair to topology.

Supported forms are ESM imports, re-exports, import-equals, import types,
unshadowed `require`/`module.require`, and dynamic `import()`. Literal strings and
templates without substitutions are resolvable. Computed arguments are recorded
as dynamic without evaluation. Local parameters, declarations or imports shadowing
`require` or `module` suppress the CommonJS interpretation. Missing symbols are
treated as the conventional ambient Node names; this is syntax recognition, not
proof of runtime binding. Aliased require functions, createRequire flow, custom
loaders and bundler plugins are outside this first pass.

`usage` is `typeOnly`, `value`, or `mixed`. Explicit import/export type markers,
type-only named bindings and import-type syntax are recognized. Mixed bindings
contribute a value edge. Ordinary imports are value-capable syntax, not proof
that an import survives compiler erasure. Usage analysis of implicitly type-only
imports is outside this contract. Cycles must therefore be described as source
dependency relationships, not established runtime initialization cycles.

## Resolution and scope

The pinned TypeScript compiler resolves each literal with the importing package's
options and the usage's import/require mode. This honors paths, package exports
and custom conditions supported by those settings. A default source scan uses
Node10 resolution; explicit tsconfigs retain TypeScript's configured or inferred
mode. Builtins are recognized through the running Node version. Both Node and
TypeScript versions are recorded under `resolutionEnvironment`.

Normal compiler resolution takes precedence. On failure, a read-only virtual
node_modules view maps uniquely named selected workspace manifests to their real
roots and lets the compiler retry. It honors manifest entry points and export
conditions; it does not invent a source entry, ignore missing build artifacts,
choose between duplicate package names, or install dependencies. An entry-point
selection does not expand to unselected sibling workspaces automatically.

If the named package is already present in an ancestor node_modules directory,
failed normal resolution stays unresolved. A private export or missing installed
build target must not be satisfied by a different same-named workspace version.

| Resolution | Meaning |
| --- | --- |
| `internal` | Target is a selected, successfully analyzed source module. |
| `external` | Compiler resolves into installed node_modules outside selected sources. |
| `builtin` | Known Node builtin reference; no source traversal. |
| `outOfScope` | Resolved target exists outside selected source, including excluded tests, declarations or unselected workspace source. |
| `unavailableSource` | Target is selected but its source could not be analyzed. |
| `unresolved` | Literal resolution failed. A declared dependency alone is not evidence of successful resolution. |
| `dynamic` | Computed/missing argument; no guessed target. |

`targetFile` records compiler resolution, with `declarationTarget` identifying
`.d.ts`, `.d.mts` and `.d.cts` targets. Those may represent package types rather
than runtime implementation. No external, excluded or unavailable source is
silently traversed for graph structure.

## Coverage and topology

`coverage` records selected/analyzed module counts, occurrence totals, literal
and resolved-literal counts, outcome counts and explicit type-only occurrences.
`literalResolutionPercent` uses all literal occurrences as its denominator and
is null when there are none. It includes resolved outside-scope and unavailable
targets because their addresses were resolved; they still create topology gaps.

Coverage status is `gaps` when source is incomplete, selected modules are missing,
or any occurrence is unresolved, dynamic, outside scope or unavailable. Otherwise
it is `observedDependenciesResolved`, a statement about supported observations
inside the declared scope, not comprehensive runtime coverage. Missing external
dependencies are visible gaps even when the observed internal graph looks simple.
Graphs with gaps do not invalidate unrelated source metrics: Architecture is
unscored throughout this first pass. Syntax failures keep the existing global
incomplete-analysis contract and mark Architecture failed as well.

Topology uses only resolved internal value/mixed edges. `internalValueFanOut`
counts distinct targets and `internalValueFanIn` distinct source modules.
Top-ten rankings retain positive values with deterministic path tie-breaking.
There is no calibrated threshold for a "high" count.

### Implementation-reference and public re-export views

The additive `dependencyViews` extension now has `version: 2` and classification
`explicit-forwarding-v2`. Each dependency observation also carries `roles`, an
array containing `implementation`, `reExport`, both, or neither for an explicitly
type-only reference. Roles describe source syntax, not runtime safety or a score:

- `implementation` retains local uses, side-effect imports, unforwarded bindings,
  and unrecognized/ambiguous forwarding. It is not proof that every import executes.
- `reExports` includes direct export-from declarations and bounded explicit
  forwarding: named/default exports of local ESM import or import-equals bindings;
  assignment to unshadowed `module.exports`, `module.exports.name` or `exports.name`
  from a direct require call or simple local require binding. Static property
  selection and parentheses are supported, including literal string member keys.
- A binding that is forwarded and used locally contributes to both views. Local
  symbol identity handles aliases and shadowing; object shorthand uses also count
  as local behavior. Bindings assigned again, updated or initialized again are
  conservatively kept in implementation evidence. Explicit type-only references
  remain outside both views; mixed references retain their value-capable edge.

Alias chains, destructured require bindings, wrapper calls, computed member names,
dynamic-import forwarding, mutations through runtime reflection, and general
value/control flow are not inferred. Unsupported cases remain implementation
references. Standard CommonJS names are recognized syntactically with lexical
shadowing checks, not proven runtime bindings. A forwarding classification does
not guarantee a safe initializer: a property can be read before it is populated.

Historical version 1 (`direct-reexport-syntax-v1`) separated only direct export-from
declarations; import-then-export and CommonJS forwarding stayed in implementation.
Do not compare role-view counts across these versions as if source code changed.

Each view reports `modules`, `highestFanOut`, `highestFanIn` and
`internalValueEdgeCount`, using the same distinct source/target counting and path
tie-breaking as the overall graph. The views split individual occurrences, not
whole files: a re-export-only module may still have imports or side effects.
`reExportOnly` is descriptive context and never removes its import/load edges.

The same source/target pair may appear in both views when imported and re-exported.
It counts once in each view and once overall. `sharedInternalValueEdgeCount`
records that overlap, so implementation edges plus re-export edges minus shared
edges equals `totalInternalValueEdgeCount`. Do not simply add the two rankings.

Coverage gaps and selected-source boundaries still apply. Unresolved, outside-scope
and external references do not become internal edges in either view. The overall
graph, degree rankings and cycle findings are unchanged; cycles may cross view
boundaries and must never be discarded by looking only at one view. No separate
cycle findings or deductions are added.

Historical graph evidence without `dependencyViews` has an unavailable split,
not zero implementation dependencies. Unknown classification versions should not
be interpreted using these rules. This additive detail does not change the
ruleset, graph scope, finding fingerprints or comparison compatibility.

The [dependency-view corpus pass](calibration-runs/javascript-typescript-dependency-views-2026-10-03.md)
records the resulting rankings and verifies that the original evidence is unchanged.

The [forwarding pass](calibration-runs/javascript-typescript-forwarding-2026-10-03.md)
records version 2 classifications, mixed local/export behavior, and preservation
of scores and complete-graph cycles.

Cycles are strongly connected components with two or more modules, plus self-loops.
One component produces one informational `moduleDependencyCycle` finding; the
sorted member list is a set, not a claimed ordered cycle path. Fingerprints use
relative members and are stable across checkout roots. The graph retains all
dependency observations so reviewers can inspect actual edges and loading syntax.
Literal dynamic imports and CommonJS calls remain edges even when deferred or
conditional. Findings explicitly state that execution order is not established.
An intentional cycle is not an automatic architecture defect.

## Consumption and compatibility

The existing v3 extension points carry graph detail without a schema change.
Inspection and SARIF preserve the informational findings. A warning/error gate
does not fail just because a new cycle appears; an explicitly requested info gate
can still report it. The new Architecture scope and ruleset require a fresh
comparison baseline, even though existing numerical scores are unchanged.

Consumers should report "Architecture: unmeasured; module dependency evidence
only", disclose coverage gaps, distinguish type-only references and preserve
barrel/ordering context. Do not infer SOLID compliance, cohesion, dependency safety
or missing-module cleanliness from this graph. Review the pinned corpus evidence
before establishing a scoring policy.

The [first corpus pass](calibration-runs/javascript-typescript-module-graph-2026-10-02.md)
records resolution gaps, re-export context, a two-module source cycle and unchanged
existing scores across four pinned selections.

The [labeled context pass](calibration-runs/javascript-typescript-architecture-context-2026-10-03.md)
contrasts observed loading outcomes for the same cycle shapes and verifies
installed-package precedence with offline npm packaging and installation.
