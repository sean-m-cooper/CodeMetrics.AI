# OrchardCore Performance & Async investigation

## Problem and conclusion

OrchardCore's latest usable scorecard reports Performance & Async 0. The arithmetic is correct under the current policy, but the score combines intentional synchronous adapters, a documented preload assumption, a completed-task cache, and unresolved blocking risks under an absolute five-error threshold. It does not establish poor runtime performance across OrchardCore.

This is a read-only investigation. No analyzer policy, thresholds, corpus source, or recorded scores were changed. No replacement score is asserted.

## Provenance and observed data

- Source commit: `4c101f5c6a6e6aca073a800797a223a958b28c0b`, Release, `OrchardCore.slnx`. The checkout has no tracked modifications.
- Analyzer: local unpublished 2.3.0, schema 3, ruleset `dotnet-2026-09-15-package-metadata`.
- Run: `1f13186d-7eaf-42cd-9a33-5b338841b99e`; audit: `a65aad6a-ddb9-4176-9cd5-088567d90d43`.
- [Evidence](E:/repos/OrchardCore/.scorecard/dotnet/runs/1f13186d-7eaf-42cd-9a33-5b338841b99e/evidence.json), [inspection](E:/repos/OrchardCore/.scorecard/dotnet/runs/1f13186d-7eaf-42cd-9a33-5b338841b99e/inspection.json), [full scorecard](dotnet-dependency-metadata-2026-09-15.md).
- Complete analysis and usable inspection; 213/239 analyzed units, 5,568 types, 24,989 raw members. These raw members are not an established denominator for blocking opportunities.
- 546 distinct performance findings: 15 errors, zero warnings, 531 informational review leads. All 15 errors are `syncOverAsync` with `classificationReason=observedHazard`.
- 61 total synchronous-wait findings: 15 scored and 46 informational. The other 485 findings are also informational: 278 cancellation-input observations, 192 sequential I/O observations, 14 fan-out observations, and one loop-save observation.
- The selected decision is `systemicErrors`, condition `errors >= 5`, score 0. One through four errors score 2; zero errors and zero warnings score 10. There is no scored-error population denominator.

The 531 review leads do not lower this score. There is no multi-framework duplication in these 546 observations.

## Review of every scored source site

| Group | Source sites | What the source establishes | Interpretation |
|---|---|---|---|
| Content scripting adapters | [ContentMethodsProvider](E:/repos/OrchardCore/src/OrchardCore.Modules/OrchardCore.Contents/Scripting/ContentMethodsProvider.cs:23): lines 23, 32, 41, 50 | Each `GlobalMethod` assigns a synchronous delegate to `Method` and an asynchronous delegate to `AsyncMethod`, forwarding to the same async implementation. | Four explicit synchronous API adapters. Waiting is real when incomplete; avoidable misuse is not established by these definitions. |
| HTTP scripting adapters | [HttpMethodsProvider](E:/repos/OrchardCore/src/OrchardCore.Modules/OrchardCore.Workflows/Http/Scripting/HttpMethodsProvider.cs:71): lines 71, 89, 142 | Response writing, body reading, and request deserialization each expose paired synchronous/asynchronous delegates. The body reader explicitly says async request-body reading is mandatory. | Three explicit adapters. Request I/O can actually block the synchronous caller; the existence of an async alternative does not make the compatibility API accidental. |
| Query scripting adapter | [QueryGlobalMethodProvider](E:/repos/OrchardCore/src/OrchardCore.Modules/OrchardCore.Queries/QueryGlobalMethodProvider.cs:20) | Paired delegates wrap `ExecuteQueryAsync`, which loads and executes a query. | One intentional adapter with potentially real database waiting. |
| Recipe scripting adapter | [VariablesMethodProvider](E:/repos/OrchardCore/src/OrchardCore/OrchardCore.Recipes.Core/VariablesMethodProvider.cs:21) | Paired delegates wrap async recursive script evaluation. | One intentional adapter; no guarantee of task completion at access. |
| Documented settings preload | [SiteServiceExtensions](E:/repos/OrchardCore/src/OrchardCore/OrchardCore.Infrastructure.Abstractions/Settings/SiteServiceExtensions.cs:46), wait at line 52 | A comment explains asynchronous preloading and synchronous subsequent retrieval. It precedes the declaration of the same task later inspected and waited upon. Only the fallback `GetResult` is flagged; the completion-guarded `Result` is already recognized. | Strong evidence of declared intent. The analyzer misses the comment's association with that task. Preloading is not a universal completion proof. |
| Completed-task cache | [DefaultShapeTableManager](E:/repos/OrchardCore/src/OrchardCore/OrchardCore.DisplayManagement/Descriptors/DefaultShapeTableManager.cs:64) | Cache hit reads `shapeTable.Result`. The observed production cache writer at line 155 stores `Task.FromResult(shapeTable)` after building; the keyed registration supplies an empty concurrent dictionary. | Strong evidence that the normal cached access is nonblocking. A general proof must account for injected/shared dictionaries and all writers. |
| Shutdown cleanup | [RedisDatabaseFactory](E:/repos/OrchardCore/src/OrchardCore.Modules/OrchardCore.Redis/Services/RedisDatabaseFactory.cs:71) | `Release` is an internal static helper called from `IDisposable.Dispose` and registered with `ApplicationStopped.Register`. It waits for lazy connection tasks before disposing multiplexers. | Lifecycle-boundary review lead candidate. Shutdown may still wait for a pending connection; this is not evidence of request-path blocking. |
| Obsolete compatibility API | [CustomSettingsService](E:/repos/OrchardCore/src/OrchardCore.Modules/OrchardCore.CustomSettings/Services/CustomSettingsService.cs:58) | `[Obsolete]` explicitly directs callers from `GetSettingsType` to `GetSettingsTypeAsync`. The old API waits on that async method; the underlying lazy task can initialize content definitions. | Deliberate migration/compatibility surface. Attribute alone does not prove necessity or safety, and should not universally exempt blocking. |
| Razor compatibility API | [RazorPage](E:/repos/OrchardCore/src/OrchardCore/OrchardCore.DisplayManagement/Razor/RazorPage.cs:300) | `public new IHtmlContent RenderSection(...)` wraps its async counterpart and comments explain replacement of the base implementation. The async path calls `DisplayAsync(zone)`. | Explicit synchronous facade, but hiding a base method is not an interface/override contract. Rendering may perform real async work. |
| File-reading facade | [FileInfoExtensions](E:/repos/OrchardCore/src/OrchardCore/OrchardCore.Abstractions/Modules/FileProviders/FileInfoExtensions.cs:8) | Public synchronous wrapper waits on a reader that opens a stream and awaits lines. No local rationale or imposed contract was found. | Retain as an unresolved blocking review target. A synchronous signature and async counterpart alone do not establish why this implementation is necessary. |

