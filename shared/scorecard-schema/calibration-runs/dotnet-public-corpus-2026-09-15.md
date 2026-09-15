# Full .NET sample corpus: 2026-09-15

Fresh Release runs use the same tested, unpublished CodeMetrics.AI 2.3.0 package, schema v3, ruleset `dotnet-2026-09-14-short-circuit-completion`. Source commits and tracked source files were verified unchanged before and after analysis. SDKs, clones, caches and artifacts remain on E:.

All scores are deterministic within the partial scopes recorded below. Overall values are partial assessments with 9/9 dimensions scored. These are static assessments: target test suites, coverage collection, runtime security checks and performance benchmarks were not run. Prior explanations of target intent and build limitations are prior context, not reverified by these scorecard runs.

## Full scorecards

| Dimension | Polly | Newtonsoft.Json | SimplCommerce | OrchardCore | Dapper | Serilog | FluentValidation | Quartz.NET |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Architecture & SOLID | 5.6 | 4.1 | Unavailable | Unavailable | 5.2 | 6.3 | 5.4 | 4.5 |
| Complexity & Decomposition | 8 | 4.8 | Unavailable | Unavailable | 5.9 | 6 | 4.8 | 5.6 |
| Testing | 8 | 6 | Unavailable | Unavailable | 6 | 10 | 6 | 6 |
| Security | 10 | 10 | Unavailable | Unavailable | 10 | 10 | 10 | 2 |
| Error Handling | 4 | 9.5 | Unavailable | Unavailable | 4 | 9.2 | 10 | 8.9 |
| Documentation | 9 | 2 | Unavailable | Unavailable | 6 | 6 | 8 | 6 |
| Dependency Management | 4 | 4 | Unavailable | Unavailable | 0 | 4 | 4 | 4 |
| Performance & Async | 2 | 10 | Unavailable | Unavailable | 2 | 10 | 10 | 10 |
| Maintainability | 8.2 | 6.9 | Unavailable | Unavailable | 7.7 | 8 | 7.5 | 7 |
| **Overall, partial assessment (9/9)** | 6.5 | 6.4 | Unavailable | Unavailable | 5.2 | 7.7 | 7.3 | 6 |
| Previous overall, partial assessment (9/9) | 6.3 | 6.1 | Unavailable | Unavailable | 5.2 | 7.7 | 7.3 | 6 |

## Comparisons and scope

The baseline for each repository is its latest complete run captured before this batch. The first four repositories last used the September 13 wait-boundaries ruleset, so their changes include subsequent source/package population, benchmark scope, explicit anonymous intent, switch-completion and short-circuit corrections. Dapper, Serilog and FluentValidation last used the September 14 scope/intent ruleset. Quartz already used this short-circuit ruleset. Numerical ladders were not changed for this batch. Different rulesets are not compatible baseline gates; descriptive changes do not establish target-code improvement. Live package feeds can change independently of source.

| Repository | Raw CSV identical | Population identical | Filters identical | Configuration fingerprint identical |
|---|---|---|---|---|
| Polly | True | True | True | True |
| Newtonsoft.Json | True | True | True | True |
| Dapper | True | True | True | True |
| Serilog | True | True | True | True |
| FluentValidation | True | True | True | True |
| Quartz.NET | True | True | True | True |

## Verification

- 6/8 runs completed with fresh matching invocation/evidence run and audit IDs, analyzer and validator exits 0, and usable inspections.
- Local package SHA256: `f48f3aec62caea5d75591e390f5df0a84b40ea3e2e10e9d14ed8eb86a9b02d11`.
- [Package](E:/repos/CodeMetrics.AI/TestResults/public-corpus-wave2/short-circuit-package/CodeMetrics.AI.2.3.0.nupkg). Packaged and installed DLL/catalog bytes match the tested Release output.
- Prior package validation on September 14: 1,095 tests, 24 new short-circuit regressions, six calibration fixtures and formatting checks passed. That suite was not repeated for this unchanged-package corpus rerun.
- Failed/incomplete results, if any, are unavailable and never recovered from CSV or old evidence.

## Per-repository evidence

### Polly

