# OrchardCore remaining waits: source review

Reviewed 2026-09-13 against the validated async-context corpus run. All 35 error-level wait sites are accounted for. This is a source review, not a new analyzer run or performance measurement. The recorded Performance & Async score remains 0; no scoring policy or source code was changed by this review.

## Provenance

- Repository: `E:/repos/OrchardCore`
- Source commit: `4c101f5c6a6e6aca073a800797a223a958b28c0b`; tracked source unchanged when reviewed.
- Run: `69683394-cc70-4cc0-a929-36950f45687d`
- Audit: `c04502bf-4f56-4711-9c51-9b046bb3b01e`
- Candidate analyzer: CodeMetrics.AI 2.3.0, schema 3, Release.
- Ruleset: `dotnet-2026-09-13-async-context`
- Package SHA256: `c8af6cd3c33b4eddc7e41a03110d191aac0bbbc3b9e75e81b997f0f50d767ec4`
- Evidence: `E:/repos/OrchardCore/.scorecard/dotnet/runs/69683394-cc70-4cc0-a929-36950f45687d/evidence.json`
- Prior full-run comparison: [async-context corpus report](dotnet-public-corpus-async-context.md).
- Local machine-readable review, including original fingerprints and source spans: `E:/repos/CodeMetrics.AI/TestResults/orchard-wait-review/classification-review.json`.

## Assessment

| Source context | Sites | Interpretation |
| --- | ---: | --- |
| Completed-task reads | 9 | Definite false positives for a potentially blocking task wait: seven completed-return helpers and two guarded ternary branches. |
| Cache invariant | 1 | Default implementation stores only completed tasks; injected mutable storage limits the proof. |
| Paired scripting adapters | 9 | Explicit sync and async script APIs; synchronous choice can still block. |
| Synchronous contracts or lifecycle | 12 | Waits in interface properties, callbacks, private helpers, or shutdown paths. Context is currently missed or inconsistently propagated. |
| Explicit synchronous or compatibility APIs | 4 | Deliberate sync surface, obsolete wrapper, or documented preloaded fallback; still reviewable. |
| Total | 35 | Source sites, not necessarily 35 independent design defects. |

The other 26 sites are not proven harmless by this review. One has a strong cache invariant; the remaining 25 can perform real waits. A synchronous contract explains why a wait appears, but does not establish acceptable latency, thread use, or deployment behavior.

### Severity currently depends on where the wait is written

`S3TusTempStore.OpenReadStream` (line 242) and `GetFileLength` (251) implement synchronous `ITusTempStore` members. Their direct S3 waits at 247 and 256 are informational. Both call `CompleteMultipartUploadIfNeeded` (267), whose five waits at 269, 275, 289, 292, and 293 remain errors. Extracting work into this private helper loses the contract context.

Similarly, `RedisKeyManagementOptionsSetup.Configure` has an informational `redis.ConnectAsync()` wait at line 35, while the same call in the `RedisXmlRepository` factory callback at line 51 remains an error. The repository constructor requires `Func<IDatabase>`, verified in the installed Microsoft.AspNetCore.DataProtection.StackExchangeRedis 10.0.11 XML API documentation. `RoleStore.Roles` is another gap: it implements the synchronous `IQueryableRoleStore<IRole>.Roles` property, confirmed against the installed framework reference documentation, whereas current contract recognition focuses on methods.

These examples support consistent recognition of resolved contracts and bounded private call chains. They do not support exempting every callback, property, or synchronous wrapper.

### The S3 path is the strongest design review candidate

The actual upload-completion handler is already async:

`OrchardCore.Media/Startup.cs:636 OnFileCompleteAsync` -> line 673 `store.OpenReadStream` -> `DistributedMediaTusStore.OpenReadStream:230` -> `S3TusTempStore.OpenReadStream:242` -> `CompleteMultipartUploadIfNeeded:267`.

That private helper waits on cache operations and S3 multipart completion. The caller then resumes awaiting file persistence. An async stream-opening/finalization contract could let this path remain async throughout, but it requires an interface and caller design change. Replacing a single wait with `await` is insufficient. No runtime measurements were taken; this is a concrete review lead rather than a demonstrated production bottleneck.

