# Security and dependency population verification

Verified 2026-09-14 against the same four Release source snapshots as the [evidence-fidelity baseline](dotnet-public-corpus-wave2-fidelity.md). The ruleset is `dotnet-2026-09-14-source-package-populations`; the local package reports version 2.3.0 and is **unpublished**. The skill helper installed the explicit local package by content hash and validated schema-v3 evidence with fresh run/audit identities.

## Results

| Dimension | Dapper | Serilog | FluentValidation | Quartz.NET (4.x main) |
|---|---:|---:|---:|---:|
| Architecture & SOLID | 5.2 | 6.3 | 5.4 | 4.5 |
| Complexity & Decomposition | 5.9 | 6 | 4.8 | 5.6 |
| Testing | 6 | 10 | 6 | 6 |
| Security | 10 | 10 | 10 | 2 |
| Error Handling | 4 | 9.2 | 10 | 4 |
| Documentation | 6 | 6 | 8 | 6 |
| Dependency Management | 0 | 4 | 4 | 4 |
| Performance & Async | 2 | 10 | 10 | 0 |
| Maintainability | 7.7 | 8 | 7.5 | 7.1 |
| **Overall, partial assessment (9/9)** | 5.2 | 7.7 | 7.3 | 4.4 |
| Previous overall, partial assessment (9/9) | 4.1 | 7.7 | 7.3 | 4.4 |

All four analyzer/validator pairs exited 0 with usable, complete evidence. These are partial static assessments: no target test suites, coverage reports, benchmarks or runtime security checks were run. Quartz is the 4.x main branch. The earlier Serilog native AOT linker limitation remains prior build context, not reverified in this rerun.

All four raw CSV files, source commits, tracked target files, filters, populations and configuration fingerprints are unchanged. All seven dimensions outside Security and Dependency Management are exactly equal to the previous evidence, including findings and scoring decisions. Dependency reports were queried afresh; row comparison results are recorded below. Numerical score ladders are unchanged. These are classification/counting/scope corrections, not target-code improvements or compatible cross-ruleset baseline gates.

## What changed

Dapper's eight CMAI4001 observations were repeated reports of the same [regex constant](E:/repos/Dapper/Dapper/CompiledRegex.cs:14). The literal is consumed as a pattern by BCL Regex and GeneratedRegex, and now passes the bounded semantic exclusion. The five vulnerability rows concern SQLitePCLRaw.lib.e_sqlite3 in tests and System.Security.Cryptography.Xml in benchmarks. Those rows remain actionable Dependency Management observations, but do not become production Security inputs. Together these changes move Dapper Security from 0 to 10 within the newly declared scope. This does not certify the library as secure.

Security findings now count once per physical file/span/category with maximum observed severity; project/framework variants remain attached. Missing source identity remains separate. Dependency scoring counts package IDs and resolved versions per input after compatibility disposition, while original report rows remain intact. Development dependencies continue to influence Dependency Management; only development-only vulnerability imports are excluded from Security. Mixed production/development or unknown-scope versions remain Security inputs. Ambiguous display-name collisions cannot transfer a test exclusion to another physical project.

| Repository | Outdated: old rows → scored versions | Deprecated: old rows → scored versions | Original package rows unchanged |
|---|---:|---:|---|
| Dapper | 119 → 25 | 7 → 3 | True |
| Serilog | 65 → 15 | 9 → 1 | True |
| FluentValidation | 25 → 11 | 3 → 1 | True |
| Quartz.NET (4.x main) | 79 → 35 | 0 → 0 | True |

Dapper Dependency Management remains 0 because its benchmark project has a direct vulnerable dependency. The Security scope correction does not erase development-environment maintenance concerns. A large count reduction can leave a first-match ladder score unchanged when another condition still determines it.

The [policy](../security-dependency-populations.md) documents the semantic regex boundary, package identities, missing-evidence behavior and scope semantics. CMAI4001's updated description ships in both JSON and generated Markdown in the analyzer package.

