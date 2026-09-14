# Scorecard Dimensions

All analyzers use a 0-10 score scale and these stable dimension keys.

| Key | Purpose |
|-----|---------|
| `codeQuality` | Complexity, decomposition, and local code-shape risks |
| `maintainability` | Maintainability index distribution and difficult-to-change areas |
| `errorHandling` | Exception, rejection, logging, and failure-path quality |
| `performanceAsync` | Async, concurrency, blocking, and avoidable performance risks |
| `security` | Static security findings and imported vulnerability signals |
| `testing` | Test presence, assertion quality, skipped tests, and coverage signals |
| `documentation` | README, docs, API docs, and onboarding material |
| `dependencyManagement` | Vulnerable, deprecated, outdated, or inconsistent dependencies |
| `architecture` | Cycles, layering, coupling hotspots, and framework-specific structure risks |

Analyzers may use language-specific rules inside each dimension. The key names, the 0-10 scale, and the `scored` / `skipped` / `failed` status values are stable.

## Score Comparability

Within an ecosystem, compare scores using compatible measurement/scoring policies, rulesets and analysis scope. An analyzer version alone is insufficient for development packages that share a version label; retain package identity and run provenance. A change in policy can change scores on identical source, and consistent scoring does not establish equal business risk across repositories.

Scores are comparable **across** ecosystems only after the analyzer has completed the corpus calibration procedure in `calibration.md`. Until then, consumers must present per-ecosystem scores side by side without averaging or ranking them against each other, and must caveat uncalibrated ecosystems.

| Ecosystem | Calibration status |
|---|---|
| `dotnet` | Baseline |
| `javascript-typescript` | Uncalibrated |
| `python` | Uncalibrated |
| `rust` | Uncalibrated |
