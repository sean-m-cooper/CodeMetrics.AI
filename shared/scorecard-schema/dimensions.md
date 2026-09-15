# Scorecard Dimensions

All analyzers use a 0-10 score scale and these stable dimension keys.

| Key | Purpose |
|-----|---------|
| `codeQuality` | Complexity, decomposition, and local code-shape risks |
| `maintainability` | Maintainability index distribution and difficult-to-change areas |
| `errorHandling` | Exception, rejection, logging, and failure-path quality |
| `performanceAsync` | Async/Blocking Usage: avoidable hazards in asynchronous operations, concurrency and blocking calls |
| `security` | Static security findings and imported vulnerability signals |
| `testing` | Test presence, assertion quality, skipped tests, and coverage signals |
| `documentation` | README, docs, API docs, and onboarding material |
| `dependencyManagement` | Vulnerable, deprecated, outdated, or inconsistent dependencies |
| `architecture` | Cycles, layering, coupling hotspots, and framework-specific structure risks |

Analyzers may use language-specific rules inside each dimension. The key names, the 0-10 scale, and the `scored` / `skipped` / `failed` status values are stable.

## Async/Blocking Usage

Display `performanceAsync` as **Async/Blocking Usage**. It evaluates asynchronous operations and blocking calls for avoidable hazards, accounting for documented intent and supported usage patterns. Scores reflect code usage, not runtime speed or throughput. I/O latency, external rate limits and deliberate throttling do not inherently indicate misuse.

Retain the evidence's declared scope. For example, a React-hooks-only probe is displayed as **Async/Blocking Usage — React hooks only**. The display name does not broaden that probe's coverage. For the current .NET policy, 10 means no scored hazards detected within the measured scope, not exceptional runtime performance.

This is a presentation and purpose clarification. The `performanceAsync` key, rule IDs, findings, numerical scores and comparison compatibility are unchanged. The proposed function-population scoring ladder remains an experimental preview pending adoption.

## Score Comparability

Within an ecosystem, compare scores using compatible measurement/scoring policies, rulesets and analysis scope. An analyzer version alone is insufficient for development packages that share a version label; retain package identity and run provenance. A change in policy can change scores on identical source, and consistent scoring does not establish equal business risk across repositories.

Scores are comparable **across** ecosystems only after the analyzer has completed the corpus calibration procedure in `calibration.md`. Until then, consumers must present per-ecosystem scores side by side without averaging or ranking them against each other, and must caveat uncalibrated ecosystems.

| Ecosystem | Calibration status |
|---|---|
| `dotnet` | Baseline |
| `javascript-typescript` | Uncalibrated |
| `python` | Uncalibrated |
| `rust` | Uncalibrated |
