# Public corpus after self-refactor pass 15

Follow-up: the [async source-finding correction](dotnet-async-source-findings.md) implements the counting fix identified below. This report retains its original package, ruleset and results as the pre-fix snapshot.

Recorded 2026-09-13 using the local unpublished CodeMetrics.AI 2.3.0 package validated for commit `eabda48`. Eight fresh runs completed: four with the recorded dependency-skipping configuration for exact regression comparison, and four with live dependency probes for full scorecards. No target source, scoring policy, threshold, suppression or calibration baseline was changed.

## Regression result

**All four fixed-source comparisons are identical.** After excluding only generated timestamps and run/audit IDs, the complete evidence objects match the function-maintainability baselines. Raw CSV is byte-identical in all four. This includes scores, findings, ordering, populations, filters, component calculations, scopes, diagnostics and suppression declarations.

The comparison establishes preserved observations on this development corpus after the analyzer refactors. It does not establish comprehensive correctness or validate the scoring anchors against independent quality labels. These repositories are not a held-out calibration set.

## Full scorecards

Every value is deterministic within its declared static scope. All overalls are **partial assessments, 9/9 dimensions scored**; target test suites were not run and no coverage files were supplied. These are the existing pinned source snapshots with current package/feed observations, not current upstream heads or a ranking of project quality.

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

### C&D components

| Component | Polly | Newtonsoft.Json | SimplCommerce | OrchardCore |
|---|---:|---:|---:|---:|
| Decomposition | 8 | 4 | 7.3 | 4.7 |
| Method complexity | 7.9 | 5.6 | 7.2 | 5.8 |

The component scores are unchanged from the same-policy baselines. Newtonsoft.Json remains a useful example of the distinction between decomposition, method complexity and function maintainability; the repeatability check does not establish that any particular method should be split. Earlier source-level explanations of intent were not reverified in this run.

### Assessment scopes

| Dimension | Included observations | Exclusions |
|---|---|---|
| Architecture & SOLID | static-coupling-and-project-structure | runtime-behavior, comprehensive-human-review |
| Complexity & Decomposition | production-type-complexity, member-complexity | runtime-behavior, comprehensive-human-review |
| Testing | test-project-signals, supplied-coverage-report | runtime-behavior, comprehensive-human-review |
| Security | static-security-patterns, dependency-vulnerability-observations | runtime-behavior, comprehensive-human-review |
| Error Handling | static-exception-patterns | runtime-behavior, comprehensive-human-review |
| Documentation | documentation-presence-and-content-signals | runtime-behavior, comprehensive-human-review |
| Dependency Management | package-version-and-feed-observations | runtime-behavior, comprehensive-human-review |
| Performance & Async | static-async-and-blocking-patterns | runtime-behavior, comprehensive-human-review |
| Maintainability | production-executable-function-maintainability-index | runtime-behavior, comprehensive-human-review |

The testing scope supports a supplied coverage report, but none was supplied here. Static scores do not measure production incident rates, runtime performance or comprehensive security. Package compatibility identifies candidates for review; it does not prove a safe migration.

### Emitted finding counts

Counts describe the emitted evidence, not independent deductions or confirmed defects. Source and project/framework counting policies differ by dimension; package grouping is retained in the linked detail files.

| Dimension | Polly | Newtonsoft.Json | SimplCommerce | OrchardCore |
|---|---:|---:|---:|---:|
| Architecture & SOLID | 61 | 359 | 212 | 754 |
| Complexity & Decomposition | 0 | 0 | 0 | 0 |
| Testing | 0 | 1 | 34 | 211 |
| Security | 0 | 0 | 44 | 66 |
| Error Handling | 23 | 10 | 0 | 168 |
| Documentation | 0 | 2 | 0 | 0 |
| Dependency Management | 146 | 43 | 280 | 546 |
| Performance & Async | 100 | 0 | 15 | 575 |
| Maintainability | 0 | 0 | 0 | 0 |

## Per-repository evidence

### Polly