## Validation and identity

- 1032 .NET tests passed, including 34 new regression cases.
- Six calibration fixtures retain identical scores/findings; only the ruleset identifier changes.
- An existing 100 ms budget regression assumed a queued cancellation stage. Repeated failures showed all requests can enter released slots before their cancellation callbacks execute. The test now accepts either package or queue budget expiry while requiring zero successful results and explicit failures covering all 20 observations. Production timeout/cancellation behavior is unchanged. An overlapping test-build attempt also hit the Windows apphost lock; the completed verification ran after that process exited.
- Formatting verification and git whitespace checks passed.
- Packaged DLL and rule catalog bytes match the tested Release output and source catalog.
- SHA256: `992f69d35038da16511e7066c54e4253d4b65efbe6f05dad3944a05ca27d5ae6`.
- [Local package](E:/repos/CodeMetrics.AI/TestResults/public-corpus-wave2/population-final-package/CodeMetrics.AI.2.3.0.nupkg).
- [Tests](E:/repos/CodeMetrics.AI/TestResults/public-corpus-wave2/population-complete-tests.log), [calibration](E:/repos/CodeMetrics.AI/TestResults/public-corpus-wave2/population-final-calibration.json), [format verification](E:/repos/CodeMetrics.AI/TestResults/public-corpus-wave2/population-complete-format-verify.log).

## Run evidence

### Dapper

