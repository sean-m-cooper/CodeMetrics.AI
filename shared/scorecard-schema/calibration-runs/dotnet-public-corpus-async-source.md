# Public corpus with async source counting

Recorded 2026-09-13 after local commit `587fd14` on `codex/public-corpus-fidelity`. The four pinned external repositories were freshly analyzed with the validated local CodeMetrics.AI 2.3.0 package, including dependency probes. Target source was not changed.

The previous [public-corpus runs](dotnet-public-corpus-refactor-15.md) use the earlier counting ruleset. This comparison isolates a counting-policy correction on fixed source; it is not a compatible baseline gate or evidence of source-code improvement. All four runs completed with usable inspections and matching invocation/evidence identities.

## Full scorecards

All values are deterministic within partial static scopes. Every overall is a **partial assessment, 9/9 dimensions scored**. No target tests were executed and no coverage files were supplied. These are pinned source snapshots, not current upstream heads or a ranking of project quality.

| Dimension | Polly | Newtonsoft.Json | SimplCommerce | OrchardCore |
|---|---:|---:|---:|---:|
| Architecture & SOLID | 5.6 | 4.1 | 2 | 4 |
| Complexity & Decomposition | 8 | 4.8 | 7.3 | 5.3 |
| Testing | 8 | 6 | 6 | 6 |
| Security | 10 | 10 | 2 | 0 |
| Error Handling | 2 | 2 | 10 | 0 |
| Documentation | 9 | 2 | 3 | 4 |
| Dependency Management | 2 | 2 | 2 | 2 |
| Performance & Async | 0 | 10 | 2 | 0 |
| Maintainability | 8.2 | 6.9 | 7.3 | 7.3 |
| Overall, partial assessment (9/9) | 5.9 | 5.3 | 4.6 | 3.2 |

| C&D component | Polly | Newtonsoft.Json | SimplCommerce | OrchardCore |
|---|---:|---:|---:|---:|
| Decomposition | 8 | 4 | 7.3 | 4.7 |
| Method complexity | 7.9 | 5.6 | 7.2 | 5.8 |

## Counting correction

| Repository | Async findings before | Distinct sites after | Framework observations retained | Async score before → after |
|---|---:|---:|---:|---|
| Polly | 100 | 24 | 100 | 0 → 0 |
| Newtonsoft.Json | 0 | 0 | 0 | 10 → 10 |
| SimplCommerce | 15 | 15 | 15 | 2 → 2 |
| OrchardCore | 575 | 575 | 575 | 0 → 0 |

Every grouped async finding uses the highest observed severity for its file/span/rule site. Expanding the stored framework observations exactly reproduces the previous category/file/line/project/severity/message tuples, with multiplicity. Distinct source identities remain unique. Raw CSV is byte-identical for all four repositories.

Other non-feed dimension objects are identical, as are source populations, filters, configurations, diagnostics and suppressions. Dependency/security differences, if present, are recorded separately below because feed observations can change. Pattern recognition and ladder thresholds were not changed.

## Scope and observed findings

| Dimension | Included scope | Polly findings | Newtonsoft findings | SimplCommerce findings | OrchardCore findings |
|---|---|---:|---:|---:|---:|
| Architecture & SOLID | static-coupling-and-project-structure | 61 | 359 | 212 | 754 |
| Complexity & Decomposition | production-type-complexity, member-complexity | 0 | 0 | 0 | 0 |
| Testing | test-project-signals, supplied-coverage-report | 0 | 1 | 34 | 211 |
| Security | static-security-patterns, dependency-vulnerability-observations | 0 | 0 | 44 | 66 |
| Error Handling | static-exception-patterns | 23 | 10 | 0 | 168 |
| Documentation | documentation-presence-and-content-signals | 0 | 2 | 0 | 0 |
| Dependency Management | package-version-and-feed-observations | 146 | 43 | 280 | 547 |
| Performance & Async | static-async-and-blocking-patterns | 24 | 0 | 15 | 575 |
| Maintainability | production-executable-function-maintainability-index | 0 | 0 | 0 | 0 |

All scopes exclude runtime behavior and comprehensive human review. The testing scope supports supplied coverage; none was supplied in these runs. Counts describe observations, not independent deductions or confirmed defects. Package findings use project/TFM occurrence counts; the per-package detail below retains those rows without presenting them as distinct packages.

## Run provenance

- Analyzer commit: `587fd14`; local unpublished version 2.3.0; schema v3; Release; calibration `baseline`.
- Ruleset: `dotnet-2026-09-13-async-source-findings`; prior ruleset: `dotnet-2026-09-12-function-maintainability`.
- Tested NuGet SHA-256: `97488adaed562f4bbb5f4dfb8420b43d01d576c1ddb6277df302b2a8013f0263`.
- The committed implementation previously passed 837 tests, including 21 new counting cases, six calibration fixtures and formatting checks. These are recorded validation results, not additional test executions during this replay.
- SDKs, packages, caches, temporary files and run artifacts remain on E:. No package was published and no target repository source was modified.

