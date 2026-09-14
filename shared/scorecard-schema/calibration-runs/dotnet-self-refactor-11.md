# Self-refactor pass 11: solution selection and diagnostics

This pass follows commit `8bab387` on `codex/public-corpus-fidelity`. It separates project selection and compilation diagnostics from the solution loader without changing production/test scope or scoring policy.

## Changes

`SolutionProjectSelection` owns entry-point and build-scope filtering, declared test-project selection and semantic test reclassification. An ordinal name set replaces repeated scans of skipped projects while preserving the existing exact-name matching rule. `SolutionCompilationLoader` coordinates bounded compilation and metric collection. `SolutionCompilationDiagnostics` owns missing-compilation, compiler-error and empty-population reporting.

References remain available to Roslyn without becoming additional scored projects in project-entry-point mode. Named and semantically recognized tests still enter testing compilations and compiler diagnostics while staying outside production metrics. Disabled, out-of-scope and excluded host/benchmark projects retain their previous treatment. Diagnostic order, skipped-project order, the twenty-error cap per compilation and cancellation propagation are unchanged.

## Verification

- **773 .NET tests passed**, with no failures or skips; build has no warnings or errors.
- Five new tests cover mixed production/test/host/benchmark roles, disabled/out-of-scope projects containing invalid source, reference resolution in project mode, blocking test-compilation errors and their twenty-error cap, null-compilation/empty-population diagnostics, skipped ordering and caller cancellation.
- Four of those behavioral tests also passed against the previous packaged analyzer DLL. The null-compilation case directly exercises the new diagnostic component in the current implementation.
- Six pinned .NET calibration fixtures passed without recording baseline changes. Full formatting and Git whitespace checks passed.
- Old and new analyzers evaluated identical current production source: 135 types and 732 raw members. Complete evidence matches except fresh IDs and generated timestamp; raw CSV is byte-identical. This comparison skips live dependency queries.
- A separate fresh helper audit includes dependency commands. It is complete, usable and catalog-validated, with matching invocation/evidence IDs and no analysis diagnostics. One production project was analyzed out of two loaded units; the test project was excluded from production metrics.

The full public corpus remains deferred to the release checkpoint unless measurement behavior changes. Compatibility evidence is limited to these tests, fixtures and source comparisons.

## Self-scorecard

All rounded dimension scores are unchanged. Overall **8.5, partial assessment, 9/9 dimensions scored**. Scores are deterministic within their declared static scope. No coverage report was supplied; runtime behavior and comprehensive human review remain outside the assessment.

| Dimension | Score | Scope and evidence |
|---|---:|---|
| Architecture & SOLID | 9.0 | Static coupling/project structure; four coupling hotspots in 87 eligible types |
| Complexity & Decomposition | 6.3 | Method complexity 7.8; Decomposition 4.7 |
| Testing | 10 | Static test signals; no coverage file |
| Security | 10 | Static patterns and vulnerability observations; no findings |
| Error Handling | 10 | Static exception patterns; six informational findings, no errors/warnings |
| Documentation | 8 | Presence/content signals; one stale marker |
| Dependency Management | 6 | Six included outdated occurrences; one incompatible candidate excluded; no unknown compatibility results |
| Performance & Async | 10 | Static async/blocking patterns; no findings |
| Maintainability | 6.8 | Own MI for 1,128 distinct executable functions |

Maintainability's unrounded aggregate moves from **6.7579 to 6.7590**, with 1,128 functions rather than 1,124. `SolutionCompilationLoader.LoadAsync` moves from MI 42.24 to 53.18. Project-selection creation measures 64.17, classification 54.43, semantic test reclassification 68.42 and diagnostic collection 56.07. Extracted functions remain measured. The primary benefit is clearer responsibility boundaries and direct behavioral coverage, not a meaningful aggregate score increase. No threshold, policy or calibration baseline changed.

Live dependency results retain six scored outdated candidates and one incompatible exclusion, with no reported vulnerabilities, deprecations, unsupported frameworks or version drift. These remain upgrade-review observations; this loader refactor does not establish safe upgrades or change package policy.

## Provenance

- Tool: local unpublished 2.3.0, schema v3, ruleset `dotnet-2026-09-12-function-maintainability`, calibration `baseline`, Release.
- Entry point: `analyzers/dotnet/CodeMetrics.AI.slnx`.
- Package SHA-256: `6a3a214f16d8a5aad8b4cb6c9f6c6a978fec23588988b1972cf9a4910c9c5d09`.
- Audit/run: `798757d2-9ab3-4e42-8d1d-37c738a93b9c` / `bbb80324-154b-48fa-ba9a-ea9ad2976e25`.
- Configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- [Fresh evidence](<E:/repos/CodeMetrics.AI/.scorecard/dotnet/runs/bbb80324-154b-48fa-ba9a-ea9ad2976e25/evidence.json>).
- [Validation and score comparison](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-11/verification.json>).
- [Behavior comparison identities](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-11/contract-verification.json>).

Packages, caches and artifacts remain on E:. No package was published.
