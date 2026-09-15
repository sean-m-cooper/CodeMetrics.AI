# Public-corpus evidence-fidelity corrections

Verified 2026-09-14 against the same four source commits and Release configurations as the [released 2.3.0 baseline](dotnet-public-corpus-wave2-2.3.0.md). This is an **unpublished local build**, still reporting tool version 2.3.0, using the distinct ruleset `dotnet-2026-09-14-evidence-fidelity`. The skill helper installed the explicitly supplied local NuGet package in its content-addressed E: cache and validated every run with the packaged schema-v3 inspector.

## Outcome

All four final invocations completed with analyzer/validator exit 0, usable inspections, matching fresh run/audit IDs, no failed dimensions, and complete dependency compatibility assessment. Dapper and Quartz.NET now have valid scorecards. Serilog and FluentValidation receive credit for documentation already present in the unchanged source.

All four raw CSV files are **byte-for-byte identical** to their released-baseline files. Pinned commits, source changes (none), populations, filters and entry points were verified. Configuration fingerprints were checked against their recorded inputs: Dapper changes because this fingerprint includes dependency status, now scored instead of failed; the other three remain identical. This is an assessment-status change, not an input-configuration edit. Seven dimensions outside Documentation and Dependency Management are identical, including findings and scoring decisions, for both previously usable scorecards. Dependency feeds were queried afresh. These are evidence-collection fixes, not improvements to the target code or changes to numeric score ladders. Rulesets differ, so these snapshots are not interchangeable baseline-gate inputs.

| Dimension | Dapper | Serilog | FluentValidation | Quartz.NET (4.x main) |
|---|---:|---:|---:|---:|
| Architecture & SOLID | 5.2 | 6.3 | 5.4 | 4.5 |
| Complexity & Decomposition | 5.9 | 6 | 4.8 | 5.6 |
| Testing | 6 | 10 | 6 | 6 |
| Security | 0 | 10 | 10 | 2 |
| Error Handling | 4 | 9.2 | 10 | 4 |
| Documentation | 6 | 6 | 8 | 6 |
| Dependency Management | 0 | 4 | 4 | 4 |
| Performance & Async | 2 | 10 | 10 | 0 |
| Maintainability | 7.7 | 8 | 7.5 | 7.1 |
| **Overall, partial assessment (9/9)** | 4.1 | 7.7 | 7.3 | 4.4 |

Scores are deterministic within partial static scopes. No coverage reports, runtime measurements or target test-suite runs were used. Testing measures source signals; Security includes static patterns and available vulnerability observations. The earlier Serilog native AOT linker limitation remains: its core library builds across all seven TFMs, but its full native test app could not link. Quartz.NET is its 4.x development branch.

| C&D component | Dapper | Serilog | FluentValidation | Quartz.NET (4.x main) |
|---|---:|---:|---:|---:|
| Method complexity | 5.8 | 7.3 | 5.6 | 5.8 |
| Decomposition | 6 | 4.7 | 4 | 5.3 |

## Corrections and regression boundaries

### 1. Package layout

Nerdbank.GitVersioning contains `build/MSBuildCore`, `build/MSBuildFull` and `build/runtimes`; linq2db.SqlServer contains architecture-specific scaffolding tool folders. Those directories describe tool hosts or processors rather than consumer TFMs. The bounded exclusion now covers those names under build/tool roots. Framework-specific lib/ref/runtime assets and dependency groups remain authoritative. Unknown framework names stay unknown; missing downloads and unsupported metadata still fail assessment. Tool-host runtime/CPU support and upgrade safety are not established by consumer-framework compatibility.

The first candidate removed the scaffolding failure but still rejected Nerdbank's nested `runtimes` directory. That unsuccessful run remains saved. A new regression covers that layout, and the final Dapper run resolves every candidate observation. No dependency checks were skipped.

### 2. Compiler diagnostic fidelity

