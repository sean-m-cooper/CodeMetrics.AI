# Self-refactor pass 10: dependency command execution

This pass follows commit `c3fab85` on `codex/public-corpus-fidelity`. It separates process execution from package-report validation while preserving the dependency probe's public contract and score policy.

## Changes

`DependencyCommandRunner` owns SDK-isolated process setup, concurrent stdout/stderr capture and timeout/cancellation cleanup. It returns raw command observations. `DependencyProbe` validates zero-exit output as version-1 NuGet JSON and retains the existing exception-to-diagnostic conversion. Nonzero exits retain stdout/stderr without parsing; malformed zero-exit reports remain failures, and caller cancellation propagates. Production keeps the five-minute limit and process-tree termination behavior.

The runner's internal capture boundary accepts a timeout and process configuration so tests can exercise real subprocess behavior quickly without changing global PATH or invoking package feeds. The test assembly has internal visibility; no new public API is introduced. Legacy text replay remains available only through the existing output processor.

## Verification

- **768 .NET tests passed**, with no failures or skips; build has no warnings or errors.
- **13 new command tests** cover command flags/environment isolation, simultaneous capture of 128 KiB on each stream, zero and nonzero exits, actual child termination after timeout/caller cancellation, start failure, malformed/unsupported/error-bearing reports and retention of valid or nonzero-exit observations. Fixture cleanup retries bounded Windows file-release delays after child exit.
- **51 existing dependency probe tests passed against the previous packaged analyzer DLL** in an isolated test-output copy. The new internal-runner tests target the current implementation.
- Six pinned .NET calibration fixtures passed without recording baseline changes. Full formatting and Git whitespace checks passed.
- Old and new analyzers evaluated identical current production source: 133 types and 729 raw members. Complete evidence matches except fresh IDs and generated timestamp; raw CSV is byte-identical. This comparison skips live dependency queries and therefore does not itself establish equivalence of all external command outcomes.
- A separate fresh helper audit includes real dependency commands. It is complete, usable and catalog-validated, with matching invocation/evidence IDs and no analysis diagnostics. One production project was analyzed out of two loaded units; the test project was excluded from production metrics.

The full public corpus remains deferred to the release checkpoint unless measurement behavior changes. Compatibility evidence is limited to the tests, fixtures and comparisons described above.

## Self-scorecard

Overall **8.5, partial assessment, 9/9 dimensions scored**, previously **8.4, partial assessment, 9/9 dimensions scored**. Scores are deterministic within the declared static scope. No coverage report was supplied; runtime behavior and comprehensive human review remain outside this assessment.

| Dimension | Score | Scope and evidence |
|---|---:|---|
| Architecture & SOLID | 9.0 | Static coupling/project structure; four coupling hotspots in 85 eligible types |
| Complexity & Decomposition | 6.3 | Method complexity 7.8; Decomposition 4.7 |
| Testing | 10 | Static test signals; no coverage file |
| Security | 10 | Static patterns and vulnerability observations; no findings |
| Error Handling | 10 | Static exception patterns; six informational findings, no errors/warnings |
| Documentation | 8 | Presence/content signals; one stale marker |
| Dependency Management | 6 | Six included outdated occurrences, one incompatible candidate excluded |
| Performance & Async | 10 | Static async/blocking patterns; no findings |
| Maintainability | 6.8 | Own MI for 1,124 distinct executable functions |

Architecture moves from **8.6 to 9.0**. `DependencyProbe` structural coupling decreases from 14 to 10; the worst remaining coupling is 12. The complete coupling census still contains four hotspots, while eligibility grows from 84 to 85 types. The recorded coupling component increases from 8.6286 to 9.0353. DependencyProbe's size finding remains, at 517 source lines rather than 540. Moving process ownership removed concrete dependencies from the probe; the new runner remains measured.

Maintainability's unrounded aggregate moves from **6.7536 to 6.7579**, with 1,124 functions rather than 1,120. `RunDotnetListAsync` moves from MI 41.08 to 59.86; capture measures 53.67, setup 60.85 and validation 75.78. No threshold, policy or calibration baseline changed.

The live dependency check now resolves framework compatibility for all seven reported candidates. Six distinct packages are included, one occurrence each across two projects: Microsoft.NET.StringTools, Microsoft.Build.Framework and Microsoft.Build.Utilities.Core 18.9.6 → 18.10.1; System.CommandLine 2.0.11 → 2.0.12; xunit.v3 4.0.0 → 4.0.1; Microsoft.NET.Test.Sdk 18.9.0 → 18.10.0. Microsoft.Build 18.9.6 → 18.10.1 is excluded as incompatible with net10.0. These remain upgrade-review candidates; compatible assets do not establish upgrade safety. No vulnerabilities, deprecations, unsupported frameworks or version drift were reported. The changed availability of compatibility observations is not attributed to this process refactor.

## Provenance

- Tool: local unpublished 2.3.0, schema v3, ruleset `dotnet-2026-09-12-function-maintainability`, calibration `baseline`, Release.
- Package SHA-256: `ee4750e15af37525d1ce504e192e18ec0216c7ba9ef30799038bb929f2a14376`.
- Audit/run: `d1f1c1c0-80b1-4461-a9cb-379b474e4c33` / `dd3c4cfa-278d-46ba-98b0-d559e256352c`.
- Configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- [Fresh evidence](<E:/repos/CodeMetrics.AI/.scorecard/dotnet/runs/dd3c4cfa-278d-46ba-98b0-d559e256352c/evidence.json>).
- [Validation and score comparison](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-10/verification.json>).
- [Behavior comparison identities](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-10/contract-verification.json>).

Packages, caches and artifacts remain on E:. No package was published.
