# Public corpus after wait-boundary corrections

Fresh full Release runs with the local unpublished CodeMetrics.AI 2.3.0 candidate, schema v3, ruleset `dotnet-2026-09-13-wait-boundaries`. Analyzer changes are uncommitted on base `2fb130663ad240a8b7a0ece0858a46c1e2ba3c4f`. Package SHA-256: `895cc43c28c336777475f7b058b8b05f6754e40f7f48ecef32ebf9819bd2c9a0`.

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
| Polly | 2 | 2 | 4 / 4 | 0 / 0 | 14 | 0 |
| Newtonsoft.Json | 10 | 10 | 0 / 0 | 0 / 0 | 0 | 0 |
| SimplCommerce | 10 | 10 | 0 / 0 | 0 / 0 | 15 | 0 |
| OrchardCore | 0 | 0 | 35 / 15 | 0 / 0 | 531 | 9 |

Review leads remain in evidence with `classificationReason`, `scoreDisposition: excludedReviewLead` and `excluded` finding effects. They do not affect the ladder. Source-site counting and maximum severity across frameworks remain intact.

## Provenance and validation

All invocation and evidence run/audit IDs match; inspection reports usable evidence and analyzer/validation exits are zero. Source commits and tracked files were checked before and after analysis. Raw CSV, populations, filters and the six dimensions outside performance/error handling/dependencies match the preceding full run exactly.

| Repository | Source commit | Run ID | Audit ID |
|---|---|---|---|
| Polly | `1a80392b1f093f40e59c515f4aeb989bea5db857` | `c8048bd5-ae81-47a3-b39b-0ff9478e6487` | `b8c2fa53-267e-496d-9255-5a311af66b9e` |
| Newtonsoft.Json | `09bb545d72969ad7fb4ea07db0d5c34f4fc07877` | `2175f64c-906f-46ce-9b17-43bf2b230b17` | `ca805faa-0d7b-4ce2-8fce-be897d70d141` |
| SimplCommerce | `3472ba02a6f2d9b6bdca7f7fb84957176aa799dc` | `8f95c981-87c1-490a-b201-3db71e4294ba` | `7d3534ec-3ddb-474c-a1b6-80cc5ad1db76` |
| OrchardCore | `4c101f5c6a6e6aca073a800797a223a958b28c0b` | `045c01bf-8ba2-47b8-be26-419777575693` | `f9d26fd2-d63f-40d5-ae00-d10bf68c30c6` |

The baseline is the [previous full corpus](dotnet-public-corpus-async-context.md). Dependency probes ran afresh against current package-source responses; changes there are reported separately from static classification.

### Polly

- [Fresh evidence](<E:/repos/Polly/.scorecard/dotnet/runs/c8048bd5-ae81-47a3-b39b-0ff9478e6487/evidence.json>).
- Performance categories: `{"missingCancellationToken:info": 9, "syncOverAsync:error": 4, "syncOverAsync:info": 4, "threadSleep:info": 1}`.
- Classification reasons: `{"cancellationContractRequiresReview": 9, "documentedLocalChoice": 3, "observedHazard": 4, "synchronousContract": 2}`.
- Error Handling: 4 → 4.
- Dependency object changed: false.
- Dependency metric changes: `{}`.
- Population: `{"members": 10988, "types": 1858}`.

### Newtonsoft.Json

- [Fresh evidence](<E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/2175f64c-906f-46ce-9b17-43bf2b230b17/evidence.json>).
- Performance categories: `{}`.
- Classification reasons: `{}`.
- Error Handling: 9.5 → 9.5.
- Dependency object changed: false.
- Dependency metric changes: `{}`.
- Population: `{"members": 25381, "types": 1707}`.

- Analysis diagnostic: `{"kind": "workspaceWarning", "message": "Found project reference without a matching metadata reference: E:\\repos\\Newtonsoft.Json\\Src\\Newtonsoft.Json.Tests\\Newtonsoft.Json.Tests.csproj"}`.
### SimplCommerce

- [Fresh evidence](<E:/repos/SimplCommerce/.scorecard/dotnet/runs/8f95c981-87c1-490a-b201-3db71e4294ba/evidence.json>).
- Performance categories: `{"awaitedIoInsideLoop:info": 7, "missingCancellationToken:info": 7, "saveChangesInsideLoop:info": 1}`.
- Classification reasons: `{"cancellationContractRequiresReview": 7, "orderingAndConcurrencySafetyRequireReview": 7, "transactionAndBatchingContextRequired": 1}`.
- Error Handling: 10 → 10.
- Dependency object changed: false.
- Dependency metric changes: `{}`.
- Population: `{"members": 3823, "types": 715}`.

### OrchardCore

