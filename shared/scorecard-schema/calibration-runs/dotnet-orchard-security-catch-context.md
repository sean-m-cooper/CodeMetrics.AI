# OrchardCore security and error-handling context correction

Local unpublished CodeMetrics.AI 2.3.0, schema v3, ruleset `dotnet-2026-09-13-security-catch-context`. This applies the [documented policy](../error-handling-policy.md) on the same pinned OrchardCore source (`4c101f5c6a6e6aca073a800797a223a958b28c0b`). It is a policy/classification comparison across incompatible rulesets, not evidence that OrchardCore source improved or a compatible baseline gate.

## Verified results

| Dimension | Previous | Corrected |
|---|---:|---:|
| Security | 0 | 2 |
| Error Handling | 0 | 4 |
| Overall, partial assessment (9/9 dimensions) | 3.2 | 3.8 |

All nine dimensions remain partial static assessments. Target tests were not executed and no coverage file was supplied. A successful analyzer run does not establish comprehensive security or exception-strategy coverage.

All nine previously reviewed hardcoded-secret false positives and the guarded CORS false positive are absent. Credential-positive and unguarded same-builder CORS regression fixtures remain detected. Security still includes imported vulnerable-package observations and authorization warnings; the corrected result is not a security certification.

Error Handling now records 461 distinct source catches, 51 affected catches, and 44 catches with local explanatory comments. The weighted affected sum is 34.0; weighted rate is 7.375271%. Its population component is 9.26247/10. Caps: criticalCatchCap=9 (notLimiting), syncBlockingCap=4 (selected). The synchronous-blocking cap is retained from the preceding policy.

The analyzer trusts local prose as declared intent without evaluating the business decision. It also recognizes direct diagnostic output parameters. Findings retain their source spans and framework observations; overlapping catch rules contribute only their maximum site weight. Remaining findings are review leads, not proof of broken behavior. No method-name-only exemptions were added.

## Findings

| Dimension / category | Previous sites | Corrected sites |
|---|---:|---:|
| security / allowAnonymous | 30 | 30 |
| security / allowAnyOriginWithCredentials | 1 | 0 |
| security / hardcodedSecret | 9 | 0 |
| security / missingAuthorization | 26 | 26 |
| errorHandling / broadCatchReturnsDefault | 11 | 8 |
| errorHandling / broadCatchWithoutLoggingOrRethrow | 46 | 38 |
| errorHandling / consoleWriteLine | 4 | 4 |
| errorHandling / emptyCatch | 24 | 13 |
| errorHandling / missingLoggerForMultipleCatches | 4 | 4 |
| errorHandling / syncBlockingCall | 79 | 79 |

## Validation and provenance

- Release build: zero warnings/errors. Full final suite: 883 tests passed; focused final suite: 248 tests passed. Includes true/false secret candidates, guarded/unguarded CORS, documented and undocumented catches, invalid/deferred explanations, diagnostic outputs, overlapping rules and framework-invariant population counting.
- Six pinned .NET calibration fixtures passed independently after review. Only the all-empty single-catch fixture changes score (2 to 0), alongside the expected error-handling distribution and ruleset metadata. Fixture hashes, populations, findings and accuracy labels are unchanged.
- Package SHA-256: `d76e29eaa96b21d1301768b2a79f776382b9d3e21d798b16debb98343d8e2744`. Updated JSON and Markdown CMAI catalogs are shipped in the package. No package was published.
- Run ID: `65d9a754-37aa-4fab-a43f-b24fc490e6e2`; audit ID: `0877ea77-31a9-418b-b211-e198ae99d914`. Invocation IDs match complete evidence and a usable inspection; analyzer and validator exit codes are both zero.
- Raw CSV is byte-identical. Source revision and tracked files, analysis population, filters, configuration fingerprint and suppressions match the prior run. Unaffected dimension objects are identical: codeQuality, maintainability, performanceAsync, testing, documentation, architecture.
- Dependency dimension object changed between feed-enabled runs: False. Feed observations are reported independently of the static classification changes.
- Workspace diagnostics: `[{"kind": "workspaceWarning", "message": "Duplicate source file 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\AnalyzerReleases.Shipped.md' in project 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\OrchardCore.SourceGenerators.csproj'"}, {"kind": "workspaceWarning", "message": "Duplicate source file 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\AnalyzerReleases.Unshipped.md' in project 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\OrchardCore.SourceGenerators.csproj'"}, {"kind": "workspaceWarning", "message": "Msbuild failed when processing the file 'E:\\repos\\OrchardCore\\test\\OrchardCore.Tests.Integration\\OrchardCore.Tests.Integration.csproj' with message: Package 'SSH.NET' 2025.1.0 has a known high severity vulnerability, https://github.com/advisories/GHSA-q939-rpr3-3284"}]`. These include the existing SSH.NET advisory in an integration-test project and two duplicate analyzer-release-file warnings.
- [Fresh evidence](<E:/repos/OrchardCore/.scorecard/dotnet/runs/65d9a754-37aa-4fab-a43f-b24fc490e6e2/evidence.json>), [inspection](<E:/repos/OrchardCore/.scorecard/dotnet/runs/65d9a754-37aa-4fab-a43f-b24fc490e6e2/inspection.json>), [prior evidence](<E:/repos/OrchardCore/.scorecard/dotnet/runs/cdcca8b7-cf21-4cd0-bded-0f0ff91be6f8/evidence.json>).
- [Machine-readable comparison, removed findings and executed scoring decisions](<E:/repos/CodeMetrics.AI/TestResults/security-catch-context/comparison.json>). Invocation JSON, runner, validation scripts and logs are saved alongside it.