- Source commit: `1a80392b1f093f40e59c515f4aeb989bea5db857`; tracked files unchanged before and after analysis, with no untracked C#/project/build input files found.
- Entry point: `E:\repos\Polly\Polly.slnx`.
- Full audit/run: `2169dfa5-3544-429d-badd-12fd0bd5777f` / `14c04b1c-bd36-4248-b7ac-d4b1c6ca67d4`.
- Matched audit/run: `f3a5d714-4f08-4c82-95b3-b9540c6d31b7` / `182f3ba6-37e1-4c9b-8e34-9f38693eb5c1`.
- Baseline audit/run: `4d878766-1c2b-4c05-b2e1-ccda846cdea1` / `254e7c25-9db6-4a1e-b802-f77e2ae59734`.
- Loaded/analyzed units: 53/21; population: {"types": 1858, "members": 10988}.
- Full configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- Matched configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- [Full evidence](<E:/repos/Polly/.scorecard/dotnet/runs/14c04b1c-bd36-4248-b7ac-d4b1c6ca67d4/evidence.json>), [inspection](<E:/repos/Polly/.scorecard/dotnet/runs/14c04b1c-bd36-4248-b7ac-d4b1c6ca67d4/inspection.json>), [matched evidence](<E:/repos/Polly/.scorecard/dotnet/runs/182f3ba6-37e1-4c9b-8e34-9f38693eb5c1/evidence.json>).

Workspace diagnostics: none.

Dependency observations (project/TFM occurrences): `{"countUnit": "package occurrences per project and target framework; not unique package IDs", "vulnerableDirect": 0, "vulnerableTransitive": 0, "outdated": 142, "outdatedAspireExcluded": 0, "outdatedFrameworkIncompatibleExcluded": 0, "outdatedFrameworkCompatibilityUnknown": 113, "deprecated": 0, "unsupportedTFMs": 4, "versionDrift": 0, "cpmEnabled": true, "anyCommandFailed": false}`.

Grouped dependency detail: [packages, projects, TFMs, versions, advisories and scoring dispositions](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-refactor-15/Polly-dependencies.json>). Findings remain the analyzer's observations, not independently confirmed migration recommendations.

### Newtonsoft.Json

- Source commit: `09bb545d72969ad7fb4ea07db0d5c34f4fc07877`; tracked files unchanged before and after analysis, with no untracked C#/project/build input files found.
- Entry point: `E:\repos\Newtonsoft.Json\Src\Newtonsoft.Json.slnx`.
- Full audit/run: `09b63e11-95b5-423d-8479-3639506c41b6` / `0933d97c-8427-4e2d-ace6-8c2f6a0d6668`.
- Matched audit/run: `45ecb7d0-42bd-4882-b9d4-249ac9d5952f` / `fbd22cf5-4d8e-4a43-b696-4a90aba9f460`.
- Baseline audit/run: `c8a79a0d-5d90-4f17-b9f1-f8e6bc702da2` / `bd1dea25-ebc8-4204-a180-44e2fde5d887`.
- Loaded/analyzed units: 16/9; population: {"types": 1707, "members": 25381}.
- Full configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- Matched configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- [Full evidence](<E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/0933d97c-8427-4e2d-ace6-8c2f6a0d6668/evidence.json>), [inspection](<E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/0933d97c-8427-4e2d-ace6-8c2f6a0d6668/inspection.json>), [matched evidence](<E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/fbd22cf5-4d8e-4a43-b696-4a90aba9f460/evidence.json>).

Workspace diagnostics: [{"kind": "workspaceWarning", "message": "Found project reference without a matching metadata reference: E:\\repos\\Newtonsoft.Json\\Src\\Newtonsoft.Json.Tests\\Newtonsoft.Json.Tests.csproj"}]

Dependency observations (project/TFM occurrences): `{"countUnit": "package occurrences per project and target framework; not unique package IDs", "vulnerableDirect": 0, "vulnerableTransitive": 0, "outdated": 15, "outdatedAspireExcluded": 0, "outdatedFrameworkIncompatibleExcluded": 22, "outdatedFrameworkCompatibilityUnknown": 0, "deprecated": 3, "unsupportedTFMs": 2, "versionDrift": 0, "cpmEnabled": false, "anyCommandFailed": false}`.

Grouped dependency detail: [packages, projects, TFMs, versions, advisories and scoring dispositions](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-refactor-15/Newtonsoft.Json-dependencies.json>). Findings remain the analyzer's observations, not independently confirmed migration recommendations.

### SimplCommerce

