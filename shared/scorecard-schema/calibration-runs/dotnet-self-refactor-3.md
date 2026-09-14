# Self-refactor pass 3: shared executable-function collection

The prior scoring and refactoring work is checkpointed in CodeMetrics.AI commit `4a7e092`; the corresponding skill guidance is in `ai_tools-scorecard-integration` commit `34e9042`, both on `codex/public-corpus-fidelity`.

## Change and rationale

ExecutableFunctionCollector previously traversed each included declaration twice to serve C&D and Maintainability, repeating classification, own-CC measurement and source identity/symbol work. It now discovers and measures functions in one pass, then routes observations into the two existing populations. Discovery, measurement and eligibility have explicit boundaries. The public raw metrics, scoring policies, ruleset and schema remain unchanged. This removes duplicate work; no wall-clock speedup is claimed without a controlled benchmark.

The populations intentionally differ. Branch-free runtime initializers qualify for Maintainability, while constant initializers do not. A branching constant can remain a C&D observation. Lambda-only initializer wrappers supply no MI credit. C&D evidence omits the optional MI payload. A new regression case verifies these boundaries and shared identity fields.

## Verification

- 729 .NET tests passed, including the new population-boundary regression.
- Six .NET calibration fixtures passed against the existing baseline without recording changes.
- Full .NET formatting and Git whitespace checks passed. The checkpoint's JS/TS refactor also passed all 40 tests before commit.
- The previous packaged analyzer and refactored analyzer both analyzed the same current production source (111 types / 653 raw members). Complete evidence is identical except generated timestamp and fresh run/audit IDs; raw CSV is byte-identical. This checks source anchors, ordering, function contributions, findings, scores and provenance across the refactor.
- The separate fresh helper run is complete, usable and catalog-validated with matching IDs. Dependencies were skipped and no coverage file was supplied, so it remains a partial assessment.

## Self-scorecard

The former collector entry method had own CC 15. The responsibilities are now separated, and the worst remaining own CC in the codebase is 14. Method Complexity improves from 7.7 to 7.8; Decomposition remains 4. Combined C&D remains 5.9 after rounding and Maintainability remains 6.7. These results compare changed source under the same scoring policies.

The new Maintainability population contains 1074 distinct functions. Other dimension scores remain unchanged. The next substantial control-flow review candidates are ClassCouplingCalculator's raw-symbol collection and structural-type traversal; compact syntax dispatch tables should not be rewritten solely to reduce a metric.

## Provenance

- Tool: local unpublished 2.3.0, schema v3, ruleset `dotnet-2026-09-12-function-maintainability`, Release.
- Package SHA-256: `960704ca2bd75951fe4f0d7b8899db7fb78a648e7c12c60f54c8082f5faec70c`.
- Audit/run: `a6312d06-186c-4cde-96c3-dd185560aa35` / `3c705f83-0f4f-47bc-b666-3fb0b01a6bde`.
- Configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- Previous self audit/run: `60eaee69-3039-45dc-afb1-5dd1bb917ff7` / `fb2dfb6a-dbbd-421e-86e1-f4a10810a88b`.
- [Fresh evidence](<E:/repos/CodeMetrics.AI/.scorecard/dotnet/runs/3c705f83-0f4f-47bc-b666-3fb0b01a6bde/evidence.json>).
- [Verification and comparison identities](<E:/repos/CodeMetrics.AI/TestResults/self-refactor-3/verification.json>).

All artifacts, caches and packages remain on E:. No package was published.
