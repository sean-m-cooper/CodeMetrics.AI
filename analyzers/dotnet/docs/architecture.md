# CodeMetrics.AI architecture

CodeMetrics.AI is a deterministic, read-only analyzer. It loads a .NET solution or explicit project with Roslyn, collects production metrics, runs nine independent scorecard probes, and writes CSV metrics plus schema-v3 JSON evidence.

## Analysis flow

1. `Program.cs` parses command-line options and passes cancellation to the analysis pipeline.
2. `SolutionScope` reads solution build mappings with SolutionPersistence. `Program` loads the solution or project with the selected MSBuild configuration (Any CPU). Project mode scores only the selected project while keeping references in the workspace for semantic resolution. `SolutionCompilationLoader` classifies enabled projects, bounds concurrent compilations to four, and records missing compilations and compiler errors. Disabled projects remain explicit filter entries and are excluded from both source compilation diagnostics and package checks; enabled production compilation errors remain blocking.
3. `MetricsCollector` groups included production declarations by Roslyn type symbol within each project/framework compilation. Partial declarations contribute to one logical type: member aggregates are calculated once, partial member definitions/implementations count once, and raw/structural coupling uses the union of dependencies. Generated/excluded declarations are not imported through symbol references. Source-line counts retain physical declaration overhead. A deterministic representative file and the full included source-file list retain provenance; component hotspot samples expose the logical identity and files.
4. Each class in `Probes/` evaluates one scorecard dimension. Probes receive immutable metric or compilation inputs and return a `DimensionResult`.
5. `CsvWriter` and `EvidenceWriter` persist the two public output formats.

The dependency probe is the only probe that invokes external commands or reads package feeds. Those operations are bounded, cancellable, and degrade to explicit failed or unknown evidence rather than silently inventing compatibility.

Dependency subprocesses remove the MSBuild paths installed by the analyzer's locator and resolve their own SDK from the repository working directory. When a solution excludes projects from its build, package commands receive a disposable solution containing only enabled projects. Static dependency checks use the same selected paths. Source architecture cycles use production paths, and testing aggregates a union of unique source sites across target frameworks. Test/support/sample exclusions do not remove enabled package dependencies from the dependency assessment.

## Important boundaries

- `Metrics/` owns Roslyn metric calculation. Raw Roslyn-compatible class coupling remains available even when architecture scoring uses structural coupling.
- `Probes/` owns findings and score ladders. A probe should report observable evidence; it should not mutate the analyzed solution.
- `Output/` owns serialization only. Evidence shape changes must remain compatible with `shared/scorecard-schema/evidence.schema.v3.json`.
- `Rules/rules.json` owns permanent CMAI aliases, descriptions and annotation capabilities. The package embeds the catalog and ships JSON/Markdown copies. `code-metrics rules` serves the installed version without loading a solution. Findings retain their existing rule IDs and fingerprints while exposing codes in observations; dimensions reference metric-only entries separately. New rules must be cataloged and assigned unused codes; do not renumber or reuse existing codes. Regenerate `Rules/rules.md` with the CLI after editing the catalog. Consumers must check package version and catalog version before presenting annotation guidance.
- Tests exercise probes with in-memory Roslyn compilations where possible. End-to-end tests are reserved for solution loading, command execution, and output contracts.

## Design invariants

- Scores must be deterministic for the same source, configuration, analyzer version, and package-source responses.
- Error-handling penalties count distinct rule/severity/source-span findings, not repeated project/framework observations. Shared authored files count once; different files, source spans and rules remain distinct. Each finding retains affected project/framework labels and original messages. Missing file identity is conservatively left unmerged. This counting policy currently applies to error handling; it does not change type-metric populations or package observations in other dimensions.
- Error handling recognizes exception-bearing returns and invoked error delegates alongside logging/rethrow. Roslyn must resolve the caught exception symbol and the invoked factory/constructor/delegate; a reassigned catch variable does not qualify. Returned factories, constructors, object initializers and tuples can carry the exception, including returned factory wrappers. An outcome stored by a direct catch statement and returned through a local or an operation returning that same outcome type also qualifies when no intervening assignment or ref/out use is found. Void delegates and awaited delegate invocations receiving that exception are handling paths and do not require an ILogger member. Discarded results, unrelated exceptions and deferred lambda/local-function bodies within the catch are not evidence of handling. Existing recognition of captured exceptions logged by callbacks registered after the catch is preserved. These are bounded static observations, not proof that a result factory retains the exception or that an optional callback runs on every path. Exception aliases, custom reporting methods and interprocedural mutations remain review cases.
- Splitting a type into partial files must preserve its member complexity, decomposition, maintainability and coupling measurements. Physical source-line counts can still change with declaration headers and formatting. Same-named nested/generic types and different project/framework instances remain separate; CSV membership uses logical identity, not a short-name join.
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
