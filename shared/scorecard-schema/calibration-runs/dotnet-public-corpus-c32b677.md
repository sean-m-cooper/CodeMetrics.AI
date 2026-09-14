# Committed async-classification evaluation

Fresh full Release evaluations of the four pinned public repositories using CodeMetrics.AI 2.3.0, schema 3, built from commit `c32b6771a7e41c12dc25b56e24a11fa07a0311d6` on `codex/public-corpus-fidelity`. The analyzer source was unchanged during evaluation. Ruleset: `dotnet-2026-09-13-wait-boundaries`.

Package SHA256: `4aa1c87a325f2f48df4b75a1fa5d0a2f3fa940ce3f2fbc76adab94980d829dbe`.

All 956 .NET tests, six labeled calibration fixtures, formatting verification and whitespace checks passed. The packaged analyzer DLL matches the tested build. Calibration scores and finding baselines are unchanged.

## Scorecards

All scores are deterministic within partial static scopes. Overall values are **partial assessments, 9/9 dimensions scored**. No coverage reports were supplied, and target test suites and runtime benchmarks were not executed. Calibration `baseline` describes regression fixtures, not empirical cross-ecosystem calibration.

| Dimension | Polly | Newtonsoft.Json | SimplCommerce | OrchardCore |
|---|---:|---:|---:|---:|
| Architecture & SOLID | 5.6 | 4.1 | 2 | 4 |
| Complexity & Decomposition | 8 | 4.8 | 7.3 | 5.3 |
| Testing | 8 | 6 | 6 | 6 |
| Security | 10 | 10 | 2 | 2 |
| Error Handling | 4 | 9.5 | 10 | 4 |
| Documentation | 9 | 2 | 3 | 4 |
| Dependency Management | 2 | 2 | 2 | 2 |
| Performance & Async | 2 | 10 | 10 | 0 |
| Maintainability | 8.2 | 6.9 | 7.3 | 7.3 |
| **Overall (partial, 9/9)** | 6.3 | 6.1 | 5.5 | 3.8 |

## Regression comparison

Baseline: [the validated pre-commit wait-boundary run](dotnet-public-corpus-wait-boundaries.md). Every current invocation is fresh and complete, has analyzer/validation exit 0, and matches the run/audit IDs in its usable inspection. Repository commits, tracked source, populations, filters, configuration fingerprints and raw CSV files match the baseline.

| Repository | Scores changed | Performance errors | Review leads | Performance finding changes | Dependency observations changed |
|---|---|---:|---:|---:|---|
| Polly | None | 4 | 14 | 0 | False |
| Newtonsoft.Json | None | 0 | 0 | 0 | False |
| SimplCommerce | None | 0 | 15 | 0 | False |
| OrchardCore | None | 15 | 531 | 0 | True |

All eight dimensions outside Dependency Management match the prior evidence exactly, including findings and scoring decisions. Dependency queries ran afresh; any observed differences are recorded separately. This rerun tests reproducibility of the committed implementation and introduces no classification or numerical policy changes.

### OrchardCore dependency uncertainty

Unknown upgrade framework compatibility increased from **6 to 536 project/TFM
observations** across 52 distinct packages. Microsoft.SourceLink.GitHub and
Microsoft.CodeAnalysis.CSharp.CodeStyle account for 235 observations each. The
reported latest versions did not change between matched package/project/TFM rows.
Dependency commands completed successfully (`anyCommandFailed=false`), but the
evidence does not expose why compatibility became unknown. A specific network,
timeout, or metadata failure has not been established.

Scored outdated observations increased from 541 to 542, and framework-incompatible
exclusions fell from 1 to 0; Dependency Management remained 2. Preserve these unknowns
when reviewing upgrade candidates. The next useful investigation is compatibility
lookup reliability and failure-reason reporting. This run was retained rather than
replaced with a cleaner-looking result. The original package summary also counted
three Aspire-excluded findings; the counts above use the scored unknown population.
[Follow-up investigation and controlled reproductions](dotnet-orchard-dependency-compatibility-investigation.md).

## Declared scopes

Each dimension excludes runtime behavior and comprehensive human review. The exact per-repository scopes and basis are retained below and in the linked evidence.

