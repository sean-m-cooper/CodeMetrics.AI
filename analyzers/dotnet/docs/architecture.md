# CodeMetrics.AI architecture

CodeMetrics.AI is a deterministic, read-only analyzer. It loads a .NET solution or explicit project with Roslyn, collects production metrics, runs nine independent scorecard probes, and writes CSV metrics plus schema-v3 JSON evidence.

## Analysis flow

1. `Program.cs` parses command-line options and passes cancellation to the analysis pipeline.
2. `Program` loads the solution or project with the selected MSBuild configuration. Project mode scores only the selected project while keeping references in the workspace for semantic resolution. `SolutionCompilationLoader` classifies projects, bounds concurrent compilations to four, and records missing compilations and compiler errors.
3. `MetricsCollector` calculates type/member metrics from production syntax and symbols.
4. Each class in `Probes/` evaluates one scorecard dimension. Probes receive immutable metric or compilation inputs and return a `DimensionResult`.
5. `CsvWriter` and `EvidenceWriter` persist the two public output formats.

The dependency probe is the only probe that invokes external commands or reads package feeds. Those operations are bounded, cancellable, and degrade to explicit failed or unknown evidence rather than silently inventing compatibility.

## Important boundaries

- `Metrics/` owns Roslyn metric calculation. Raw Roslyn-compatible class coupling remains available even when architecture scoring uses structural coupling.
- `Probes/` owns findings and score ladders. A probe should report observable evidence; it should not mutate the analyzed solution.
- `Output/` owns serialization only. Evidence shape changes must remain compatible with `shared/scorecard-schema/evidence.schema.v3.json`.
- Tests exercise probes with in-memory Roslyn compilations where possible. End-to-end tests are reserved for solution loading, command execution, and output contracts.

## Design invariants

- Scores must be deterministic for the same source, configuration, analyzer version, and package-source responses.
- Generated code, build output, and test projects never enter production type metrics.
- A missing or failed signal is reported as skipped, failed, or unknown; it is never treated as a clean result.
- Suppressions are category-specific and require a nearby reason.
- Concurrency recommendations must account for ordering, back-pressure, shared contexts, and framework thread-safety.
- Package upgrade scoring uses the candidate package's target-framework assets, not version-major guesses.

## Verification

From the repository root:

```powershell
dotnet restore analyzers/dotnet/CodeMetrics.AI.slnx
dotnet test --solution analyzers/dotnet/CodeMetrics.AI.slnx --configuration Release --no-restore
dotnet build analyzers/dotnet/CodeMetrics.AI.slnx -c Debug --no-restore /p:Version=1.3.3
dotnet analyzers/dotnet/src/CodeMetrics.AI/bin/Debug/net10.0/CodeMetrics.AI.dll --solution analyzers/dotnet/CodeMetrics.AI.slnx
```

The final command refreshes `.scorecard/dotnet/metrics.csv` and `.scorecard/dotnet/evidence.json` beneath `analyzers/dotnet`.

EvidenceEnricher assigns rule identities, relative member locations, fingerprints, confidence, and aggregate scoring explanations. Incomplete source analysis retains findings but removes source-derived scores and returns exit code 2. CoverageReport attributes Cobertura line observations to matching production files and preserves unknown branch rates.

`WorkspaceDiagnostics` records the exact MSBuild design-time load in temporary binary logs because Roslyn 5.9 flattens MSBuild warning/error severity in workspace callbacks. Only callbacks matched to recorded warning events (and no matching error) become nonblocking `workspaceWarning` diagnostics. Explicit errors and unknown failures remain blocking. Logs are replayed with cancellation and removed after analysis; the evidence schema and CSV columns are unchanged.