### Polly

- Source: `1a80392b1f093f40e59c515f4aeb989bea5db857`.
- Entry point: `E:\repos\Polly\Polly.slnx`.
- Audit/run: `fc51817c-7b7e-4783-a9e2-7e8c34b749ec` / `71bd5afb-c0c9-4746-b8fa-1ce6e5b869ad`.
- Baseline audit/run: `2169dfa5-3544-429d-badd-12fd0bd5777f` / `14c04b1c-bd36-4248-b7ac-d4b1c6ca67d4`.
- Configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- Analyzed units: 21 of 53; population: {"types": 1858, "members": 10988}.
- Async severity counts after aggregation: `{"error": 11, "warning": 12, "info": 1}`.
- Dimensions with score changes: `{}`.
- Changed feed-sensitive dimension objects: `["dependencyManagement"]`.
- Changed dependency counts/availability: `{"outdatedFrameworkCompatibilityUnknown": [113, 0]}`. These are observations from this invocation; a change does not by itself identify whether the feed, metadata availability or another environmental factor caused it.
- Workspace diagnostics: `[]`.
- [Evidence](<E:/repos/Polly/.scorecard/dotnet/runs/71bd5afb-c0c9-4746-b8fa-1ce6e5b869ad/evidence.json>), [inspection](<E:/repos/Polly/.scorecard/dotnet/runs/71bd5afb-c0c9-4746-b8fa-1ce6e5b869ad/inspection.json>).

Dependency counts (project/TFM occurrences): `{"countUnit": "package occurrences per project and target framework; not unique package IDs", "vulnerableDirect": 0, "vulnerableTransitive": 0, "outdated": 142, "outdatedAspireExcluded": 0, "outdatedFrameworkIncompatibleExcluded": 0, "outdatedFrameworkCompatibilityUnknown": 0, "deprecated": 0, "unsupportedTFMs": 4, "versionDrift": 0, "cpmEnabled": true, "anyCommandFailed": false}`.

[Grouped packages, versions, TFMs, advisories and scoring dispositions](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-async-source/Polly-dependencies.json>). Compatibility observations identify review candidates; they do not prove that an upgrade is safe.

### Newtonsoft.Json

- Source: `09bb545d72969ad7fb4ea07db0d5c34f4fc07877`.
- Entry point: `E:\repos\Newtonsoft.Json\Src\Newtonsoft.Json.slnx`.
- Audit/run: `2a1227ae-828b-4f73-8b26-32b3bb2c2a58` / `0b58ae72-c2fb-497d-b4a3-04e2b98bf679`.
- Baseline audit/run: `09b63e11-95b5-423d-8479-3639506c41b6` / `0933d97c-8427-4e2d-ace6-8c2f6a0d6668`.
- Configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- Analyzed units: 9 of 16; population: {"types": 1707, "members": 25381}.
- Async severity counts after aggregation: `{}`.
- Dimensions with score changes: `{}`.
- Changed feed-sensitive dimension objects: `[]`.
- Changed dependency counts/availability: `{}`. These are observations from this invocation; a change does not by itself identify whether the feed, metadata availability or another environmental factor caused it.
- Workspace diagnostics: `[{"kind": "workspaceWarning", "message": "Found project reference without a matching metadata reference: E:\\repos\\Newtonsoft.Json\\Src\\Newtonsoft.Json.Tests\\Newtonsoft.Json.Tests.csproj"}]`.
- [Evidence](<E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/0b58ae72-c2fb-497d-b4a3-04e2b98bf679/evidence.json>), [inspection](<E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/0b58ae72-c2fb-497d-b4a3-04e2b98bf679/inspection.json>).

Dependency counts (project/TFM occurrences): `{"countUnit": "package occurrences per project and target framework; not unique package IDs", "vulnerableDirect": 0, "vulnerableTransitive": 0, "outdated": 15, "outdatedAspireExcluded": 0, "outdatedFrameworkIncompatibleExcluded": 22, "outdatedFrameworkCompatibilityUnknown": 0, "deprecated": 3, "unsupportedTFMs": 2, "versionDrift": 0, "cpmEnabled": false, "anyCommandFailed": false}`.

[Grouped packages, versions, TFMs, advisories and scoring dispositions](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-async-source/Newtonsoft.Json-dependencies.json>). Compatibility observations identify review candidates; they do not prove that an upgrade is safe.

### SimplCommerce