| Dimension | Included signals |
|---|---|
| Architecture & SOLID | static-coupling-and-project-structure |
| Complexity & Decomposition | production-type-complexity, member-complexity |
| Testing | test-project-signals, supplied-coverage-report |
| Security | static-security-patterns, dependency-vulnerability-observations |
| Error Handling | static-exception-patterns |
| Documentation | documentation-presence-and-content-signals |
| Dependency Management | package-version-and-feed-observations |
| Performance & Async | static-async-and-blocking-patterns |
| Maintainability | production-executable-function-maintainability-index |

## Run provenance and evidence

### Polly

- Source commit: `1a80392b1f093f40e59c515f4aeb989bea5db857`.
- Entry point: `E:\repos\Polly\Polly.slnx`; Release; fresh, complete, usable.
- Run ID: `80720b91-6135-429e-a6b0-ac5941a5c6d8`; audit ID: `73f60b32-f982-4bd8-96de-bd1db407fd0e`.
- Ruleset: `dotnet-2026-09-13-wait-boundaries`; configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; calibration: `baseline`.
- Population: 1858 types, 10988 members. Units: 21/53 analyzed.
- Skips by reason: `{"Benchmark project": 6, "Sample / demo code": 1, "Test project": 20, "Test support / fixture": 5}`.
- Suppressions: `[]`.
- [Evidence](<E:/repos/Polly/.scorecard/dotnet/runs/80720b91-6135-429e-a6b0-ac5941a5c6d8/evidence.json>), [inspection](<E:/repos/Polly/.scorecard/dotnet/runs/80720b91-6135-429e-a6b0-ac5941a5c6d8/inspection.json>), [raw CSV](<E:/repos/Polly/.scorecard/dotnet/runs/80720b91-6135-429e-a6b0-ac5941a5c6d8/metrics.csv>).