- Source [8becae8d0e2b](https://github.com/DapperLib/Dapper/tree/8becae8d0e2b360165ae03c0d5d1330b0273473d); `Dapper.slnx`, Release.
- runId `c7b496b1-3a00-437f-873a-c73be8699b26`; auditId `52c54a12-f012-4028-b2ec-babee750a504`.
- Previous runId `10ffa8c9-bfa7-400b-bef2-2d62850f1b27`; auditId `484b526c-ef83-4528-bcf8-d493bc15ebb3`.
- Configuration fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; calibration baseline; schema 3; complete, fresh and usable.
- 20/25 units analyzed; 555 type and 7222 member observations (not unique source counts).
- [evidence](E:/repos/Dapper/.scorecard/dotnet/runs/c7b496b1-3a00-437f-873a-c73be8699b26/evidence.json), [inspection](E:/repos/Dapper/.scorecard/dotnet/runs/c7b496b1-3a00-437f-873a-c73be8699b26/inspection.json), [metrics](E:/repos/Dapper/.scorecard/dotnet/runs/c7b496b1-3a00-437f-873a-c73be8699b26/metrics.csv).

| Dimension | Score | Scope includes | Recorded basis |
|---|---:|---|---|
| Architecture & SOLID | 5.2 | static-coupling-and-project-structure | Findings: 43 (errors: 0, warnings: 43). Cycles: 0, hotspots: 43 (showing 10). Excluded passive data carriers: 30, DI extension types: 0, framework coupling archetypes: 0, application composition roots: 0. coupling: 35/525 eligible types, score 5.20; complexity: 0/525 eligible types, score 10.00; size: 8/525 eligible types, score 5.82. Final = min(metric score 5.20, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | 5.9 | production-type-complexity, member-complexity | Eligible types: 429. Passive data carriers excluded: 30. Method complexity: 5.8, Decomposition: 6. Combined: 5.9. |
| Testing | 6 | test-project-signals, supplied-coverage-report | testProjects=1, testMethods=389, skipped=1, placeholders=0, assertions=1228, assertionDensity=3.16, uncoveredProjects=7, coverageFile=False, lineRate=n/a. |
| Security | 10 | static-security-patterns, production-and-unknown-project-dependency-vulnerabilities | Findings: 0 distinct source sites from 0 observations (errors: 0, warnings: 0). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | 4 | static-exception-patterns | Distinct source findings: 15 across 70 project/framework observations (errors: 6, warnings: 9). emptyCatch=4, throwEx=0, broadDefaults=2. Affected catches: 8/19; documented catches: 9. |
| Documentation | 6 | documentation-presence-and-content-signals | hasReadme=True, readmeNonBlankLines=373, hasDocsDir=True, architectureDocs=0, hasAiInstructions=False, libraryXmlDocRatio=0.85, publicApiDocCoverage=0.90, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | 0 | package-version-and-feed-observations, production-and-development-project-dependencies | vulnerableDirect=1, vulnerableTransitive=1, outdated=25, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=4, outdatedFrameworkCompatibilityUnknown=0, deprecated=3, unsupportedTFMs=1, versionDrift=0, cpmEnabled=True, anyCommandFailed=False. |
| Performance & Async | 2 | static-async-and-blocking-patterns | Distinct source findings: 6 across 33 project/framework observations (errors: 2, warnings: 0, unscored review leads: 4). syncOverAsync=2, threadSleep=0, saveChangesInsideLoop=0, missingCancellationToken=4, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
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
- runId `46ebd3df-0133-4064-b5b0-f320f1739635`; auditId `6b890466-e3aa-4eb9-81b8-ef9daba011e2`.
- Previous runId `400d7095-ff58-4c2b-841f-f9d58f7e2cde`; auditId `ea2b05c9-3f6a-48b9-8029-e162a669d995`.
- Configuration fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; calibration baseline; schema 3; complete, fresh and usable.
- 7/19 units analyzed; 790 type and 6812 member observations (not unique source counts).
- [evidence](E:/repos/serilog/.scorecard/dotnet/runs/46ebd3df-0133-4064-b5b0-f320f1739635/evidence.json), [inspection](E:/repos/serilog/.scorecard/dotnet/runs/46ebd3df-0133-4064-b5b0-f320f1739635/inspection.json), [metrics](E:/repos/serilog/.scorecard/dotnet/runs/46ebd3df-0133-4064-b5b0-f320f1739635/metrics.csv).

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
- runId `47fb8ed0-1869-4384-a86f-1b6653868deb`; auditId `e2b827a7-5490-443b-be6d-878a1324f71f`.
- Previous runId `087154fb-24ba-48c4-9b74-efb1f52a6bdf`; auditId `82d3b781-15ae-4914-8e07-10d59e2fb8b3`.
- Configuration fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; calibration baseline; schema 3; complete, fresh and usable.
- 2/6 units analyzed; 196 type and 1017 member observations (not unique source counts).
- [evidence](E:/repos/FluentValidation/.scorecard/dotnet/runs/47fb8ed0-1869-4384-a86f-1b6653868deb/evidence.json), [inspection](E:/repos/FluentValidation/.scorecard/dotnet/runs/47fb8ed0-1869-4384-a86f-1b6653868deb/inspection.json), [metrics](E:/repos/FluentValidation/.scorecard/dotnet/runs/47fb8ed0-1869-4384-a86f-1b6653868deb/metrics.csv).

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
- runId `654ee0d5-5f0b-47b9-8654-24405aca344e`; auditId `d1f90bda-2ee0-4349-9c55-234ec512bf89`.
- Previous runId `dece046a-3e66-4ea8-a6a9-96d29ae93d42`; auditId `aee899b0-416f-43e0-8a1e-099a9c9545af`.
- Configuration fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; calibration baseline; schema 3; complete, fresh and usable.
- 21/30 units analyzed; 988 type and 9098 member observations (not unique source counts).
- [evidence](E:/repos/quartznet/.scorecard/dotnet/runs/654ee0d5-5f0b-47b9-8654-24405aca344e/evidence.json), [inspection](E:/repos/quartznet/.scorecard/dotnet/runs/654ee0d5-5f0b-47b9-8654-24405aca344e/inspection.json), [metrics](E:/repos/quartznet/.scorecard/dotnet/runs/654ee0d5-5f0b-47b9-8654-24405aca344e/metrics.csv).

| Dimension | Score | Scope includes | Recorded basis |
|---|---:|---|---|
| Architecture & SOLID | 4.5 | static-coupling-and-project-structure | Findings: 112 (errors: 0, warnings: 112). Cycles: 0, hotspots: 112 (showing 10). Excluded passive data carriers: 156, DI extension types: 4, framework coupling archetypes: 1, application composition roots: 6. coupling: 105/821 eligible types, score 4.47; complexity: 1/828 eligible types, score 9.96; size: 6/828 eligible types, score 5.91. Final = min(metric score 4.47, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | 5.6 | production-type-complexity, member-complexity | Eligible types: 685. Passive data carriers excluded: 156. Method complexity: 5.8, Decomposition: 5.3. Combined: 5.6. |
| Testing | 6 | test-project-signals, supplied-coverage-report | testProjects=3, testMethods=4355, skipped=0, placeholders=0, assertions=10502, assertionDensity=2.41, uncoveredProjects=20, coverageFile=False, lineRate=n/a. |
| Security | 2 | static-security-patterns, production-and-unknown-project-dependency-vulnerabilities | Findings: 7 distinct source sites from 7 observations (errors: 7, warnings: 0). hardcodedSecrets=1, rawSql=6, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | 4 | static-exception-patterns | Distinct source findings: 356 across 356 project/framework observations (errors: 24, warnings: 123). emptyCatch=10, throwEx=0, broadDefaults=14. Affected catches: 62/352; documented catches: 101. |
| Documentation | 6 | documentation-presence-and-content-signals | hasReadme=True, readmeNonBlankLines=41, hasDocsDir=True, architectureDocs=0, hasAiInstructions=True, libraryXmlDocRatio=0.77, publicApiDocCoverage=1.00, staleMarkers=0, unresolvedCrefs=2. |
| Dependency Management | 4 | package-version-and-feed-observations, production-and-development-project-dependencies | vulnerableDirect=0, vulnerableTransitive=0, outdated=35, outdatedAspireExcluded=12, outdatedFrameworkIncompatibleExcluded=0, outdatedFrameworkCompatibilityUnknown=0, deprecated=0, unsupportedTFMs=0, versionDrift=0, cpmEnabled=True, anyCommandFailed=False. |
| Performance & Async | 0 | static-async-and-blocking-patterns | Distinct source findings: 74 across 74 project/framework observations (errors: 67, warnings: 0, unscored review leads: 7). syncOverAsync=70, threadSleep=0, saveChangesInsideLoop=0, missingCancellationToken=0, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=4, sharedStateMutationInFanOut=0. |
| Maintainability | 7.1 | production-executable-function-maintainability-index | Distinct executable functions: 6849. Weakest 1370: 3.49; remaining 5479: 9.56. Each function contributes once; distribution statistics are diagnostic only. |

Vulnerability scope: {"policy": "production-and-unknown-projects-v1", "importedPackageVersions": 0, "excludedDevelopmentObservations": 0, "detailDimension": "dependencyManagement"}.

## Next review priorities

1. **Development dependency advisories (Metrics).** Dapper's test and benchmark advisories remain in the dependency evidence with their actual project scope. Review the affected package updates; do not report them as established vulnerabilities in the shipped Dapper library.
2. **Remaining async/wait and error-handling findings (Metrics; behavioral review still required).** Dapper and Quartz still have low scores here. These findings were not reclassified in this change, and their sites need source-level review before prescribing concurrency or exception-handling changes.
3. **Dispatch-heavy complexity (prior context, not reverified here).** FluentValidation's language dispatch and Serilog's documented formatter tradeoff remain candidates for explicit calibration review. The present rerun does not validate the performance rationale or change complexity thresholds.

Machine-readable verification: [dotnet-public-corpus-wave2-populations.json](dotnet-public-corpus-wave2-populations.json).
