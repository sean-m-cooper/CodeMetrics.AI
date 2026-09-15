# Public corpus, second wave: released 2.3.0 baseline

Evaluated 2026-09-14 using the published CodeMetrics.AI 2.3.0 package, schema 3, Release configuration, SDK 10.0.401. The installed executable reports source revision `01d3dc1767c85923d1bb0763cb23c4f005657677`. Ruleset: `dotnet-2026-09-14-dependency-availability`; calibration: `baseline` (regression fixtures, not empirical validation of these repositories). The released ai_tools code-scorecard helper selected its compatibility-manifest pin and validated evidence with codemetrics-ai 0.3.0.

All four repositories were freshly cloned under `E:/repos`; tools, caches, temporary files and artifacts stayed on E:. Their source commits remained unchanged. No analyzer rules, numerical thresholds, target sources or dependencies were changed. No test suite, runtime benchmark or coverage measurement was executed. There was one full scorecard invocation per repository; failed runs were retained, without CSV fallback or dependency-skipping retries.

## Results

Serilog and FluentValidation completed with usable inspections and all nine dimensions scored. Dapper failed dependency compatibility assessment; Quartz.NET failed source analysis. Every score from those failed audits is withheld here, including overall. A failed assessment is not a zero or a code-quality judgment.

Scores are deterministic within the partial scopes below. Overall values are **partial assessments, 9/9 dimensions scored**, calculated as the unweighted mean of the nine supplied scores, rounded to one decimal.

| Dimension | Dapper | Serilog | FluentValidation | Quartz.NET | Scope |
|---|---:|---:|---:|---:|---|
| Architecture & SOLID | Unavailable | 6.3 | 5.4 | Unavailable | Static coupling and project structure |
| Complexity & Decomposition | Unavailable | 6 | 4.8 | Unavailable | Production type/member complexity |
| Testing | Unavailable | 10 | 6 | Unavailable | Test signals; no coverage report |
| Security | Unavailable | 10 | 10 | Unavailable | Static patterns and dependency vulnerability observations |
| Error Handling | Unavailable | 9.2 | 10 | Unavailable | Static exception patterns |
| Documentation | Unavailable | 4 | 3 | Unavailable | Documentation presence and content signals |
| Dependency Management | Unavailable | 4 | 4 | Unavailable | Package/feed observations |
| Performance & Async | Unavailable | 10 | 10 | Unavailable | Static async/blocking patterns |
| Maintainability | Unavailable | 8 | 7.5 | Unavailable | Executable-function maintainability index |
| **Overall, partial assessment (9/9 for usable runs)** | Unavailable | 7.5 | 6.7 | Unavailable | Partial static assessment |

| C&D component | Serilog | FluentValidation |
|---|---:|---:|
| Method complexity | 7.3 | 5.6 |
| Decomposition | 4.7 | 4 |

The scopes exclude runtime behavior and comprehensive human review. Testing scores do not establish coverage or test correctness. Dependency observations include test, benchmark and build-tool packages; source filters do not mean those packages are absent from feed checks. Population counts below include project/framework observations and must not be described as unique source types or members.

## Repository and execution provenance

