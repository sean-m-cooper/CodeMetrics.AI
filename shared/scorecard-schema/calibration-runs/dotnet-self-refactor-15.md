# Self-refactor pass 15: C&D reaches the sevens

This pass follows commit `b36494e` on `codex/public-corpus-fidelity`. The user requested continued meaningful refactoring until C&D reached the sevens or worthwhile candidates were exhausted. The final validated score is **7.3**; implementation stops at that checkpoint. No scoring thresholds, population eligibility policies, suppressions or calibration baselines were changed.

## Changes and behavior boundaries

- MemberMetricAccumulator owns the ordered member list and symbol-to-position map for one logical type. MemberMetricsCollector measures included declarations and delegates partial identity/replacement. Signature/implementation ordering, overloaded symbols, partial properties and authored-only bodies retain their prior behavior.
- TargetFrameworkCompatibility separates three-valued asset evaluation, pairwise compatibility, supported version limits and platform checks. Existing .NET Standard support bands are descending data with shared Version instances. All names are still parsed before evaluation; unknown/mixed results, same-family precedence and platform restrictions are unchanged.
- ControllerAuthorizationAnalysis scans each hierarchy and action once for either authorization attribute, retaining the project gate, recognition names and empty-action behavior.
- EvidenceEnricher separates suppression-comment parsing from source traversal and computes repository root once. Legacy markers, source ordering, categories, reasons and declared status remain unchanged.
- ControllerActionCollector owns architecture's controller discovery, authored action eligibility and measurement. The observer still uses the existing constructor dependency collector; partial declaration order, generic dependency normalization, NonAction and FromServices handling remain unchanged. Security's different action heuristic remains separate.
- WeightedScore shares decimal penalty arithmetic between complexity and maintainability. Each dimension retains its own populations, individual score mapping, weights and evidence. Subtraction order, missing remaining-group handling, ceilings and final rounding are unchanged.
- ConcurrentFanOutProbe caches resolved calls, positional argument mappings and interface-method enumeration for the analysis. The fixed-point loop retains call-before-interface propagation, source/call order and mutation-set insertion order. Direct assignments still precede mutating calls. Alias recognition remains deliberately bounded; this is not broader flow analysis. Repeated call binding and argument-root inspection are removed from propagation. Runtime throughput was not benchmarked.

## Verification

- **816 .NET tests passed**, with no failures or skips. Build has no warnings or errors.
- Twenty-six new cases cover framework support boundaries, platform versions, three-valued asset sets, and fan-out propagation through interfaces and aliases in different declaration orders. The fan-out cases verify the first reported mutated argument across multiple propagation passes.
- **324 compatibility tests passed against the previous packaged analyzer DLL**, including all new cases and existing raw-member/partial, scoring, security, architecture, performance and evidence tests.
- Six pinned .NET calibration fixtures passed without recording baseline changes. Formatting and Git whitespace checks passed.
- Old and new analyzers evaluated identical final source: 142 production types and 764 raw members. Complete evidence matches except fresh IDs and generated timestamp; raw CSV is byte-identical. This comparison skips live dependency queries.
- A separate fresh helper audit includes dependency commands. It is complete, usable and catalog-validated, with matching invocation/evidence IDs and no analysis diagnostics. One production project was analyzed out of two loaded units; the test project is excluded from production metrics.

Compatibility claims are limited to these tests, fixtures and comparisons. The broader public corpus remains deferred to the release checkpoint because measurement policy is unchanged.

## Full scorecard

Overall **8.8, partial assessment, 9/9 dimensions scored**, previously **8.7, partial assessment, 9/9 dimensions scored**. Scores are deterministic within their declared static scopes. No measured test coverage was supplied; runtime behavior and comprehensive human review remain outside the assessment.

| Dimension | Before | After | Scope and evidence |
|---|---:|---:|---|
| Architecture & SOLID | 9.2 | 9.2 | Static coupling/project structure; three coupling hotspots among 93 eligible types, one size hotspot, no cycles |
| Complexity & Decomposition | 6.3 | 7.3 | Method complexity 7.8; decomposition 4.7 → 6.7 |
| Testing | 10 | 10 | Static signals; 576 authored test methods, 1,213 assertions; no coverage file |
| Security | 10 | 10 | Static patterns and vulnerability observations; no findings |
| Error Handling | 10 | 10 | Static exception patterns; no errors/warnings |
| Documentation | 8 | 8 | Presence/content signals; one stale marker |
| Dependency Management | 8 | 8 | Three included outdated MSBuild companion occurrences; one incompatible upgrade excluded; retained .NET 10 support rationale from pass 14 |
| Performance & Async | 10 | 10 | Static async/blocking patterns; no findings |
| Maintainability | 6.8 | 6.9 | Own MI for 1,172 distinct executable functions |

High-decomposition types decrease from **11/72 (15.28%) to 4/73 (5.48%)**. The displayed p90 ratio decreases from **4.48 to 4.00**. The population component improves from 0 to 6, the tail component remains 4 and the extreme component remains 10. Decomposition becomes `(6 + 4 + 10) / 3`, rounded to **6.7**. C&D averages 6.7 and 7.8 to 7.25, with the existing half-up rule producing **7.3**.

The remaining four high-decomposition types are FunctionMaintainabilityCalculator, ExecutableFunctionCollector, ArchitectureLayeringAnalysis and PerformanceAsyncProbe. Compact syntax dispatch remains intact. Several improved types land exactly at ratio 4, which the unchanged policy does not classify as above 4; this is a measured boundary, not proof that those types are ideal.

High-complexity functions decrease from **8 to 6**. Worst own CC remains 14. Method complexity's unrounded aggregate improves from 7.8190633609 to 7.8281735986, still rounding to 7.8. Maintainability improves from 6.8279506432 to 6.8745907852, rounding to 6.9.

All extracted behavior remains in complexity and maintainability populations. The new accumulator's Add method has CC 9; like every type with only one qualifying function, that type does not enter decomposition's two-function population. The shared arithmetic helper also has one qualifying function. ControllerActionCollector enters the decomposition population. No special exclusions were introduced. Responsibility grouping affects this proxy, so the score gain is reported alongside the concrete implementation and compatibility evidence rather than treated as independent proof of design quality.

## Provenance

- Tool: local unpublished 2.3.0, schema v3, Release; entry point `analyzers/dotnet/CodeMetrics.AI.slnx`.
- Ruleset: `dotnet-2026-09-12-function-maintainability`; calibration: `baseline`.
- Configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- Audit ID: `c263af8f-55b0-4da0-b78b-7b146164c4ea`.
- Run ID: `55e726a8-28ae-43d6-97eb-52241d3f69af`.
- Package SHA-256: `673bc2d55c0161acf1ee08b97a5176851bf75456c813f025c3aea3f451e779b6`.
- [Validated run evidence](<E:/repos/CodeMetrics.AI/.scorecard/dotnet/runs/55e726a8-28ae-43d6-97eb-52241d3f69af/evidence.json>).
- [Validation and score comparison](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-15/verification.json>).
- [Behavior comparison identities](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-15/contract-verification.json>).

Packages, caches and artifacts remain on E:. No packages were published.
