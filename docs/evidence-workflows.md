# Evidence comparison and CI

Both analyzers emit schema-v3 evidence. The `codemetrics-evidence` command ships in the `codemetrics-ai` NPM package and works with either ecosystem. It requires Node 20 or later; the .NET analyzer itself retains its native .NET-only runtime.

```sh
# Analyze a .NET solution and compare with a committed baseline.
code-metrics --solution App.slnx --configuration Release --skip-dependency-probe
npx --package codemetrics-ai codemetrics-evidence \
  --input .scorecard/dotnet/evidence.json \
  --baseline quality-baselines/dotnet.json \
  --output .scorecard/dotnet/comparison.json \
  --fail-on-new warning --max-score-drop 0 \
  --sarif .scorecard/dotnet/results.sarif

# JS/TS also accepts comparison options directly.
npx codemetrics-ai --project package.json \
  --baseline quality-baselines/javascript-typescript.json \
  --comparison-output .scorecard/javascript-typescript/comparison.json \
  --fail-on-new error --sarif .scorecard/javascript-typescript/results.sarif
```

Pin analyzer/package versions in CI. To establish a baseline, review a successful evidence run and copy it into `quality-baselines/`. Gate flags are optional; generation alone never fails solely because a quality score is low. Exit codes are 0 for success, 1 for a requested quality gate, 2 for invalid/incompatible/incomplete analysis, and 130 for cancelled .NET analysis.

Comparison reports contain new, resolved, changed-severity/confidence, and unchanged findings plus per-dimension before/after/delta values. Checkout-root and line changes are tolerated. Version, ruleset, configuration, subject, and dimension-availability differences reject comparison by default. `codemetrics-evidence --allow-incompatible` produces an exploratory report with null score deltas; it cannot be used with gates. Failed dimensions and incomplete runs cannot pass a quality gate.

SARIF 2.1.0 export includes stable rule IDs, fingerprints, relative locations, observations, and analysis completion. Upload it with GitHub's `github/codeql-action/upload-sarif` action in repositories with code scanning available. Give only that upload job `security-events: write`; run analysis with read-only repository permissions. For repositories without code scanning, upload the JSON comparison and SARIF files as ordinary CI artifacts.

Do not average ecosystem scores. JS/TS scores are uncalibrated across ecosystems, and its implemented dimensions have a narrower scope than .NET. See `shared/scorecard-schema/migration-v3.md` for consumer migration and missing-signal handling.