- [Fresh evidence](<E:/repos/OrchardCore/.scorecard/dotnet/runs/045c01bf-8ba2-47b8-be26-419777575693/evidence.json>).
- Performance categories: `{"awaitedIoInsideLoop:info": 192, "missingCancellationToken:info": 278, "saveChangesInsideLoop:info": 1, "syncOverAsync:error": 15, "syncOverAsync:info": 46, "unboundedWhenAll:info": 14}`.
- Classification reasons: `{"cancellationContractRequiresReview": 278, "contextRequired": 14, "iterationScopedPersistenceLifetime": 1, "observedHazard": 15, "orderingAndConcurrencySafetyRequireReview": 192, "synchronousCallbackContract": 2, "synchronousContract": 36, "synchronousContractCallChain": 8}`.
- Error Handling: 4 → 4.
- Dependency object changed: false.
- Dependency metric changes: `{}`.
- Population: `{"members": 24989, "types": 5568}`.

- Analysis diagnostic: `{"kind": "workspaceWarning", "message": "Duplicate source file 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\AnalyzerReleases.Shipped.md' in project 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\OrchardCore.SourceGenerators.csproj'"}`.
- Analysis diagnostic: `{"kind": "workspaceWarning", "message": "Duplicate source file 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\AnalyzerReleases.Unshipped.md' in project 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\OrchardCore.SourceGenerators.csproj'"}`.
- Analysis diagnostic: `{"kind": "workspaceWarning", "message": "Msbuild failed when processing the file 'E:\\repos\\OrchardCore\\test\\OrchardCore.Tests.Integration\\OrchardCore.Tests.Integration.csproj' with message: Package 'SSH.NET' 2025.1.0 has a known high severity vulnerability, https://github.com/advisories/GHSA-q939-rpr3-3284"}`.
## Verification

All 956 .NET tests pass, as do six calibration fixtures with unchanged score/accuracy baselines. Full formatting verification and git diff whitespace checks pass.

## Reproduction artifacts

[Comparison, scoring decisions and finding deltas](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-wait-boundaries/comparison.json>). Runner, verification scripts, invocation manifests, logs, calibration report and the exact package are retained beside it. All extracted findings retain invocation identities.

## Review before calibration

Inspect remaining blocking sites and the recorded classification reasons before changing thresholds. Existing absolute cutoffs may still dominate after false positives are removed. Review leads identify questions about transactions, ordering, cancellation and resource limits; their exclusion is not a claim that those areas are optimized. Population/severity calibration remains deferred.
## Reconciliation with the 35-site OrchardCore source review

The [source review](dotnet-orchard-wait-review.md) was reconciled by file and exact source span against this fresh run, retaining old/new fingerprints. All nine confirmed completed-task false positives are removed. Other waits remain observable unless a completion proof applies.

The 15 remaining scored sites comprise nine paired scripting adapters, four explicit
synchronous/compatibility APIs, one injected cache invariant, and RedisDatabaseFactory.Release.
Release is **internal**, not private: its known shutdown callers explain the source,
but the private-member traversal cannot establish all possible assembly/friend-assembly
uses. It intentionally remains scored. The five S3 helper waits now inherit their
synchronous interface context consistently with the direct waits in those methods.

The other three repositories have identical performance finding sets. All nine scores
and all raw CSV files are unchanged across all four repositories. Dependency objects
also match exactly despite being queried afresh. This pass improves classification
fidelity without moving the existing numerical cutoffs.

| Outcome | Sites |
|---|---:|
| stillScored | 15 |
| reviewLead | 11 |
| removedCompletedTask | 9 |

