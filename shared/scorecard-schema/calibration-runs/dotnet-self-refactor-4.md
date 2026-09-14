# Self-refactor pass 4: coupling traversal

This pass follows commit `50dbe5d` on `codex/public-corpus-fidelity`. It changes implementation structure while preserving coupling definitions, scoring policies, ruleset and output contracts.

## Changes

- Raw collection separates per-node semantic binding from explicit base-type and attribute collection. The main raw traversal resolves SymbolInfo and TypeInfo once per visited, eligible syntax node. Action-level raw collection also reuses TypeInfo for its type and converted type. Base/attribute compatibility passes remain explicit.
- Raw and structural recursion share array normalization and primitive, self and directly nested type exclusions. Structural generic expansion retains its passive-carrier, compiler-generated and framework-presentation boundaries; raw recursion retains full argument provenance.
- An existing generic definition in the output set does not stop recursion through another constructed instance's arguments. This avoids dropping distinct dependencies behind repeated generic definitions.

The benefit is less repeated semantic work and clearer responsibilities. No wall-clock speedup is claimed without a controlled benchmark. The refactor does not broaden FromServices handling or introduce new exclusions.

## Verification

- 732 .NET tests passed. Three focused cases cover arrays and repeated constructed generics, neutral primitive/self/type-parameter/dynamic references, passive generic-carrier boundaries, and base/attribute/invoked-method signature types.
- Six pinned .NET calibration fixtures passed against the existing baseline without recording changes.
- Full .NET formatting and Git whitespace checks passed.
- The old packaged analyzer and current analyzer analyzed identical current production source: 111 types and 658 raw members. Complete evidence matches except generated timestamp and fresh IDs; raw CSV is byte-identical. Scores, function contributions, coupling provenance and findings are preserved for that fixed source.
- A separate fresh helper run is complete, usable and catalog-validated. Dependency checks were skipped and no coverage file was supplied; this remains a partial static assessment.

The first test attempt identified a missing DynamicAttribute framework reference in the new in-memory test fixture. Adding that reference corrected the fixture; the final full suite passed. No analyzer behavior was changed to accommodate the fixture.

## Self-scorecard

On the changed source under unchanged scoring policy, Maintainability improves from **6.7 to 6.8**. Method Complexity remains **7.8**, Decomposition **4**, combined C&D **5.9**, and Architecture **8.5**. Other scores are unchanged.

The narrower methods have higher own MI: raw collection entry 43.02 → 58.14; structural recursion 48.00 → 62.81; raw recursion 50.89 → 66.24. Extracted helpers remain in the scored population. These measurements describe the resulting source; the code review rationale is removal of duplication and clearer traversal boundaries.

A full public-corpus rerun remains deferred to the release checkpoint unless measurement behavior changes. This pass uses targeted regression cases, unchanged calibration fixtures, an old/new full-evidence comparison and a fresh self-scorecard.

## Provenance

- Tool: local unpublished 2.3.0, schema v3, ruleset `dotnet-2026-09-12-function-maintainability`, Release.
- Package SHA-256: `08df1401186e296964436c3477bce612f85a8a1edff4020048294a0335c4c4f3`.
- Audit/run: `50fbfef1-9e58-4b97-9985-975135d8bb92` / `54adb3d7-5ff0-433c-ae59-1bc0f1201510`.
- Configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- [Fresh evidence](<E:/repos/CodeMetrics.AI/.scorecard/dotnet/runs/54adb3d7-5ff0-433c-ae59-1bc0f1201510/evidence.json>).
- [Verification and comparison identities](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-4/verification.json>).

All packages, caches and artifacts remain on E:. No package was published.