- Source commit `1a80392b1f093f40e59c515f4aeb989bea5db857`; `Polly.slnx`, Release.
- runId `93f2989b-78d0-4f75-9660-c96d58ceff97`; auditId `9ccd7d4a-2943-412b-9291-02e0f6d99495`.
- Previous runId `80720b91-6135-429e-a6b0-ac5941a5c6d8`; auditId `73f60b32-f982-4bd8-96de-bd1db407fd0e`; ruleset `dotnet-2026-09-13-wait-boundaries`.
- Config fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; baseline calibration; schema 3; complete/fresh/usable.
- 21/53 analyzed units; 1858 type and 10988 member observations (not unique source counts).
- [evidence](E:/repos/Polly/.scorecard/dotnet/runs/93f2989b-78d0-4f75-9660-c96d58ceff97/evidence.json), [inspection](E:/repos/Polly/.scorecard/dotnet/runs/93f2989b-78d0-4f75-9660-c96d58ceff97/inspection.json), [metrics](E:/repos/Polly/.scorecard/dotnet/runs/93f2989b-78d0-4f75-9660-c96d58ceff97/metrics.csv).

| Dimension | Previous | Current | Scope includes | Recorded basis |
|---|---:|---:|---|---|
| Architecture & SOLID | 5.6 | 5.6 | static-coupling-and-project-structure | Findings: 61 (errors: 0, warnings: 61). Cycles: 0, hotspots: 61 (showing 10). Excluded passive data carriers: 242, DI extension types: 5, framework coupling archetypes: 0, application composition roots: 0. coupling: 57/1611 eligible types, score 5.58; complexity: 0/1611 eligible types, score 10.00; size: 4/1611 eligible types, score 5.97. Final = min(metric score 5.58, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | 8 | 8 | production-type-complexity, member-complexity | Eligible types: 1242. Passive data carriers excluded: 242. Method complexity: 7.9, Decomposition: 8. Combined: 8. |
| Testing | 8 | 8 | test-project-signals, supplied-coverage-report | testProjects=5, testMethods=2632, skipped=1, placeholders=0, assertions=8710, assertionDensity=3.31, uncoveredProjects=0, coverageFile=False, lineRate=n/a. |
| Security | 10 | 10 | static-security-patterns, production-and-unknown-project-dependency-vulnerabilities | Findings: 0 distinct source sites from 0 observations (errors: 0, warnings: 0). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | 4 | 4 | static-exception-patterns | Distinct source findings: 17 across 79 project/framework observations (errors: 0, warnings: 13). emptyCatch=0, throwEx=0, broadDefaults=0. Affected catches: 7/48; documented catches: 5. |
| Documentation | 9 | 9 | documentation-presence-and-content-signals | hasReadme=True, readmeNonBlankLines=405, hasDocsDir=True, architectureDocs=0, hasAiInstructions=True, libraryXmlDocRatio=1.00, publicApiDocCoverage=0.96, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | 2 | 4 | package-version-and-feed-observations, production-and-development-project-dependencies | vulnerableDirect=0, vulnerableTransitive=0, outdated=27, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=0, outdatedFrameworkCompatibilityUnknown=0, deprecated=0, unsupportedTFMs=1, versionDrift=0, cpmEnabled=True, anyCommandFailed=False. |
| Performance & Async | 2 | 2 | static-async-and-blocking-patterns | Distinct source findings: 18 across 71 project/framework observations (errors: 4, warnings: 0, unscored review leads: 14). syncOverAsync=8, threadSleep=1, saveChangesInsideLoop=0, missingCancellationToken=9, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | 8.2 | 8.2 | production-executable-function-maintainability-index | Distinct executable functions: 2218. Weakest 444: 5.68; remaining 1774: 9.90. Each function contributes once; distribution statistics are diagnostic only. |

Finding changes (observations can disappear through consolidation as well as exclusion):

| Dimension | Previous count | Current count | Removed/consolidated | Added | Reclassified |
|---|---:|---:|---:|---:|---:|
| Dependency Management | 146 | 163 | 3 | 20 | 0 |

Dependency Management selected scoring conditions: previous multipleUnsupportedFrameworks: 2; current manyOutdated: 4.

### Newtonsoft.Json

- Source commit `09bb545d72969ad7fb4ea07db0d5c34f4fc07877`; `Src\Newtonsoft.Json.slnx`, Release.
- runId `fb53b98d-4fe9-46a3-87f4-e395eb4aba3a`; auditId `3b5747ab-d8e9-416e-8dad-8331db1b697f`.
- Previous runId `6a90cb04-b6a2-4281-84b0-1b3a6554e50c`; auditId `bbf5b02a-540b-41ac-8ae0-6ddea5533995`; ruleset `dotnet-2026-09-13-wait-boundaries`.
- Config fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; baseline calibration; schema 3; complete/fresh/usable.
- 9/16 analyzed units; 1707 type and 25381 member observations (not unique source counts).
- [evidence](E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/fb53b98d-4fe9-46a3-87f4-e395eb4aba3a/evidence.json), [inspection](E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/fb53b98d-4fe9-46a3-87f4-e395eb4aba3a/inspection.json), [metrics](E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/fb53b98d-4fe9-46a3-87f4-e395eb4aba3a/metrics.csv).

| Dimension | Previous | Current | Scope includes | Recorded basis |
|---|---:|---:|---|---|
| Architecture & SOLID | 4.1 | 4.1 | static-coupling-and-project-structure | Findings: 359 (errors: 0, warnings: 359). Cycles: 0, hotspots: 359 (showing 10). Excluded passive data carriers: 92, DI extension types: 0, framework coupling archetypes: 0, application composition roots: 2. coupling: 256/1613 eligible types, score 4.10; complexity: 14/1615 eligible types, score 9.03; size: 89/1615 eligible types, score 5.34. Final = min(metric score 4.10, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | 4.8 | 4.8 | production-type-complexity, member-complexity | Eligible types: 1402. Passive data carriers excluded: 92. Method complexity: 5.6, Decomposition: 4. Combined: 4.8. |
| Testing | 6 | 6 | test-project-signals, supplied-coverage-report | testProjects=1, testMethods=3252, skipped=0, placeholders=0, assertions=13027, assertionDensity=4.01, uncoveredProjects=1, coverageFile=False, lineRate=n/a. |
| Security | 10 | 10 | static-security-patterns, production-and-unknown-project-dependency-vulnerabilities | Findings: 0 distinct source sites from 0 observations (errors: 0, warnings: 0). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | 9.5 | 9.5 | static-exception-patterns | Distinct source findings: 8 across 28 project/framework observations (errors: 2, warnings: 5). emptyCatch=0, throwEx=0, broadDefaults=2. Affected catches: 4/50; documented catches: 8. |
| Documentation | 2 | 2 | documentation-presence-and-content-signals | hasReadme=True, readmeNonBlankLines=10, hasDocsDir=False, architectureDocs=0, hasAiInstructions=False, libraryXmlDocRatio=1.00, publicApiDocCoverage=0.99, staleMarkers=0, unresolvedCrefs=2. |
| Dependency Management | 2 | 4 | package-version-and-feed-observations, production-and-development-project-dependencies | vulnerableDirect=0, vulnerableTransitive=0, outdated=8, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=22, outdatedFrameworkCompatibilityUnknown=0, deprecated=2, unsupportedTFMs=1, versionDrift=0, cpmEnabled=False, anyCommandFailed=False. |
| Performance & Async | 10 | 10 | static-async-and-blocking-patterns | Distinct source findings: 0 across 0 project/framework observations (errors: 0, warnings: 0, unscored review leads: 0). syncOverAsync=0, threadSleep=0, saveChangesInsideLoop=0, missingCancellationToken=0, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | 6.9 | 6.9 | production-executable-function-maintainability-index | Distinct executable functions: 3448. Weakest 690: 3.01; remaining 2758: 9.52. Each function contributes once; distribution statistics are diagnostic only. |

Finding changes (observations can disappear through consolidation as well as exclusion):

| Dimension | Previous count | Current count | Removed/consolidated | Added | Reclassified |
|---|---:|---:|---:|---:|---:|
| Dependency Management | 43 | 42 | 1 | 0 | 0 |

Nonblocking diagnostics: {"workspaceWarning": 1}. Full messages remain in the linked evidence.

Dependency Management selected scoring conditions: previous multipleUnsupportedFrameworks: 2; current deprecatedPackage: 4.

### SimplCommerce

Deterministic assessment unavailable; no current score or overall is reported.

- Source `3472ba02a6f2d9b6bdca7f7fb84957176aa799dc`; `SimplCommerce.sln`, Release.
- runId `4a368ef0-49a0-4c19-a6ff-e183cb921df5`; auditId `ef241d63-7c2d-45ca-9e66-f81c5f9aea9d`. Fresh identity verified; inspection unusable; analyzer/validator exits 2.
- Previous runId `3eb3c45f-a4e2-4e5e-9db8-4698d4587466`; auditId `dbc0da5b-4e4f-4a89-874b-e74f34b542a2`.
- [evidence](E:/repos/SimplCommerce/.scorecard/dotnet/runs/4a368ef0-49a0-4c19-a6ff-e183cb921df5/evidence.json), [inspection](E:/repos/SimplCommerce/.scorecard/dotnet/runs/4a368ef0-49a0-4c19-a6ff-e183cb921df5/inspection.json), [metrics](E:/repos/SimplCommerce/.scorecard/dotnet/runs/4a368ef0-49a0-4c19-a6ff-e183cb921df5/metrics.csv). Diagnostic artifacts only; they do not restore scores.

| Package | Latest-version evidence | Failure | Affected observations |
|---|---|---|---:|
| IdentityServer4.AspNetIdentity | Not found at the sources | latestVersionUnavailable | 1 |

### OrchardCore

Deterministic assessment unavailable; no current score or overall is reported.

- Source `4c101f5c6a6e6aca073a800797a223a958b28c0b`; `OrchardCore.slnx`, Release.
- runId `5e6fb9db-4181-4028-be75-37ab22458441`; auditId `0068c238-cd83-40f6-be92-e95db4d3a57d`. Fresh identity verified; inspection unusable; analyzer/validator exits 2.
- Previous runId `a818413b-6095-472b-8b97-ce8e53aa52af`; auditId `b6baed34-5995-40a3-b263-d0b0cd45f1da`.
- [evidence](E:/repos/OrchardCore/.scorecard/dotnet/runs/5e6fb9db-4181-4028-be75-37ab22458441/evidence.json), [inspection](E:/repos/OrchardCore/.scorecard/dotnet/runs/5e6fb9db-4181-4028-be75-37ab22458441/inspection.json), [metrics](E:/repos/OrchardCore/.scorecard/dotnet/runs/5e6fb9db-4181-4028-be75-37ab22458441/metrics.csv). Diagnostic artifacts only; they do not restore scores.

| Package | Latest-version evidence | Failure | Affected observations |
|---|---|---|---:|
| Lucene.Net.Analysis.Common | Not found at the sources | latestVersionUnavailable | 1 |
| Lucene.Net.QueryParser | Not found at the sources | latestVersionUnavailable | 2 |
| Lucene.Net.Spatial | Not found at the sources | latestVersionUnavailable | 2 |
| Microsoft.Playwright | 1.62.0 | packageSizeLimit | 1 |

### Dapper

- Source commit `8becae8d0e2b360165ae03c0d5d1330b0273473d`; `Dapper.slnx`, Release.
- runId `e978e3a3-fec9-42f3-a102-5104ea41fa99`; auditId `d530f853-2b93-4018-9b21-4782d1f6f61d`.
- Previous runId `069be269-f6df-4fab-9218-d499e80e0b16`; auditId `3b342ea8-50b2-46c6-9704-54dcde1a13e7`; ruleset `dotnet-2026-09-14-scope-explicit-intent`.
- Config fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; baseline calibration; schema 3; complete/fresh/usable.
- 20/25 analyzed units; 555 type and 7222 member observations (not unique source counts).
- [evidence](E:/repos/Dapper/.scorecard/dotnet/runs/e978e3a3-fec9-42f3-a102-5104ea41fa99/evidence.json), [inspection](E:/repos/Dapper/.scorecard/dotnet/runs/e978e3a3-fec9-42f3-a102-5104ea41fa99/inspection.json), [metrics](E:/repos/Dapper/.scorecard/dotnet/runs/e978e3a3-fec9-42f3-a102-5104ea41fa99/metrics.csv).

| Dimension | Previous | Current | Scope includes | Recorded basis |
|---|---:|---:|---|---|
| Architecture & SOLID | 5.2 | 5.2 | static-coupling-and-project-structure | Findings: 43 (errors: 0, warnings: 43). Cycles: 0, hotspots: 43 (showing 10). Excluded passive data carriers: 30, DI extension types: 0, framework coupling archetypes: 0, application composition roots: 0. coupling: 35/525 eligible types, score 5.20; complexity: 0/525 eligible types, score 10.00; size: 8/525 eligible types, score 5.82. Final = min(metric score 5.20, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | 5.9 | 5.9 | production-type-complexity, member-complexity | Eligible types: 429. Passive data carriers excluded: 30. Method complexity: 5.8, Decomposition: 6. Combined: 5.9. |
| Testing | 6 | 6 | test-project-signals, supplied-coverage-report | testProjects=1, testMethods=389, skipped=1, placeholders=0, assertions=1228, assertionDensity=3.16, uncoveredProjects=7, coverageFile=False, lineRate=n/a. |
| Security | 10 | 10 | static-security-patterns, production-and-unknown-project-dependency-vulnerabilities | Findings: 0 distinct source sites from 0 observations (errors: 0, warnings: 0). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | 4 | 4 | static-exception-patterns | Distinct source findings: 14 across 62 project/framework observations (errors: 6, warnings: 8). emptyCatch=4, throwEx=0, broadDefaults=2. Affected catches: 8/19; documented catches: 9. |
| Documentation | 6 | 6 | documentation-presence-and-content-signals | hasReadme=True, readmeNonBlankLines=373, hasDocsDir=True, architectureDocs=0, hasAiInstructions=False, libraryXmlDocRatio=0.85, publicApiDocCoverage=0.90, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | 0 | 0 | package-version-and-feed-observations, production-and-development-project-dependencies | vulnerableDirect=1, vulnerableTransitive=1, outdated=25, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=4, outdatedFrameworkCompatibilityUnknown=0, deprecated=3, unsupportedTFMs=1, versionDrift=0, cpmEnabled=True, anyCommandFailed=False. |
| Performance & Async | 2 | 2 | static-async-and-blocking-patterns | Distinct source findings: 5 across 25 project/framework observations (errors: 1, warnings: 0, unscored review leads: 4). syncOverAsync=1, threadSleep=0, saveChangesInsideLoop=0, missingCancellationToken=4, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | 7.7 | 7.7 | production-executable-function-maintainability-index | Distinct executable functions: 949. Weakest 190: 4.29; remaining 759: 9.93. Each function contributes once; distribution statistics are diagnostic only. |

Finding changes (observations can disappear through consolidation as well as exclusion):

| Dimension | Previous count | Current count | Removed/consolidated | Added | Reclassified |
|---|---:|---:|---:|---:|---:|

Nonblocking diagnostics: {"workspaceWarning": 10}. Full messages remain in the linked evidence.

### Serilog

- Source commit `bebc7719004f76187ae72e64ce138ec2540f2070`; `Serilog.sln`, Release.
- runId `e9ac37ef-b4fb-4bcc-b38c-04f33504ac17`; auditId `4b0fc4b9-2ee3-48af-bde5-92bdcba8b36f`.
- Previous runId `a63c3361-1cf7-4750-bf2e-6c7a1e903142`; auditId `6cf39ad9-b834-4d9e-ab1f-19e0e2b74af3`; ruleset `dotnet-2026-09-14-scope-explicit-intent`.
- Config fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; baseline calibration; schema 3; complete/fresh/usable.
- 7/19 analyzed units; 790 type and 6812 member observations (not unique source counts).
- [evidence](E:/repos/serilog/.scorecard/dotnet/runs/e9ac37ef-b4fb-4bcc-b38c-04f33504ac17/evidence.json), [inspection](E:/repos/serilog/.scorecard/dotnet/runs/e9ac37ef-b4fb-4bcc-b38c-04f33504ac17/inspection.json), [metrics](E:/repos/serilog/.scorecard/dotnet/runs/e9ac37ef-b4fb-4bcc-b38c-04f33504ac17/metrics.csv).

| Dimension | Previous | Current | Scope includes | Recorded basis |
|---|---:|---:|---|---|
| Architecture & SOLID | 6.3 | 6.3 | static-coupling-and-project-structure | Findings: 63 (errors: 0, warnings: 63). Cycles: 0, hotspots: 63 (showing 10). Excluded passive data carriers: 21, DI extension types: 0, framework coupling archetypes: 0, application composition roots: 0. coupling: 56/769 eligible types, score 6.33; complexity: 0/769 eligible types, score 10.00; size: 7/769 eligible types, score 9.78. Final = min(metric score 6.33, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | 6 | 6 | production-type-complexity, member-complexity | Eligible types: 615. Passive data carriers excluded: 21. Method complexity: 7.3, Decomposition: 4.7. Combined: 6. |
| Testing | 10 | 10 | test-project-signals, supplied-coverage-report | testProjects=3, testMethods=437, skipped=0, placeholders=0, assertions=825, assertionDensity=1.89, uncoveredProjects=0, coverageFile=False, lineRate=n/a. |
| Security | 10 | 10 | static-security-patterns, production-and-unknown-project-dependency-vulnerabilities | Findings: 0 distinct source sites from 0 observations (errors: 0, warnings: 0). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | 9.2 | 9.2 | static-exception-patterns | Distinct source findings: 5 across 26 project/framework observations (errors: 0, warnings: 4). emptyCatch=0, throwEx=0, broadDefaults=0. Affected catches: 3/19; documented catches: 4. |
| Documentation | 6 | 6 | documentation-presence-and-content-signals | hasReadme=True, readmeNonBlankLines=84, hasDocsDir=False, architectureDocs=0, hasAiInstructions=False, libraryXmlDocRatio=1.00, publicApiDocCoverage=1.00, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | 4 | 4 | package-version-and-feed-observations, production-and-development-project-dependencies | vulnerableDirect=0, vulnerableTransitive=0, outdated=15, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=1, outdatedFrameworkCompatibilityUnknown=0, deprecated=1, unsupportedTFMs=1, versionDrift=1, cpmEnabled=False, anyCommandFailed=False. |
| Performance & Async | 10 | 10 | static-async-and-blocking-patterns | Distinct source findings: 1 across 7 project/framework observations (errors: 0, warnings: 0, unscored review leads: 1). syncOverAsync=1, threadSleep=0, saveChangesInsideLoop=0, missingCancellationToken=0, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | 8 | 8 | production-executable-function-maintainability-index | Distinct executable functions: 871. Weakest 175: 5.01; remaining 696: 9.97. Each function contributes once; distribution statistics are diagnostic only. |

Finding changes (observations can disappear through consolidation as well as exclusion):

| Dimension | Previous count | Current count | Removed/consolidated | Added | Reclassified |
|---|---:|---:|---:|---:|---:|

### FluentValidation

- Source commit `fa9787a0d4fd0c0b99f9682292d76d5ad97ba6cb`; `FluentValidation.sln`, Release.
- runId `1ad7336a-879f-4a8d-9b0c-0102a638ef56`; auditId `4922b681-4798-41bf-8cc2-4e60f1780ee6`.
- Previous runId `4b49d28c-b358-4c5a-a66a-2ca0ad6143a7`; auditId `17c6e4b5-6ed1-492f-bf49-e61afb334b1a`; ruleset `dotnet-2026-09-14-scope-explicit-intent`.
- Config fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; baseline calibration; schema 3; complete/fresh/usable.
- 2/6 analyzed units; 196 type and 1017 member observations (not unique source counts).
- [evidence](E:/repos/FluentValidation/.scorecard/dotnet/runs/1ad7336a-879f-4a8d-9b0c-0102a638ef56/evidence.json), [inspection](E:/repos/FluentValidation/.scorecard/dotnet/runs/1ad7336a-879f-4a8d-9b0c-0102a638ef56/inspection.json), [metrics](E:/repos/FluentValidation/.scorecard/dotnet/runs/1ad7336a-879f-4a8d-9b0c-0102a638ef56/metrics.csv).

| Dimension | Previous | Current | Scope includes | Recorded basis |
|---|---:|---:|---|---|
| Architecture & SOLID | 5.4 | 5.4 | static-coupling-and-project-structure | Findings: 9 (errors: 0, warnings: 9). Cycles: 0, hotspots: 9. Excluded passive data carriers: 2, DI extension types: 1, framework coupling archetypes: 0, application composition roots: 0. coupling: 9/193 eligible types, score 5.44; complexity: 0/193 eligible types, score 10.00; size: 0/193 eligible types, score 10.00. Final = min(metric score 5.44, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | 4.8 | 4.8 | production-type-complexity, member-complexity | Eligible types: 142. Passive data carriers excluded: 2. Method complexity: 5.6, Decomposition: 4. Combined: 4.8. |
| Testing | 6 | 6 | test-project-signals, supplied-coverage-report | testProjects=1, testMethods=828, skipped=1, placeholders=0, assertions=1319, assertionDensity=1.59, uncoveredProjects=1, coverageFile=False, lineRate=n/a. |
| Security | 10 | 10 | static-security-patterns, production-and-unknown-project-dependency-vulnerabilities | Findings: 0 distinct source sites from 0 observations (errors: 0, warnings: 0). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | 10 | 10 | static-exception-patterns | Distinct source findings: 0 across 0 project/framework observations (errors: 0, warnings: 0). emptyCatch=0, throwEx=0, broadDefaults=0. Affected catches: 0/4; documented catches: 1. |
| Documentation | 8 | 8 | documentation-presence-and-content-signals | hasReadme=True, readmeNonBlankLines=50, hasDocsDir=True, architectureDocs=0, hasAiInstructions=False, libraryXmlDocRatio=1.00, publicApiDocCoverage=0.62, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | 4 | 4 | package-version-and-feed-observations, production-and-development-project-dependencies | vulnerableDirect=0, vulnerableTransitive=0, outdated=11, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=0, outdatedFrameworkCompatibilityUnknown=0, deprecated=1, unsupportedTFMs=0, versionDrift=0, cpmEnabled=False, anyCommandFailed=False. |
| Performance & Async | 10 | 10 | static-async-and-blocking-patterns | Distinct source findings: 0 across 0 project/framework observations (errors: 0, warnings: 0, unscored review leads: 0). syncOverAsync=0, threadSleep=0, saveChangesInsideLoop=0, missingCancellationToken=0, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | 7.5 | 7.5 | production-executable-function-maintainability-index | Distinct executable functions: 852. Weakest 171: 3.79; remaining 681: 9.92. Each function contributes once; distribution statistics are diagnostic only. |

Finding changes (observations can disappear through consolidation as well as exclusion):

| Dimension | Previous count | Current count | Removed/consolidated | Added | Reclassified |
|---|---:|---:|---:|---:|---:|

### Quartz.NET

- Source commit `97afe142210e9c616b434ec7d617286a2752e9d4`; `Quartz.slnx`, Release.
- runId `121405ff-1393-4a4e-adee-f0312cf7e393`; auditId `0def164c-fbe6-467c-9934-395d5e50d2ca`.
- Previous runId `b1eb3e16-c3a6-4c18-ab43-3c05a7e787a0`; auditId `aeb58165-c749-4ed1-adf5-fb69a2afc0e4`; ruleset `dotnet-2026-09-14-short-circuit-completion`.
- Config fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; baseline calibration; schema 3; complete/fresh/usable.
- 20/30 analyzed units; 903 type and 8059 member observations (not unique source counts).
- [evidence](E:/repos/quartznet/.scorecard/dotnet/runs/121405ff-1393-4a4e-adee-f0312cf7e393/evidence.json), [inspection](E:/repos/quartznet/.scorecard/dotnet/runs/121405ff-1393-4a4e-adee-f0312cf7e393/inspection.json), [metrics](E:/repos/quartznet/.scorecard/dotnet/runs/121405ff-1393-4a4e-adee-f0312cf7e393/metrics.csv).

| Dimension | Previous | Current | Scope includes | Recorded basis |
|---|---:|---:|---|---|
| Architecture & SOLID | 4.5 | 4.5 | static-coupling-and-project-structure | Findings: 99 (errors: 0, warnings: 99). Cycles: 0, hotspots: 99 (showing 10). Excluded passive data carriers: 154, DI extension types: 4, framework coupling archetypes: 1, application composition roots: 5. coupling: 92/739 eligible types, score 4.51; complexity: 1/745 eligible types, score 9.96; size: 6/745 eligible types, score 5.90. Final = min(metric score 4.51, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | 5.6 | 5.6 | production-type-complexity, member-complexity | Eligible types: 605. Passive data carriers excluded: 154. Method complexity: 5.8, Decomposition: 5.3. Combined: 5.6. |
| Testing | 6 | 6 | test-project-signals, supplied-coverage-report | testProjects=3, testMethods=4355, skipped=0, placeholders=0, assertions=10502, assertionDensity=2.41, uncoveredProjects=19, coverageFile=False, lineRate=n/a. |
| Security | 2 | 2 | static-security-patterns, production-and-unknown-project-dependency-vulnerabilities | Findings: 7 distinct source sites from 7 observations (errors: 7, warnings: 0). hardcodedSecrets=1, rawSql=6, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | 8.9 | 8.9 | static-exception-patterns | Distinct source findings: 277 across 277 project/framework observations (errors: 24, warnings: 55). emptyCatch=10, throwEx=0, broadDefaults=14. Affected catches: 61/349; documented catches: 100. |
| Documentation | 6 | 6 | documentation-presence-and-content-signals | hasReadme=True, readmeNonBlankLines=41, hasDocsDir=True, architectureDocs=0, hasAiInstructions=True, libraryXmlDocRatio=0.77, publicApiDocCoverage=1.00, staleMarkers=0, unresolvedCrefs=1. |
| Dependency Management | 4 | 4 | package-version-and-feed-observations, production-and-development-project-dependencies | vulnerableDirect=0, vulnerableTransitive=0, outdated=35, outdatedAspireExcluded=12, outdatedFrameworkIncompatibleExcluded=0, outdatedFrameworkCompatibilityUnknown=0, deprecated=0, unsupportedTFMs=0, versionDrift=0, cpmEnabled=True, anyCommandFailed=False. |
| Performance & Async | 10 | 10 | static-async-and-blocking-patterns | Distinct source findings: 7 across 7 project/framework observations (errors: 0, warnings: 0, unscored review leads: 7). syncOverAsync=3, threadSleep=0, saveChangesInsideLoop=0, missingCancellationToken=0, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=4, sharedStateMutationInFanOut=0. |
| Maintainability | 7 | 7 | production-executable-function-maintainability-index | Distinct executable functions: 6022. Weakest 1205: 3.37; remaining 4817: 9.49. Each function contributes once; distribution statistics are diagnostic only. |

Finding changes (observations can disappear through consolidation as well as exclusion):

| Dimension | Previous count | Current count | Removed/consolidated | Added | Reclassified |
|---|---:|---:|---:|---:|---:|

## Review priorities

1. **Missing latest-version evidence (Current context).** SimplCommerce IdentityServer4.AspNetIdentity 4.1.2 is unlisted according to NuGet registration, and a separate dotnet package query reproduced the same missing-version result. The previous baseline already contained it; this is newly enforced availability behavior, not a new source defect. OrchardCore Lucene.Net.QueryParser and Lucene.Net.Spatial list only prereleases, and Lucene.Net.Analysis.Common 4.9.0 is unlisted. Next: specify and test how unlisted and prerelease packages should be assessed when no stable upgrade candidate is returned. Preserve unknown compatibility rather than inventing an upgrade.
2. **Large-package metadata inspection (Current context).** OrchardCore Microsoft.Playwright 1.62.0 hits packageSizeLimit. A HEAD request confirms a 211,404,073-byte compressed package. Next: review bounded metadata inspection for packages with bundled tooling; avoid solving it by simply removing resource limits.
3. **Remaining scored waits (Metrics).** Polly and Dapper retain their previous Performance & Async penalties. The completion correction does not remove them. Review the recorded sites and execution contracts before changing classification; this batch does not reverify prior explanations of intent.

Failure checks: [SimplCommerce independent NuGet query](E:/repos/CodeMetrics.AI/TestResults/public-corpus-2026-09-15/SimplCommerce-outdated-recheck.json), [IdentityServer registration](E:/repos/CodeMetrics.AI/TestResults/public-corpus-2026-09-15/IdentityServer4.AspNetIdentity-registration.json), [OrchardCore feed checks](E:/repos/CodeMetrics.AI/TestResults/public-corpus-2026-09-15/OrchardCore-feed-checks.json), [Lucene registration](E:/repos/CodeMetrics.AI/TestResults/public-corpus-2026-09-15/Lucene.Net.Analysis.Common-registration.json). The live registration endpoints are [IdentityServer4.AspNetIdentity 4.1.2](https://api.nuget.org/v3/registration5-semver1/identityserver4.aspnetidentity/4.1.2.json) and [Lucene.Net.Analysis.Common 4.9.0](https://api.nuget.org/v3/registration5-semver1/lucene.net.analysis.common/4.9.0.json).
The companion JSON records removed, added and reclassified findings with source locations and both run identities. No analyzer policy, source code, numerical ladder or target package was changed during this rerun.

[Machine-readable comparisons](dotnet-public-corpus-2026-09-15.json).