Quartz exposed two independent differences from a normal build. Project diagnostic suppressors remove six CS8618 initialization warnings; the raw Roslyn diagnostic call had never applied those suppressors. Ten CS4014 diagnostics arise from source references to async methods in another project, whereas the normal build binds to assembly metadata. A minimal two-project fixture reproduces that discrepancy.

The diagnostic pipeline now applies project suppressors with project analyzer options. For promoted warnings involving source references, it creates fresh metadata-only references in memory and checks against that build boundary. Emitted metadata is cached only for the current load. Source references remain in metrics and probe inputs, and stale bin/obj assemblies are never used to dismiss errors. Tests retain genuine compilation errors, unsuppressed promoted warnings, same-project and async-caller CS4014 warnings, disabled suppressors, failed suppressors and failed metadata emission. Errors in enabled tests remain blocking because those compilations feed test analysis; there is no blanket test exclusion.

The first candidate removed only the six initialization diagnostics. That failed intermediate run is retained. The final Quartz invocation has no compilation diagnostics. Matching normal-build diagnostics does not prove discarded async results are behaviorally safe; runtime correctness remains a separate review question.

### 3. Documentation discovery

The probe now checks a case-insensitive README.md at the repository root, .github, then docs, with explicit precedence and no double-counting of a selected docs README's stale markers. Loaded compiler `/doc` settings determine XML documentation enablement, and evaluated output kind determines library/executable classification. Imported props/targets and configuration overrides therefore override raw project-file text.

FluentValidation's .github README and XML documentation were already present: Documentation changes from 3 to 8. Serilog's imported XML documentation setting changes Documentation from 4 to 6. README size thresholds, documentation deductions, C&D/MI formulas, dependency ladders and all other numerical scoring policies are unchanged. The CLI regression verifies imported XML documentation enabled in Release and explicitly disabled in Debug.

Implementation: [package inspection](E:/repos/CodeMetrics.AI/analyzers/dotnet/src/CodeMetrics.AI/Probes/PackageAssetInspector.cs), [compiler diagnostics](E:/repos/CodeMetrics.AI/analyzers/dotnet/src/CodeMetrics.AI/SolutionCompilationDiagnostics.cs), [fresh metadata references](E:/repos/CodeMetrics.AI/analyzers/dotnet/src/CodeMetrics.AI/CompilationReferenceMetadata.cs), [documentation probe](E:/repos/CodeMetrics.AI/analyzers/dotnet/src/CodeMetrics.AI/Probes/DocumentationProbe.cs).

