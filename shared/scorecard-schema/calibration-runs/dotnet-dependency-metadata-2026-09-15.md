# Dependency metadata verification: 2026-09-15

Both previously unavailable assessments now complete successfully with matching fresh invocation/evidence identities and usable schema-v3 inspections. Local unpublished CodeMetrics.AI 2.3.0 uses ruleset `dotnet-2026-09-15-package-metadata`. Numerical ladders and target source files are unchanged.

## Scorecards

| Dimension | SimplCommerce | OrchardCore |
|---|---:|---:|
| Architecture & SOLID | 2 | 4 |
| Complexity & Decomposition | 7.3 | 5.3 |
| Testing | 6 | 6 |
| Security | 2 | 6 |
| Error Handling | 10 | 4 |
| Documentation | 3 | 4 |
| Dependency Management | 2 | 2 |
| Performance & Async | 10 | 0 |
| Maintainability | 7.3 | 7.3 |
| **Overall, partial assessment (9/9)** | 5.5 | 4.3 |

All scores are deterministic within their recorded partial scopes. Target test suites, coverage collection and runtime testing were not performed. The previous attempts were unavailable; their partial scores are not baselines. There is no claimed source-code improvement or compatible cross-ruleset gate.

## Verified behavior

- Exact NuGet no-candidate sentinels remain informational with no outdated penalty. Their compatibility is not applicable, not invented as compatible. Empty/malformed versions and unknown real candidates still withhold scoring. Stable-only querying is unchanged; this does not discover or recommend prerelease upgrades.
- Configuration fingerprints change because the existing fingerprint includes dependency assessment status (failed to scored); Release and coverage inputs are unchanged.
- Vulnerability/deprecation rows and every other dimension findings are identical to the previous diagnostic artifacts. Raw CSV, source populations, filters and source commits are unchanged.
- Live Microsoft.Playwright 1.62.0 inspection reads four range bodies totaling 79,045 bytes from a 211,404,073-byte archive, identifies netstandard2.0 assets and reports no lookup failure. Exact ranges, unchanged strong/legacy Azure tags, strict ZIP bounds, manifest CRC and bounded XML parsing are enforced. The 50 MiB ordinary download limit is retained.
- [Policy and limits](../dependency-metadata-policy.md). The updated CMAI7004 description ships in the JSON and generated Markdown catalogs.

## Validation

- 1,122 tests passed, including 27 new cases. Six calibration fixtures and formatting/whitespace checks passed.
- Packaged and installed DLL/catalog bytes match the tested Release output.
- Package SHA256 `d2bfd8d0ecb01858f05040247e9f4118c363440841027b6600ff94e8f71484ed`.
- [Live package probe](E:/repos/CodeMetrics.AI/TestResults/dependency-metadata/playwright-live.json).

## SimplCommerce

- Source `3472ba02a6f2d9b6bdca7f7fb84957176aa799dc`; `SimplCommerce.sln`, Release.
- runId `1d8ec4b4-45d4-4749-a5e8-44b1ed574ad9`; auditId `bff3c212-fe13-4b31-9ca7-2dec1a55eab8`.
- Previous failed run `4a368ef0-49a0-4c19-a6ff-e183cb921df5`; audit `ef241d63-7c2d-45ca-9e66-f81c5f9aea9d`.
- Config fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; calibration baseline; 42/49 analyzed units; 715 types / 3823 members.
- [evidence](E:/repos/SimplCommerce/.scorecard/dotnet/runs/1d8ec4b4-45d4-4749-a5e8-44b1ed574ad9/evidence.json), [inspection](E:/repos/SimplCommerce/.scorecard/dotnet/runs/1d8ec4b4-45d4-4749-a5e8-44b1ed574ad9/inspection.json), [metrics](E:/repos/SimplCommerce/.scorecard/dotnet/runs/1d8ec4b4-45d4-4749-a5e8-44b1ed574ad9/metrics.csv).

| Dimension | Scope includes | Recorded basis |
|---|---|---|
| Architecture & SOLID | static-coupling-and-project-structure | Findings: 212 (errors: 150, warnings: 62). Cycles: 0, hotspots: 62 (showing 10). Excluded passive data carriers: 216, DI extension types: 2, framework coupling archetypes: 2, application composition roots: 0. coupling: 60/495 eligible types, score 4.55; complexity: 0/497 eligible types, score 10.00; size: 2/497 eligible types, score 5.95. Final = min(metric score 4.55, graph/layering cap 2.0), rounded to 1 decimal. |
| Complexity & Decomposition | production-type-complexity, member-complexity | Eligible types: 391. Passive data carriers excluded: 216. Method complexity: 7.2, Decomposition: 7.3. Combined: 7.3. |
| Testing | test-project-signals, supplied-coverage-report | testProjects=7, testMethods=34, skipped=0, placeholders=0, assertions=68, assertionDensity=2.00, uncoveredProjects=34, coverageFile=False, lineRate=n/a. |
| Security | static-security-patterns, production-and-unknown-project-dependency-vulnerabilities | Findings: 44 distinct source sites from 44 observations (errors: 2, warnings: 22). hardcodedSecrets=2, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=14. |
| Error Handling | static-exception-patterns | Distinct source findings: 0 across 0 project/framework observations (errors: 0, warnings: 0). emptyCatch=0, throwEx=0, broadDefaults=0. Affected catches: 0/11; documented catches: 1. |
| Documentation | documentation-presence-and-content-signals | hasReadme=True, readmeNonBlankLines=81, hasDocsDir=False, architectureDocs=0, hasAiInstructions=False, libraryXmlDocRatio=0.00, publicApiDocCoverage=0.01, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | package-version-and-feed-observations, production-and-development-project-dependencies | vulnerableDirect=0, vulnerableTransitive=14, outdated=26, outdatedAspireExcluded=0, outdatedFrameworkIncompatibleExcluded=12, outdatedFrameworkCompatibilityUnknown=0, deprecated=3, unsupportedTFMs=0, versionDrift=2, cpmEnabled=False, anyCommandFailed=False. |
| Performance & Async | static-async-and-blocking-patterns | Distinct source findings: 15 across 15 project/framework observations (errors: 0, warnings: 0, unscored review leads: 15). syncOverAsync=0, threadSleep=0, saveChangesInsideLoop=1, missingCancellationToken=7, materializationBeforeQueryShape=0, awaitedIoInsideLoop=7, unboundedWhenAll=0, sharedStateMutationInFanOut=0. |
| Maintainability | production-executable-function-maintainability-index | Distinct executable functions: 2890. Weakest 578: 3.62; remaining 2312: 9.68. Each function contributes once; distribution statistics are diagnostic only. |

