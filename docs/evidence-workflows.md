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

```sh
npx --package codemetrics-ai@0.3.0 codemetrics-evidence --input evidence.json \
  --inspect-output inspection.json --expected-ecosystem dotnet \
  --expected-version 2.0.0 --expected-entry-point /repo/src/App/App.csproj \
  --expected-root /repo --expected-variant Release
```

Inspection contains the unchanged `evidence`, `compatibility` metadata and a `usable` flag. Exit status still matters: incomplete analysis or failed dimensions return 2. V2 is readable for explicitly historical audits; completeness and scope remain unknown, and comparisons, gates and SARIF require v3. The package exports `readCompatibleEvidence` and `inspectEvidence` and includes the canonical v2/v3 schemas and historical contract examples for downstream tests.

The .NET `--solution` option also accepts an explicit `.csproj`. Project mode scores only that project; Roslyn loads references for semantic resolution. Solution mode scores the selected solution's production projects. Both retain the source boundary at the entry point's directory. Automatic CLI discovery remains limited to exactly one solution; orchestration tools should resolve project ambiguity explicitly.

Dimension `scope` has a stable `id`, `coverage` (`partial` or `unsupported`), `includes` and `excludes`. Status separately says whether a probe ran successfully. All current static probes cover only part of the broader quality dimension. JS/TS performance scope explicitly covers React hooks/effects and excludes general async, concurrency and runtime performance. Missing scope in historical v3 means unknown, not comprehensive. Changed scope rejects baseline comparisons.

For coordinated changes, test the skill against local `.nupkg` and `.tgz` artifacts from an exact CodeMetrics.AI revision before publication. Release the packages before merging a consumer update that installs those versions by default; do not substitute `latest` when a release is unavailable.

## Invocation identity and stale findings

Both analyzers generate `analysis.runId` and `analysis.auditId` UUIDs. An orchestrator can supply `--run-id <uuid>` and `--audit-id <uuid>` before launching analysis. Standalone invocations generate a new run ID and default the audit ID to it. A polyglot audit uses one audit ID and distinct run IDs for its ecosystem runs.

Validate findings with `codemetrics-evidence --input evidence.json --expected-run-id <current-run-uuid> --expected-audit-id <current-audit-uuid> --inspect-output inspection.json`. The expected IDs must come from the invocation that requested analysis, never from the file being checked. Missing or mismatched IDs return exit 2 before writing an inspection, comparison or SARIF output. An old findings file copied into a new run directory therefore fails validation even when its schema, version, entry point and configuration match.

Comparison JSON records `currentRun` and `baselineRun`; SARIF records IDs in each run's `properties`. Finding fingerprints and comparison compatibility deliberately exclude invocation IDs so findings remain trackable across distinct runs. Retain the enclosing IDs when extracting findings into another report. CSV remains a raw metrics export with its existing header, not independent proof of fresh findings.

The v3 schema keeps these fields optional for historical compatibility. Older evidence without IDs is readable as historical data but cannot satisfy an expected current run ID. Identity binds an artifact to a requested run; source changes after that run still require fresh analysis.
