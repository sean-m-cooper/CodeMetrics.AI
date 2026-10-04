# Evidence comparison and CI

Both analyzers emit schema-v3 evidence. The `codemetrics-evidence` command ships in the `codemetrics-ai` NPM package and works with either ecosystem. It requires Node 20 or later; the .NET analyzer itself retains its native .NET-only runtime.

```sh
# Analyze a .NET solution and compare with a committed baseline.
code-metrics --solution App.slnx --configuration Release --skip-dependency-probe
npx --package codemetrics-ai@0.3.0 codemetrics-evidence \
  --input .scorecard/dotnet/evidence.json \
  --baseline quality-baselines/dotnet.json \
  --output .scorecard/dotnet/comparison.json \
  --fail-on-new warning --max-score-drop 0 \
  --sarif .scorecard/dotnet/results.sarif

# JS/TS also accepts comparison options directly.
npx codemetrics-ai@0.3.0 --project package.json \
  --baseline quality-baselines/javascript-typescript.json \
  --comparison-output .scorecard/javascript-typescript/comparison.json \
  --fail-on-new error --sarif .scorecard/javascript-typescript/results.sarif
```

Pin analyzer/package versions in CI. To establish a baseline, review a successful evidence run and copy it into `quality-baselines/`. Gate flags are optional; generation alone never fails solely because a quality score is low. Exit codes are 0 for success, 1 for a requested quality gate, 2 for invalid/incompatible/incomplete analysis, and 130 for cancelled .NET analysis.

Comparison reports contain new, resolved, changed-severity/confidence, and unchanged findings plus per-dimension before/after/delta values. Checkout-root and line changes are tolerated. Version, ruleset, configuration, subject, and dimension-availability differences reject comparison by default. `codemetrics-evidence --allow-incompatible` produces an exploratory report with null score deltas; it cannot be used with gates. Failed dimensions and incomplete runs cannot pass a quality gate.

SARIF 2.1.0 export includes stable rule IDs, fingerprints, relative locations, observations, and analysis completion. Upload it with GitHub's `github/codeql-action/upload-sarif` action in repositories with code scanning available. Give only that upload job `security-events: write`; run analysis with read-only repository permissions. For repositories without code scanning, upload the JSON comparison and SARIF files as ordinary CI artifacts.

Do not average ecosystem scores. JS/TS scores are uncalibrated across ecosystems, and its implemented dimensions have a narrower scope than .NET. See `shared/scorecard-schema/migration-v3.md` for consumer migration and missing-signal handling.

## Skill and other evidence consumers

The `code-scorecard` skill in `ai_tools` is a version-aware consumer. Keep thresholds and schemas here; downstream consumers pin tested package versions and use the packaged validator. Generate a fresh run by default, validate both process exit and evidence provenance, and expose scope alongside every score. Never promote partial CSV into a replacement score after a failed analysis.

### JS/TS complexity explanations

Development JS/TS evidence exposes `complexityBreakdown` version 1 on
`dimensions.codeQuality.scoring.observations.functionContributions`, sampled
`topOffenders` (including `componentDetails.methodComplexity.topOffenders`), and
complexity finding `observations`. Inspection preserves these optional fields.
They contain baseline 1, eight named decision counts, their `decisionIncrements`
sum and the total owned CC. Present a concise selection when explaining a hotspot:
“CC23: baseline 1 + 14 if statements + 4 case labels + 4 logical operators.”

Use these counts as explanatory context only. They do not add deductions, alter
the recorded score, measure nesting or establish defects. Case labels can share
a body; logical operators include both conditions and value expressions. Preserve
documented protocol and ordering responsibilities when discussing possible changes.
If the field is absent or its version is unknown, omit the breakdown and retain
the supported score/observations. Do not infer counts from prose, restore scores
from CSV, or present the top-five sample as the entire population. This additive
detail does not itself require a new scoring ruleset or baseline migration.

### Packaged inspection

The development `javascript-typescript-2026-10-03-owner-population` ruleset scores
Async/Blocking Usage using distinct affected/eligible function or module owners.
Show both counts with the score; only owners with observed async/React usage enter
the denominator. Review leads do not enter the numerator. A small population can
score 0 from one affected owner, and a rate below 1% can score 10 with findings.
This is a usage policy, not a runtime-performance claim. A changed ruleset/scope
requires a fresh baseline; do not bypass compatibility checks to claim improvement.

`codeQuality.componentDetails.decomposition` version 1 contains unscored size and
module-distribution evidence. Code Quality still measures method complexity only.
`errorHandling.handlerEvidence` version 1 reports inspected handler counts and
body-comment context, with no error-handling score. Recognize documented swallowing;
do not turn empty-body review leads or code presence into correctness judgments.
Disclose uninspected callbacks and absent populations. Missing optional evidence
means unavailable, not zero. See the [context contract](../shared/scorecard-schema/javascript-typescript-context-evidence.md).

