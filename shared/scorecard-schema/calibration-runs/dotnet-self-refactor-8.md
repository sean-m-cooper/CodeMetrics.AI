# Self-refactor pass 8: method-complexity responsibilities

This pass follows commit `6b2fdf2` on `codex/public-corpus-fidelity`. It separates population preparation, arithmetic and evidence construction in `MethodComplexityProbe`, preserving score policy and output behavior.

## Changes

`MethodComplexityPopulation` owns physical-source identity, variant grouping, representative selection and deterministic ordering. `MethodComplexityScoring` owns the individual CC ladder, the worst/remaining 40/60 calculation, single-function behavior and decimal rounding. `MethodComplexityEvidence` creates the existing decision trace, metrics and five-function sample from the complete assessment. The original probe remains a small coordinator with compatible constants and result type.

The formula can now be inspected without reading dictionaries and presentation metadata. The sample never limits the scoring population. Missing identities, worst-variant aggregation, tied-worst handling, field names and metadata values are unchanged. This introduces no new scoring exclusions or thresholds.

## Verification

- **751 .NET tests passed**, with no failures or skips. The new combined case checks seven distinct functions across fourteen observations, one variant difference, tied worst functions, six remaining contributions, a deterministic five-item display sample and stable output under reversed project order.
- **57 complexity tests** also passed against the previous packaged analyzer DLL in an isolated test-output copy, including the new case.
- Six pinned .NET calibration fixtures passed without recording baseline changes.
- Full .NET formatting and Git whitespace checks passed.
- The previous packaged analyzer and current analyzer analyzed identical current production source: **128 types and 721 raw members**. Complete evidence matches except fresh IDs and generated timestamp; raw CSV is byte-identical. This comparison skips live dependency probes to keep external feed responses outside the equivalence check.
- A separate fresh helper run includes live dependency checks. It is complete, usable and catalog-validated, with matching invocation/evidence IDs and no analysis diagnostics. One production project was analyzed; the test project was excluded from production metrics.

The full public corpus remains deferred to the release checkpoint unless measurement behavior changes. Compatibility is verified on the tested source and fixtures, not exhaustively for every possible input.

## Self-scorecard

All dimension scores are unchanged from the previous full audit. Overall **8.4, partial assessment, 9/9 dimensions scored**. Scores are deterministic within partial static scope; no coverage report was supplied, and runtime behavior and comprehensive human review remain outside the assessment.

| Dimension | Score | Scope and evidence |
|---|---:|---|
| Architecture & SOLID | 8.6 | Static coupling/project structure |
| Complexity & Decomposition | 6.3 | Method complexity 7.8; Decomposition 4.7 |
| Testing | 10 | Static test signals; no coverage file |
| Security | 10 | Static patterns and vulnerability observations |
| Error Handling | 10 | Static exception patterns |
| Documentation | 8 | Presence/content signals |
| Dependency Management | 6 | Six scored outdated occurrences; one incompatible candidate excluded; no reported vulnerabilities or deprecations |
| Performance & Async | 10 | Static async/blocking patterns |
| Maintainability | 6.8 | Own MI for 1,125 distinct executable functions |

Maintainability's unrounded score moves slightly from **6.7909 to 6.7710**. The original coordinator's own MI moves from 33.03 to 74.52; extracted population collection measures 59.09, arithmetic 53.72 and decision construction 40.89. All helpers remain in the measured population. The coordinator's higher MI is not a claim of an aggregate score improvement; the benefit is clearer responsibility boundaries and an independently readable scoring calculation.

Method complexity retains `source-function-own-cc-v1`; Decomposition retains `executable-function-ownership-v1`. No policy or calibration baseline was adjusted to obtain a preferred score. Legacy maintainability behavior and package versions were left unchanged.

## Provenance

- Tool: local unpublished 2.3.0, schema v3, ruleset `dotnet-2026-09-12-function-maintainability`, calibration `baseline`, Release.
- Package SHA-256: `bc5706949f514c923399bf27d6d0f46f6d48783d5a0c5a3c627536175afbb249`.
- Audit/run: `0d11ad2d-09e0-4671-9bc2-e6d8e0fb333a` / `662391b9-0c32-40c5-a1e3-763f07ada0e2`.
- Configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- [Fresh evidence](<E:/repos/CodeMetrics.AI/.scorecard/dotnet/runs/662391b9-0c32-40c5-a1e3-763f07ada0e2/evidence.json>).
- [Verification and comparison identities](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-8/verification.json>).

Packages, caches and artifacts remain on E:. No package was published.
