# Public corpus after async classification corrections

Fresh full Release runs with the local unpublished CodeMetrics.AI 2.3.0 candidate, schema v3, ruleset `dotnet-2026-09-13-async-context`. Analyzer changes are uncommitted on base `2fb130663ad240a8b7a0ece0858a46c1e2ba3c4f`. Package SHA-256: `c8af6cd3c33b4eddc7e41a03110d191aac0bbbc3b9e75e81b997f0f50d767ec4`.

Only classifications changed. The numeric performance ladder and catch-population weights remain unchanged. See the [classification policy](../performance-async-policy.md). These are incompatible-ruleset comparisons on unchanged pinned source, not baseline gates or evidence that the corpus code improved.

## Full scorecards

All dimensions are deterministic within partial static scopes. Every overall is a **partial assessment, 9/9 dimensions scored**. No target test suites or runtime benchmarks were executed, and no coverage files were supplied. A 10 means no scored findings within scope, not measured runtime excellence.

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
| **Overall (partial, 9/9)** | **6.3** | **6.1** | **5.5** | **3.8** |

## Performance comparison

| Repository | Before | After | Errors before / after | Warnings before / after | Review leads after | Removed false-positive sites |
|---|---:|---:|---:|---:|---:|---:|
| Polly | 0 | 2 | 11 / 4 | 12 / 0 | 14 | 6 |
| Newtonsoft.Json | 10 | 10 | 0 / 0 | 0 / 0 | 0 | 0 |
| SimplCommerce | 2 | 10 | 1 / 0 | 14 / 0 | 15 | 0 |
| OrchardCore | 0 | 0 | 78 / 35 | 481 / 0 | 520 | 20 |

Review leads remain in evidence with `classificationReason`, `scoreDisposition: excludedReviewLead` and `excluded` finding effects. They do not affect the ladder. Source-site counting and maximum severity across frameworks remain intact.

## Provenance and validation

All invocation and evidence run/audit IDs match; inspection reports usable evidence and analyzer/validation exits are zero. Source commits and tracked files were checked before and after analysis. Raw CSV, populations, filters and the six dimensions outside performance/error handling/dependencies match the preceding full run exactly.

| Repository | Source commit | Run ID | Audit ID |
|---|---|---|---|
| Polly | `1a80392b1f093f40e59c515f4aeb989bea5db857` | `3adc0506-b0b2-4711-8c0a-e1a140907406` | `8407cf62-d6ca-4b4b-8bef-1b6872628856` |
| Newtonsoft.Json | `09bb545d72969ad7fb4ea07db0d5c34f4fc07877` | `bc9ff8ad-66f4-4fab-ad4a-4ae69806a399` | `a8dfa195-7dd0-4f54-9fc8-e4dfd39d8247` |
| SimplCommerce | `3472ba02a6f2d9b6bdca7f7fb84957176aa799dc` | `4a23ea47-3b7d-453f-9dfc-f57c41ca44fa` | `84bcda1c-4282-4ef0-9d7a-9a558c18fd34` |
| OrchardCore | `4c101f5c6a6e6aca073a800797a223a958b28c0b` | `69683394-cc70-4cc0-a929-36950f45687d` | `c04502bf-4f56-4711-9c51-9b046bb3b01e` |

The baseline is the [previous full corpus](dotnet-public-corpus-catch-context.md). Dependency probes ran afresh against current package-source responses; changes there are reported separately from static classification.

### Polly

- [Fresh evidence](<E:/repos/Polly/.scorecard/dotnet/runs/3adc0506-b0b2-4711-8c0a-e1a140907406/evidence.json>).
- Performance categories: `{"missingCancellationToken:info": 9, "syncOverAsync:error": 4, "syncOverAsync:info": 4, "threadSleep:info": 1}`.
- Classification reasons: `{"cancellationContractRequiresReview": 9, "documentedLocalChoice": 3, "observedHazard": 4, "synchronousContract": 2}`.
- Error Handling: 4 → 4.
- Dependency object changed: false.
- Dependency metric changes: `{}`.
- Population: `{"members": 10988, "types": 1858}`.

### Newtonsoft.Json

- [Fresh evidence](<E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/bc9ff8ad-66f4-4fab-ad4a-4ae69806a399/evidence.json>).
- Performance categories: `{}`.
- Classification reasons: `{}`.
- Error Handling: 9.5 → 9.5.
- Dependency object changed: true.
- Dependency metric changes: `{"outdated": {"after": 15, "before": 32}, "outdatedFrameworkCompatibilityUnknown": {"after": 0, "before": 24}, "outdatedFrameworkIncompatibleExcluded": {"after": 22, "before": 5}}`.
- Population: `{"members": 25381, "types": 1707}`.

- Analysis diagnostic: `{"kind": "workspaceWarning", "message": "Found project reference without a matching metadata reference: E:\\repos\\Newtonsoft.Json\\Src\\Newtonsoft.Json.Tests\\Newtonsoft.Json.Tests.csproj"}`.
### SimplCommerce

