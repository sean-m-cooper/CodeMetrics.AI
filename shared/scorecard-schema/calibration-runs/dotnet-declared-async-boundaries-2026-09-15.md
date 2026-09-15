# Declared asynchronous boundaries: OrchardCore verification

The local unpublished 2.3.0 candidate reclassifies exactly ten source sites in both Performance & Async and Error Handling. Nine are cataloged synchronous scripting delegates; one is the documented settings-task accessor. All findings remain visible, with unchanged fingerprints and raw metrics. The numerical ladder is unchanged.

## Scorecard

All values are deterministic within their partial static scopes. These are classification comparisons across incompatible rulesets, not source improvements or compatible baseline gates.

| Dimension | Before | Current | Scope includes |
|---|---:|---:|---|
| Architecture & SOLID | 4 | 4 | static-coupling-and-project-structure |
| Complexity & Decomposition | 5.3 | 5.3 | production-type-complexity, member-complexity |
| Testing | 6 | 6 | test-project-signals, supplied-coverage-report |
| Security | 6 | 6 | static-security-patterns, production-and-unknown-project-dependency-vulnerabilities |
| Error Handling | 4 | 4 | static-exception-patterns |
| Documentation | 4 | 4 | documentation-presence-and-content-signals |
| Dependency Management | 2 | 2 | package-version-and-feed-observations, production-and-development-project-dependencies |
| Performance & Async | 0 | 0 | static-async-and-blocking-patterns |
| Maintainability | 7.3 | 7.3 | production-executable-function-maintainability-index |
| **Overall, partial assessment (9/9)** | **4.3** | **4.3** | Static assessment; no runtime or coverage run |

Performance & Async still scores **0**: scored waits fall from **15 to 5**, and informational review leads rise from **531 to 541**. The `errors >= 5` rung remains selected. Error Handling stays **4**. All dimension scores are unchanged. Dependency vulnerability/deprecation findings are unchanged; the live feed now reports Aspire.Hosting.AppHost 13.5.4 instead of 13.5.3 in one informational row already excluded by Aspire policy. The comparison verifies that only its message/latest-version field changed. Every other finding outside the ten wait sites is unchanged.

## Retained, reclassified findings

These locations change to informational review leads in both wait dimensions. Each dimension retains its own rule ID and fingerprint. The current metadata records the reason and `excludedReviewLead` disposition.

| Source | Classification reason |
|---|---|
| [ContentMethodsProvider.cs:23](E:/repos/OrchardCore/src/OrchardCore.Modules/OrchardCore.Contents/Scripting/ContentMethodsProvider.cs:23) | `synchronousScriptingContract` |
| [ContentMethodsProvider.cs:32](E:/repos/OrchardCore/src/OrchardCore.Modules/OrchardCore.Contents/Scripting/ContentMethodsProvider.cs:32) | `synchronousScriptingContract` |
| [ContentMethodsProvider.cs:41](E:/repos/OrchardCore/src/OrchardCore.Modules/OrchardCore.Contents/Scripting/ContentMethodsProvider.cs:41) | `synchronousScriptingContract` |
| [ContentMethodsProvider.cs:50](E:/repos/OrchardCore/src/OrchardCore.Modules/OrchardCore.Contents/Scripting/ContentMethodsProvider.cs:50) | `synchronousScriptingContract` |
| [QueryGlobalMethodProvider.cs:20](E:/repos/OrchardCore/src/OrchardCore.Modules/OrchardCore.Queries/QueryGlobalMethodProvider.cs:20) | `synchronousScriptingContract` |
| [HttpMethodsProvider.cs:71](E:/repos/OrchardCore/src/OrchardCore.Modules/OrchardCore.Workflows/Http/Scripting/HttpMethodsProvider.cs:71) | `synchronousScriptingContract` |
| [HttpMethodsProvider.cs:89](E:/repos/OrchardCore/src/OrchardCore.Modules/OrchardCore.Workflows/Http/Scripting/HttpMethodsProvider.cs:89) | `synchronousScriptingContract` |
| [HttpMethodsProvider.cs:142](E:/repos/OrchardCore/src/OrchardCore.Modules/OrchardCore.Workflows/Http/Scripting/HttpMethodsProvider.cs:142) | `synchronousScriptingContract` |
| [SiteServiceExtensions.cs:52](E:/repos/OrchardCore/src/OrchardCore/OrchardCore.Infrastructure.Abstractions/Settings/SiteServiceExtensions.cs:52) | `documentedTaskLocalChoice` |
| [VariablesMethodProvider.cs:21](E:/repos/OrchardCore/src/OrchardCore/OrchardCore.Recipes.Core/VariablesMethodProvider.cs:21) | `synchronousScriptingContract` |

The remaining scored performance waits are the Redis shutdown helper, completed-task cache access, obsolete custom-settings facade, Razor facade and file-reading facade. Their context and limitations are recorded in the [investigation](dotnet-orchard-performance-review-2026-09-15.md). They were not reclassified by this change.

## Identity and validation

- OrchardCore source `4c101f5c6a6e6aca073a800797a223a958b28c0b`, `OrchardCore.slnx`, Release; 213/239 analyzed units, 5,568 types and 24,989 raw members.
- Current run `fd82da6f-ce91-4a01-bc1f-d38af68c4b34`, audit `bd769c8e-2687-4a0a-bdd0-c12b94c26c31`; fresh complete analysis and usable schema-v3 inspection.
- Previous run `1f13186d-7eaf-42cd-9a33-5b338841b99e`, audit `a65aad6a-ddb9-4176-9cd5-088567d90d43`.
- Ruleset `dotnet-2026-09-15-declared-async-boundaries`; performance policy `context-classification-v4`.
- Configuration fingerprint `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`; unchanged configuration, source population, filters and byte-identical CSV.
- **1,151 tests passed**, including 29 new cases; six calibration fixtures and format verification passed. Fixture scores/findings are unchanged; only the baseline ruleset identifier changed.
- Package SHA256 `2dd5fc2e948801cfdd1b37acfea49afd5fed0d466fcbdad4c5d40d4857d20779`. Packaged/installed DLL and catalogs match the tested output.
- [Evidence](E:/repos/OrchardCore/.scorecard/dotnet/runs/fd82da6f-ce91-4a01-bc1f-d38af68c4b34/evidence.json), [inspection](E:/repos/OrchardCore/.scorecard/dotnet/runs/fd82da6f-ce91-4a01-bc1f-d38af68c4b34/inspection.json), [metrics](E:/repos/OrchardCore/.scorecard/dotnet/runs/fd82da6f-ce91-4a01-bc1f-d38af68c4b34/metrics.csv).

## Interpretation and next decisions

1. The implementation now honors the documented task rationale and an explicit framework scripting contract while retaining the observations. Negative cases cover different receivers, reassignment, ref escape, hidden mutation, unrelated calls/comments, intervening statements, deferred functions, async delegates and similar named arbitrary APIs.
2. A synchronous boundary describes intent, not task completion or runtime safety. The package catalog documents the treatment under CMAI8001 and CMAI5005; existing rationale-bearing annotations cover reviewed cases outside the bounded recognition.
3. Review the remaining cache/lifecycle proof boundaries and then calibrate the absolute-count ladder separately. No population denominator or new threshold was invented in this change. No runtime performance improvement is claimed.

[Policy and limitations](../performance-async-policy.md) · [Machine-readable verification](dotnet-declared-async-boundaries-2026-09-15.json).