Conventions and API references: [NuGet build assets](https://learn.microsoft.com/en-us/nuget/concepts/msbuild-props-and-targets), [Roslyn diagnostic suppressors](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.diagnosticsuppressor), [Roslyn metadata-only emit](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.emit.emitoptions).

## Validation and package identity

- 998 .NET tests passed, including 27 added regression cases and the expanded CLI configuration case.
- Six calibration fixtures passed with identical scores/findings; only the expected ruleset identifier changed.
- Full formatting verification and git whitespace checks passed.
- All four final scorecards used the same packaged DLL; its bytes match the tested Release output.
- Package SHA256: `d2ca3e8d8e25b045b955ea62fbcb4d6e2e56b224c86fdc96373a00ec539ed101`.
- Base checkout commit: `edc293f764d8cd2800fcc977995f4ac29f97a49a`; implementation remains local/uncommitted. Changed-source SHA256 values are recorded in the machine-readable verification snapshot.
- [Test log](E:/repos/CodeMetrics.AI/TestResults/public-corpus-wave2/fidelity-final-tests.log), [calibration report](E:/repos/CodeMetrics.AI/TestResults/public-corpus-wave2/fidelity-final-calibration.json), [format check](E:/repos/CodeMetrics.AI/TestResults/public-corpus-wave2/fidelity-final-format-verify.log).

## Final run evidence

### Dapper

- Source: [8becae8d0e2b](https://github.com/DapperLib/Dapper/tree/8becae8d0e2b360165ae03c0d5d1330b0273473d); `Dapper.slnx`, Release.
- runId `10ffa8c9-bfa7-400b-bef2-2d62850f1b27`; auditId `484b526c-ef83-4528-bcf8-d493bc15ebb3`.
- Baseline runId `9a991b6a-8512-4c4a-bc4a-bd356442f93c`; auditId `7c6bfc93-70c6-43dd-9e1f-0fad6656c258`.
- Status complete; analyzer/validator 0/0; inspection usable; 9/9 dimensions scored.
- Units 20/25; 555 type observations / 7222 member observations (not necessarily unique source counts).
- Configuration fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; calibration `baseline`; suppressions 0.
- [evidence](E:/repos/Dapper/.scorecard/dotnet/runs/10ffa8c9-bfa7-400b-bef2-2d62850f1b27/evidence.json), [inspection](E:/repos/Dapper/.scorecard/dotnet/runs/10ffa8c9-bfa7-400b-bef2-2d62850f1b27/inspection.json), [metrics](E:/repos/Dapper/.scorecard/dotnet/runs/10ffa8c9-bfa7-400b-bef2-2d62850f1b27/metrics.csv).

| Dimension | Score | Scope includes | Recorded basis |
|---|---:|---|---|
| Architecture & SOLID | 5.2 | static-coupling-and-project-structure | Findings: 43 (errors: 0, warnings: 43). Cycles: 0, hotspots: 43 (showing 10). Excluded passive data carriers: 30, DI extension types: 0, framework coupling archetypes: 0, application composition roots: 0. coupling: 35/525 eligible types, score 5.20; complexity: 0/525 eligible types, score 10.00; size: 8/525 eligible types, score 5.82. Final = min(metric score 5.20, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | 5.9 | production-type-complexity, member-complexity | Eligible types: 429. Passive data carriers excluded: 30. Method complexity: 5.8, Decomposition: 6. Combined: 5.9. |
| Testing | 6 | test-project-signals, supplied-coverage-report | testProjects=1, testMethods=389, skipped=1, placeholders=0, assertions=1228, assertionDensity=3.16, uncoveredProjects=7, coverageFile=False, lineRate=n/a. |
| Security | 0 | static-security-patterns, dependency-vulnerability-observations | Findings: 8 (errors: 8, warnings: 0). hardcodedSecrets=8, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=5. |
| Error Handling | 4 | static-exception-patterns | Distinct source findings: 15 across 70 project/framework observations (errors: 6, warnings: 9). emptyCatch=4, throwEx=0, broadDefaults=2. Affected catches: 8/19; documented catches: 9. |
| Documentation | 6 | documentation-presence-and-content-signals | hasReadme=True, readmeNonBlankLines=373, hasDocsDir=True, architectureDocs=0, hasAiInstructions=False, libraryXmlDocRatio=0.85, publicApiDocCoverage=0.90, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | 0 | package-version-and-feed-observations | vulnerableDirect=2, vulnerableTransitive=3, outdated=119, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=4, outdatedFrameworkCompatibilityUnknown=0, deprecated=7, unsupportedTFMs=1, versionDrift=0, cpmEnabled=True, anyCommandFailed=False. |
| Performance & Async | 2 | static-async-and-blocking-patterns | Distinct source findings: 6 across 33 project/framework observations (errors: 2, warnings: 0, unscored review leads: 4). syncOverAsync=2, threadSleep=0, saveChangesInsideLoop=0, missingCancellationToken=4, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | 7.7 | production-executable-function-maintainability-index | Distinct executable functions: 949. Weakest 190: 4.29; remaining 759: 9.93. Each function contributes once; distribution statistics are diagnostic only. |

Compatibility: 123/123 observations across 25 unique package/version lookups; 0 failures.

Nonblocking diagnostics retained:

- Found project reference without a matching metadata reference: E:\repos\Dapper\Dapper.StrongName\Dapper.StrongName.csproj
- Duplicate source file 'E:\repos\Dapper\Dapper\PublicAPI.Shipped.txt' in project 'E:\repos\Dapper\Dapper\Dapper.csproj'
- Duplicate source file 'E:\repos\Dapper\Dapper\PublicAPI.Unshipped.txt' in project 'E:\repos\Dapper\Dapper\Dapper.csproj'
- Found project reference without a matching metadata reference: E:\repos\Dapper\Dapper\Dapper.csproj
- Msbuild failed when processing the file 'E:\repos\Dapper\benchmarks\Dapper.Tests.Performance\Dapper.Tests.Performance.csproj' with message: Package 'System.Security.Cryptography.Xml' 10.0.8 has a known high severity vulnerability, https://github.com/advisories/GHSA-23rf-6693-g89p
- Msbuild failed when processing the file 'E:\repos\Dapper\benchmarks\Dapper.Tests.Performance\Dapper.Tests.Performance.csproj' with message: Package 'System.Security.Cryptography.Xml' 10.0.8 has a known high severity vulnerability, https://github.com/advisories/GHSA-8q5v-6pqq-x66h
- Msbuild failed when processing the file 'E:\repos\Dapper\benchmarks\Dapper.Tests.Performance\Dapper.Tests.Performance.csproj' with message: Package 'System.Security.Cryptography.Xml' 10.0.8 has a known high severity vulnerability, https://github.com/advisories/GHSA-cvvh-rhrc-wg4q
- Msbuild failed when processing the file 'E:\repos\Dapper\benchmarks\Dapper.Tests.Performance\Dapper.Tests.Performance.csproj' with message: Package 'System.Security.Cryptography.Xml' 10.0.8 has a known high severity vulnerability, https://github.com/advisories/GHSA-g8r8-53c2-pm3f
- Msbuild failed when processing the file 'E:\repos\Dapper\benchmarks\Dapper.Tests.Performance\Dapper.Tests.Performance.csproj' with message: Package 'System.Security.Cryptography.Xml' 10.0.8 has a known high severity vulnerability, https://github.com/advisories/GHSA-mmjf-rqrv-855v
- Msbuild failed when processing the file 'E:\repos\Dapper\tests\Dapper.Tests\Dapper.Tests.csproj' with message: Package 'SQLitePCLRaw.lib.e_sqlite3' 2.1.11 has a known high severity vulnerability, https://github.com/advisories/GHSA-2m69-gcr7-jv3q

### Serilog

- Source: [bebc7719004f](https://github.com/serilog/serilog/tree/bebc7719004f76187ae72e64ce138ec2540f2070); `Serilog.sln`, Release.
- runId `400d7095-ff58-4c2b-841f-f9d58f7e2cde`; auditId `ea2b05c9-3f6a-48b9-8029-e162a669d995`.
- Baseline runId `daf9ef96-ef03-4b9a-9275-070116a67668`; auditId `992d791b-92ed-42da-869b-4789b75675b4`.
- Status complete; analyzer/validator 0/0; inspection usable; 9/9 dimensions scored.
- Units 7/19; 790 type observations / 6812 member observations (not necessarily unique source counts).
- Configuration fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; calibration `baseline`; suppressions 0.
- [evidence](E:/repos/serilog/.scorecard/dotnet/runs/400d7095-ff58-4c2b-841f-f9d58f7e2cde/evidence.json), [inspection](E:/repos/serilog/.scorecard/dotnet/runs/400d7095-ff58-4c2b-841f-f9d58f7e2cde/inspection.json), [metrics](E:/repos/serilog/.scorecard/dotnet/runs/400d7095-ff58-4c2b-841f-f9d58f7e2cde/metrics.csv).

| Dimension | Score | Scope includes | Recorded basis |
|---|---:|---|---|
| Architecture & SOLID | 6.3 | static-coupling-and-project-structure | Findings: 63 (errors: 0, warnings: 63). Cycles: 0, hotspots: 63 (showing 10). Excluded passive data carriers: 21, DI extension types: 0, framework coupling archetypes: 0, application composition roots: 0. coupling: 56/769 eligible types, score 6.33; complexity: 0/769 eligible types, score 10.00; size: 7/769 eligible types, score 9.78. Final = min(metric score 6.33, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | 6 | production-type-complexity, member-complexity | Eligible types: 615. Passive data carriers excluded: 21. Method complexity: 7.3, Decomposition: 4.7. Combined: 6. |
| Testing | 10 | test-project-signals, supplied-coverage-report | testProjects=3, testMethods=437, skipped=0, placeholders=0, assertions=825, assertionDensity=1.89, uncoveredProjects=0, coverageFile=False, lineRate=n/a. |
| Security | 10 | static-security-patterns, dependency-vulnerability-observations | Findings: 0 (errors: 0, warnings: 0). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | 9.2 | static-exception-patterns | Distinct source findings: 5 across 26 project/framework observations (errors: 0, warnings: 4). emptyCatch=0, throwEx=0, broadDefaults=0. Affected catches: 3/19; documented catches: 4. |
| Documentation | 6 | documentation-presence-and-content-signals | hasReadme=True, readmeNonBlankLines=84, hasDocsDir=False, architectureDocs=0, hasAiInstructions=False, libraryXmlDocRatio=1.00, publicApiDocCoverage=1.00, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | 4 | package-version-and-feed-observations | vulnerableDirect=0, vulnerableTransitive=0, outdated=65, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=1, outdatedFrameworkCompatibilityUnknown=0, deprecated=9, unsupportedTFMs=1, versionDrift=1, cpmEnabled=False, anyCommandFailed=False. |
| Performance & Async | 10 | static-async-and-blocking-patterns | Distinct source findings: 1 across 7 project/framework observations (errors: 0, warnings: 0, unscored review leads: 1). syncOverAsync=1, threadSleep=0, saveChangesInsideLoop=0, missingCancellationToken=0, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | 8 | production-executable-function-maintainability-index | Distinct executable functions: 871. Weakest 175: 5.01; remaining 696: 9.97. Each function contributes once; distribution statistics are diagnostic only. |

Compatibility: 66/66 observations across 14 unique package/version lookups; 0 failures.

### FluentValidation

- Source: [fa9787a0d4fd](https://github.com/FluentValidation/FluentValidation/tree/fa9787a0d4fd0c0b99f9682292d76d5ad97ba6cb); `FluentValidation.sln`, Release.
- runId `087154fb-24ba-48c4-9b74-efb1f52a6bdf`; auditId `82d3b781-15ae-4914-8e07-10d59e2fb8b3`.
- Baseline runId `c84a5ac7-0c35-4ee7-a6f9-535703c5b941`; auditId `a820df20-6d4d-4fbe-b3c0-52cceb0ead6d`.
- Status complete; analyzer/validator 0/0; inspection usable; 9/9 dimensions scored.
- Units 2/6; 196 type observations / 1017 member observations (not necessarily unique source counts).
- Configuration fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; calibration `baseline`; suppressions 0.
- [evidence](E:/repos/FluentValidation/.scorecard/dotnet/runs/087154fb-24ba-48c4-9b74-efb1f52a6bdf/evidence.json), [inspection](E:/repos/FluentValidation/.scorecard/dotnet/runs/087154fb-24ba-48c4-9b74-efb1f52a6bdf/inspection.json), [metrics](E:/repos/FluentValidation/.scorecard/dotnet/runs/087154fb-24ba-48c4-9b74-efb1f52a6bdf/metrics.csv).

| Dimension | Score | Scope includes | Recorded basis |
|---|---:|---|---|
| Architecture & SOLID | 5.4 | static-coupling-and-project-structure | Findings: 9 (errors: 0, warnings: 9). Cycles: 0, hotspots: 9. Excluded passive data carriers: 2, DI extension types: 1, framework coupling archetypes: 0, application composition roots: 0. coupling: 9/193 eligible types, score 5.44; complexity: 0/193 eligible types, score 10.00; size: 0/193 eligible types, score 10.00. Final = min(metric score 5.44, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | 4.8 | production-type-complexity, member-complexity | Eligible types: 142. Passive data carriers excluded: 2. Method complexity: 5.6, Decomposition: 4. Combined: 4.8. |
| Testing | 6 | test-project-signals, supplied-coverage-report | testProjects=1, testMethods=828, skipped=1, placeholders=0, assertions=1319, assertionDensity=1.59, uncoveredProjects=1, coverageFile=False, lineRate=n/a. |
| Security | 10 | static-security-patterns, dependency-vulnerability-observations | Findings: 0 (errors: 0, warnings: 0). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | 10 | static-exception-patterns | Distinct source findings: 0 across 0 project/framework observations (errors: 0, warnings: 0). emptyCatch=0, throwEx=0, broadDefaults=0. Affected catches: 0/4; documented catches: 1. |
| Documentation | 8 | documentation-presence-and-content-signals | hasReadme=True, readmeNonBlankLines=50, hasDocsDir=True, architectureDocs=0, hasAiInstructions=False, libraryXmlDocRatio=1.00, publicApiDocCoverage=0.62, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | 4 | package-version-and-feed-observations | vulnerableDirect=0, vulnerableTransitive=0, outdated=25, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=0, outdatedFrameworkCompatibilityUnknown=0, deprecated=3, unsupportedTFMs=0, versionDrift=0, cpmEnabled=False, anyCommandFailed=False. |
| Performance & Async | 10 | static-async-and-blocking-patterns | Distinct source findings: 0 across 0 project/framework observations (errors: 0, warnings: 0, unscored review leads: 0). syncOverAsync=0, threadSleep=0, saveChangesInsideLoop=0, missingCancellationToken=0, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | 7.5 | production-executable-function-maintainability-index | Distinct executable functions: 852. Weakest 171: 3.79; remaining 681: 9.92. Each function contributes once; distribution statistics are diagnostic only. |

Compatibility: 25/25 observations across 11 unique package/version lookups; 0 failures.

### Quartz.NET (4.x main)

- Source: [97afe142210e](https://github.com/quartznet/quartznet/tree/97afe142210e9c616b434ec7d617286a2752e9d4); `Quartz.slnx`, Release.
- runId `dece046a-3e66-4ea8-a6a9-96d29ae93d42`; auditId `aee899b0-416f-43e0-8a1e-099a9c9545af`.
- Baseline runId `f2791085-aaf6-4917-81bb-622d27e5babb`; auditId `a98f087b-ebe3-4ed8-83c4-4bd4c1b371e9`.
- Status complete; analyzer/validator 0/0; inspection usable; 9/9 dimensions scored.
- Units 21/30; 988 type observations / 9098 member observations (not necessarily unique source counts).
- Configuration fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; calibration `baseline`; suppressions 0.
- [evidence](E:/repos/quartznet/.scorecard/dotnet/runs/dece046a-3e66-4ea8-a6a9-96d29ae93d42/evidence.json), [inspection](E:/repos/quartznet/.scorecard/dotnet/runs/dece046a-3e66-4ea8-a6a9-96d29ae93d42/inspection.json), [metrics](E:/repos/quartznet/.scorecard/dotnet/runs/dece046a-3e66-4ea8-a6a9-96d29ae93d42/metrics.csv).

| Dimension | Score | Scope includes | Recorded basis |
|---|---:|---|---|
| Architecture & SOLID | 4.5 | static-coupling-and-project-structure | Findings: 112 (errors: 0, warnings: 112). Cycles: 0, hotspots: 112 (showing 10). Excluded passive data carriers: 156, DI extension types: 4, framework coupling archetypes: 1, application composition roots: 6. coupling: 105/821 eligible types, score 4.47; complexity: 1/828 eligible types, score 9.96; size: 6/828 eligible types, score 5.91. Final = min(metric score 4.47, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | 5.6 | production-type-complexity, member-complexity | Eligible types: 685. Passive data carriers excluded: 156. Method complexity: 5.8, Decomposition: 5.3. Combined: 5.6. |
| Testing | 6 | test-project-signals, supplied-coverage-report | testProjects=3, testMethods=4355, skipped=0, placeholders=0, assertions=10502, assertionDensity=2.41, uncoveredProjects=20, coverageFile=False, lineRate=n/a. |
| Security | 2 | static-security-patterns, dependency-vulnerability-observations | Findings: 7 (errors: 7, warnings: 0). hardcodedSecrets=1, rawSql=6, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | 4 | static-exception-patterns | Distinct source findings: 356 across 356 project/framework observations (errors: 24, warnings: 123). emptyCatch=10, throwEx=0, broadDefaults=14. Affected catches: 62/352; documented catches: 101. |
| Documentation | 6 | documentation-presence-and-content-signals | hasReadme=True, readmeNonBlankLines=41, hasDocsDir=True, architectureDocs=0, hasAiInstructions=True, libraryXmlDocRatio=0.77, publicApiDocCoverage=1.00, staleMarkers=0, unresolvedCrefs=2. |
| Dependency Management | 4 | package-version-and-feed-observations | vulnerableDirect=0, vulnerableTransitive=0, outdated=79, outdatedAspireExcluded=12, outdatedFrameworkIncompatibleExcluded=0, outdatedFrameworkCompatibilityUnknown=0, deprecated=0, unsupportedTFMs=0, versionDrift=0, cpmEnabled=True, anyCommandFailed=False. |
| Performance & Async | 0 | static-async-and-blocking-patterns | Distinct source findings: 74 across 74 project/framework observations (errors: 67, warnings: 0, unscored review leads: 7). syncOverAsync=70, threadSleep=0, saveChangesInsideLoop=0, missingCancellationToken=0, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=4, sharedStateMutationInFanOut=0. |
| Maintainability | 7.1 | production-executable-function-maintainability-index | Distinct executable functions: 6849. Weakest 1370: 3.49; remaining 5479: 9.56. Each function contributes once; distribution statistics are diagnostic only. |

Compatibility: 79/79 observations across 28 unique package/version lookups; 0 failures.

## Interpretation limits

Source review confirmed a Dapper secret-detection false positive: all eight hardcoded-secret observations identify the same `LiteralTokensPattern` constant at [CompiledRegex.cs:14](E:/repos/Dapper/Dapper/CompiledRegex.cs:14). It is a regular expression for SQL literal placeholders, consumed by GeneratedRegex and Regex construction, not a credential. The observations repeat across build variants. The five dependency vulnerability observations concern SQLitePCLRaw.lib.e_sqlite3 in tests and System.Security.Cryptography.Xml in benchmarks; they do not establish vulnerable dependencies in the shipped Dapper library. These are priorities for classification, source-identity and dependency-scope review. This change preserves their recorded scores and does not predict a corrected security score.

The newly available low scores identify review populations, not confirmed defects. Quartz's Performance & Async 0 is driven by 67 scored synchronous-wait observations; seven other performance findings are unscored review leads. Its security observations are static patterns, not imported vulnerabilities. These sites have not received a new source-by-source behavioral review in this change. Existing findings, including test/benchmark dependency advisories, remain in their exact evidence artifacts. No concurrency, architecture or dependency upgrade is prescribed solely from these scores.

Flat language dispatch in FluentValidation and Serilog's documented formatter tradeoff remain calibration observations from the released baseline. Their complexity scores were deliberately preserved. Any later change to their treatment should be an explicit product/calibration decision supported by new regression cases.

Machine-readable record: [dotnet-public-corpus-wave2-fidelity.json](dotnet-public-corpus-wave2-fidelity.json).