- Source: `3472ba02a6f2d9b6bdca7f7fb84957176aa799dc`.
- Entry point: `E:\repos\SimplCommerce\SimplCommerce.sln`.
- Audit/run: `90033f83-00ae-4b6c-8433-0a9695b10335` / `0516a15e-519b-4fd3-a08f-cbf7c25ef731`.
- Baseline audit/run: `d99a1bc8-6f3d-4a65-a8e5-5ec81e5fda39` / `b5cfc9a7-7b90-4b9a-bfe9-6bf384e28b18`.
- Configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- Analyzed units: 42 of 49; population: {"types": 715, "members": 3823}.
- Async severity counts after aggregation: `{"warning": 14, "error": 1}`.
- Dimensions with score changes: `{}`.
- Changed feed-sensitive dimension objects: `["dependencyManagement"]`.
- Changed dependency counts/availability: `{}`. These are observations from this invocation; a change does not by itself identify whether the feed, metadata availability or another environmental factor caused it.
- Workspace diagnostics: `[]`.
- [Evidence](<E:/repos/SimplCommerce/.scorecard/dotnet/runs/0516a15e-519b-4fd3-a08f-cbf7c25ef731/evidence.json>), [inspection](<E:/repos/SimplCommerce/.scorecard/dotnet/runs/0516a15e-519b-4fd3-a08f-cbf7c25ef731/inspection.json>).

Dependency counts (project/TFM occurrences): `{"countUnit": "package occurrences per project and target framework; not unique package IDs", "vulnerableDirect": 0, "vulnerableTransitive": 203, "outdated": 55, "outdatedAspireExcluded": 0, "outdatedFrameworkIncompatibleExcluded": 12, "outdatedFrameworkCompatibilityUnknown": 1, "deprecated": 8, "unsupportedTFMs": 0, "versionDrift": 2, "cpmEnabled": false, "anyCommandFailed": false}`.

[Grouped packages, versions, TFMs, advisories and scoring dispositions](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-async-source/SimplCommerce-dependencies.json>). Compatibility observations identify review candidates; they do not prove that an upgrade is safe.

### OrchardCore

- Source: `4c101f5c6a6e6aca073a800797a223a958b28c0b`.
- Entry point: `E:\repos\OrchardCore\OrchardCore.slnx`.
- Audit/run: `3bde16ef-43aa-4b7d-9e7a-09cee21bb484` / `cdcca8b7-cf21-4cd0-bded-0f0ff91be6f8`.
- Baseline audit/run: `ad6edd43-88d7-4c06-88d1-e2e083518089` / `5f9eb879-7c7e-4d87-96fe-7a1f68226d03`.
- Configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- Analyzed units: 213 of 239; population: {"types": 5568, "members": 24989}.
- Async severity counts after aggregation: `{"warning": 481, "info": 16, "error": 78}`.
- Dimensions with score changes: `{}`.
- Changed feed-sensitive dimension objects: `["dependencyManagement"]`.
- Changed dependency counts/availability: `{"outdated": [540, 541]}`. These are observations from this invocation; a change does not by itself identify whether the feed, metadata availability or another environmental factor caused it.
- Workspace diagnostics: `[{"kind": "workspaceWarning", "message": "Duplicate source file 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\AnalyzerReleases.Shipped.md' in project 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\OrchardCore.SourceGenerators.csproj'"}, {"kind": "workspaceWarning", "message": "Duplicate source file 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\AnalyzerReleases.Unshipped.md' in project 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\OrchardCore.SourceGenerators.csproj'"}, {"kind": "workspaceWarning", "message": "Msbuild failed when processing the file 'E:\\repos\\OrchardCore\\test\\OrchardCore.Tests.Integration\\OrchardCore.Tests.Integration.csproj' with message: Package 'SSH.NET' 2025.1.0 has a known high severity vulnerability, https://github.com/advisories/GHSA-q939-rpr3-3284"}]`.
- [Evidence](<E:/repos/OrchardCore/.scorecard/dotnet/runs/cdcca8b7-cf21-4cd0-bded-0f0ff91be6f8/evidence.json>), [inspection](<E:/repos/OrchardCore/.scorecard/dotnet/runs/cdcca8b7-cf21-4cd0-bded-0f0ff91be6f8/inspection.json>).

Dependency counts (project/TFM occurrences): `{"countUnit": "package occurrences per project and target framework; not unique package IDs", "vulnerableDirect": 0, "vulnerableTransitive": 1, "outdated": 541, "outdatedAspireExcluded": 3, "outdatedFrameworkIncompatibleExcluded": 1, "outdatedFrameworkCompatibilityUnknown": 6, "deprecated": 1, "unsupportedTFMs": 0, "versionDrift": 0, "cpmEnabled": true, "anyCommandFailed": false}`.

[Grouped packages, versions, TFMs, advisories and scoring dispositions](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-async-source/OrchardCore-dependencies.json>). Compatibility observations identify review candidates; they do not prove that an upgrade is safe.

## Interpretation

Source counting is now invariant to repeated framework observations while retaining the underlying evidence. A score that remains low after aggregation needs context review before any threshold change. Prior explanations of intentional library behavior were not reverified here; these remain review candidates rather than confirmed defects or exemptions.

[Machine-readable comparisons and original/new run identities](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-async-source/comparison.json>) are saved alongside invocation JSON, logs, the runner and validation scripts.
