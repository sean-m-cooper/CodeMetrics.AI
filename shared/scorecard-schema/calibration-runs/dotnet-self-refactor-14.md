# Self-refactor pass 14: supported dependencies and decomposition

This pass follows commit `f0a44b0` on `codex/public-corpus-fidelity`. It updates supported CLI/test dependencies and simplifies seven source components without changing scoring thresholds, recognition policies, public evidence or raw metric definitions. The requested direction is C&D well into the sevens; this pass improves it to 6.3 and does not claim that target is reached.

## Dependencies

| Package | Before | After |
|---|---|---|
| System.CommandLine | 2.0.11 | 2.0.12 |
| Microsoft.NET.Test.Sdk | 18.9.0 | 18.10.0 |
| xunit.v3 | 4.0.0 | 4.0.1 |

Microsoft.Build, Microsoft.Build.Framework, Microsoft.Build.Utilities.Core and Microsoft.NET.StringTools remain aligned at 18.9.6. Trying the three asset-compatible 18.10.1 companion packages restored successfully, but Utilities.Core and StringTools emitted explicit build warnings that .NET 10 is unsupported. Their .NET Standard reference assets are not evidence of supported runtime execution. Microsoft's [Utilities.Core package documentation](https://www.nuget.org/packages/Microsoft.Build.Utilities.Core/18.10.1) explains that its .NET Standard target contains reference assemblies, with runtime implementations targeting .NET 11 or .NET Framework. The final restore/build has no warnings. The version rationale is recorded in Directory.Packages.props; no warnings or findings were suppressed.

The analyzer still includes three outdated MSBuild companion occurrences and excludes Microsoft.Build itself as framework-incompatible. Dependency Management therefore improves from 6 to 8, rather than 10. This is a concrete limitation of interpreting asset compatibility as upgrade guidance: an available reference asset does not establish vendor runtime support. No dependency scoring or compatibility policy was changed to remove these findings.

## Source changes

- ConstructorDependencyCollector binds one ordered enumeration of regular and supported primary parameters, replacing three repeated parameter loops. Regular-before-primary ordering and class/record/record-struct scope remain unchanged.
- WebTypeClassifier reuses hierarchy traversal, separating framework/attribute controller recognition from data-layer naming conventions. Original-type NonController precedence and original-name data conventions remain unchanged.
- CompletedTaskAccess separates binary Boolean proof composition from expression dispatch and shares Task-property symbol validation. AND/OR semantics, equality operand order, receiver identity and bounded helper recognition remain unchanged.
- TargetFrameworkParser reuses matched and optional version parsing. Accepted syntax, alias precedence and optional platform-version behavior remain unchanged.
- FindingSourceResolver separates path normalization from node/member/statement anchoring. Finding locations, token anchors and fingerprint inputs remain unchanged.
- CatchClassifier separates empty-handler classification, narrow exception recognition, immediate fallback placement and default-return syntax. It preserves existing literal/qualified-name/parenthesis distinctions and traversal scope.
- TestingProbe shares recognized test-attribute enumeration with skip/ignore detection, replacing repeated nested loops. Unrelated attributes remain excluded, and both named properties and named constructor arguments retain their behavior.

These are responsibility and traversal simplifications. Compact syntax dispatch in FunctionMaintainabilityCalculator and ExecutableFunctionCollector remains intact; no trivial wrappers or suppression declarations were added to achieve a requested score. Runtime throughput was not benchmarked.

## Verification

- **790 .NET tests passed**, with no failures or skips; final restore/build has zero warnings and errors.
- Eleven new cases cover default-return expression recognition and mixed attribute lists, including unrelated Skip properties and named constructor arguments.
- **236 compatibility tests passed against the previous packaged analyzer DLL**, including the new cases and existing architecture, error-handling, testing, performance and evidence tests.
- Six pinned .NET calibration fixtures passed without recording baseline changes. Formatting and Git whitespace checks passed.
- Previous and current packaged behavior on identical final source produced identical complete evidence except fresh IDs and timestamp, plus byte-identical CSV. This comparison skips live dependency queries and covers 138 production types and 749 raw members.
- A separate fresh helper run includes dependency commands and validates complete, usable evidence, rule catalog availability, matching invocation/evidence IDs and no analysis diagnostics. One production project was analyzed from two loaded units; the test project is excluded from production metrics.