### Scripting bridges are explicit API choices

Nine waits belong to `GlobalMethod.Method` delegates, each paired with an `AsyncMethod` delegate for the same operation. `JavaScriptScope.cs:52-59` registers these as separate `name` and `nameAsync` functions; `JavaScriptEngine.cs:181-208` supports the same distinction for lazy globals. They execute when a script invokes the synchronous function, despite findings being labeled with the enclosing provider constructor. This calls for accurate boundary attribution and retained review evidence, not a claim that the constructor itself blocks or that no async API exists.

### Completed-return proof needs precise scope

Seven `ShellDbTablesInfo` helpers return `Task.CompletedTask`. The task waits therefore add no blocking. However, `DropForeignKeyAsync` can execute synchronous database work before returning that completed task. Removing the sync-over-async finding should not imply that the entire operation is asynchronous or free of blocking I/O.

`LiquidViewTemplate` and the success branch of `SiteServiceExtensions.GetSiteSettings` guard `.Result` with `IsCompletedSuccessfully` in ternary expressions. The latter's fallback `.GetAwaiter().GetResult()` is a distinct site on the same line and must remain independently assessed.

## Recommended next analyzer work

1. Extend deterministic completion proof to guarded ternary branches and narrowly resolved completed-return helpers. Require proof across normal returns; preserve receiver mutation, deferred execution, and dispatch safeguards.
2. Apply synchronous boundary context consistently to interface properties, resolved synchronous callback contracts, and bounded private helper call chains. Preserve potentially blocking observations as review leads. Mixed sync/async callers and arbitrary delegates must not gain blanket exemptions.
3. Keep paired API and cache-invariant cases explicit in evidence. Pairing or comments alone should not become a universal safety rule. Use supported CMAI rationale mechanisms where the analyzer cannot prove context reliably.

Keep numeric calibration unchanged until these classification gaps are addressed and reviewed. Do not mechanically subtract these manual groups and present a new score: they include different proof strengths, and the score is nonlinear.

## All 35 source sites

Paths below are relative to the reviewed OrchardCore checkout. Source spans and fingerprints disambiguate sites on the same line in the machine-readable review.

