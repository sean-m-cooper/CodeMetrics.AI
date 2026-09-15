# Production scope and explicit intent verification

Fresh Release runs use local unpublished CodeMetrics.AI 2.3.0, schema 3, ruleset `dotnet-2026-09-14-scope-explicit-intent`. These snapshots follow the [population baseline](dotnet-public-corpus-wave2-populations.md) at identical source commits. Numerical ladders are unchanged. Scope/classification changes explain score differences; these snapshots are not compatible baseline gates and do not demonstrate target-code improvement.

## Scorecards

| Dimension | Dapper | Serilog | FluentValidation | Quartz.NET (4.x main) |
|---|---:|---:|---:|---:|
| Architecture & SOLID | 5.2 | 6.3 | 5.4 | 4.5 |
| Complexity & Decomposition | 5.9 | 6 | 4.8 | 5.6 |
| Testing | 6 | 10 | 6 | 6 |
| Security | 10 | 10 | 10 | 2 |
| Error Handling | 4 | 9.2 | 10 | 4 |
| Documentation | 6 | 6 | 8 | 6 |
| Dependency Management | 0 | 4 | 4 | 4 |
| Performance & Async | 2 | 10 | 10 | 2 |
| Maintainability | 7.7 | 8 | 7.5 | 7 |
| **Overall, partial assessment (9/9)** | 5.2 | 7.7 | 7.3 | 4.6 |
| Previous overall, partial assessment (9/9) | 5.2 | 7.7 | 7.3 | 4.4 |

All four analyzer/validator pairs exited 0 with complete, fresh, usable evidence. All dimensions use deterministic scores within their declared partial scope. No target test suites, coverage reports, benchmarks or runtime security checks were run. The earlier Serilog native AOT build limitation is prior context, not reverified here.

## Policy corrections

- Quartz.Benchmark is now excluded from production source metrics. Benchmark dependencies remain in Dependency Management. Retained-project CSV rows match the prior run exactly; only Quartz.Benchmark rows were removed. Other repositories retain identical raw CSV, filters and populations.
- Dapper terminal Task.Status switch branches now establish nonblocking Result access. The same source finding disappears from Performance & Async and Error Handling, while both scores remain unchanged because other findings determine them. This does not excuse fault/cancellation exceptions or general synchronous waits.
- Quartz production units fall from 21 to 20; type/member observations fall from 988/9,098 to 903/8,059. Performance & Async findings fall from 74 to 8, removing 66 scored benchmark waits; the score moves 0 to 2. Maintainability moves 7.1 to 7.0 because the benchmark types leave its population. This is a scope effect.
- No recognized anonymous-access annotations occur in these four production populations, so that policy has no corpus score effect here. Dedicated semantic regressions verify that multiple recognized declarations preserve a score of 10 while unrelated secrets still reduce it.
- Recognized anonymous-access annotations, including aliases and the ASP.NET Core metadata contract, are informational explicit intent. They do not lower Security or exempt unrelated security findings. The CMAI4005 catalog entry ships with this policy.
- [Scope and intent policy](../scope-explicit-intent.md) records naming, semantic recognition, opt-outs and proof limitations.

## Verification

- 1071 tests passed, including 39 new regression cases.
- Six calibration fixtures passed with unchanged scores/findings and the new ruleset identifier.
- Formatting and whitespace checks passed. Packaged DLL/catalog bytes match the tested Release output and source catalog.
- All four source commits and tracked target files remain unchanged.
- Package SHA256: `51bfa495a1d93c1b0f2bcea9185c57805034bee8fb5eafe4be0b05bb41afa229`.
- [Local package](E:/repos/CodeMetrics.AI/TestResults/public-corpus-wave2/scope-package/CodeMetrics.AI.2.3.0.nupkg).
- [Tests](E:/repos/CodeMetrics.AI/TestResults/public-corpus-wave2/scope-tests.log), [calibration](E:/repos/CodeMetrics.AI/TestResults/public-corpus-wave2/scope-calibration.json), [format verification](E:/repos/CodeMetrics.AI/TestResults/public-corpus-wave2/scope-format-verify.log).

## Run evidence

### Dapper