- [Fresh evidence](<E:/repos/SimplCommerce/.scorecard/dotnet/runs/4a23ea47-3b7d-453f-9dfc-f57c41ca44fa/evidence.json>).
- Performance categories: `{"awaitedIoInsideLoop:info": 7, "missingCancellationToken:info": 7, "saveChangesInsideLoop:info": 1}`.
- Classification reasons: `{"cancellationContractRequiresReview": 7, "orderingAndConcurrencySafetyRequireReview": 7, "transactionAndBatchingContextRequired": 1}`.
- Error Handling: 10 → 10.
- Dependency object changed: false.
- Dependency metric changes: `{}`.
- Population: `{"members": 3823, "types": 715}`.

### OrchardCore

- [Fresh evidence](<E:/repos/OrchardCore/.scorecard/dotnet/runs/69683394-cc70-4cc0-a929-36950f45687d/evidence.json>).
- Performance categories: `{"awaitedIoInsideLoop:info": 192, "missingCancellationToken:info": 278, "saveChangesInsideLoop:info": 1, "syncOverAsync:error": 35, "syncOverAsync:info": 35, "unboundedWhenAll:info": 14}`.
- Classification reasons: `{"cancellationContractRequiresReview": 278, "contextRequired": 14, "iterationScopedPersistenceLifetime": 1, "observedHazard": 35, "orderingAndConcurrencySafetyRequireReview": 192, "synchronousContract": 35}`.
- Error Handling: 4 → 4.
- Dependency object changed: false.
- Dependency metric changes: `{}`.
- Population: `{"members": 24989, "types": 5568}`.

- Analysis diagnostic: `{"kind": "workspaceWarning", "message": "Duplicate source file 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\AnalyzerReleases.Shipped.md' in project 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\OrchardCore.SourceGenerators.csproj'"}`.
- Analysis diagnostic: `{"kind": "workspaceWarning", "message": "Duplicate source file 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\AnalyzerReleases.Unshipped.md' in project 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\OrchardCore.SourceGenerators.csproj'"}`.
- Analysis diagnostic: `{"kind": "workspaceWarning", "message": "Msbuild failed when processing the file 'E:\\repos\\OrchardCore\\test\\OrchardCore.Tests.Integration\\OrchardCore.Tests.Integration.csproj' with message: Package 'SSH.NET' 2025.1.0 has a known high severity vulnerability, https://github.com/advisories/GHSA-q939-rpr3-3284"}`.
## Verification

All 918 .NET tests pass, as do six calibration fixtures with unchanged score/accuracy baselines. Full formatting verification and git diff whitespace checks pass. Two pre-existing formatting issues in CorsPolicyAnalysis and CatchIntentRecognition were normalized; their diffs are whitespace-only.

## Reproduction artifacts

[Comparison, scoring decisions and finding deltas](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-async-context-final/comparison.json>). Runner, verification scripts, invocation manifests, logs, calibration report and the exact package are retained beside it. All extracted findings retain invocation identities.

## Review before calibration

Inspect remaining blocking sites and the recorded classification reasons before changing thresholds. Existing absolute cutoffs may still dominate after false positives are removed. Review leads identify questions about transactions, ordering, cancellation and resource limits; their exclusion is not a claim that those areas are optimized. Population/severity calibration remains deferred.

Current source spot checks identify two useful follow-ups, not recommendations to rewrite the libraries:

1. **Completed-return helper analysis (Metrics + Current context).** OrchardCore's [CreateReduceIndexTableAsync](<E:/repos/OrchardCore/src/OrchardCore/OrchardCore.Data.YesSql/Removing/ShellDbTablesInfo.cs:89>) performs synchronous bookkeeping and returns `Task.CompletedTask`. Its caller's wait remains scored because this pass does not follow arbitrary helper return paths. This is a concrete remaining false positive and a reason to review recognition before calibration.
2. **Property and callback contracts (Metrics + Current context).** OrchardCore's [Roles property](<E:/repos/OrchardCore/src/OrchardCore.Modules/OrchardCore.Roles/Services/RoleStore.cs:38>) and [Redis repository factory callback](<E:/repos/OrchardCore/src/OrchardCore.Modules/OrchardCore.Redis/Options/RedisKeyManagementOptionsSetup.cs:47>) contain actual waits outside the method-contract recognition added here. Their property/delegate and initialization contracts need examination before prescribing async replacements. They are review candidates, not verified defects.

Polly's four remaining scored sites are synchronous initialization, the pessimistic timeout wait, a defensive fallback when a completion invariant fails, and a circuit-breaker duration callback. These were re-read at their recorded paths during this review. The waits are observable, but their counts do not establish four bugs or four interchangeable refactoring opportunities.

Newtonsoft.Json's fresh dependency assessment resolved the 24 previously unknown compatibility observations: scored outdated occurrences changed from 32 to 15 and incompatible exclusions from 5 to 22. Its dependency score remains 2. This package-response difference is separate from the async classification changes.