- Source commit: `3472ba02a6f2d9b6bdca7f7fb84957176aa799dc`; tracked files unchanged before and after analysis, with no untracked C#/project/build input files found.
- Entry point: `E:\repos\SimplCommerce\SimplCommerce.sln`.
- Full audit/run: `d99a1bc8-6f3d-4a65-a8e5-5ec81e5fda39` / `b5cfc9a7-7b90-4b9a-bfe9-6bf384e28b18`.
- Matched audit/run: `24dcc8f2-403d-477c-822d-f163b26ba1b3` / `f202b22b-1de1-4314-af93-9ab0a4f35a3e`.
- Baseline audit/run: `fb2e820e-6e72-4176-b606-35002b8e18ab` / `7a3878ed-b381-475f-85cf-da8f2a490728`.
- Loaded/analyzed units: 49/42; population: {"types": 715, "members": 3823}.
- Full configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- Matched configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- [Full evidence](<E:/repos/SimplCommerce/.scorecard/dotnet/runs/b5cfc9a7-7b90-4b9a-bfe9-6bf384e28b18/evidence.json>), [inspection](<E:/repos/SimplCommerce/.scorecard/dotnet/runs/b5cfc9a7-7b90-4b9a-bfe9-6bf384e28b18/inspection.json>), [matched evidence](<E:/repos/SimplCommerce/.scorecard/dotnet/runs/f202b22b-1de1-4314-af93-9ab0a4f35a3e/evidence.json>).

Workspace diagnostics: none.

Dependency observations (project/TFM occurrences): `{"countUnit": "package occurrences per project and target framework; not unique package IDs", "vulnerableDirect": 0, "vulnerableTransitive": 203, "outdated": 55, "outdatedAspireExcluded": 0, "outdatedFrameworkIncompatibleExcluded": 12, "outdatedFrameworkCompatibilityUnknown": 1, "deprecated": 8, "unsupportedTFMs": 0, "versionDrift": 2, "cpmEnabled": false, "anyCommandFailed": false}`.

Grouped dependency detail: [packages, projects, TFMs, versions, advisories and scoring dispositions](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-refactor-15/SimplCommerce-dependencies.json>). Findings remain the analyzer's observations, not independently confirmed migration recommendations.

### OrchardCore

- Source commit: `4c101f5c6a6e6aca073a800797a223a958b28c0b`; tracked files unchanged before and after analysis, with no untracked C#/project/build input files found.
- Entry point: `E:\repos\OrchardCore\OrchardCore.slnx`.
- Full audit/run: `ad6edd43-88d7-4c06-88d1-e2e083518089` / `5f9eb879-7c7e-4d87-96fe-7a1f68226d03`.
- Matched audit/run: `194e42d3-1580-437b-aef0-ab8ec83d8c78` / `b35edc9e-0c83-43ce-a4ee-2c12de4908d3`.
- Baseline audit/run: `19f58e59-09ec-4d10-a067-e84d52c63ddb` / `9f985397-25cc-4352-aecb-85ea76e4cfb1`.
- Loaded/analyzed units: 239/213; population: {"types": 5568, "members": 24989}.
- Full configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- Matched configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- [Full evidence](<E:/repos/OrchardCore/.scorecard/dotnet/runs/5f9eb879-7c7e-4d87-96fe-7a1f68226d03/evidence.json>), [inspection](<E:/repos/OrchardCore/.scorecard/dotnet/runs/5f9eb879-7c7e-4d87-96fe-7a1f68226d03/inspection.json>), [matched evidence](<E:/repos/OrchardCore/.scorecard/dotnet/runs/b35edc9e-0c83-43ce-a4ee-2c12de4908d3/evidence.json>).

Workspace diagnostics: [{"kind": "workspaceWarning", "message": "Duplicate source file 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\AnalyzerReleases.Shipped.md' in project 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\OrchardCore.SourceGenerators.csproj'"}, {"kind": "workspaceWarning", "message": "Duplicate source file 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\AnalyzerReleases.Unshipped.md' in project 'E:\\repos\\OrchardCore\\src\\OrchardCore\\OrchardCore.SourceGenerators\\OrchardCore.SourceGenerators.csproj'"}, {"kind": "workspaceWarning", "message": "Msbuild failed when processing the file 'E:\\repos\\OrchardCore\\test\\OrchardCore.Tests.Integration\\OrchardCore.Tests.Integration.csproj' with message: Package 'SSH.NET' 2025.1.0 has a known high severity vulnerability, https://github.com/advisories/GHSA-q939-rpr3-3284"}]