Compatibility claims are limited to the tested cases, six fixtures and same-source comparison. A full public-corpus rerun remains part of the release checkpoint.

## Full scorecard

Overall **8.7, partial assessment, 9/9 dimensions scored**, previously **8.4, partial assessment, 9/9 dimensions scored**. All dimension scores are deterministic within their declared static scopes. No measured test coverage was supplied; runtime behavior and comprehensive human review remain outside the assessment.

| Dimension | Before | After | Scope and evidence |
|---|---:|---:|---|
| Architecture & SOLID | 9.2 | 9.2 | Static coupling/project structure; three coupling hotspots among 90 eligible types, one size hotspot, no cycles |
| Complexity & Decomposition | 5.9 | 6.3 | Method complexity 7.8; decomposition 4.0 → 4.7 |
| Testing | 10 | 10 | Static signals; 573 authored test methods, 1,205 assertions; no coverage file |
| Security | 10 | 10 | Static patterns and vulnerability observations; no findings |
| Error Handling | 10 | 10 | Static exception patterns; no errors/warnings |
| Documentation | 8 | 8 | Presence/content signals; one stale marker |
| Dependency Management | 6 | 8 | Three included outdated occurrences, one incompatible upgrade excluded; no vulnerabilities, deprecations, unsupported current TFMs or version drift |
| Performance & Async | 10 | 10 | Static async/blocking patterns; no findings |
| Maintainability | 6.8 | 6.8 | Own MI for 1,151 distinct executable functions |

High-decomposition types decrease from **18/72 (25.00%) to 11/72 (15.28%)**. The displayed p90 ratio decreases from **5.15 to 4.48**, improving its component from 2 to 4. Population remains just above the unchanged 15% boundary and its component remains 0; extreme ratio remains 10. Decomposition is therefore `(0 + 4 + 10) / 3`, rounded to 4.7. These observations do not justify adjusting a threshold for this repository.

High-complexity functions decrease from **11 to 8**. Worst own CC remains 14. Method complexity's unrounded score improves from 7.8022825070 to 7.8190633609, still rounding to 7.8. Maintainability improves from 6.7609001223 to 6.8279506432, still rounding to 6.8. All new helper functions remain in their applicable populations.

The remaining C&D target requires further substantive work. A next source-reviewed candidate is MemberMetricsCollector.Collect: it still combines member collection, partial-symbol normalization and ordered implementation replacement. Those are distinct responsibilities, but changing them must preserve authored-only collection and existing partial-member identity/order tests. A higher aggregate score is not guaranteed by extracting them.

## Provenance

- Tool: local unpublished 2.3.0, schema v3, Release; entry point `analyzers/dotnet/CodeMetrics.AI.slnx`.
- Ruleset: `dotnet-2026-09-12-function-maintainability`; calibration: `baseline`.
- Configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- Audit ID: `96c75676-e18f-43dd-b5cd-e4228242aca5`.
- Run ID: `4ceb6e3f-7180-49ab-93cf-7d591baed0bd`.
- Package SHA-256: `06966bfb66a9e034e1ff67adbd9be0251e89f3c44a639334670f1753d4577e37`.
- [Validated run evidence](<E:/repos/CodeMetrics.AI/.scorecard/dotnet/runs/4ceb6e3f-7180-49ab-93cf-7d591baed0bd/evidence.json>).
- [Validation and score comparison](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-14/verification.json>).
- [Behavior comparison identities](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-14/contract-verification.json>).

Packages, caches, temporary tools and run artifacts remain on E:. No packages were published.