The four scripting rows account for nine sites; each other row accounts for one, totaling 15.

## Upstream and downstream evidence

### Scripting is an actual API boundary

[GlobalMethod](E:/repos/OrchardCore/src/OrchardCore/OrchardCore.Infrastructure.Abstractions/Scripting/GlobalMethod.cs:3) exposes separate `Method` and `AsyncMethod` factories. [JavaScriptScope](E:/repos/OrchardCore/src/OrchardCore/OrchardCore.Scripting.JavaScript/JavaScriptScope.cs:48) registers synchronous delegates under `method.Name` and asynchronous delegates under `method.Name + "Async"`. These are deferred script-call adapters, not blocking operations executed while constructing the provider.

The evidence's enclosing-member labels name constructors for these nine lambdas. Those labels locate the registration code; they do not identify when the wait executes. This is a reporting-context limitation worth preserving in the investigation.

### The preload explanation is backed by a real lifecycle hook

[PreloadSiteSettingsTenantEventHandler.ActivatedAsync](E:/repos/OrchardCore/src/OrchardCore.Modules/OrchardCore.Settings/Services/PreloadSiteSettingsTenantEventHandler.cs:17) awaits settings retrieval. [Startup](E:/repos/OrchardCore/src/OrchardCore.Modules/OrchardCore.Settings/Startup.cs:65) registers that handler. [SiteService.GetSiteSettingsAsync](E:/repos/OrchardCore/src/OrchardCore.Modules/OrchardCore.Settings/Services/SiteService.cs:31) retrieves an immutable cached document.

Graph caller leads include options configuration and route selection, consistent with synchronous consumers. This supports intentional design, not a guarantee for every custom service implementation, lifecycle ordering, or later cache invalidation.

### Cache ownership matters

[OrchardCoreBuilderExtensions](E:/repos/OrchardCore/src/OrchardCore/OrchardCore.DisplayManagement/OrchardCoreBuilderExtensions.cs:63) registers the keyed `IDictionary<string, Task<ShapeTable>>`. The observed production writer stores completed factories. The constructor accepts the dictionary externally, so a generic analyzer cannot infer universal completion merely from the field's name or its one local writer.

### The Redis boundary exceeds current visibility bounds

The same class shows the two lifecycle uses. Graph traversal also returned unrelated `Release` callers, illustrating ambiguous call edges; those were not accepted as evidence. Qualified-source inspection and a targeted search found no external `RedisDatabaseFactory.Release` calls. Absence of a textual call is not proof against arbitrary delegates or reflective use.

## Analyzer causes and existing safeguards