Dependency observations (project/TFM occurrences): `{"countUnit": "package occurrences per project and target framework; not unique package IDs", "vulnerableDirect": 0, "vulnerableTransitive": 1, "outdated": 540, "outdatedAspireExcluded": 3, "outdatedFrameworkIncompatibleExcluded": 1, "outdatedFrameworkCompatibilityUnknown": 6, "deprecated": 1, "unsupportedTFMs": 0, "versionDrift": 0, "cpmEnabled": true, "anyCommandFailed": false}`.

Grouped dependency detail: [packages, projects, TFMs, versions, advisories and scoring dispositions](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-refactor-15/OrchardCore-dependencies.json>). Findings remain the analyzer's observations, not independently confirmed migration recommendations.

## Provenance and limits

- Schema v3; Release; ruleset `dotnet-2026-09-12-function-maintainability`; calibration `baseline`.
- Current NuGet SHA-256: `673bc2d55c0161acf1ee08b97a5176851bf75456c813f025c3aea3f451e779b6`.
- Historical function-maintainability NuGet SHA-256: `be0e7ee145f020c5038423ed35c019396c7a3fd71afce8a46add6d090265093c`. Both packages identify as local unpublished 2.3.0; content hashes distinguish them.
- Every helper invocation and validator exited zero. Inspection is usable; current invocation, evidence and audit IDs match; packaged rule catalogs are available.
- Full runs retain byte-identical CSV and unchanged evidence outside dependency/security observations and the dependency-probe configuration or diagnostics. Live dependency data is not part of the fixed-source regression assertion.
- The tested package already passed 816 analyzer tests, 324 previous-package compatibility tests and six pinned calibration fixtures in pass 15. These are prior verified results, not additional test executions during this corpus rerun.
- SDKs, caches, packages, temporary files and artifacts remained on E:. No packages were published and no external repositories were modified.
- [Machine-readable comparison](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-refactor-15/comparison.json>) includes all run identities, scopes, differences and artifact paths. Logs, the runner and validation scripts are in the same local directory.

## Next review

This run supports retaining the current calibration while preparing the validated refactors for release. Further calibration work should review representative findings against source and explicitly labeled expectations. Two concrete metric review candidates are Polly's `missingCancellationToken` finding at [AsyncPolicy.ExecuteOverloads.cs:32](<E:/repos/Polly/src/Polly/AsyncPolicy.ExecuteOverloads.cs:32>) and Newtonsoft.Json's `broadCatchWithoutLoggingOrRethrow` finding for `VersionTryParse` at [ConvertUtils.cs:668](<E:/repos/Newtonsoft.Json/Src/Newtonsoft.Json/Utilities/ConvertUtils.cs:668>). The former needs overload/delegation context; the latter needs the parse-failure contract reviewed. This run confirms the observations recur; it does not confirm defects or revalidate earlier intent explanations.

Performance finding identity is also worth reviewing before threshold changes. Polly emits 100 async findings across 24 file/line/member/category groups. The warning above appears separately for net462, net472, net6.0 and netstandard2.0. These are descriptive evidence groups, not a proven replacement source-identity algorithm: distinct operations can occupy the same line. The current probe basis reports all 100 rows, including 48 errors and 47 warnings. This provides a concrete lead for checking source-site aggregation across frameworks; it does not establish a corrected score or prove the warnings are false positives.

Current analyzer source was rechecked: [PerformanceAsyncProbe.Analyze](<E:/repos/CodeMetrics.AI/analyzers/dotnet/src/CodeMetrics.AI/Probes/PerformanceAsyncProbe.cs:21>) gathers findings per project/compilation and [counts that combined list](<E:/repos/CodeMetrics.AI/analyzers/dotnet/src/CodeMetrics.AI/Probes/PerformanceAsyncProbe.cs:42>) directly for the scoring ladder. Source-site aggregation before scoring is therefore the concrete next implementation candidate, consistent with the project's one-authored-site counting goal. Preserve distinct operations, conditional variants and severity differences; retain framework observations as diagnostic detail. [Grouped evidence with current run IDs](<E:/repos/CodeMetrics.AI/TestResults/public-corpus-refactor-15/Polly-async-review.json>) is saved for that work. No counting changes were made during this rerun.