| Repository / branch | Pinned commit | Entry point | Restore / Release build |
|---|---|---|---|
| Dapper / `main` | [8becae8d0e2b](https://github.com/DapperLib/Dapper/tree/8becae8d0e2b360165ae03c0d5d1330b0273473d) | `Dapper.slnx` | Restore 0. Release solution build passed after fetching omitted historical Git blobs. 21 warnings; no source edits. |
| Serilog / `dev` | [bebc7719004f](https://github.com/serilog/serilog/tree/bebc7719004f76187ae72e64ce138ec2540f2070) | `Serilog.sln` | Restore 0. Library Release build passed all seven TFMs; full solution build failed only at AotTestApp native linking (C++ linker unavailable). |
| FluentValidation / `main` | [fa9787a0d4fd](https://github.com/FluentValidation/FluentValidation/tree/fa9787a0d4fd0c0b99f9682292d76d5ad97ba6cb) | `FluentValidation.sln` | Restore 0. Release solution build passed with zero warnings and errors. |
| Quartz.NET (4.x main) / `main` | [97afe142210e](https://github.com/quartznet/quartznet/tree/97afe142210e9c616b434ec7d617286a2752e9d4) | `Quartz.slnx` | Restore 0. Release solution build passed with zero warnings and errors. |

Dapper initially failed because Nerdbank.GitVersioning could not read historical blobs omitted by the filtered clone. `git fetch --refetch --no-filter origin` supplied them; the same commit then built. Its 21 build warnings include package advisory observations and documentation-generation suggestions in tests/benchmarks and SqlBuilder. Serilog's core library builds across all seven TFMs, but its full solution requires a native C++ linker for AotTestApp; native AOT validation remains unavailable. Quartz.NET is the current 4.x development branch, not its 3.x maintenance release.

Every inspection was checked against the run/audit IDs returned by its own fresh helper invocation, the requested entry point, Release variant, schema, tool version and ruleset. All four repositories still have the recorded HEAD and no tracked source changes; only `.scorecard/` is untracked.

### Dapper

- Helper: `failed`; analyzer exit 2; validator exit 2; inspection usable `false`; source analysis `complete`.
- runId: `9a991b6a-8512-4c4a-bc4a-bd356442f93c`; auditId: `7c6bfc93-70c6-43dd-9e1f-0fad6656c258`.
- Configuration fingerprint: `df6e6db6fd7474f40c9130e6bb1b2bd56d06b96f7d91c1c7ed5903e60b5eeeb2`; suppressions: 0.
- Units: 20/25 analyzed. Population: 555 type observations / 7222 member observations.
- Artifacts: [evidence](E:/repos/Dapper/.scorecard/dotnet/runs/9a991b6a-8512-4c4a-bc4a-bd356442f93c/evidence.json), [inspection](E:/repos/Dapper/.scorecard/dotnet/runs/9a991b6a-8512-4c4a-bc4a-bd356442f93c/inspection.json), [metrics](E:/repos/Dapper/.scorecard/dotnet/runs/9a991b6a-8512-4c4a-bc4a-bd356442f93c/metrics.csv).
- Excluded units: Dapper.Tests.Performance(net472) (Test project); Dapper.Tests.Performance(net10.0) (Test project); Dapper.Tests(net481) (Test project); Dapper.Tests(net8.0) (Test project); Dapper.Tests(net10.0) (Test project).

Dependency compatibility: `failed`, 96/123 observations resolved across 25 package/version lookups. Independent vulnerability assessment available: `true`.

- `Nerdbank.GitVersioning` 3.10.94: 25 affected observations; reasons `frameworkMetadataUnsupported`.
- `linq2db.SqlServer` 6.5.0: 2 affected observations; reasons `frameworkMetadataUnsupported`.

Diagnostics retained:

- `workspaceWarning`: Found project reference without a matching metadata reference: E:\repos\Dapper\Dapper.StrongName\Dapper.StrongName.csproj
- `workspaceWarning`: Duplicate source file 'E:\repos\Dapper\Dapper\PublicAPI.Shipped.txt' in project 'E:\repos\Dapper\Dapper\Dapper.csproj'
- `workspaceWarning`: Duplicate source file 'E:\repos\Dapper\Dapper\PublicAPI.Unshipped.txt' in project 'E:\repos\Dapper\Dapper\Dapper.csproj'
- `workspaceWarning`: Found project reference without a matching metadata reference: E:\repos\Dapper\Dapper\Dapper.csproj
- `workspaceWarning`: Msbuild failed when processing the file 'E:\repos\Dapper\benchmarks\Dapper.Tests.Performance\Dapper.Tests.Performance.csproj' with message: Package 'System.Security.Cryptography.Xml' 10.0.8 has a known high severity vulnerability, https://github.com/advisories/GHSA-23rf-6693-g89p
- `workspaceWarning`: Msbuild failed when processing the file 'E:\repos\Dapper\benchmarks\Dapper.Tests.Performance\Dapper.Tests.Performance.csproj' with message: Package 'System.Security.Cryptography.Xml' 10.0.8 has a known high severity vulnerability, https://github.com/advisories/GHSA-8q5v-6pqq-x66h
- `workspaceWarning`: Msbuild failed when processing the file 'E:\repos\Dapper\benchmarks\Dapper.Tests.Performance\Dapper.Tests.Performance.csproj' with message: Package 'System.Security.Cryptography.Xml' 10.0.8 has a known high severity vulnerability, https://github.com/advisories/GHSA-cvvh-rhrc-wg4q
- `workspaceWarning`: Msbuild failed when processing the file 'E:\repos\Dapper\benchmarks\Dapper.Tests.Performance\Dapper.Tests.Performance.csproj' with message: Package 'System.Security.Cryptography.Xml' 10.0.8 has a known high severity vulnerability, https://github.com/advisories/GHSA-g8r8-53c2-pm3f
- `workspaceWarning`: Msbuild failed when processing the file 'E:\repos\Dapper\benchmarks\Dapper.Tests.Performance\Dapper.Tests.Performance.csproj' with message: Package 'System.Security.Cryptography.Xml' 10.0.8 has a known high severity vulnerability, https://github.com/advisories/GHSA-mmjf-rqrv-855v
- `workspaceWarning`: Msbuild failed when processing the file 'E:\repos\Dapper\tests\Dapper.Tests\Dapper.Tests.csproj' with message: Package 'SQLitePCLRaw.lib.e_sqlite3' 2.1.11 has a known high severity vulnerability, https://github.com/advisories/GHSA-2m69-gcr7-jv3q

Package findings from independently completed feed checks (not a score for a failed audit):

| Category | Package / resolved version | Projects | Project/TFM occurrences | Frameworks |
|---|---|---:|---:|---|
| deprecatedDependency | xunit 2.9.3 | 1 | 3 | net10.0, net481, net8.0 |
| vulnerableTransitiveDependency | SQLitePCLRaw.lib.e_sqlite3 2.1.11 | 1 | 3 | net10.0, net481, net8.0 |
| deprecatedDependency | PetaPoco 5.1.306 | 1 | 2 | net10.0, net472 |
| deprecatedDependency | EntityFramework 6.1.3 | 2 | 2 | net461 |
| vulnerableDirectDependency | System.Security.Cryptography.Xml 10.0.8 | 1 | 2 | net10.0, net472 |

Exact project paths, deprecation alternatives, advisories and per-version grouping are retained in the machine-readable snapshot and original evidence. Compatibility with framework assets does not establish that an upgrade is safe. The xunit.v3 alternatives are migration candidates, not automatic replacements.

### Serilog

- Helper: `complete`; analyzer exit 0; validator exit 0; inspection usable `true`; source analysis `complete`.
- runId: `daf9ef96-ef03-4b9a-9275-070116a67668`; auditId: `992d791b-92ed-42da-869b-4789b75675b4`.
- Configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; suppressions: 0.
- Units: 7/19 analyzed. Population: 790 type observations / 6812 member observations.
- Artifacts: [evidence](E:/repos/serilog/.scorecard/dotnet/runs/daf9ef96-ef03-4b9a-9275-070116a67668/evidence.json), [inspection](E:/repos/serilog/.scorecard/dotnet/runs/daf9ef96-ef03-4b9a-9275-070116a67668/inspection.json), [metrics](E:/repos/serilog/.scorecard/dotnet/runs/daf9ef96-ef03-4b9a-9275-070116a67668/metrics.csv).
- Excluded units: TestDummies(netstandard2.0) (Test support / fixture); TestDummies(net462) (Test support / fixture); Serilog.Tests(net48) (Test project); Serilog.Tests(net462) (Test project); Serilog.Tests(net10.0) (Test project); Serilog.Tests(net9.0) (Test project); Serilog.Tests(net8.0) (Test project); Serilog.PerformanceTests(net10.0) (Test project); Serilog.PerformanceTests(net9.0) (Test project); Serilog.PerformanceTests(net8.0) (Test project); Serilog.ApprovalTests (Test project); AotTestApp (Test support / fixture).

| Dimension | Recorded basis |
|---|---|
| Architecture & SOLID | Findings: 63 (errors: 0, warnings: 63). Cycles: 0, hotspots: 63 (showing 10). Excluded passive data carriers: 21, DI extension types: 0, framework coupling archetypes: 0, application composition roots: 0. coupling: 56/769 eligible types, score 6.33; complexity: 0/769 eligible types, score 10.00; size: 7/769 eligible types, score 9.78. Final = min(metric score 6.33, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | Eligible types: 615. Passive data carriers excluded: 21. Method complexity: 7.3, Decomposition: 4.7. Combined: 6. |
| Testing | testProjects=3, testMethods=437, skipped=0, placeholders=0, assertions=825, assertionDensity=1.89, uncoveredProjects=0, coverageFile=False, lineRate=n/a. |
| Security | Findings: 0 (errors: 0, warnings: 0). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | Distinct source findings: 5 across 26 project/framework observations (errors: 0, warnings: 4). emptyCatch=0, throwEx=0, broadDefaults=0. Affected catches: 3/19; documented catches: 4. |
| Documentation | hasReadme=True, readmeNonBlankLines=84, hasDocsDir=False, architectureDocs=0, hasAiInstructions=False, libraryXmlDocRatio=0.00, publicApiDocCoverage=1.00, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | vulnerableDirect=0, vulnerableTransitive=0, outdated=65, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=1, outdatedFrameworkCompatibilityUnknown=0, deprecated=9, unsupportedTFMs=1, versionDrift=1, cpmEnabled=False, anyCommandFailed=False. |
| Performance & Async | Distinct source findings: 1 across 7 project/framework observations (errors: 0, warnings: 0, unscored review leads: 1). syncOverAsync=1, threadSleep=0, saveChangesInsideLoop=0, missingCancellationToken=0, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | Distinct executable functions: 871. Weakest 175: 5.01; remaining 696: 9.97. Each function contributes once; distribution statistics are diagnostic only. |

Dependency compatibility: `complete`, 66/66 observations resolved across 14 package/version lookups. Independent vulnerability assessment available: `true`.

Package findings from independently completed feed checks (not a score for a failed audit):

| Category | Package / resolved version | Projects | Project/TFM occurrences | Frameworks |
|---|---|---:|---:|---|
| deprecatedDependency | xunit 2.9.2 | 3 | 9 | net10.0, net462, net48, net8.0, net9.0 |

Exact project paths, deprecation alternatives, advisories and per-version grouping are retained in the machine-readable snapshot and original evidence. Compatibility with framework assets does not establish that an upgrade is safe. The xunit.v3 alternatives are migration candidates, not automatic replacements.

### FluentValidation

- Helper: `complete`; analyzer exit 0; validator exit 0; inspection usable `true`; source analysis `complete`.
- runId: `c84a5ac7-0c35-4ee7-a6f9-535703c5b941`; auditId: `a820df20-6d4d-4fbe-b3c0-52cceb0ead6d`.
- Configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; suppressions: 0.
- Units: 2/6 analyzed. Population: 196 type observations / 1017 member observations.
- Artifacts: [evidence](E:/repos/FluentValidation/.scorecard/dotnet/runs/c84a5ac7-0c35-4ee7-a6f9-535703c5b941/evidence.json), [inspection](E:/repos/FluentValidation/.scorecard/dotnet/runs/c84a5ac7-0c35-4ee7-a6f9-535703c5b941/inspection.json), [metrics](E:/repos/FluentValidation/.scorecard/dotnet/runs/c84a5ac7-0c35-4ee7-a6f9-535703c5b941/metrics.csv).
- Excluded units: FluentValidation.Tests(net8.0) (Test project); FluentValidation.Tests(net9.0) (Test project); FluentValidation.Tests(net10.0) (Test project); FluentValidation.Tests.Benchmarks (Test project).

| Dimension | Recorded basis |
|---|---|
| Architecture & SOLID | Findings: 9 (errors: 0, warnings: 9). Cycles: 0, hotspots: 9. Excluded passive data carriers: 2, DI extension types: 1, framework coupling archetypes: 0, application composition roots: 0. coupling: 9/193 eligible types, score 5.44; complexity: 0/193 eligible types, score 10.00; size: 0/193 eligible types, score 10.00. Final = min(metric score 5.44, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | Eligible types: 142. Passive data carriers excluded: 2. Method complexity: 5.6, Decomposition: 4. Combined: 4.8. |
| Testing | testProjects=1, testMethods=828, skipped=1, placeholders=0, assertions=1319, assertionDensity=1.59, uncoveredProjects=1, coverageFile=False, lineRate=n/a. |
| Security | Findings: 0 (errors: 0, warnings: 0). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | Distinct source findings: 0 across 0 project/framework observations (errors: 0, warnings: 0). emptyCatch=0, throwEx=0, broadDefaults=0. Affected catches: 0/4; documented catches: 1. |
| Documentation | hasReadme=False, readmeNonBlankLines=0, hasDocsDir=True, architectureDocs=0, hasAiInstructions=False, libraryXmlDocRatio=0.00, publicApiDocCoverage=0.62, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | vulnerableDirect=0, vulnerableTransitive=0, outdated=25, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=0, outdatedFrameworkCompatibilityUnknown=0, deprecated=3, unsupportedTFMs=0, versionDrift=0, cpmEnabled=False, anyCommandFailed=False. |
| Performance & Async | Distinct source findings: 0 across 0 project/framework observations (errors: 0, warnings: 0, unscored review leads: 0). syncOverAsync=0, threadSleep=0, saveChangesInsideLoop=0, missingCancellationToken=0, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | Distinct executable functions: 852. Weakest 171: 3.79; remaining 681: 9.92. Each function contributes once; distribution statistics are diagnostic only. |

Dependency compatibility: `complete`, 25/25 observations resolved across 11 package/version lookups. Independent vulnerability assessment available: `true`.

Package findings from independently completed feed checks (not a score for a failed audit):

| Category | Package / resolved version | Projects | Project/TFM occurrences | Frameworks |
|---|---|---:|---:|---|
| deprecatedDependency | xunit 2.2.0 | 1 | 3 | net10.0, net8.0, net9.0 |

Exact project paths, deprecation alternatives, advisories and per-version grouping are retained in the machine-readable snapshot and original evidence. Compatibility with framework assets does not establish that an upgrade is safe. The xunit.v3 alternatives are migration candidates, not automatic replacements.

### Quartz.NET (4.x main)

- Helper: `failed`; analyzer exit 2; validator exit 2; inspection usable `false`; source analysis `incomplete`.
- runId: `f2791085-aaf6-4917-81bb-622d27e5babb`; auditId: `a98f087b-ebe3-4ed8-83c4-4bd4c1b371e9`.
- Configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; suppressions: 0.
- Units: 21/30 analyzed. Population: 988 type observations / 9098 member observations.
- Artifacts: [evidence](E:/repos/quartznet/.scorecard/dotnet/runs/f2791085-aaf6-4917-81bb-622d27e5babb/evidence.json), [inspection](E:/repos/quartznet/.scorecard/dotnet/runs/f2791085-aaf6-4917-81bb-622d27e5babb/inspection.json), [metrics](E:/repos/quartznet/.scorecard/dotnet/runs/f2791085-aaf6-4917-81bb-622d27e5babb/metrics.csv).
- Excluded units: _build (Excluded by solution build configuration); Quartz.Documentation.Samples (Sample / demo code); Quartz.Examples.Aspire.AppHost (Aspire orchestration host); Quartz.Examples.FSharp (Reference outside selected solution); Quartz.Extensions.Hosting (Aspire / generic hosting); Quartz.Tests.AspNetCore (Test project); Quartz.Tests.Integration.Seeder (Test project); Quartz.Tests.Integration (Test project); Quartz.Tests.Unit (Test project).

Dependency compatibility: `complete`, 79/79 observations resolved across 28 package/version lookups. Independent vulnerability assessment available: `true`.

Diagnostics retained:

- `compilationError`: E:\repos\quartznet\src\Quartz.Tests.AspNetCore\HttpApi\TraceContextOverHttpTest.cs(37,44): error CS8618: Non-nullable field 'factory' must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring the field as nullable.
- `compilationError`: E:\repos\quartznet\src\Quartz.Tests.AspNetCore\HttpApi\TraceContextOverHttpTest.cs(38,29): error CS8618: Non-nullable field 'provider' must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring the field as nullable.
- `compilationError`: E:\repos\quartznet\src\Quartz.Tests.AspNetCore\HttpApi\TraceContextOverHttpTest.cs(39,24): error CS8618: Non-nullable field 'scheduler' must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring the field as nullable.
- `compilationError`: E:\repos\quartznet\src\Quartz.Tests.AspNetCore\HttpApi\TraceContextOverHttpTest.cs(40,30): error CS8618: Non-nullable field 'listener' must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring the field as nullable.
- `compilationError`: E:\repos\quartznet\src\Quartz.Tests.AspNetCore\HttpApi\TraceContextOverHttpTest.cs(41,20): error CS8618: Non-nullable field 'schedulerName' must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring the field as nullable.
- `compilationError`: E:\repos\quartznet\src\Quartz.Tests.AspNetCore\HttpApi\TraceContextOverHttpTest.cs(42,20): error CS8618: Non-nullable field 'jobKey' must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring the field as nullable.
- `compilationError`: E:\repos\quartznet\src\Quartz.Tests.Unit\Jobs\SendMailJobTest.cs(51,9): error CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
- `compilationError`: E:\repos\quartznet\src\Quartz.Tests.Unit\Jobs\SendMailJobTest.cs(80,9): error CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
- `compilationError`: E:\repos\quartznet\src\Quartz.Tests.Unit\Jobs\SendMailJobTest.cs(110,9): error CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
- `compilationError`: E:\repos\quartznet\src\Quartz.Tests.Unit\Jobs\SendMailJobTest.cs(143,9): error CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
- `compilationError`: E:\repos\quartznet\src\Quartz.Tests.Unit\Jobs\SendMailJobTest.cs(172,9): error CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
- `compilationError`: E:\repos\quartznet\src\Quartz.Tests.Unit\Jobs\SendMailJobTest.cs(217,9): error CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
- `compilationError`: E:\repos\quartznet\src\Quartz.Tests.Unit\Jobs\SendMailJobTest.cs(241,9): error CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
- `compilationError`: E:\repos\quartznet\src\Quartz.Tests.Unit\Jobs\SendMailJobTest.cs(253,9): error CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
- `compilationError`: E:\repos\quartznet\src\Quartz.Tests.Unit\Jobs\SendMailJobTest.cs(261,9): error CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.
- `compilationError`: E:\repos\quartznet\src\Quartz.Tests.Unit\Jobs\NativeJobStreamLoggingTest.cs(94,13): error CS4014: Because this call is not awaited, execution of the current method continues before the call is completed. Consider applying the 'await' operator to the result of the call.

## Next priorities for CodeMetrics.AI

1. **Package-layout parsing (Current context; confirmed parser limitation).** Dapper's compatibility failures involve Nerdbank.GitVersioning 3.10.94 and linq2db.SqlServer 6.5.0, affecting 25 and 2 project/TFM observations. Both archives were downloaded successfully for inspection. The former has `build/MSBuildCore/` and `build/MSBuildFull/`; the latter has scaffolding tools under `tools/arm64/`, `tools/x64/` and `tools/x86/`. [PackageAssetInspector.cs](E:/repos/CodeMetrics.AI/analyzers/dotnet/src/CodeMetrics.AI/Probes/PackageAssetInspector.cs:62) sends those directory names through the framework parser. Add bounded package-layout fixtures and distinguish target-framework assets from tool-host and architecture directories. Preserve genuine unknown compatibility and tool-host constraints; do not make every build/tool package universally compatible. The released fail-closed behavior correctly withheld scores.

2. **Compilation fidelity and excluded tests (Metrics + Current context; cause still under investigation).** Quartz.NET's Release solution build passes, but the analyzer records 16 promoted-warning compiler errors across two excluded test projects: six CS8618 initialization diagnostics and ten CS4014 unawaited-call diagnostics. The test project references include NUnit.Analyzers. Reconcile the analyzer's diagnostic pipeline with the real build, including diagnostic suppressors and exclusion policy, before changing any source or treating these as product defects. Source analysis, not NuGet, blocked this run: all 79 compatibility observations across 28 package/version lookups resolved. Both affected test projects also rebuilt successfully with `RunAnalyzers=false`; that switch did not reproduce the analyzer diagnostics and does not establish the precise cause. A final ordinary Release solution build again passed with zero warnings/errors. The captured logs preserve the clean builds and the analyzer discrepancy.

3. **Documentation discovery (Metrics + Current context).** FluentValidation records `hasReadme=false` despite [.github/README.md](E:/repos/FluentValidation/.github/README.md:18) containing installation instructions and a usage example, plus package READMEs and a docs tree. Serilog records `libraryXmlDocRatio=0.00` despite [Directory.Build.props](E:/repos/serilog/Directory.Build.props:14) enabling XML documentation through an imported property. Check evaluated MSBuild properties and recognized README locations. Add detection fixtures before considering scoring changes; neither mismatch establishes what the corrected score should be.

## Calibration observations, without changing policy

FluentValidation's C&D 4.8 comprises method complexity 5.6 and decomposition 4. Its worst function, [LanguageManager.GetTranslation](E:/repos/FluentValidation/src/FluentValidation/Resources/LanguageManager.cs:39), has own CC 62 and is a flat culture-to-language dispatch switch. A sampled [language translation method](E:/repos/FluentValidation/src/FluentValidation/Resources/Languages/KhmerLanguage.cs:28) is a key-to-string switch. LanguageManager also supplies the worst structural coupling value (61), helping limit Architecture to 5.4. These are measured branch and coupling counts; they do not establish tangled business logic or justify splitting responsibilities. Other decomposition review candidates include collection/property rule execution, so dispatch alone is not the entire C&D story.

Serilog's worst method, [JsonValueFormatter.FormatLiteralValue](E:/repos/serilog/src/Serilog/Formatting/Json/JsonValueFormatter.cs:173), has maximum own CC 22 across seven framework observations, counted as one source function. Its comment explicitly explains a performance rationale for type checks instead of dictionary lookup. That rationale was inspected this run; its performance claim was not benchmarked. This is a useful documented-tradeoff calibration case, not grounds for an automatic exemption or a recommendation to replace it with a dictionary.

Serilog's [BatchingSink.Dispose](E:/repos/serilog/src/Serilog/Core/Sinks/Batching/BatchingSink.cs:263) signals shutdown, waits for the run loop, reports disposal failures, then disposes the target sink. The analyzer groups seven framework observations into one `synchronousContract` review lead with no score penalty. Performance & Async remains 10 within its static scope; this does not claim all waits are cheap or all runtime performance is optimal.

Repair evidence-fidelity problems first. Preserve this unmodified release baseline when reviewing any later decision about dispatch-heavy code or documented tradeoffs.

## Saved records

- [Machine-readable snapshot](E:/repos/CodeMetrics.AI/shared/scorecard-schema/calibration-runs/dotnet-public-corpus-wave2-2.3.0.json)
- [Manifest and execution logs](E:/repos/CodeMetrics.AI/TestResults/public-corpus-wave2/manifest.json)
- [Provenance verification script](E:/repos/CodeMetrics.AI/TestResults/public-corpus-wave2/summarize.py)
- [Downloaded build-tool package metadata](E:/repos/CodeMetrics.AI/TestResults/public-corpus-wave2/nerdbank.gitversioning.3.10.94.nupkg.nuspec.xml)
- [Downloaded scaffolding package metadata](E:/repos/CodeMetrics.AI/TestResults/public-corpus-wave2/linq2db.sqlserver.6.5.0.nupkg.nuspec.xml)
