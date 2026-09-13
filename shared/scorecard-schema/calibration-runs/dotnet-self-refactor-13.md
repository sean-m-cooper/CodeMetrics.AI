# Self-refactor pass 13: architecture layering analysis

This pass follows commit `d0b4f41` on `codex/public-corpus-fidelity`. It separates architecture layering rules and avoids resolving constructor parameters for types to which neither rule applies. Scoring policies, thresholds and rule identities are unchanged.

## Changes

ArchitectureLayeringAnalysis owns controller data-dependency and service concrete-infrastructure checks, with separate controller and service methods. Controller classification still takes precedence over the Service suffix convention. ConstructorDependencyCollector supplies the same authored parameter observations to layering analysis and controller-action evidence. ArchitectureObservationCollector continues coordinating the source observations.

Constructor ordering remains regular constructors before primary constructors, even when primary parameters occur earlier in the source. Class, record and record-struct behavior, declaration-local partial inspection, parameter locations, syntax names, semantic types and finding order are preserved. The existing controller cross-cutting prefix and service infrastructure keyword checks still use authored type syntax; this refactor does not normalize aliases or qualified names. Semantic interface, abstract-class and framework exclusions remain unchanged.

Unrelated types no longer have constructor parameters bound for layering checks. Controller-action evidence retains its separate observation pass. Throughput was not benchmarked, so the performance claim is limited to avoiding that unnecessary semantic work.

## Verification

- **779 .NET tests passed**, with no failures or skips; the build has no warnings or errors.
- Four new cases cover mixed controller/service types, inherited controller recognition, controller precedence, NonController service fallback, partial declarations, aliases, qualified names, semantic exclusions, messages, source locations and constructor ordering for classes, records and record structs.
- **50 architecture tests passed against the previous packaged analyzer DLL**, including all four new cases.
- Six pinned .NET calibration fixtures passed without recording baseline changes. Formatting and Git whitespace checks passed.
- Old and new analyzers evaluated identical current source: 138 production types and 734 raw members. Complete evidence matches except fresh IDs and generated timestamp; raw CSV is byte-identical. This comparison skips live dependency queries.
- A separate fresh helper audit includes dependency commands. It is complete, usable and catalog-validated, with matching invocation/evidence IDs and no analysis diagnostics. One production project was analyzed out of two loaded units; the test project was excluded from production metrics.

The full public corpus remains deferred to the release checkpoint unless measurement behavior changes. Compatibility evidence is limited to these tests, fixtures and comparisons.

## Self-scorecard

Overall **8.4, partial assessment, 9/9 dimensions scored**, unchanged. Scores are deterministic within their declared static scope. No coverage report was supplied; runtime behavior and comprehensive human review remain outside the assessment.

| Dimension | Score | Scope and evidence |
|---|---:|---|
| Architecture & SOLID | 9.2 | Static coupling/project structure; three coupling hotspots in 90 eligible types, one size hotspot, no cycles |
| Complexity & Decomposition | 5.9 | Method complexity 7.8; Decomposition 4.0 |
| Testing | 10 | Static test signals; 571 authored test methods and 1,201 assertions; no coverage file |
| Security | 10 | Static patterns and vulnerability observations; no findings |
| Error Handling | 10 | Static exception patterns; six informational findings, no errors/warnings |
| Documentation | 8 | Presence/content signals; one stale marker |
| Dependency Management | 6 | Six included outdated occurrences; one incompatible candidate excluded; no unknown compatibility results |
| Performance & Async | 10 | Static async/blocking patterns; no findings |
| Maintainability | 6.8 | Own MI for 1,131 distinct executable functions |

The former AnalyzeLayeringViolations method had CC 13 and own MI 41.11. Its responsibilities now reside in Analyze (CC 4, own MI 65.82), AnalyzeController (CC 4, own MI 54.45), AnalyzeService (CC 3, own MI 55.42) and IsConcreteInfrastructure (CC 6, own MI 63.20). Constructor collection is shared, with its existing CC 9. The high-complexity function census falls from 12 to 11; the worst remaining own CC is still 14. Method complexity's unrounded aggregate moves from 7.80191 to 7.80228 and still rounds to 7.8.

Decomposition's displayed p90 ratio rises from 5.02 to 5.15 and its population above ratio 4 rises from 22.86% to 25.00%; the component remains 4.0. Maintainability's unrounded aggregate moves slightly down from 6.7612747534 to 6.7609001223 and still rounds to 6.8. Its function count rises from 1,130 to 1,131, and the weakest-fifth group consequently grows from 226 to 227 functions. All extracted functions remain measured. These results establish a local hotspot reduction, not an aggregate maintainability score improvement. No calibration adjustment was made to obtain a preferred result.

## Provenance

- Tool: local unpublished 2.3.0, schema v3, ruleset `dotnet-2026-09-12-function-maintainability`, calibration `baseline`, Release.
- Entry point: `analyzers/dotnet/CodeMetrics.AI.slnx`.
- Package SHA-256: `fa9301496268dbbea172f2175ea2be6eb86a55ba61d6d1644540f73b49355cf0`.
- Audit/run: `f2f48fab-2b17-403a-bb3f-44f490b4b9c2` / `671e3dce-0399-42fc-a8c8-6cb0863712e0`.
- Configuration fingerprint: `55a2905da4b6f5b4abe0c76ea5cf5c652d34a1a1344d243dab163d59e3fa8d0d`.
- [Fresh evidence](<E:/repos/CodeMetrics.AI/.scorecard/dotnet/runs/671e3dce-0399-42fc-a8c8-6cb0863712e0/evidence.json>).
- [Validation and score comparison](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-13/verification.json>).
- [Behavior comparison identities](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-13/contract-verification.json>).

Packages, caches and artifacts remain on E:. No package was published.