| Original site | Source | Outcome | Current classification reason |
|---:|---|---|---|
| 1 | `src/OrchardCore.Modules/OrchardCore.Contents/Scripting/ContentMethodsProvider.cs:23` | stillScored | observedHazard |
| 2 | `src/OrchardCore.Modules/OrchardCore.Contents/Scripting/ContentMethodsProvider.cs:32` | stillScored | observedHazard |
| 3 | `src/OrchardCore.Modules/OrchardCore.Contents/Scripting/ContentMethodsProvider.cs:41` | stillScored | observedHazard |
| 4 | `src/OrchardCore.Modules/OrchardCore.Contents/Scripting/ContentMethodsProvider.cs:50` | stillScored | observedHazard |
| 5 | `src/OrchardCore.Modules/OrchardCore.CustomSettings/Services/CustomSettingsService.cs:60` | stillScored | observedHazard |
| 6 | `src/OrchardCore.Modules/OrchardCore.DataProtection.Azure/BlobOptionsConfiguration.cs:75` | reviewLead | synchronousContractCallChain |
| 7 | `src/OrchardCore.Modules/OrchardCore.Https/Startup.cs:51` | reviewLead | synchronousCallbackContract |
| 8 | `src/OrchardCore.Modules/OrchardCore.Media.AmazonS3/Services/S3TusTempStore.cs:269` | reviewLead | synchronousContractCallChain |
| 9 | `src/OrchardCore.Modules/OrchardCore.Media.AmazonS3/Services/S3TusTempStore.cs:275` | reviewLead | synchronousContractCallChain |
| 10 | `src/OrchardCore.Modules/OrchardCore.Media.AmazonS3/Services/S3TusTempStore.cs:289` | reviewLead | synchronousContractCallChain |
| 11 | `src/OrchardCore.Modules/OrchardCore.Media.AmazonS3/Services/S3TusTempStore.cs:292` | reviewLead | synchronousContractCallChain |
| 12 | `src/OrchardCore.Modules/OrchardCore.Media.AmazonS3/Services/S3TusTempStore.cs:293` | reviewLead | synchronousContractCallChain |
| 13 | `src/OrchardCore.Modules/OrchardCore.OpenId/Configuration/OpenIdValidationConfiguration.cs:247` | reviewLead | synchronousContractCallChain |
| 14 | `src/OrchardCore.Modules/OrchardCore.Queries/QueryGlobalMethodProvider.cs:20` | stillScored | observedHazard |
| 15 | `src/OrchardCore.Modules/OrchardCore.Redis/Options/RedisKeyManagementOptionsSetup.cs:51` | reviewLead | synchronousCallbackContract |
| 16 | `src/OrchardCore.Modules/OrchardCore.Redis/Services/RedisDatabaseFactory.cs:71` | stillScored | observedHazard |
| 17 | `src/OrchardCore.Modules/OrchardCore.Roles/Services/RoleStore.cs:39` | reviewLead | synchronousContract |
| 18 | `src/OrchardCore.Modules/OrchardCore.Workflows/Http/Scripting/HttpMethodsProvider.cs:71` | stillScored | observedHazard |
| 19 | `src/OrchardCore.Modules/OrchardCore.Workflows/Http/Scripting/HttpMethodsProvider.cs:89` | stillScored | observedHazard |
| 20 | `src/OrchardCore.Modules/OrchardCore.Workflows/Http/Scripting/HttpMethodsProvider.cs:142` | stillScored | observedHazard |
| 21 | `src/OrchardCore/OrchardCore.Abstractions/Modules/FileProviders/FileInfoExtensions.cs:8` | stillScored | observedHazard |
| 22 | `src/OrchardCore/OrchardCore.Abstractions/Shell/Configuration/ShellConfiguration.cs:83` | reviewLead | synchronousContractCallChain |
| 23 | `src/OrchardCore/OrchardCore.Data.YesSql/Removing/ShellDbTablesInfo.cs:67` | removedCompletedTask | completed task proven |
| 24 | `src/OrchardCore/OrchardCore.Data.YesSql/Removing/ShellDbTablesInfo.cs:84` | removedCompletedTask | completed task proven |
| 25 | `src/OrchardCore/OrchardCore.Data.YesSql/Removing/ShellDbTablesInfo.cs:104` | removedCompletedTask | completed task proven |
| 26 | `src/OrchardCore/OrchardCore.Data.YesSql/Removing/ShellDbTablesInfo.cs:124` | removedCompletedTask | completed task proven |
| 27 | `src/OrchardCore/OrchardCore.Data.YesSql/Removing/ShellDbTablesInfo.cs:139` | removedCompletedTask | completed task proven |
| 28 | `src/OrchardCore/OrchardCore.Data.YesSql/Removing/ShellDbTablesInfo.cs:158` | removedCompletedTask | completed task proven |
| 29 | `src/OrchardCore/OrchardCore.Data.YesSql/Removing/ShellDbTablesInfo.cs:258` | removedCompletedTask | completed task proven |
| 30 | `src/OrchardCore/OrchardCore.DisplayManagement.Liquid/LiquidViewTemplate.cs:39` | removedCompletedTask | completed task proven |
| 31 | `src/OrchardCore/OrchardCore.DisplayManagement/Descriptors/DefaultShapeTableManager.cs:64` | stillScored | observedHazard |
| 32 | `src/OrchardCore/OrchardCore.DisplayManagement/Razor/RazorPage.cs:306` | stillScored | observedHazard |
| 33 | `src/OrchardCore/OrchardCore.Infrastructure.Abstractions/Settings/SiteServiceExtensions.cs:52` | stillScored | observedHazard |
| 34 | `src/OrchardCore/OrchardCore.Infrastructure.Abstractions/Settings/SiteServiceExtensions.cs:52` | removedCompletedTask | completed task proven |
| 35 | `src/OrchardCore/OrchardCore.Recipes.Core/VariablesMethodProvider.cs:21` | stillScored | observedHazard |

S3 helper waits now retain their synchronous interface context as review leads; this does not remove the async upload-to-sync storage design opportunity. Paired scripting adapters and the injected task-cache invariant are not automatically exempted. No runtime measurements or target source refactors were performed.