- Source [8becae8d0e2b](https://github.com/DapperLib/Dapper/tree/8becae8d0e2b360165ae03c0d5d1330b0273473d); `Dapper.slnx`, Release.
- runId `069be269-f6df-4fab-9218-d499e80e0b16`; auditId `3b342ea8-50b2-46c6-9704-54dcde1a13e7`.
- Previous runId `c7b496b1-3a00-437f-873a-c73be8699b26`; auditId `52c54a12-f012-4028-b2ec-babee750a504`.
- Configuration fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; calibration baseline; schema 3; complete, fresh and usable.
- 20/25 units analyzed; 555 type and 7222 member observations (not unique source counts).
- [evidence](E:/repos/Dapper/.scorecard/dotnet/runs/069be269-f6df-4fab-9218-d499e80e0b16/evidence.json), [inspection](E:/repos/Dapper/.scorecard/dotnet/runs/069be269-f6df-4fab-9218-d499e80e0b16/inspection.json), [metrics](E:/repos/Dapper/.scorecard/dotnet/runs/069be269-f6df-4fab-9218-d499e80e0b16/metrics.csv).

| Dimension | Score | Scope includes | Recorded basis |
|---|---:|---|---|
| Architecture & SOLID | 5.2 | static-coupling-and-project-structure | Findings: 43 (errors: 0, warnings: 43). Cycles: 0, hotspots: 43 (showing 10). Excluded passive data carriers: 30, DI extension types: 0, framework coupling archetypes: 0, application composition roots: 0. coupling: 35/525 eligible types, score 5.20; complexity: 0/525 eligible types, score 10.00; size: 8/525 eligible types, score 5.82. Final = min(metric score 5.20, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | 5.9 | production-type-complexity, member-complexity | Eligible types: 429. Passive data carriers excluded: 30. Method complexity: 5.8, Decomposition: 6. Combined: 5.9. |
| Testing | 6 | test-project-signals, supplied-coverage-report | testProjects=1, testMethods=389, skipped=1, placeholders=0, assertions=1228, assertionDensity=3.16, uncoveredProjects=7, coverageFile=False, lineRate=n/a. |
| Security | 10 | static-security-patterns, production-and-unknown-project-dependency-vulnerabilities | Findings: 0 distinct source sites from 0 observations (errors: 0, warnings: 0). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | 4 | static-exception-patterns | Distinct source findings: 14 across 62 project/framework observations (errors: 6, warnings: 8). emptyCatch=4, throwEx=0, broadDefaults=2. Affected catches: 8/19; documented catches: 9. |
| Documentation | 6 | documentation-presence-and-content-signals | hasReadme=True, readmeNonBlankLines=373, hasDocsDir=True, architectureDocs=0, hasAiInstructions=False, libraryXmlDocRatio=0.85, publicApiDocCoverage=0.90, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | 0 | package-version-and-feed-observations, production-and-development-project-dependencies | vulnerableDirect=1, vulnerableTransitive=1, outdated=25, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=4, outdatedFrameworkCompatibilityUnknown=0, deprecated=3, unsupportedTFMs=1, versionDrift=0, cpmEnabled=True, anyCommandFailed=False. |
| Performance & Async | 2 | static-async-and-blocking-patterns | Distinct source findings: 5 across 25 project/framework observations (errors: 1, warnings: 0, unscored review leads: 4). syncOverAsync=1, threadSleep=0, saveChangesInsideLoop=0, missingCancellationToken=4, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | 7.7 | production-executable-function-maintainability-index | Distinct executable functions: 949. Weakest 190: 4.29; remaining 759: 9.93. Each function contributes once; distribution statistics are diagnostic only. |

Vulnerability scope: {"policy": "production-and-unknown-projects-v1", "importedPackageVersions": 0, "excludedDevelopmentObservations": 5, "detailDimension": "dependencyManagement"}.

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

- Source [bebc7719004f](https://github.com/serilog/serilog/tree/bebc7719004f76187ae72e64ce138ec2540f2070); `Serilog.sln`, Release.
- runId `a63c3361-1cf7-4750-bf2e-6c7a1e903142`; auditId `6cf39ad9-b834-4d9e-ab1f-19e0e2b74af3`.
- Previous runId `46ebd3df-0133-4064-b5b0-f320f1739635`; auditId `6b890466-e3aa-4eb9-81b8-ef9daba011e2`.
- Configuration fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; calibration baseline; schema 3; complete, fresh and usable.
- 7/19 units analyzed; 790 type and 6812 member observations (not unique source counts).
- [evidence](E:/repos/serilog/.scorecard/dotnet/runs/a63c3361-1cf7-4750-bf2e-6c7a1e903142/evidence.json), [inspection](E:/repos/serilog/.scorecard/dotnet/runs/a63c3361-1cf7-4750-bf2e-6c7a1e903142/inspection.json), [metrics](E:/repos/serilog/.scorecard/dotnet/runs/a63c3361-1cf7-4750-bf2e-6c7a1e903142/metrics.csv).

| Dimension | Score | Scope includes | Recorded basis |
|---|---:|---|---|
| Architecture & SOLID | 6.3 | static-coupling-and-project-structure | Findings: 63 (errors: 0, warnings: 63). Cycles: 0, hotspots: 63 (showing 10). Excluded passive data carriers: 21, DI extension types: 0, framework coupling archetypes: 0, application composition roots: 0. coupling: 56/769 eligible types, score 6.33; complexity: 0/769 eligible types, score 10.00; size: 7/769 eligible types, score 9.78. Final = min(metric score 6.33, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | 6 | production-type-complexity, member-complexity | Eligible types: 615. Passive data carriers excluded: 21. Method complexity: 7.3, Decomposition: 4.7. Combined: 6. |
| Testing | 10 | test-project-signals, supplied-coverage-report | testProjects=3, testMethods=437, skipped=0, placeholders=0, assertions=825, assertionDensity=1.89, uncoveredProjects=0, coverageFile=False, lineRate=n/a. |
| Security | 10 | static-security-patterns, production-and-unknown-project-dependency-vulnerabilities | Findings: 0 distinct source sites from 0 observations (errors: 0, warnings: 0). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | 9.2 | static-exception-patterns | Distinct source findings: 5 across 26 project/framework observations (errors: 0, warnings: 4). emptyCatch=0, throwEx=0, broadDefaults=0. Affected catches: 3/19; documented catches: 4. |
| Documentation | 6 | documentation-presence-and-content-signals | hasReadme=True, readmeNonBlankLines=84, hasDocsDir=False, architectureDocs=0, hasAiInstructions=False, libraryXmlDocRatio=1.00, publicApiDocCoverage=1.00, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | 4 | package-version-and-feed-observations, production-and-development-project-dependencies | vulnerableDirect=0, vulnerableTransitive=0, outdated=15, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=1, outdatedFrameworkCompatibilityUnknown=0, deprecated=1, unsupportedTFMs=1, versionDrift=1, cpmEnabled=False, anyCommandFailed=False. |
| Performance & Async | 10 | static-async-and-blocking-patterns | Distinct source findings: 1 across 7 project/framework observations (errors: 0, warnings: 0, unscored review leads: 1). syncOverAsync=1, threadSleep=0, saveChangesInsideLoop=0, missingCancellationToken=0, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | 8 | production-executable-function-maintainability-index | Distinct executable functions: 871. Weakest 175: 5.01; remaining 696: 9.97. Each function contributes once; distribution statistics are diagnostic only. |

Vulnerability scope: {"policy": "production-and-unknown-projects-v1", "importedPackageVersions": 0, "excludedDevelopmentObservations": 0, "detailDimension": "dependencyManagement"}.

### FluentValidation

- Source [fa9787a0d4fd](https://github.com/FluentValidation/FluentValidation/tree/fa9787a0d4fd0c0b99f9682292d76d5ad97ba6cb); `FluentValidation.sln`, Release.
- runId `4b49d28c-b358-4c5a-a66a-2ca0ad6143a7`; auditId `17c6e4b5-6ed1-492f-bf49-e61afb334b1a`.
- Previous runId `47fb8ed0-1869-4384-a86f-1b6653868deb`; auditId `e2b827a7-5490-443b-be6d-878a1324f71f`.
- Configuration fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; calibration baseline; schema 3; complete, fresh and usable.
- 2/6 units analyzed; 196 type and 1017 member observations (not unique source counts).
- [evidence](E:/repos/FluentValidation/.scorecard/dotnet/runs/4b49d28c-b358-4c5a-a66a-2ca0ad6143a7/evidence.json), [inspection](E:/repos/FluentValidation/.scorecard/dotnet/runs/4b49d28c-b358-4c5a-a66a-2ca0ad6143a7/inspection.json), [metrics](E:/repos/FluentValidation/.scorecard/dotnet/runs/4b49d28c-b358-4c5a-a66a-2ca0ad6143a7/metrics.csv).

| Dimension | Score | Scope includes | Recorded basis |
|---|---:|---|---|
| Architecture & SOLID | 5.4 | static-coupling-and-project-structure | Findings: 9 (errors: 0, warnings: 9). Cycles: 0, hotspots: 9. Excluded passive data carriers: 2, DI extension types: 1, framework coupling archetypes: 0, application composition roots: 0. coupling: 9/193 eligible types, score 5.44; complexity: 0/193 eligible types, score 10.00; size: 0/193 eligible types, score 10.00. Final = min(metric score 5.44, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | 4.8 | production-type-complexity, member-complexity | Eligible types: 142. Passive data carriers excluded: 2. Method complexity: 5.6, Decomposition: 4. Combined: 4.8. |
| Testing | 6 | test-project-signals, supplied-coverage-report | testProjects=1, testMethods=828, skipped=1, placeholders=0, assertions=1319, assertionDensity=1.59, uncoveredProjects=1, coverageFile=False, lineRate=n/a. |
| Security | 10 | static-security-patterns, production-and-unknown-project-dependency-vulnerabilities | Findings: 0 distinct source sites from 0 observations (errors: 0, warnings: 0). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | 10 | static-exception-patterns | Distinct source findings: 0 across 0 project/framework observations (errors: 0, warnings: 0). emptyCatch=0, throwEx=0, broadDefaults=0. Affected catches: 0/4; documented catches: 1. |
| Documentation | 8 | documentation-presence-and-content-signals | hasReadme=True, readmeNonBlankLines=50, hasDocsDir=True, architectureDocs=0, hasAiInstructions=False, libraryXmlDocRatio=1.00, publicApiDocCoverage=0.62, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | 4 | package-version-and-feed-observations, production-and-development-project-dependencies | vulnerableDirect=0, vulnerableTransitive=0, outdated=11, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=0, outdatedFrameworkCompatibilityUnknown=0, deprecated=1, unsupportedTFMs=0, versionDrift=0, cpmEnabled=False, anyCommandFailed=False. |
| Performance & Async | 10 | static-async-and-blocking-patterns | Distinct source findings: 0 across 0 project/framework observations (errors: 0, warnings: 0, unscored review leads: 0). syncOverAsync=0, threadSleep=0, saveChangesInsideLoop=0, missingCancellationToken=0, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | 7.5 | production-executable-function-maintainability-index | Distinct executable functions: 852. Weakest 171: 3.79; remaining 681: 9.92. Each function contributes once; distribution statistics are diagnostic only. |

Vulnerability scope: {"policy": "production-and-unknown-projects-v1", "importedPackageVersions": 0, "excludedDevelopmentObservations": 0, "detailDimension": "dependencyManagement"}.

### Quartz.NET (4.x main)

- Source [97afe142210e](https://github.com/quartznet/quartznet/tree/97afe142210e9c616b434ec7d617286a2752e9d4); `Quartz.slnx`, Release.
- runId `d3a6eff7-0283-4184-9d73-6d75cf7d8edf`; auditId `28a2091a-9cab-497d-bfe5-4905c0dadfa6`.
- Previous runId `654ee0d5-5f0b-47b9-8654-24405aca344e`; auditId `d1f90bda-2ee0-4349-9c55-234ec512bf89`.
- Configuration fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; calibration baseline; schema 3; complete, fresh and usable.
- 20/30 units analyzed; 903 type and 8059 member observations (not unique source counts).
- [evidence](E:/repos/quartznet/.scorecard/dotnet/runs/d3a6eff7-0283-4184-9d73-6d75cf7d8edf/evidence.json), [inspection](E:/repos/quartznet/.scorecard/dotnet/runs/d3a6eff7-0283-4184-9d73-6d75cf7d8edf/inspection.json), [metrics](E:/repos/quartznet/.scorecard/dotnet/runs/d3a6eff7-0283-4184-9d73-6d75cf7d8edf/metrics.csv).

| Dimension | Score | Scope includes | Recorded basis |
|---|---:|---|---|
| Architecture & SOLID | 4.5 | static-coupling-and-project-structure | Findings: 99 (errors: 0, warnings: 99). Cycles: 0, hotspots: 99 (showing 10). Excluded passive data carriers: 154, DI extension types: 4, framework coupling archetypes: 1, application composition roots: 5. coupling: 92/739 eligible types, score 4.51; complexity: 1/745 eligible types, score 9.96; size: 6/745 eligible types, score 5.90. Final = min(metric score 4.51, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | 5.6 | production-type-complexity, member-complexity | Eligible types: 605. Passive data carriers excluded: 154. Method complexity: 5.8, Decomposition: 5.3. Combined: 5.6. |
| Testing | 6 | test-project-signals, supplied-coverage-report | testProjects=3, testMethods=4355, skipped=0, placeholders=0, assertions=10502, assertionDensity=2.41, uncoveredProjects=19, coverageFile=False, lineRate=n/a. |
| Security | 2 | static-security-patterns, production-and-unknown-project-dependency-vulnerabilities | Findings: 7 distinct source sites from 7 observations (errors: 7, warnings: 0). hardcodedSecrets=1, rawSql=6, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | 4 | static-exception-patterns | Distinct source findings: 278 across 278 project/framework observations (errors: 24, warnings: 56). emptyCatch=10, throwEx=0, broadDefaults=14. Affected catches: 61/349; documented catches: 100. |
| Documentation | 6 | documentation-presence-and-content-signals | hasReadme=True, readmeNonBlankLines=41, hasDocsDir=True, architectureDocs=0, hasAiInstructions=True, libraryXmlDocRatio=0.77, publicApiDocCoverage=1.00, staleMarkers=0, unresolvedCrefs=1. |
| Dependency Management | 4 | package-version-and-feed-observations, production-and-development-project-dependencies | vulnerableDirect=0, vulnerableTransitive=0, outdated=35, outdatedAspireExcluded=12, outdatedFrameworkIncompatibleExcluded=0, outdatedFrameworkCompatibilityUnknown=0, deprecated=0, unsupportedTFMs=0, versionDrift=0, cpmEnabled=True, anyCommandFailed=False. |
| Performance & Async | 2 | static-async-and-blocking-patterns | Distinct source findings: 8 across 8 project/framework observations (errors: 1, warnings: 0, unscored review leads: 7). syncOverAsync=4, threadSleep=0, saveChangesInsideLoop=0, missingCancellationToken=0, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=4, sharedStateMutationInFanOut=0. |
| Maintainability | 7 | production-executable-function-maintainability-index | Distinct executable functions: 6022. Weakest 1205: 3.37; remaining 4817: 9.49. Each function contributes once; distribution statistics are diagnostic only. |

Vulnerability scope: {"policy": "production-and-unknown-projects-v1", "importedPackageVersions": 0, "excludedDevelopmentObservations": 0, "detailDimension": "dependencyManagement"}.

## Interpretation

1. **Short-circuit completion guards (Current context).** [Quartz StdAdoDelegate.cs:336](E:/repos/quartznet/src/Quartz/Impl/AdoJobStore/StdAdoDelegate.cs:336) reads `isDbNullTask.Result` only on the right side of `isDbNullTask.IsCompleted &&`. This is a confirmed nonblocking access and a remaining analyzer false positive. Next: add bounded short-circuit completion proofs with mutation and negative-path regressions. It was intentionally left outside this switch-focused implementation.
2. **Synchronous pipelining (Metrics + Current context).** [Dapper SqlMapper.cs:645](E:/repos/Dapper/Dapper/SqlMapper.cs:645) calls the asynchronous multi-execution implementation and reads Result when CommandFlags.Pipelined is enabled. The blocking access is real; whether a different API contract is appropriate requires review. Do not automatically prescribe concurrency or suppress it.
3. **Remaining security observations (Metrics).** Quartz findings remain static review leads. Confirm actual SQL data flow and credential meaning before recommending changes; the anonymous-access policy does not erase unrelated findings.

Machine-readable verification: [dotnet-public-corpus-wave2-scope-intent.json](dotnet-public-corpus-wave2-scope-intent.json).
