# Public corpus after security and catch-context corrections

Recorded from committed analyzer `2fb130663ad240a8b7a0ece0858a46c1e2ba3c4f`. All four pinned repositories were freshly analyzed with the local unpublished CodeMetrics.AI 2.3.0 package, Release, schema v3, including dependency probes. Ruleset: `dotnet-2026-09-13-security-catch-context`. Source revisions and tracked files were verified unchanged before and after each run.

The earlier [full corpus report](dotnet-public-corpus-async-source.md) used the absolute-count error policy and earlier security classification. This is an incompatible-ruleset policy comparison on fixed source, not a baseline gate or source-code improvement. See the [implemented policy](../error-handling-policy.md).

## Full scorecards

All scores are deterministic within partial static scopes. Every overall below is a **partial assessment, 9/9 dimensions scored**. No target test suites were executed and no coverage files were supplied. These pinned snapshots are not current upstream heads or a general ranking of project quality.

| Dimension | Polly | Newtonsoft.Json | SimplCommerce | OrchardCore |
|---|---:|---:|---:|---:|
| Architecture & SOLID | 5.6 | 4.1 | 2 | 4 |
| Complexity & Decomposition | 8 | 4.8 | 7.3 | 5.3 |
| Testing | 8 | 6 | 6 | 6 |
| Security | 10 | 10 | 2 | 2 |
| Error Handling | 4 | 9.5 | 10 | 4 |
| Documentation | 9 | 2 | 3 | 4 |
| Dependency Management | 2 | 2 | 2 | 2 |
| Performance & Async | 0 | 10 | 2 | 0 |
| Maintainability | 8.2 | 6.9 | 7.3 | 7.3 |
| Overall, partial assessment (9/9) | 6.1 | 6.1 | 4.6 | 3.8 |

| C&D component | Polly | Newtonsoft.Json | SimplCommerce | OrchardCore |
|---|---:|---:|---:|---:|
| Decomposition | 8 | 4 | 7.3 | 4.7 |
| Method complexity | 7.9 | 5.6 | 7.2 | 5.8 |

## Error-handling population

| Measure | Polly | Newtonsoft.Json | SimplCommerce | OrchardCore |
|---|---:|---:|---:|---:|
| Distinct source catches | 48 | 50 | 11 | 461 |
| Project/framework catch observations | 220 | 310 | 11 | 461 |
| Affected source catches | 7 | 4 | 0 | 51 |
| Catches with local rationale | 5 | 8 | 1 | 44 |
| Weighted affected sum | 3.5 | 2.5 | 0 | 34.0 |
| Population component (before caps) | 9.271 | 9.500 | 10.000 | 9.262 |
| Final Error Handling | 4 | 9.5 | 10 | 4 |

Each source catch contributes its greatest issue weight across rules and frameworks. Rationale is treated as a declaration, not proof of correct business behavior. A catch without a scored issue is not necessarily verified as correctly handled. Critical-site and synchronous-blocking caps remain separately visible in the executed decisions.

## Changes from the preceding full corpus

| Repository | Security | Error Handling | Overall, partial assessment (9/9) |
|---|---:|---:|---:|
| Polly | 10 → 10 | 2 → 4 | 5.9 → 6.1 |
| Newtonsoft.Json | 10 → 10 | 2 → 9.5 | 5.3 → 6.1 |
| SimplCommerce | 2 → 2 | 10 → 10 | 4.6 → 4.6 |
| OrchardCore | 0 → 2 | 0 → 4 | 3.2 → 3.8 |

Raw CSV is byte-identical for every repository. Analysis populations, filters, configurations and suppressions match the earlier runs. The entire Architecture, C&D, Testing, Documentation, Performance & Async and Maintainability dimension objects are unchanged. Feed-sensitive dependency observations are recorded independently below.

## Scope

