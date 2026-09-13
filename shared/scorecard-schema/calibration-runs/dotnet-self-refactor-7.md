# Self-refactor pass 7: remaining high-CC review

This pass follows commit `5dfef1d` on `codex/public-corpus-fidelity`. It reviews the five sampled highest-CC functions and refactors three with distinct decision responsibilities. Scoring policies, thresholds, ruleset and output contracts are unchanged.

## Changes and retained code

- `CompletedTaskAccess.IsKnownCompleted` separates enclosing-guard recognition from backwards statement history. Guard checks retain their deferred-body and receiver-write boundaries. History checks retain the priority of an awaited WhenAny assignment, followed by the write barrier, followed by an awaited WhenAll proof.
- `DependencyProbe.AddOutdatedFindings` separates assessment from presentation. Counts and findings share `AssessOutdatedPackage`, which preserves Aspire precedence and distinguishes absent assessment, missing compatibility entry, compatible and incompatible states. Absent assessment and a missing entry both permit scoring, but only the latter enters the unknown-upgrade diagnostic list.
- `SecurityProbe.AnalyzeHardcodedSecrets` uses a shared literal/placeholder predicate and finding constructor for declarations and assignments. Existing name extraction, length threshold, messages and declaration-before-assignment ordering remain unchanged.
- `DataCarrierClassifier.IsPassiveValue` and `FunctionMaintainabilityCalculator.Bodies` remain unchanged. Their compact switches enumerate syntax forms directly. Splitting these dispatch tables solely to reduce measured CC would introduce indirection without an identified clarity benefit. This decision does not exempt them from scoring.

## Verification

- **750 .NET tests passed**, with no failures or skips. Eleven new cases cover assessment/count/finding agreement, Aspire precedence over an incompatible asset result, secret-length/placeholder boundaries and finding order, and receiver-value history after task reassignment.
- **200 subsystem tests** also passed against the previous packaged analyzer DLL in an isolated test-output copy, including the new cases and public-corpus regression tests. Dependency tests use supplied reports and compatibility observations rather than live feeds.
- Six pinned .NET calibration fixtures passed without recording baseline changes.
- Full .NET formatting and Git whitespace checks passed.
- The previous packaged analyzer and current analyzer analyzed identical current production source: **124 types and 716 raw members**. Complete evidence matches except fresh IDs and generated timestamp; raw CSV is byte-identical. Findings, fingerprints, ordering and scoring decisions are preserved for that tested source.
- A separate fresh helper run is complete, usable and catalog-validated, with matching invocation/evidence IDs and no analysis diagnostics. One production project was analyzed; the test project was excluded from production metrics.

The full public-corpus rerun remains deferred to the release checkpoint unless measurement behavior changes. These checks establish compatibility on tested inputs, not exhaustive equivalence.

## Self-scorecard

All scores are deterministic within partial static scope. Dependency checks were skipped and no coverage file was supplied; runtime behavior and comprehensive human review are excluded. Overall **8.7, partial assessment, 8/9 dimensions scored**.

| Dimension | Previous | Current | Scope and evidence |
|---|---:|---:|---|
| Architecture & SOLID | 8.6 | 8.6 | Static coupling/project structure |
| Complexity & Decomposition | 5.9 | 6.3 | Method complexity 7.8; Decomposition 4 → 4.7 |
| Testing | 10 | 10 | Static test signals; no coverage file |
| Security | 10 | 10 | Static patterns; vulnerability feeds skipped |
| Error Handling | 10 | 10 | Static exception patterns |
| Documentation | 8 | 8 | Presence/content signals |
| Dependency Management | N/A | N/A | Intentionally skipped |
| Performance & Async | 10 | 10 | Static async/blocking patterns |
| Maintainability | 6.8 | 6.8 | Own executable-function MI |

Distinct high-CC functions (11–20) fall from **15 to 12**, with zero severe functions. The method-complexity population grows from 1,053 to 1,062 distinct functions; its worst own CC remains 14 and rounded score remains 7.8. The retained syntax-dispatch functions remain among those tied at 14.

Decomposition's tail measure reaches **5**, selecting a tail component score of 4. Its population and extreme components are 0 and 10, producing Decomposition 4.7 and combined C&D 6.3. This is a threshold crossing under the existing policy, not a policy adjustment. Maintainability's unrounded result is 6.7909. Extracted helpers remain scored; reduced duplication and clearer decision ownership are the reasons for the changes.

## Provenance

- Tool: local unpublished 2.3.0, schema v3, ruleset `dotnet-2026-09-12-function-maintainability`, calibration `baseline`, Release.
- Package SHA-256: `4afb8b3c589cab3b05bf4e28ffe5714f994f566294bf9d7288b6fc43c30388ea`.
- Audit/run: `e16ad533-9482-4039-968e-fbfd59efd66f` / `b2aaabde-4e2b-4b6a-be8a-783aa91658cc`.
- Configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- [Fresh evidence](<E:/repos/CodeMetrics.AI/.scorecard/dotnet/runs/b2aaabde-4e2b-4b6a-be8a-783aa91658cc/evidence.json>).
- [Verification and comparison identities](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-7/verification.json>).

Packages, caches and artifacts remain on E:. No package was published.