| # | Source | Expression | Assessment | Evidence |
| ---: | --- | --- | --- | --- |
| 1 | `src/OrchardCore.Modules/OrchardCore.Contents/Scripting/ContentMethodsProvider.cs:23` | `NewContentItemAsync(serviceProvider, contentType).GetAwaiter().GetResult` | Paired scripting adapter | GlobalMethod exposes synchronous Method and asynchronous AsyncMethod delegates for the same operation. JavaScriptScope registers separate name and nameAsync entry points. The wait runs when the script invokes the synchronous delegate, not during the provider constructor. |
| 2 | `src/OrchardCore.Modules/OrchardCore.Contents/Scripting/ContentMethodsProvider.cs:32` | `CreateContentItemAsync(serviceProvider, contentType, publish, properties).GetAwaiter().GetResult` | Paired scripting adapter | GlobalMethod exposes synchronous Method and asynchronous AsyncMethod delegates for the same operation. JavaScriptScope registers separate name and nameAsync entry points. The wait runs when the script invokes the synchronous delegate, not during the provider constructor. |
| 3 | `src/OrchardCore.Modules/OrchardCore.Contents/Scripting/ContentMethodsProvider.cs:41` | `UpdateContentItemAsync(serviceProvider, contentItem, properties).GetAwaiter().GetResult` | Paired scripting adapter | GlobalMethod exposes synchronous Method and asynchronous AsyncMethod delegates for the same operation. JavaScriptScope registers separate name and nameAsync entry points. The wait runs when the script invokes the synchronous delegate, not during the provider constructor. |
| 4 | `src/OrchardCore.Modules/OrchardCore.Contents/Scripting/ContentMethodsProvider.cs:50` | `DeleteContentItemAsync(serviceProvider, contentItem).GetAwaiter().GetResult` | Paired scripting adapter | GlobalMethod exposes synchronous Method and asynchronous AsyncMethod delegates for the same operation. JavaScriptScope registers separate name and nameAsync entry points. The wait runs when the script invokes the synchronous delegate, not during the provider constructor. |
| 5 | `src/OrchardCore.Modules/OrchardCore.CustomSettings/Services/CustomSettingsService.cs:60` | `GetSettingsTypeAsync(settingsTypeName).Result` | Explicit synchronous or compatibility API | Obsolete GetSettingsType delegates to GetSettingsTypeAsync; the obsolescence message directs callers to the async API. No in-repository C# callers found. |
| 6 | `src/OrchardCore.Modules/OrchardCore.DataProtection.Azure/BlobOptionsConfiguration.cs:75` | `blobContainer.CreateIfNotExistsAsync(PublicAccessType.None).GetAwaiter().GetResult` | Synchronous contract or lifecycle | IConfigureOptions<BlobOptions>.Configure calls private ConfigureContainerName; container creation performs a real network wait inside that helper. |
| 7 | `src/OrchardCore.Modules/OrchardCore.Https/Startup.cs:51` | `service.GetSettingsAsync().GetAwaiter().GetResult` | Synchronous contract or lifecycle | Synchronous options Configure callback waits for HTTPS settings. The same startup class also has an async configuration path. |
| 8 | `src/OrchardCore.Modules/OrchardCore.Media.AmazonS3/Services/S3TusTempStore.cs:269` | `_cache.GetStringAsync(UploadIdKey(fileId)).GetAwaiter().GetResult` | Synchronous contract or lifecycle | Private multipart-finalization helper waits for upload ID cache lookup. |
| 9 | `src/OrchardCore.Modules/OrchardCore.Media.AmazonS3/Services/S3TusTempStore.cs:275` | `GetPartListAsync(fileId, CancellationToken.None).GetAwaiter().GetResult` | Synchronous contract or lifecycle | Private multipart-finalization helper waits for cached part list. |
| 10 | `src/OrchardCore.Modules/OrchardCore.Media.AmazonS3/Services/S3TusTempStore.cs:289` | `_s3Client.CompleteMultipartUploadAsync(request).GetAwaiter().GetResult` | Synchronous contract or lifecycle | Private multipart-finalization helper waits for S3 multipart completion. |
| 11 | `src/OrchardCore.Modules/OrchardCore.Media.AmazonS3/Services/S3TusTempStore.cs:292` | `_cache.RemoveAsync(UploadIdKey(fileId)).GetAwaiter().GetResult` | Synchronous contract or lifecycle | Private multipart-finalization helper waits for upload ID cache removal. |
| 12 | `src/OrchardCore.Modules/OrchardCore.Media.AmazonS3/Services/S3TusTempStore.cs:293` | `_cache.RemoveAsync(PartListKey(fileId)).GetAwaiter().GetResult` | Synchronous contract or lifecycle | Private multipart-finalization helper waits for part-list cache removal. |
| 13 | `src/OrchardCore.Modules/OrchardCore.OpenId/Configuration/OpenIdValidationConfiguration.cs:247` | `_shellHost.GetScopeAsync(tenant).GetAwaiter().GetResult` | Synchronous contract or lifecycle | Private CreateTenantScope is called from three synchronous Configure overloads; the cross-tenant branch waits for GetScopeAsync. |
| 14 | `src/OrchardCore.Modules/OrchardCore.Queries/QueryGlobalMethodProvider.cs:20` | `ExecuteQueryAsync(serviceProvider, name, parameters).GetAwaiter().GetResult` | Paired scripting adapter | GlobalMethod exposes synchronous Method and asynchronous AsyncMethod delegates for the same operation. JavaScriptScope registers separate name and nameAsync entry points. The wait runs when the script invokes the synchronous delegate, not during the provider constructor. |
| 15 | `src/OrchardCore.Modules/OrchardCore.Redis/Options/RedisKeyManagementOptionsSetup.cs:51` | `redis.ConnectAsync().GetAwaiter().GetResult` | Synchronous contract or lifecycle | RedisXmlRepository accepts a synchronous Func<IDatabase>; its callback waits for reconnection. The direct ConnectAsync wait in Configure is already informational. |
| 16 | `src/OrchardCore.Modules/OrchardCore.Redis/Services/RedisDatabaseFactory.cs:71` | `factory.Value.GetAwaiter().GetResult` | Synchronous contract or lifecycle | Release runs from ApplicationStopped registration or Dispose after shutdown and waits for cached connection tasks before disposing multiplexers. |
| 17 | `src/OrchardCore.Modules/OrchardCore.Roles/Services/RoleStore.cs:39` | `GetRolesAsync().GetAwaiter().GetResult` | Synchronous contract or lifecycle | Roles implements the synchronous IQueryableRoleStore<IRole>.Roles property; its document lookup has an async API and may need initialization. |
| 18 | `src/OrchardCore.Modules/OrchardCore.Workflows/Http/Scripting/HttpMethodsProvider.cs:71` | `ResponseWriteAsync(httpContextAccessor, text).GetAwaiter().GetResult` | Paired scripting adapter | GlobalMethod exposes synchronous Method and asynchronous AsyncMethod delegates for the same operation. JavaScriptScope registers separate name and nameAsync entry points. The wait runs when the script invokes the synchronous delegate, not during the provider constructor. |
| 19 | `src/OrchardCore.Modules/OrchardCore.Workflows/Http/Scripting/HttpMethodsProvider.cs:89` | `ReadBodyAsync(httpContextAccessor).GetAwaiter().GetResult` | Paired scripting adapter | GlobalMethod exposes synchronous Method and asynchronous AsyncMethod delegates for the same operation. JavaScriptScope registers separate name and nameAsync entry points. The wait runs when the script invokes the synchronous delegate, not during the provider constructor. |
| 20 | `src/OrchardCore.Modules/OrchardCore.Workflows/Http/Scripting/HttpMethodsProvider.cs:142` | `DeserializeRequestDataAsync(httpContextAccessor).GetAwaiter().GetResult` | Paired scripting adapter | GlobalMethod exposes synchronous Method and asynchronous AsyncMethod delegates for the same operation. JavaScriptScope registers separate name and nameAsync entry points. The wait runs when the script invokes the synchronous delegate, not during the provider constructor. |
| 21 | `src/OrchardCore/OrchardCore.Abstractions/Modules/FileProviders/FileInfoExtensions.cs:8` | `ReadAllLinesAsync(fileInfo).GetAwaiter().GetResult` | Explicit synchronous or compatibility API | ReadAllLines is an explicit synchronous wrapper around ReadAllLinesAsync. No in-repository C# callers found; external callers remain possible. |
| 22 | `src/OrchardCore/OrchardCore.Abstractions/Shell/Configuration/ShellConfiguration.cs:83` | `EnsureConfigurationAsync().GetAwaiter().GetResult` | Synchronous contract or lifecycle | Configuration getter and indexer reach EnsureConfiguration, which waits only when lazy configuration has not initialized; the async path invokes the supplied factory. |
| 23 | `src/OrchardCore/OrchardCore.Data.YesSql/Removing/ShellDbTablesInfo.cs:67` | `CreateMapIndexTableAsync(indexType, table, collection).GetAwaiter().GetResult` | Completed-task read | Resolved helper in sealed ShellDbTablesInfo completes its bookkeeping synchronously and returns Task.CompletedTask on normal completion. |
| 24 | `src/OrchardCore/OrchardCore.Data.YesSql/Removing/ShellDbTablesInfo.cs:84` | `CreateReduceIndexTableAsync(indexType, table, collection).GetAwaiter().GetResult` | Completed-task read | Resolved helper in sealed ShellDbTablesInfo completes its bookkeeping synchronously and returns Task.CompletedTask on normal completion. |
| 25 | `src/OrchardCore/OrchardCore.Data.YesSql/Removing/ShellDbTablesInfo.cs:104` | `CreateTableAsync(name, table).GetAwaiter().GetResult` | Completed-task read | Resolved helper in sealed ShellDbTablesInfo completes its bookkeeping synchronously and returns Task.CompletedTask on normal completion. |
| 26 | `src/OrchardCore/OrchardCore.Data.YesSql/Removing/ShellDbTablesInfo.cs:124` | `DropMapIndexTableAsync(indexType, collection).GetAwaiter().GetResult` | Completed-task read | Resolved helper in sealed ShellDbTablesInfo completes its bookkeeping synchronously and returns Task.CompletedTask on normal completion. |
| 27 | `src/OrchardCore/OrchardCore.Data.YesSql/Removing/ShellDbTablesInfo.cs:139` | `DropReduceIndexTableAsync(indexType, collection).GetAwaiter().GetResult` | Completed-task read | Resolved helper in sealed ShellDbTablesInfo completes its bookkeeping synchronously and returns Task.CompletedTask on normal completion. |
| 28 | `src/OrchardCore/OrchardCore.Data.YesSql/Removing/ShellDbTablesInfo.cs:158` | `DropTableAsync(name).GetAwaiter().GetResult` | Completed-task read | Resolved helper in sealed ShellDbTablesInfo completes its bookkeeping synchronously and returns Task.CompletedTask on normal completion. |
| 29 | `src/OrchardCore/OrchardCore.Data.YesSql/Removing/ShellDbTablesInfo.cs:258` | `DropForeignKeyAsync(srcTable, name).GetAwaiter().GetResult` | Completed-task read | DropForeignKeyAsync returns Task.CompletedTask on normal completion. It can perform synchronous database Execute before returning; the task wait adds no blocking, but the routine is not free of synchronous I/O. |
| 30 | `src/OrchardCore/OrchardCore.DisplayManagement.Liquid/LiquidViewTemplate.cs:39` | `templateTask.Result` | Completed-task read | The ternary expression reads templateTask.Result only when IsCompletedSuccessfully is true; the other branch awaits it. |
| 31 | `src/OrchardCore/OrchardCore.DisplayManagement/Descriptors/DefaultShapeTableManager.cs:64` | `shapeTable.Result` | Cache invariant | Only observed cache writer stores Task.FromResult(shapeTable). Default registration uses a ConcurrentDictionary; the injected mutable IDictionary can be replaced, so this is a default-source invariant rather than universal proof. |
| 32 | `src/OrchardCore/OrchardCore.DisplayManagement/Razor/RazorPage.cs:306` | `RenderSectionAsync(name, required).GetAwaiter().GetResult` | Explicit synchronous or compatibility API | RazorPage deliberately replaces the synchronous RenderSection API and provides RenderSectionAsync alongside it; rendering can still block on real async work. |
| 33 | `src/OrchardCore/OrchardCore.Infrastructure.Abstractions/Settings/SiteServiceExtensions.cs:52` | `task.GetAwaiter().GetResult` | Explicit synchronous or compatibility API | GetSiteSettings is documented for synchronous paths and notes tenant-activation preloading. The activation handler awaits the preload, but this fallback can still wait when the task is incomplete. |
| 34 | `src/OrchardCore/OrchardCore.Infrastructure.Abstractions/Settings/SiteServiceExtensions.cs:52` | `task.Result` | Completed-task read | The success branch of task.IsCompletedSuccessfully ? task.Result : task.GetAwaiter().GetResult() cannot block. The fallback is a separate source site on the same line. |
| 35 | `src/OrchardCore/OrchardCore.Recipes.Core/VariablesMethodProvider.cs:21` | `GetVariableValueAsync(serviceProvider, variables, scopedMethodProviders, name).GetAwaiter().GetResult` | Paired scripting adapter | GlobalMethod exposes synchronous Method and asynchronous AsyncMethod delegates for the same operation. JavaScriptScope registers separate name and nameAsync entry points. The wait runs when the script invokes the synchronous delegate, not during the provider constructor. |