| Dimension | Included | Excluded |
|---|---|---|
| Architecture & SOLID | static-coupling-and-project-structure | runtime-behavior, comprehensive-human-review |
| Complexity & Decomposition | production-type-complexity, member-complexity | runtime-behavior, comprehensive-human-review |
| Testing | test-project-signals, supplied-coverage-report | runtime-behavior, comprehensive-human-review |
| Security | static-security-patterns, dependency-vulnerability-observations | runtime-behavior, comprehensive-human-review |
| Error Handling | static-exception-patterns | runtime-behavior, comprehensive-human-review |
| Documentation | documentation-presence-and-content-signals | runtime-behavior, comprehensive-human-review |
| Dependency Management | package-version-and-feed-observations | runtime-behavior, comprehensive-human-review |
| Performance & Async | static-async-and-blocking-patterns | runtime-behavior, comprehensive-human-review |
| Maintainability | production-executable-function-maintainability-index | runtime-behavior, comprehensive-human-review |

## Per-repository evidence

### Polly

- Entry point: `E:\repos\Polly\Polly.slnx`; pinned commit: `1a80392b1f093f40e59c515f4aeb989bea5db857`.
- Run ID: `573dfdfb-8952-4ef5-bb23-0fa53b35e0b2`; audit ID: `62389ee8-3c11-4b02-bfb7-03379742b495`. Fresh, complete, usable inspection; analyzer and validator exit codes zero; independent invocation/evidence IDs match.
- Units: 21/53 analyzed. Population: `{"types": 1858, "members": 10988}`. Calibration: `baseline` (regression fixtures). Configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- Security decision: `clean`. Error-handling limiting component/cap: `syncBlockingCap`.
- Security finding categories: `{}`.
- Error-handling finding categories: `{"broadCatchWithoutLoggingOrRethrow": 7, "syncBlockingCall": 12, "missingLoggerForMultipleCatches": 2}`.
- Dependency score: 2; 20 distinct package IDs in package findings. Counts are project/TFM occurrences: `{"outdatedDependency": 142, "unsupportedTargetFramework": 4}`. Dependency evidence object changed since prior run: `False`.
- Changed scalar dependency measurements (previous, current): `{}`. Unknown framework compatibility indicates unavailable compatibility evidence, not proof that an upgrade is supported.
- [Grouped dependency details, versions, advisories, projects, TFMs and scoring dispositions](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-catch-context/Polly-dependencies.json>). Compatible assets or a suggested alternative do not establish a safe upgrade.
- Workspace diagnostics: `[]`.
- [Evidence](<E:/repos/Polly/.scorecard/dotnet/runs/573dfdfb-8952-4ef5-bb23-0fa53b35e0b2/evidence.json>), [Inspection](<E:/repos/Polly/.scorecard/dotnet/runs/573dfdfb-8952-4ef5-bb23-0fa53b35e0b2/inspection.json>), [Metrics](<E:/repos/Polly/.scorecard/dotnet/runs/573dfdfb-8952-4ef5-bb23-0fa53b35e0b2/metrics.csv>), [Rulecatalog](<E:/repos/Polly/.scorecard/dotnet/runs/573dfdfb-8952-4ef5-bb23-0fa53b35e0b2/rules.json>).