No reported candidates: 1 project/TFM observations; IdentityServer4.AspNetIdentity.

## OrchardCore

- Source `4c101f5c6a6e6aca073a800797a223a958b28c0b`; `OrchardCore.slnx`, Release.
- runId `1f13186d-7eaf-42cd-9a33-5b338841b99e`; auditId `a65aad6a-ddb9-4176-9cd5-088567d90d43`.
- Previous failed run `5e6fb9db-4181-4028-be75-37ab22458441`; audit `0068c238-cd83-40f6-be92-e95db4d3a57d`.
- Config fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; calibration baseline; 213/239 analyzed units; 5568 types / 24989 members.
- [evidence](E:/repos/OrchardCore/.scorecard/dotnet/runs/1f13186d-7eaf-42cd-9a33-5b338841b99e/evidence.json), [inspection](E:/repos/OrchardCore/.scorecard/dotnet/runs/1f13186d-7eaf-42cd-9a33-5b338841b99e/inspection.json), [metrics](E:/repos/OrchardCore/.scorecard/dotnet/runs/1f13186d-7eaf-42cd-9a33-5b338841b99e/metrics.csv).

| Dimension | Scope includes | Recorded basis |
|---|---|---|
| Architecture & SOLID | static-coupling-and-project-structure | Findings: 754 (errors: 0, warnings: 754). Cycles: 0, hotspots: 752 (showing 10). Excluded passive data carriers: 975, DI extension types: 70, framework coupling archetypes: 1, application composition roots: 0. coupling: 744/4522 eligible types, score 4.03; complexity: 1/4523 eligible types, score 9.82; size: 7/4523 eligible types, score 7.19. Final = min(metric score 4.03, graph/layering cap 6.0), rounded to 1 decimal. |
| Complexity & Decomposition | production-type-complexity, member-complexity | Eligible types: 3632. Passive data carriers excluded: 975. Method complexity: 5.8, Decomposition: 4.7. Combined: 5.3. |
| Testing | test-project-signals, supplied-coverage-report | testProjects=3, testMethods=1705, skipped=0, placeholders=0, assertions=4558, assertionDensity=2.67, uncoveredProjects=211, coverageFile=False, lineRate=n/a. |
| Security | static-security-patterns, production-and-unknown-project-dependency-vulnerabilities | Findings: 56 distinct source sites from 56 observations (errors: 0, warnings: 26). hardcodedSecrets=0, rawSql=0, unsafeDeserialization=0, allowAnyOriginWithCredentials=0, importedVulnerabilities=0. |
| Error Handling | static-exception-patterns | Distinct source findings: 128 across 128 project/framework observations (errors: 21, warnings: 57). emptyCatch=13, throwEx=0, broadDefaults=8. Affected catches: 51/461; documented catches: 44. |
| Documentation | documentation-presence-and-content-signals | hasReadme=True, readmeNonBlankLines=50, hasDocsDir=False, architectureDocs=0, hasAiInstructions=True, libraryXmlDocRatio=1.00, publicApiDocCoverage=0.14, staleMarkers=0, unresolvedCrefs=0. |
| Dependency Management | package-version-and-feed-observations, production-and-development-project-dependencies | vulnerableDirect=0, vulnerableTransitive=1, outdated=52, outdatedAspireExcluded=3, outdatedFrameworkIncompatibleExcluded=1, outdatedFrameworkCompatibilityUnknown=0, deprecated=1, unsupportedTFMs=0, versionDrift=0, cpmEnabled=True, anyCommandFailed=False. |
| Performance & Async | static-async-and-blocking-patterns | Distinct source findings: 546 across 546 project/framework observations (errors: 15, warnings: 0, unscored review leads: 531). syncOverAsync=61, threadSleep=0, saveChangesInsideLoop=1, missingCancellationToken=278, materializationBeforeQueryShape=0, awaitedIoInsideLoop=192, unboundedWhenAll=14, sharedStateMutationInFanOut=0. |
| Maintainability | production-executable-function-maintainability-index | Distinct executable functions: 20079. Weakest 4016: 3.76; remaining 16063: 9.70. Each function contributes once; distribution statistics are diagnostic only. |

No reported candidates: 5 project/TFM observations; Lucene.Net.Analysis.Common, Lucene.Net.QueryParser, Lucene.Net.Spatial.

Nonblocking workspace diagnostics are preserved in the linked evidence, including existing duplicate-source/package warnings.

## Next review

Remaining source findings are review leads, not automatic refactoring orders. Inspect the remaining OrchardCore Performance & Async findings before further classification changes; this dependency work did not revalidate their runtime context.

[Machine-readable verification](dotnet-dependency-metadata-2026-09-15.json).
