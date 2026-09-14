# Self-refactor pass 5: architecture probe responsibilities

This pass follows commit `a975cfd` on `codex/public-corpus-fidelity`. It separates architecture observation collection, scoring and evidence construction without changing thresholds, policies, ruleset or output contracts.

## Changes

`ArchitectureProbe.Analyze` now coordinates three stages. `ArchitectureObservationCollector` gathers project cycles, semantic layering observations, type classifications and controller-action details. `ArchitectureScoring` evaluates the complete metric population and graph/layering cap. `ArchitectureEvidence` builds the basis, provenance and display samples. A shared `ArchitectureTypeScope` owns eligibility rules used by scoring, hotspot detection and exclusion reporting.

The public entry point remains unchanged. Collection order, finding order, all scoring decision fields, serialized evidence names and the ten-hotspot display limit are preserved. Display samples and controller-action summaries cannot feed back into the score. This creates clearer places to change inspection, scoring or presentation independently.

## Verification

- All 733 .NET tests passed. A new regression case combines a project cycle with 36 metric hotspots, checking the zero graph cap, complete findings census, stable ten-item sample and recorded component score.
- Six pinned .NET calibration fixtures passed against the existing baseline without recording changes.
- Full .NET formatting and Git whitespace checks passed.
- The previous packaged analyzer and current analyzer analyzed identical current production source: 118 types and 673 raw members. Complete evidence matches except generated timestamp and fresh run/audit IDs; raw CSV is byte-identical. This includes scores, finding fingerprints, ordering, decision traces and coupling provenance for that fixed source.
- A separate fresh helper run is complete, usable and catalog-validated, with matching invocation/evidence IDs and no analysis diagnostics. One production project was analyzed and the test project was skipped from production metrics. Dependency checks were intentionally skipped and no coverage file was supplied.

The full public corpus remains deferred to the release checkpoint unless measurement behavior changes. These checks establish compatibility on the tested source and fixtures, not exhaustive equivalence for every possible input.

## Self-scorecard

All scores below are deterministic within partial static scope; runtime behavior and comprehensive human review are excluded. Overall **8.7, partial assessment, 8/9 dimensions scored**; Dependency Management is omitted.

| Dimension | Previous | Current | Scope and evidence |
|---|---:|---:|---|
| Architecture & SOLID | 8.5 | 8.6 | Static coupling/structure; 4/75 coupling hotspots, size hotspots 2 → 1 |
| Complexity & Decomposition | 5.9 | 5.9 | Method complexity 7.8; Decomposition 4 |
| Testing | 10 | 10 | Test signals: 551 methods, 1,090 assertion sites; no coverage file |
| Security | 10 | 10 | Static patterns; vulnerability feeds skipped |
| Error Handling | 10 | 10 | Static exception patterns; zero error/warning findings |
| Documentation | 8 | 8 | Presence/content signals |
| Dependency Management | N/A | N/A | Intentionally skipped |
| Performance & Async | 10 | 10 | Static async/blocking patterns |
| Maintainability | 6.8 | 6.7 | Own MI for 1,091 distinct executable functions |

Method complexity continues to score distinct functions under `source-function-own-cc-v1`; Decomposition uses eligible type populations under `executable-function-ownership-v1`. Neither component changed its rounded score.

The Architecture increase is small: the same four coupling hotspots are now among 75 eligible types instead of 71, moving the limiting component from 8.524 to 8.560. The number of size hotspots also falls from two to one, but size is not the limiting component. The higher rounded score does not mean a coupling hotspot was eliminated.

Maintainability's unrounded score moves from **6.7576 to 6.7366**, crossing the one-decimal rounding boundary. The distinct function population grows from 1,081 to 1,091; its weakest fifth grows from 217 to 219. The weakest-group mean changes from 2.7862 to 2.7749 and the remaining mean from 9.4052 to 9.3778. Extracted responsibilities remain measured: for example, collection has own MI 44.94 and evidence construction 49.31. The former monolithic entry point's own MI rises from 24.94 to 73.69, but that alone is not a whole-codebase improvement claim.

The refactor is justified by clearer responsibility boundaries and preserved behavior. No policy or threshold was adjusted to obtain a preferred score, and no further extraction was made solely to raise it.

## Provenance

- Tool: local unpublished 2.3.0, schema v3, ruleset `dotnet-2026-09-12-function-maintainability`, calibration `baseline`, Release.
- Package SHA-256: `87ef5a180a4d397ff63d4dfb271d66851b80edb39e5d0efad070b4b2bfdfb972`.
- Audit/run: `db35dfbd-21be-454c-a3ca-e1345f830e34` / `2453e792-7567-44a8-9271-58c003c19621`.
- Configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- [Fresh evidence](<E:/repos/CodeMetrics.AI/.scorecard/dotnet/runs/2453e792-7567-44a8-9271-58c003c19621/evidence.json>).
- [Verification and comparison identities](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-5/verification.json>).

All packages, caches and artifacts remain on E:. No package was published.