| Dimension | Recorded basis |
|---|---|
| Architecture & SOLID | Findings: 61 (errors: 0, warnings: 61). Cycles: 0, hotspots: 61 (showing 10). Excluded passive data carriers: 242, DI extension types: 5, framework coupling archetypes: 0, application composition roots: 0. coupling: 57/1611 eligible types, score 5.58; complexity: 0/1611 eligible types, score 10.00; size: 4/1611 eligible types, score 5.97. Final = min(metric score 5.58, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | Eligible types: 1242. Passive data carriers excluded: 242. Method complexity: 7.9, Decomposition: 8. Combined: 8. |
| Testing | testProjects=5, testMethods=2632, skipped=1, placeholders=0, assertions=8710, assertionDensity=3.31, uncoveredProjects=0, coverageFile=False, lineRate=n/a. |
| Security | Findings: 0 (errors: 0, warnings: 0). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | Distinct source findings: 21 across 98 project/framework observations (errors: 0, warnings: 21). emptyCatch=0, throwEx=0, broadDefaults=0. Affected catches: 7/48; documented catches: 5. |
| Documentation | hasReadme=True, readmeNonBlankLines=405, hasDocsDir=True, architectureDocs=0, hasAiInstructions=True, libraryXmlDocRatio=1.00, publicApiDocCoverage=0.96, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | vulnerableDirect=0, vulnerableTransitive=0, outdated=142, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=0, outdatedFrameworkCompatibilityUnknown=0, deprecated=0, unsupportedTFMs=4, versionDrift=0, cpmEnabled=True, anyCommandFailed=False. |
| Performance & Async | Distinct source findings: 24 across 100 project/framework observations (errors: 11, warnings: 12). syncOverAsync=12, threadSleep=1, saveChangesInsideLoop=0, missingCancellationToken=11, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | Distinct executable functions: 2218. Weakest 444: 5.68; remaining 1774: 9.90. Each function contributes once; distribution statistics are diagnostic only. |

### Newtonsoft.Json

- Entry point: `E:\repos\Newtonsoft.Json\Src\Newtonsoft.Json.slnx`; pinned commit: `09bb545d72969ad7fb4ea07db0d5c34f4fc07877`.
- Run ID: `43f35b0f-fd0b-4193-94a1-8debddd6cbdb`; audit ID: `ea26b813-bd4b-43a1-b86c-59dd8b3e4a85`. Fresh, complete, usable inspection; analyzer and validator exit codes zero; independent invocation/evidence IDs match.
- Units: 9/16 analyzed. Population: `{"types": 1707, "members": 25381}`. Calibration: `baseline` (regression fixtures). Configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- Security decision: `clean`. Error-handling limiting component/cap: `catchPopulation`.
- Security finding categories: `{}`.
- Error-handling finding categories: `{"broadCatchWithoutLoggingOrRethrow": 4, "broadCatchReturnsDefault": 2, "consoleWriteLine": 1, "missingLoggerForMultipleCatches": 1}`.
- Dependency score: 2; 13 distinct package IDs in package findings. Counts are project/TFM occurrences: `{"outdatedDependency": 37, "deprecatedDependency": 3, "unsupportedTargetFramework": 2, "noCentralPackageManagement": 1}`. Dependency evidence object changed since prior run: `True`.
- Changed scalar dependency measurements (previous, current): `{"outdated": [15, 32], "outdatedFrameworkIncompatibleExcluded": [22, 5], "outdatedFrameworkCompatibilityUnknown": [0, 24]}`. Unknown framework compatibility indicates unavailable compatibility evidence, not proof that an upgrade is supported.
- [Grouped dependency details, versions, advisories, projects, TFMs and scoring dispositions](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-catch-context/Newtonsoft.Json-dependencies.json>). Compatible assets or a suggested alternative do not establish a safe upgrade.
- Workspace diagnostics: `[{"kind": "workspaceWarning", "message": "Found project reference without a matching metadata reference: E:\\repos\\Newtonsoft.Json\\Src\\Newtonsoft.Json.Tests\\Newtonsoft.Json.Tests.csproj"}]`.
- [Evidence](<E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/43f35b0f-fd0b-4193-94a1-8debddd6cbdb/evidence.json>), [Inspection](<E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/43f35b0f-fd0b-4193-94a1-8debddd6cbdb/inspection.json>), [Metrics](<E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/43f35b0f-fd0b-4193-94a1-8debddd6cbdb/metrics.csv>), [Rulecatalog](<E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/43f35b0f-fd0b-4193-94a1-8debddd6cbdb/rules.json>).

| Dimension | Recorded basis |
|---|---|
| Architecture & SOLID | Findings: 359 (errors: 0, warnings: 359). Cycles: 0, hotspots: 359 (showing 10). Excluded passive data carriers: 92, DI extension types: 0, framework coupling archetypes: 0, application composition roots: 2. coupling: 256/1613 eligible types, score 4.10; complexity: 14/1615 eligible types, score 9.03; size: 89/1615 eligible types, score 5.34. Final = min(metric score 4.10, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | Eligible types: 1402. Passive data carriers excluded: 92. Method complexity: 5.6, Decomposition: 4. Combined: 4.8. |
| Testing | testProjects=1, testMethods=3252, skipped=0, placeholders=0, assertions=13027, assertionDensity=4.01, uncoveredProjects=1, coverageFile=False, lineRate=n/a. |
| Security | Findings: 0 (errors: 0, warnings: 0). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | Distinct source findings: 8 across 28 project/framework observations (errors: 2, warnings: 5). emptyCatch=0, throwEx=0, broadDefaults=2. Affected catches: 4/50; documented catches: 8. |
| Documentation | hasReadme=True, readmeNonBlankLines=10, hasDocsDir=False, architectureDocs=0, hasAiInstructions=False, libraryXmlDocRatio=1.00, publicApiDocCoverage=0.99, staleMarkers=0, unresolvedCrefs=2. |
| Dependency Management | vulnerableDirect=0, vulnerableTransitive=0, outdated=32, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=5, outdatedFrameworkCompatibilityUnknown=24, deprecated=3, unsupportedTFMs=2, versionDrift=0, cpmEnabled=False, anyCommandFailed=False. |
| Performance & Async | Distinct source findings: 0 across 0 project/framework observations (errors: 0, warnings: 0). syncOverAsync=0, threadSleep=0, saveChangesInsideLoop=0, missingCancellationToken=0, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | Distinct executable functions: 3448. Weakest 690: 3.01; remaining 2758: 9.52. Each function contributes once; distribution statistics are diagnostic only. |

### SimplCommerce

- Entry point: `E:\repos\SimplCommerce\SimplCommerce.sln`; pinned commit: `3472ba02a6f2d9b6bdca7f7fb84957176aa799dc`.
- Run ID: `72e16a60-da2d-4ebf-8c72-f4488fae04d5`; audit ID: `a99dff18-1171-4f7c-85fd-b1c658b2111f`. Fresh, complete, usable inspection; analyzer and validator exit codes zero; independent invocation/evidence IDs match.
- Units: 42/49 analyzed. Population: `{"types": 715, "members": 3823}`. Calibration: `baseline` (regression fixtures). Configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- Security decision: `secret`. Error-handling limiting component/cap: `catchPopulation, criticalCatchCap, syncBlockingCap`.
- Security finding categories: `{"missingAuthorization": 22, "allowAnonymous": 20, "hardcodedSecret": 2}`.
- Error-handling finding categories: `{}`.
- Dependency score: 2; 48 distinct package IDs in package findings. Counts are project/TFM occurrences: `{"outdatedDependency": 67, "vulnerableTransitiveDependency": 203, "deprecatedDependency": 8, "versionDrift": 1, "noCentralPackageManagement": 1}`. Dependency evidence object changed since prior run: `False`.
- Changed scalar dependency measurements (previous, current): `{}`. Unknown framework compatibility indicates unavailable compatibility evidence, not proof that an upgrade is supported.
- [Grouped dependency details, versions, advisories, projects, TFMs and scoring dispositions](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-catch-context/SimplCommerce-dependencies.json>). Compatible assets or a suggested alternative do not establish a safe upgrade.
- Workspace diagnostics: `[]`.
- [Evidence](<E:/repos/SimplCommerce/.scorecard/dotnet/runs/72e16a60-da2d-4ebf-8c72-f4488fae04d5/evidence.json>), [Inspection](<E:/repos/SimplCommerce/.scorecard/dotnet/runs/72e16a60-da2d-4ebf-8c72-f4488fae04d5/inspection.json>), [Metrics](<E:/repos/SimplCommerce/.scorecard/dotnet/runs/72e16a60-da2d-4ebf-8c72-f4488fae04d5/metrics.csv>), [Rulecatalog](<E:/repos/SimplCommerce/.scorecard/dotnet/runs/72e16a60-da2d-4ebf-8c72-f4488fae04d5/rules.json>).

| Dimension | Recorded basis |
|---|---|
| Architecture & SOLID | Findings: 212 (errors: 150, warnings: 62). Cycles: 0, hotspots: 62 (showing 10). Excluded passive data carriers: 216, DI extension types: 2, framework coupling archetypes: 2, application composition roots: 0. coupling: 60/495 eligible types, score 4.55; complexity: 0/497 eligible types, score 10.00; size: 2/497 eligible types, score 5.95. Final = min(metric score 4.55, graph/layering cap 2.0), rounded to 1 decimal. |
| Complexity & Decomposition | Eligible types: 391. Passive data carriers excluded: 216. Method complexity: 7.2, Decomposition: 7.3. Combined: 7.3. |
| Testing | testProjects=7, testMethods=34, skipped=0, placeholders=0, assertions=68, assertionDensity=2.00, uncoveredProjects=34, coverageFile=False, lineRate=n/a. |
| Security | Findings: 44 (errors: 2, warnings: 42). hardcodedSecrets=2, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=203. |
| Error Handling | Distinct source findings: 0 across 0 project/framework observations (errors: 0, warnings: 0). emptyCatch=0, throwEx=0, broadDefaults=0. Affected catches: 0/11; documented catches: 1. |
| Documentation | hasReadme=True, readmeNonBlankLines=81, hasDocsDir=False, architectureDocs=0, hasAiInstructions=False, libraryXmlDocRatio=0.00, publicApiDocCoverage=0.01, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | vulnerableDirect=0, vulnerableTransitive=203, outdated=55, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=12, outdatedFrameworkCompatibilityUnknown=1, deprecated=8, unsupportedTFMs=0, versionDrift=2, cpmEnabled=False, anyCommandFailed=False. |
| Performance & Async | Distinct source findings: 15 across 15 project/framework observations (errors: 1, warnings: 14). syncOverAsync=0, threadSleep=0, saveChangesInsideLoop=1, missingCancellationToken=7, materializationBeforeQueryShape=0, awaitedIoInsideLoop=7, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | Distinct executable functions: 2890. Weakest 578: 3.62; remaining 2312: 9.68. Each function contributes once; distribution statistics are diagnostic only. |

### OrchardCore

- Entry point: `E:\repos\OrchardCore\OrchardCore.slnx`; pinned commit: `4c101f5c6a6e6aca073a800797a223a958b28c0b`.
- Run ID: `161c3d8c-298e-49de-9e78-a2d52fec1beb`; audit ID: `1cceaac0-ba9c-4b91-9720-441db976c7ea`. Fresh, complete, usable inspection; analyzer and validator exit codes zero; independent invocation/evidence IDs match.
- Units: 213/239 analyzed. Population: `{"types": 5568, "members": 24989}`. Calibration: `baseline` (regression fixtures). Configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- Security decision: `importedVulnerabilities`. Error-handling limiting component/cap: `syncBlockingCap`.
- Security finding categories: `{"missingAuthorization": 26, "allowAnonymous": 30}`.
- Error-handling finding categories: `{"syncBlockingCall": 79, "broadCatchWithoutLoggingOrRethrow": 38, "consoleWriteLine": 4, "broadCatchReturnsDefault": 8, "emptyCatch": 13, "missingLoggerForMultipleCatches": 4}`.
- Dependency score: 2; 56 distinct package IDs in package findings. Counts are project/TFM occurrences: `{"outdatedDependency": 545, "deprecatedDependency": 1, "vulnerableTransitiveDependency": 1}`. Dependency evidence object changed since prior run: `False`.
- Changed scalar dependency measurements (previous, current): `{}`. Unknown framework compatibility indicates unavailable compatibility evidence, not proof that an upgrade is supported.
- [Grouped dependency details, versions, advisories, projects, TFMs and scoring dispositions](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-catch-context/OrchardCore-dependencies.json>). Compatible assets or a suggested alternative do not establish a safe upgrade.
- Workspace diagnostics: `[{"kind": "workspaceWarning", "message": "Duplicate source file 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\AnalyzerReleases.Shipped.md' in project 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\OrchardCore.SourceGenerators.csproj'"}, {"kind": "workspaceWarning", "message": "Duplicate source file 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\AnalyzerReleases.Unshipped.md' in project 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\OrchardCore.SourceGenerators.csproj'"}, {"kind": "workspaceWarning", "message": "Msbuild failed when processing the file 'E:\\repos\\OrchardCore\\test\\OrchardCore.Tests.Integration\\OrchardCore.Tests.Integration.csproj' with message: Package 'SSH.NET' 2025.1.0 has a known high severity vulnerability, https://github.com/advisories/GHSA-q939-rpr3-3284"}]`.
- [Evidence](<E:/repos/OrchardCore/.scorecard/dotnet/runs/161c3d8c-298e-49de-9e78-a2d52fec1beb/evidence.json>), [Inspection](<E:/repos/OrchardCore/.scorecard/dotnet/runs/161c3d8c-298e-49de-9e78-a2d52fec1beb/inspection.json>), [Metrics](<E:/repos/OrchardCore/.scorecard/dotnet/runs/161c3d8c-298e-49de-9e78-a2d52fec1beb/metrics.csv>), [Rulecatalog](<E:/repos/OrchardCore/.scorecard/dotnet/runs/161c3d8c-298e-49de-9e78-a2d52fec1beb/rules.json>).

| Dimension | Recorded basis |
|---|---|
| Architecture & SOLID | Findings: 754 (errors: 0, warnings: 754). Cycles: 0, hotspots: 752 (showing 10). Excluded passive data carriers: 975, DI extension types: 70, framework coupling archetypes: 1, application composition roots: 0. coupling: 744/4522 eligible types, score 4.03; complexity: 1/4523 eligible types, score 9.82; size: 7/4523 eligible types, score 7.19. Final = min(metric score 4.03, graph/layering cap 6.0), rounded to 1 decimal. |
| Complexity & Decomposition | Eligible types: 3632. Passive data carriers excluded: 975. Method complexity: 5.8, Decomposition: 4.7. Combined: 5.3. |
| Testing | testProjects=3, testMethods=1705, skipped=0, placeholders=0, assertions=4558, assertionDensity=2.67, uncoveredProjects=211, coverageFile=False, lineRate=n/a. |
| Security | Findings: 56 (errors: 0, warnings: 56). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=1. |
| Error Handling | Distinct source findings: 146 across 146 project/framework observations (errors: 21, warnings: 121). emptyCatch=13, throwEx=0, broadDefaults=8. Affected catches: 51/461; documented catches: 44. |
| Documentation | hasReadme=True, readmeNonBlankLines=50, hasDocsDir=False, architectureDocs=0, hasAiInstructions=True, libraryXmlDocRatio=0.01, publicApiDocCoverage=0.14, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | vulnerableDirect=0, vulnerableTransitive=1, outdated=541, outdatedAspireExcluded=3, outdatedFrameworkIncompatibleExcluded=1, outdatedFrameworkCompatibilityUnknown=6, deprecated=1, unsupportedTFMs=0, versionDrift=0, cpmEnabled=True, anyCommandFailed=False. |
| Performance & Async | Distinct source findings: 575 across 575 project/framework observations (errors: 78, warnings: 481). syncOverAsync=79, threadSleep=0, saveChangesInsideLoop=1, missingCancellationToken=289, materializationBeforeQueryShape=0, awaitedIoInsideLoop=192, unboundedWhenAll=14, sharedStateMutationInFanOut=0. |
| Maintainability | Distinct executable functions: 20079. Weakest 4016: 3.76; remaining 16063: 9.70. Each function contributes once; distribution statistics are diagnostic only. |

## Verification and follow-up

- Local package SHA-256: `93b45e7cfcada1b3d6ec2db1f826d6dd0267e71f9318c8a68be7adf0576c4e48`. No package publication or target-source changes were performed.
- The committed implementation had already passed 883 tests and six calibration fixtures. This turn rebuilt the package and validated four fresh real-solution runs; it did not change scoring code or calibration baselines.
- Initial Polly startup produced no evidence and was correctly rejected. A duplicate-process OrchardCore attempt was stopped. Both were rerun in isolated per-repository tool caches; only the later complete validated runs above are scored. Original failed attempts remain in `TestResults/public-corpus-catch-context/failed-attempts/`. Cache involvement is a troubleshooting hypothesis, not an established root cause.
- [Machine-readable comparison, scopes, bases, decisions and run identities](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-catch-context/comparison.json>). Runner scripts, invocation results and logs are alongside it.
- Follow-up candidates (Metrics): review the remaining synchronous-blocking cap against intended library/host boundaries; inspect C&D hotspots in source before proposing decomposition; review dependency advisories and framework support in their affected projects. These are review leads. Earlier conclusions about intentional behavior were not reverified in source during this rerun.