The development JS/TS module-graph scope adds
`dimensions.architecture.dependencyGraph` version 1. Present Architecture as
unmeasured: its skipped status means scoring is not calibrated, while graph
observations may still be available. Report resolution/scope gaps and interpret
cycles and fan-in/out as review leads. Explicit type-only dependencies do not
participate in the value graph; ordinary imports are value-capable syntax, not
verified emitted runtime imports. See the [module graph contract](../shared/scorecard-schema/javascript-typescript-module-graph.md).

When `dependencyGraph.dependencyViews` is present, report implementation-reference
rankings separately from public re-export rankings. Version 1 separates direct
export-from declarations; version 2 additionally recognizes bounded ESM/CommonJS
forwarding and preserves local uses in both views. Interpret the recorded version
and classification; a changed view count between versions is not code improvement.
This is not a runtime-use or responsibility analysis. Preserve overall cycle
findings, resolution gaps and the reported overlap between views; do not add
view counts or infer a score. Missing view detail means unavailable, not zero.

```sh
npx --package codemetrics-ai@0.3.0 codemetrics-evidence --input evidence.json \
  --inspect-output inspection.json --expected-ecosystem dotnet \
  --expected-version 2.0.0 --expected-entry-point /repo/src/App/App.csproj \
  --expected-root /repo --expected-variant Release
```

Inspection contains the unchanged `evidence`, `compatibility` metadata and a `usable` flag. Exit status still matters: incomplete analysis or failed dimensions return 2. V2 is readable for explicitly historical audits; completeness and scope remain unknown, and comparisons, gates and SARIF require v3. The package exports `readCompatibleEvidence` and `inspectEvidence` and includes the canonical v2/v3 schemas and historical contract examples for downstream tests.

The .NET `--solution` option also accepts an explicit `.csproj`. Project mode scores only that project; Roslyn loads references for semantic resolution. Solution mode scores the selected solution's production projects. Both retain the source boundary at the entry point's directory. Automatic CLI discovery remains limited to exactly one solution; orchestration tools should resolve project ambiguity explicitly.

Dimension `scope` has a stable `id`, `coverage` (`partial` or `unsupported`), `includes` and `excludes`. Status separately says whether a probe ran successfully. All current static probes cover only part of the broader quality dimension. JS/TS async coverage depends on the recorded ruleset: development evidence includes bounded standard Promise usage as well as React hooks/effects, while general promise flow, concurrency safety, and runtime performance remain outside scope. Missing scope in historical v3 means unknown, not comprehensive. Changed scope rejects baseline comparisons.

For .NET 2.3.0+, `dependencyCompatibility` records lookup coverage and grouped reasons. Missing compatibility makes Dependency Management failed with no score. Unknown candidate observations are informational and `scoreDisposition: unavailable`. Successful vulnerability/deprecation checks retain verified findings; unavailable vulnerability evidence also withholds Security. These assessment failures preserve the existing exit-2/unusable contract and cannot support an overall score.

For coordinated changes, test the skill against local `.nupkg` and `.tgz` artifacts from an exact CodeMetrics.AI revision before publication. Release the packages before merging a consumer update that installs those versions by default; do not substitute `latest` when a release is unavailable.

## Invocation identity and stale findings

Both analyzers generate `analysis.runId` and `analysis.auditId` UUIDs. An orchestrator can supply `--run-id <uuid>` and `--audit-id <uuid>` before launching analysis. Standalone invocations generate a new run ID and default the audit ID to it. A polyglot audit uses one audit ID and distinct run IDs for its ecosystem runs.

Validate findings with `codemetrics-evidence --input evidence.json --expected-run-id <current-run-uuid> --expected-audit-id <current-audit-uuid> --inspect-output inspection.json`. The expected IDs must come from the invocation that requested analysis, never from the file being checked. Missing or mismatched IDs return exit 2 before writing an inspection, comparison or SARIF output. An old findings file copied into a new run directory therefore fails validation even when its schema, version, entry point and configuration match.

Comparison JSON records `currentRun` and `baselineRun`; SARIF records IDs in each run's `properties`. Finding fingerprints and comparison compatibility deliberately exclude invocation IDs so findings remain trackable across distinct runs. Retain the enclosing IDs when extracting findings into another report. CSV remains a raw metrics export with its existing header, not independent proof of fresh findings.

The v3 schema keeps these fields optional for historical compatibility. Older evidence without IDs is readable as historical data but cannot satisfy an expected current run ID. Identity binds an artifact to a requested run; source changes after that run still require fresh analysis.