| Dimension | Recorded evidence basis |
|---|---|
| Architecture & SOLID | Findings: 61 (errors: 0, warnings: 61). Cycles: 0, hotspots: 61 (showing 10). Excluded passive data carriers: 242, DI extension types: 5, framework coupling archetypes: 0, application composition roots: 0. coupling: 57/1611 eligible types, score 5.58; complexity: 0/1611 eligible types, score 10.00; size: 4/1611 eligible types, score 5.97. Final = min(metric score 5.58, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | Eligible types: 1242. Passive data carriers excluded: 242. Method complexity: 7.9, Decomposition: 8. Combined: 8. |
| Testing | testProjects=5, testMethods=2632, skipped=1, placeholders=0, assertions=8710, assertionDensity=3.31, uncoveredProjects=0, coverageFile=False, lineRate=n/a. |
| Security | Findings: 0 (errors: 0, warnings: 0). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | Distinct source findings: 17 across 79 project/framework observations (errors: 0, warnings: 13). emptyCatch=0, throwEx=0, broadDefaults=0. Affected catches: 7/48; documented catches: 5. |
| Documentation | hasReadme=True, readmeNonBlankLines=405, hasDocsDir=True, architectureDocs=0, hasAiInstructions=True, libraryXmlDocRatio=1.00, publicApiDocCoverage=0.96, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | vulnerableDirect=0, vulnerableTransitive=0, outdated=142, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=0, outdatedFrameworkCompatibilityUnknown=0, deprecated=0, unsupportedTFMs=4, versionDrift=0, cpmEnabled=True, anyCommandFailed=False. |
| Performance & Async | Distinct source findings: 18 across 71 project/framework observations (errors: 4, warnings: 0, unscored review leads: 14). syncOverAsync=8, threadSleep=1, saveChangesInsideLoop=0, missingCancellationToken=9, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | Distinct executable functions: 2218. Weakest 444: 5.68; remaining 1774: 9.90. Each function contributes once; distribution statistics are diagnostic only. |

Current analysis diagnostics:

- None.

### Newtonsoft.Json

- Source commit: `09bb545d72969ad7fb4ea07db0d5c34f4fc07877`.
- Entry point: `E:\repos\Newtonsoft.Json\Src\Newtonsoft.Json.slnx`; Release; fresh, complete, usable.
- Run ID: `6a90cb04-b6a2-4281-84b0-1b3a6554e50c`; audit ID: `bbf5b02a-540b-41ac-8ae0-6ddea5533995`.
- Ruleset: `dotnet-2026-09-13-wait-boundaries`; configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; calibration: `baseline`.
- Population: 1707 types, 25381 members. Units: 9/16 analyzed.
- Skips by reason: `{"Test project": 7}`.
- Suppressions: `[]`.
- [Evidence](<E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/6a90cb04-b6a2-4281-84b0-1b3a6554e50c/evidence.json>), [inspection](<E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/6a90cb04-b6a2-4281-84b0-1b3a6554e50c/inspection.json>), [raw CSV](<E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/6a90cb04-b6a2-4281-84b0-1b3a6554e50c/metrics.csv>).

| Dimension | Recorded evidence basis |
|---|---|
| Architecture & SOLID | Findings: 359 (errors: 0, warnings: 359). Cycles: 0, hotspots: 359 (showing 10). Excluded passive data carriers: 92, DI extension types: 0, framework coupling archetypes: 0, application composition roots: 2. coupling: 256/1613 eligible types, score 4.10; complexity: 14/1615 eligible types, score 9.03; size: 89/1615 eligible types, score 5.34. Final = min(metric score 4.10, graph/layering cap 10.0), rounded to 1 decimal. |
| Complexity & Decomposition | Eligible types: 1402. Passive data carriers excluded: 92. Method complexity: 5.6, Decomposition: 4. Combined: 4.8. |
| Testing | testProjects=1, testMethods=3252, skipped=0, placeholders=0, assertions=13027, assertionDensity=4.01, uncoveredProjects=1, coverageFile=False, lineRate=n/a. |
| Security | Findings: 0 (errors: 0, warnings: 0). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | Distinct source findings: 8 across 28 project/framework observations (errors: 2, warnings: 5). emptyCatch=0, throwEx=0, broadDefaults=2. Affected catches: 4/50; documented catches: 8. |
| Documentation | hasReadme=True, readmeNonBlankLines=10, hasDocsDir=False, architectureDocs=0, hasAiInstructions=False, libraryXmlDocRatio=1.00, publicApiDocCoverage=0.99, staleMarkers=0, unresolvedCrefs=2. |
| Dependency Management | vulnerableDirect=0, vulnerableTransitive=0, outdated=15, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=22, outdatedFrameworkCompatibilityUnknown=0, deprecated=3, unsupportedTFMs=2, versionDrift=0, cpmEnabled=False, anyCommandFailed=False. |
| Performance & Async | Distinct source findings: 0 across 0 project/framework observations (errors: 0, warnings: 0, unscored review leads: 0). syncOverAsync=0, threadSleep=0, saveChangesInsideLoop=0, missingCancellationToken=0, materializationBeforeQueryShape=0, awaitedIoInsideLoop=0, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | Distinct executable functions: 3448. Weakest 690: 3.01; remaining 2758: 9.52. Each function contributes once; distribution statistics are diagnostic only. |

Current analysis diagnostics:

- `{"kind": "workspaceWarning", "message": "Found project reference without a matching metadata reference: E:\\repos\\Newtonsoft.Json\\Src\\Newtonsoft.Json.Tests\\Newtonsoft.Json.Tests.csproj"}`.

### SimplCommerce

- Source commit: `3472ba02a6f2d9b6bdca7f7fb84957176aa799dc`.
- Entry point: `E:\repos\SimplCommerce\SimplCommerce.sln`; Release; fresh, complete, usable.
- Run ID: `3eb3c45f-a4e2-4e5e-9db8-4698d4587466`; audit ID: `dbc0da5b-4e4f-4a89-874b-e74f34b542a2`.
- Ruleset: `dotnet-2026-09-13-wait-boundaries`; configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; calibration: `baseline`.
- Population: 715 types, 3823 members. Units: 42/49 analyzed.
- Skips by reason: `{"Test project": 7}`.
- Suppressions: `[]`.
- [Evidence](<E:/repos/SimplCommerce/.scorecard/dotnet/runs/3eb3c45f-a4e2-4e5e-9db8-4698d4587466/evidence.json>), [inspection](<E:/repos/SimplCommerce/.scorecard/dotnet/runs/3eb3c45f-a4e2-4e5e-9db8-4698d4587466/inspection.json>), [raw CSV](<E:/repos/SimplCommerce/.scorecard/dotnet/runs/3eb3c45f-a4e2-4e5e-9db8-4698d4587466/metrics.csv>).

| Dimension | Recorded evidence basis |
|---|---|
| Architecture & SOLID | Findings: 212 (errors: 150, warnings: 62). Cycles: 0, hotspots: 62 (showing 10). Excluded passive data carriers: 216, DI extension types: 2, framework coupling archetypes: 2, application composition roots: 0. coupling: 60/495 eligible types, score 4.55; complexity: 0/497 eligible types, score 10.00; size: 2/497 eligible types, score 5.95. Final = min(metric score 4.55, graph/layering cap 2.0), rounded to 1 decimal. |
| Complexity & Decomposition | Eligible types: 391. Passive data carriers excluded: 216. Method complexity: 7.2, Decomposition: 7.3. Combined: 7.3. |
| Testing | testProjects=7, testMethods=34, skipped=0, placeholders=0, assertions=68, assertionDensity=2.00, uncoveredProjects=34, coverageFile=False, lineRate=n/a. |
| Security | Findings: 44 (errors: 2, warnings: 42). hardcodedSecrets=2, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=203. |
| Error Handling | Distinct source findings: 0 across 0 project/framework observations (errors: 0, warnings: 0). emptyCatch=0, throwEx=0, broadDefaults=0. Affected catches: 0/11; documented catches: 1. |
| Documentation | hasReadme=True, readmeNonBlankLines=81, hasDocsDir=False, architectureDocs=0, hasAiInstructions=False, libraryXmlDocRatio=0.00, publicApiDocCoverage=0.01, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | vulnerableDirect=0, vulnerableTransitive=203, outdated=55, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=12, outdatedFrameworkCompatibilityUnknown=1, deprecated=8, unsupportedTFMs=0, versionDrift=2, cpmEnabled=False, anyCommandFailed=False. |
| Performance & Async | Distinct source findings: 15 across 15 project/framework observations (errors: 0, warnings: 0, unscored review leads: 15). syncOverAsync=0, threadSleep=0, saveChangesInsideLoop=1, missingCancellationToken=7, materializationBeforeQueryShape=0, awaitedIoInsideLoop=7, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | Distinct executable functions: 2890. Weakest 578: 3.62; remaining 2312: 9.68. Each function contributes once; distribution statistics are diagnostic only. |

Current analysis diagnostics:

- None.

### OrchardCore

- Source commit: `4c101f5c6a6e6aca073a800797a223a958b28c0b`.
- Entry point: `E:\repos\OrchardCore\OrchardCore.slnx`; Release; fresh, complete, usable.
- Run ID: `a818413b-6095-472b-8b97-ce8e53aa52af`; audit ID: `b6baed34-5995-40a3-b263-d0b0cd45f1da`.
- Ruleset: `dotnet-2026-09-13-wait-boundaries`; configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; calibration: `baseline`.
- Population: 5568 types, 24989 members. Units: 213/239 analyzed.
- Skips by reason: `{"Benchmark project": 1, "Demo code": 1, "Excluded by solution build configuration": 3, "Test project": 4, "Test support / fixture": 17}`.
- Suppressions: `[]`.
- [Evidence](<E:/repos/OrchardCore/.scorecard/dotnet/runs/a818413b-6095-472b-8b97-ce8e53aa52af/evidence.json>), [inspection](<E:/repos/OrchardCore/.scorecard/dotnet/runs/a818413b-6095-472b-8b97-ce8e53aa52af/inspection.json>), [raw CSV](<E:/repos/OrchardCore/.scorecard/dotnet/runs/a818413b-6095-472b-8b97-ce8e53aa52af/metrics.csv>).

| Dimension | Recorded evidence basis |
|---|---|
| Architecture & SOLID | Findings: 754 (errors: 0, warnings: 754). Cycles: 0, hotspots: 752 (showing 10). Excluded passive data carriers: 975, DI extension types: 70, framework coupling archetypes: 1, application composition roots: 0. coupling: 744/4522 eligible types, score 4.03; complexity: 1/4523 eligible types, score 9.82; size: 7/4523 eligible types, score 7.19. Final = min(metric score 4.03, graph/layering cap 6.0), rounded to 1 decimal. |
| Complexity & Decomposition | Eligible types: 3632. Passive data carriers excluded: 975. Method complexity: 5.8, Decomposition: 4.7. Combined: 5.3. |
| Testing | testProjects=3, testMethods=1705, skipped=0, placeholders=0, assertions=4558, assertionDensity=2.67, uncoveredProjects=211, coverageFile=False, lineRate=n/a. |
| Security | Findings: 56 (errors: 0, warnings: 56). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=1. |
| Error Handling | Distinct source findings: 128 across 128 project/framework observations (errors: 21, warnings: 57). emptyCatch=13, throwEx=0, broadDefaults=8. Affected catches: 51/461; documented catches: 44. |
| Documentation | hasReadme=True, readmeNonBlankLines=50, hasDocsDir=False, architectureDocs=0, hasAiInstructions=True, libraryXmlDocRatio=0.01, publicApiDocCoverage=0.14, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | vulnerableDirect=0, vulnerableTransitive=1, outdated=542, outdatedAspireExcluded=3, outdatedFrameworkIncompatibleExcluded=0, outdatedFrameworkCompatibilityUnknown=536, deprecated=1, unsupportedTFMs=0, versionDrift=0, cpmEnabled=True, anyCommandFailed=False. |
| Performance & Async | Distinct source findings: 546 across 546 project/framework observations (errors: 15, warnings: 0, unscored review leads: 531). syncOverAsync=61, threadSleep=0, saveChangesInsideLoop=1, missingCancellationToken=278, materializationBeforeQueryShape=0, awaitedIoInsideLoop=192, unboundedWhenAll=14, sharedStateMutationInFanOut=0. |
| Maintainability | Distinct executable functions: 20079. Weakest 4016: 3.76; remaining 16063: 9.70. Each function contributes once; distribution statistics are diagnostic only. |

Current analysis diagnostics:

- `{"kind": "workspaceWarning", "message": "Duplicate source file 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\AnalyzerReleases.Shipped.md' in project 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\OrchardCore.SourceGenerators.csproj'"}`.
- `{"kind": "workspaceWarning", "message": "Duplicate source file 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\AnalyzerReleases.Unshipped.md' in project 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\OrchardCore.SourceGenerators.csproj'"}`.
- `{"kind": "workspaceWarning", "message": "Msbuild failed when processing the file 'E:\\repos\\OrchardCore\\test\\OrchardCore.Tests.Integration\\OrchardCore.Tests.Integration.csproj' with message: Package 'SSH.NET' 2025.1.0 has a known high severity vulnerability, https://github.com/advisories/GHSA-q939-rpr3-3284"}`.

Dependency metric changes: `{"outdated": {"after": 542, "before": 541}, "outdatedFrameworkCompatibilityUnknown": {"after": 536, "before": 6}, "outdatedFrameworkIncompatibleExcluded": {"after": 0, "before": 1}}`.

## Interpretation

The remaining waits are current metric findings. Their earlier business/lifecycle explanations are **prior context, not re-reviewed this run**; see the [35-site source review](dotnet-orchard-wait-review.md) and subsequent [classification reconciliation](dotnet-public-corpus-wait-boundaries.md). This evaluation does not independently establish runtime safety or recommend changing sample code. Review leads remain observable but excluded from scoring. The unchanged absolute cutoff still selects OrchardCore Performance & Async 0 with 15 scored errors.

## Reproduction artifacts

[Comparison and scoring decisions](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-c32b677/comparison.json>) and [verification record](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-c32b677/validation.json>). The exact package, runner, verifier, invocation manifests, test/calibration logs and report generator are retained alongside these files. Generated `.scorecard` artifacts remain local.
