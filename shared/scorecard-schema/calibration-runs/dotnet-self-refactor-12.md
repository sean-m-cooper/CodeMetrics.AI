# Self-refactor pass 12: raw member collection

This pass follows commit `8b4f47c` on `codex/public-corpus-fidelity`. It separates raw member measurement from logical type assembly and reuses member symbols without changing metric definitions or public output.

## Changes

`MemberMetricsCollector` owns raw member naming, body measurement and partial-member reconciliation. The symbol resolved for naming is reused for method/property partial identity instead of resolving the declaration again. `MetricsCollector` retains logical type grouping, type aggregates, coupling and executable-function collection.

Historical raw counting remains unchanged: field and event-field declarations contribute their first variable, nested types remain separate, and authored partial implementations replace definitions at the original list position. Generated bodies are not imported through symbols. Constructor/destructor/operator/indexer names, bodyless defaults, body detection, raw MI and rounding are preserved. Distinct executable-function scoring remains a separate collection path. Runtime throughput was not benchmarked; the performance claim is limited to the removed duplicate symbol binding.

## Verification

- **775 .NET tests passed**, with no failures or skips; build has no warnings or errors.
- Two new combined cases cover declaration-first and implementation-first partial types, overload identity, fields/events, constructors/destructors, indexers/operators/conversions, nested types, bodyless defaults, complete counts and stable serialized metrics under source-tree reversal.
- **13 collector and partial-type tests passed against the previous packaged analyzer DLL**, including both new cases. Existing cases include generated partial bodies and CSV membership.
- Six pinned .NET calibration fixtures passed without recording baseline changes. Full formatting and Git whitespace checks passed.
- Old and new analyzers evaluated identical current production source: 136 types and 733 raw members. Complete evidence matches except fresh IDs and generated timestamp; raw CSV is byte-identical. This comparison skips live dependency queries.
- A separate fresh helper audit includes dependency commands. It is complete, usable and catalog-validated, with matching invocation/evidence IDs and no analysis diagnostics. One production project was analyzed out of two loaded units; the test project was excluded from production metrics.

The full public corpus remains deferred to the release checkpoint unless measurement behavior changes. Compatibility evidence is limited to these tests, fixtures and comparisons.

## Self-scorecard and tradeoff

Overall **8.4, partial assessment, 9/9 dimensions scored**, previously **8.5, partial assessment, 9/9 dimensions scored**. Scores are deterministic within their declared static scope. No coverage report was supplied; runtime behavior and comprehensive human review remain outside the assessment.

| Dimension | Score | Scope and evidence |
|---|---:|---|
| Architecture & SOLID | 9.2 | Static coupling/project structure; three coupling hotspots in 88 eligible types |
| Complexity & Decomposition | 5.9 | Method complexity 7.8; Decomposition 4.0 |
| Testing | 10 | Static test signals; no coverage file |
| Security | 10 | Static patterns and vulnerability observations; no findings |
| Error Handling | 10 | Static exception patterns; six informational findings, no errors/warnings |
| Documentation | 8 | Presence/content signals; one stale marker |
| Dependency Management | 6 | Six included outdated occurrences; one incompatible candidate excluded; no unknown compatibility results |
| Performance & Async | 10 | Static async/blocking patterns; no findings |
| Maintainability | 6.8 | Own MI for 1,130 distinct executable functions |

Architecture increases from **9.0 to 9.2**. MetricsCollector's structural coupling decreases from 10 to 8, below the hotspot threshold; MemberMetricsCollector has structural coupling 4. The coupling hotspot census falls from four to three. The original type now delegates raw member measurement to a cohesive component.

C&D decreases from **6.3 to 5.9**, entirely through Decomposition moving from 4.7 to 4.0. Method complexity stays at 7.8. The recorded p90 decomposition ratio moves from **5.00000 to 5.01667**, crossing the existing `<= 5` boundary: its component score moves from 4 to 2. The fraction of types above ratio 4 decreases from 23.1884% to 22.8571%; its component remains 0. The extreme-ratio component remains 10. Thus the decomposition mean changes from `(0 + 4 + 10) / 3` to `(0 + 2 + 10) / 3`. No score was reconstructed from raw CSV.

Maintainability's unrounded aggregate moves from **6.7590 to 6.7613**, with 1,130 functions rather than 1,128. All extracted functions remain measured. The change is retained for responsibility separation and reduced duplicate semantic work, despite the lower overall score. No threshold, policy or calibration baseline was adjusted to obtain a preferred result.

This is a concrete case for a future review of decomposition band sensitivity across representative codebases. It is not sufficient evidence to change calibration for this repository alone.

## Provenance

- Tool: local unpublished 2.3.0, schema v3, ruleset `dotnet-2026-09-12-function-maintainability`, calibration `baseline`, Release.
- Entry point: `analyzers/dotnet/CodeMetrics.AI.slnx`.
- Package SHA-256: `899671b22feaac5dbe8df824085132599f1ec154c50f0fe3b6c072bb76476129`.
- Audit/run: `b2b35fac-1215-4836-89ac-61397ab78dcc` / `a14e9135-cc0d-4b26-897a-64457b4901e8`.
- Configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- [Fresh evidence](<E:/repos/CodeMetrics.AI/.scorecard/dotnet/runs/a14e9135-cc0d-4b26-897a-64457b4901e8/evidence.json>).
- [Validation and score comparison](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-12/verification.json>).
- [Behavior comparison identities](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-12/contract-verification.json>).

Packages, caches and artifacts remain on E:. No package was published.
