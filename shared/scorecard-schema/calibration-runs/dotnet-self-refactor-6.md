# Self-refactor pass 6: exception-handling recognition

This pass follows commit `674208a` on `codex/public-corpus-fidelity`. It separates catch classification, exception-propagation recognition and finding construction, preserving existing handling patterns, score policies and evidence contracts.

## Changes

- `CatchObservation` lazily collects active and unrestricted descendants, resolves the caught symbol and caches handling recognition within one tree analysis. Catch classification and missing-logger checks reuse those observations. No cache survives the analysis of that tree.
- `CatchClassifier` produces rule kinds and source nodes. `ErrorHandlingProbe` constructs messages and findings and retains source deduplication and scoring.
- `CatchHandlingRecognition` combines the existing logging, rethrow, cancellation, deferred-reporting and propagation checks.
- `ExceptionPropagationAnalysis` separates direct exception-bearing returns, stored outcomes and invoked callbacks. `ReturnedExceptionLocalAnalysis` collects candidate returns and symbol-resolved write references once when stored-outcome recognition is needed, instead of rescanning the containing scope for each candidate return/local.

The traversal boundaries are deliberate. Handling recognition skips deferred lambdas and local functions. The legacy throw-ex/default-return checks retain their unrestricted traversal, and the conservative intervening-write check still includes writes inside deferred bodies. Symbol identity, catch-variable reassignment checks, same-outcome-type returns and lexical write ranges are unchanged. No broader control-flow or interprocedural analysis is claimed.

The practical benefit is independently understandable recognition paths and less repeated inspection. No wall-clock performance improvement is claimed without a controlled benchmark.

## Verification

- All **739 .NET tests** passed, with no skips or failures. Six added cases cover independently written result locals, a recognized return before later reassignment, replacement of every candidate outcome, out writes, deferred writes and unrelated-local writes.
- All **144 error-handling test cases**, including the six new cases, also passed against the previous packaged analyzer DLL in an isolated copy of the test output. This verifies that the added expectations describe existing behavior.
- Six pinned .NET calibration fixtures passed without recording baseline changes.
- Full .NET formatting and Git whitespace checks passed.
- The previous packaged analyzer and current analyzer analyzed identical current production source: **124 types and 707 raw members**. Complete evidence matches except generated timestamp and fresh run/audit IDs; raw CSV is byte-identical. This includes findings, fingerprints, ordering, scores and decision traces for that fixed source.
- A separate fresh helper run is complete, usable and catalog-validated, with matching invocation/evidence IDs and no analysis diagnostics. One production project was analyzed and one test project was skipped from production metrics.

The full public-corpus rerun remains deferred to the release checkpoint unless measurement behavior changes. The checks establish compatibility for the tested source and fixtures, not exhaustive equivalence for every possible input.

## Self-scorecard

All scores are deterministic within partial static scope. Dependency checks were skipped and no coverage file was supplied; runtime behavior and comprehensive human review remain outside the assessment. Overall **8.7, partial assessment, 8/9 dimensions scored**.

| Dimension | Previous | Current | Scope and evidence |
|---|---:|---:|---|
| Architecture & SOLID | 8.6 | 8.6 | Static coupling/structure; 4/80 coupling hotspots |
| Complexity & Decomposition | 5.9 | 5.9 | Method complexity 7.8; Decomposition 4 |
| Testing | 10 | 10 | Test signals; 551 methods, 1,090 assertion sites; no coverage file |
| Security | 10 | 10 | Static patterns; vulnerability feeds skipped |
| Error Handling | 10 | 10 | Static exception patterns |
| Documentation | 8 | 8 | Presence/content signals |
| Dependency Management | N/A | N/A | Intentionally skipped |
| Performance & Async | 10 | 10 | Static async/blocking patterns |
| Maintainability | 6.7 | 6.8 | Own MI for 1,114 distinct executable functions |

Maintainability's unrounded score changes from **6.7366 to 6.7706**, with the distinct function population growing from 1,091 to 1,114. This is a small change under the same policy. The extracted classification method has own MI 51.74; propagation recognition has own MI 60.85; stored-outcome recognition has own MI 62.10. Extracted helpers remain in the population, and individual-method MI values are not whole-codebase improvement claims.

Method complexity still uses `source-function-own-cc-v1`; Decomposition retains `executable-function-ownership-v1`. Their rounded scores are unchanged. No thresholds, ruleset, catalog definitions or comment-exclusion behavior changed to obtain a preferred score.

## Provenance

- Tool: local unpublished 2.3.0, schema v3, ruleset `dotnet-2026-09-12-function-maintainability`, calibration `baseline`, Release.
- Package SHA-256: `a5c59df514208502564ca59232675d252e697bee3f8c11249ffeac449d565842`.
- Audit/run: `df32ebd2-d276-424f-a81b-822dba5a1f6e` / `ff68a3a5-21f5-417b-99a7-713e8ca7e708`.
- Configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- [Fresh evidence](<E:/repos/CodeMetrics.AI/.scorecard/dotnet/runs/ff68a3a5-21f5-417b-99a7-713e8ca7e708/evidence.json>).
- [Verification and comparison identities](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-6/verification.json>).

Packages, caches and artifacts remain on E:. No package was published.