| Cause | Direct evidence | Confidence |
|---|---|---|
| Absolute-count score cliff | [PerformanceAsyncProbe](E:/repos/CodeMetrics.AI/analyzers/dotnet/src/CodeMetrics.AI/Probes/PerformanceAsyncProbe.cs:72) selects 0 at five errors. | Certain; matches recorded scoring decision. |
| Comment association is narrower than developer intent | [LocalPerformanceRationale](E:/repos/CodeMetrics.AI/analyzers/dotnet/src/CodeMetrics.AI/Probes/LocalPerformanceRationale.cs:13) reads containing statements/branches, not the preceding task declaration. | High; explains the settings accessor finding. |
| Scripting delegate properties are outside the callback catalog | [SynchronousCallbackContract](E:/repos/CodeMetrics.AI/analyzers/dotnet/src/CodeMetrics.AI/Probes/SynchronousCallbackContract.cs:17) requires a callback argument to a recognized API. A nested delegate in a `GlobalMethod` initializer does not qualify. | High; accounts for nine findings. |
| Helper-context propagation is private-only | [SynchronousBoundaryContext](E:/repos/CodeMetrics.AI/analyzers/dotnet/src/CodeMetrics.AI/Probes/SynchronousBoundaryContext.cs:36) deliberately rejects internal helpers. | High; explains Redis even though the known callers are lifecycle boundaries. |
| Cache, obsolete facade, and hidden base API are outside proof scope | Current policy recognizes local completion and explicit override/interface contracts, not arbitrary shared cache invariants, obsolescence, or base-member hiding. | High as an explanation; appropriate treatment requires separate product decisions. |

The bounded behavior is intentional, not a regression introduced by the dependency work. Commit `c32b677` introduced the context classifier. [AsyncContextTests](E:/repos/CodeMetrics.AI/analyzers/dotnet/tests/CodeMetrics.AI.Tests/Probes/AsyncContextTests.cs:117) explicitly requires that explanations do not transfer to unrelated sibling waits or nested functions. A better association rule must preserve that protection.

Orchard history also explicitly records the custom-settings migration in `95473785e` (obsolete synchronous GetSettingsType), supporting the compatibility interpretation.

## Dependency map and unavailable runtime evidence

| Dependency | Observed identity | Role and verification limit |
|---|---|---|
| CodeMetrics.AI | Local 2.3.0; context-classification-v3 | Deterministic findings and absolute-count scoring verified from current source and evidence. |
| OrchardCore | Pinned commit above | Source definitions, registrations, and adapter pairs inspected. |
| Roslyn | Analyzer's installed package dependencies | Semantic contract and completion classification; no dependency upgrade proposed. |
| Jint / scripting layer | Referenced by the inspected JavaScript scope | Consumer registration inspected; script traffic and sync/async usage rates not measured. |
| ASP.NET Core / hosting contracts | Referenced by Razor, options, and lifecycle source | Synchronous surfaces observed; no deployment-specific behavior measured. |
| StackExchange.Redis / document storage / file providers | Referenced by the inspected awaited implementations | Potential external waits; server latency, timeouts, cache misses, and actual providers were not exercised. |

Missing evidence includes call frequency, task completion at access, latency, thread-pool pressure, shutdown duration, external users of public compatibility APIs, and custom DI replacements. No runtime incident, deadlock, or throughput defect is established. This investigation did not run OrchardCore tests or workloads.

## Calibration sensitivity, not replacement scores

| Hypothetical classification only | Remaining scored errors | Current ladder result |
|---|---:|---:|
| Recorded assessment | 15 | 0 |
| Treat nine proven scripting API adapters and the documented settings accessor as review leads | 5 | 0 |
| Also establish the lifecycle boundary and completed-cache treatment | 3 | 2 |

These are arithmetic scenarios, not analyzer outputs or approved changes. They expose how the current ladder can hide meaningful classification improvements. Zero is triggered by five source sites regardless of codebase size; it is not a population-based demonstration of systemic poor performance.

## Recommended decision sequence

1. Prioritize associating a local explanation with the same task's subsequent access. Preserve symbol identity, mutation checks, function boundaries, and the prohibition on unrelated-comment spillover. This is the clearest mismatch with the project's stated intent policy.
2. Decide how explicit synchronous adapters should declare their boundary. The nine scripting sites provide strong evidence for review-lead treatment, but arbitrary `Func`/`Action` usage, paired names, `[Obsolete]`, or `new` alone should not be blanket exemptions. A bounded semantic contract or the existing rationale-bearing CMAI mechanism keeps the decision deterministic and auditable.
3. Keep the completed cache and internal lifecycle helper as distinct proof questions. Do not broaden private-only analysis to internal visibility without accounting for other assembly callers, or infer completion from arbitrary injected caches.
4. Review population/severity calibration as a separate product decision after classification. Keep a meaningful hotspot penalty, but choose a defensible opportunity population and record a baseline before changing the five-error cliff. Raw members and all informational findings are not automatically suitable denominators.

The immediate priority is explanation/contract fidelity. The remaining score cliff is a calibration issue, not a reason to keep widening exclusions until OrchardCore receives a preferred score.
