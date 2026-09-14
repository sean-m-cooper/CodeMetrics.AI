# Executable-function scoring: public-corpus verification

Fresh runs of four pinned repositories with CodeMetrics.AI development package 2.3.0, schema v3, Release, ruleset `dotnet-2026-09-11-executable-functions` and `executable-function-ownership-v1`. This is a measurement-consistency check, not a calibrated quality ranking. Threshold bands and component weights are unchanged.

Every run completed and passed evidence validation with matching invocation/evidence IDs. All four repositories remain at the recorded commits with no tracked source changes. Every raw CSV cell, production population and filter entry matches its earlier logical-partial-type baseline. Dependencies were deliberately skipped and no coverage report was supplied; no target test suites were run. Only C&D components are compared here, not whole-scorecard totals or baseline gates across incompatible rulesets.

| Repository | Method complexity before / after | Decomposition before / after | C&D before / after |
|---|---:|---:|---:|
| Polly | 9.3 / 9.3 | 10 / 8 | 9.6 / 8.6 |
| Newtonsoft.Json | 0 / 0 | 5.3 / 4 | 2.6 / 2 |
| SimplCommerce | 8.7 / 8.7 | 9.3 / 7.3 | 9 / 8 |
| OrchardCore | 6.7 / 6.7 | 6.7 / 4.7 | 6.7 / 5.7 |

All scores are partial static assessments. Lower decomposition scores after removing storage members reflect the corrected denominator, not source regressions. Nested functions retain their decisions as separate observations. Branch-free callbacks remain visible without adding decomposition units. Newtonsoft.Json still falls into the lowest method-complexity bands; the correction does not remove its measured branching concentration.

## CodeMetrics.AI self-check

The current analyzer implementation scores **3.6** for C&D: Method complexity **3.3**, Decomposition **4**. This is a partial static assessment. The analyzer's own source changed during implementation, so its earlier 2.6 score is not a same-source policy comparison like the four public repositories above.

- Audit/run: `21bded18-f33f-47f8-9903-d15c51e69276` / `c38b8118-a58f-4719-886c-9d1563dd14c5`.
- Measurement inputs: `{"roundingDecimals": 1, "roundingMode": "ToEven", "unroundedScore": 3.65, "eligibleTypes": 60, "measurementPolicy": "executable-function-ownership-v1", "legacyInputTypes": 0, "executableFunctions": 904, "decompositionFunctions": 504, "decompositionEligibleTypes": 48}`.
- [Self-check evidence](<E:/repos/CodeMetrics.AI/.scorecard/dotnet/runs/c38b8118-a58f-4719-886c-9d1563dd14c5/evidence.json>).

## Threshold decision

Retain the existing thresholds for this change. The corrected measurements address demonstrated inconsistencies, while these four repositories have no independently established target C&D scores. Raising bands to make a familiar repository score better would be unsupported calibration. The current equal weighting of correlated prevalence, percentile and extreme-prevalence signals remains a calibration limitation. A future threshold change should compare candidate policies against labeled ordinary, deliberate and excessive-complexity examples, including libraries and orchestration code, before setting new bands.

## Validation and compatibility

- 624 full-suite analyzer tests passed; the final focused ownership suite passed all eight tests, covering 625 distinct tests in total after adding the complex-callback retention case.
- Local versus private helpers, fields/automatic properties, nested lambdas/local functions, branch-free callbacks, accessor/initializer decisions and partial/nested types have regression coverage. Existing raw metrics remain unchanged.
- All 12 pinned calibration fixtures preserve scores, populations, findings and accuracy labels. The reviewed .NET baseline diff changes only the ruleset identifier; the JavaScript baseline is unchanged.
- CMAI2001/CMAI2002 catalog descriptions and the code-scorecard skill distinguish scoring function counts from raw member counts. No annotation capabilities changed. The skill passed structural validation.

## Provenance

### Polly

- Commit: `1a80392b1f093f40e59c515f4aeb989bea5db857`.
- Current audit/run: `c8f2768f-addc-4c8b-8a56-a15d8fec46f9` / `f756af9f-1171-41e9-bc96-239c11ec018c`.
- Baseline audit/run: `8cc05791-3662-4153-9503-c00d74c59ae9` / `ed8a28ed-e7dd-4b0b-bcd5-c6b41c2fd267`.
- Configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- Population: `{"types": 1858, "members": 10988}`; analyzed units 21/53.
- Nonblocking analysis diagnostics: 0; see current evidence for messages.
- Current evidence: [Polly evidence](<E:/repos/Polly/.scorecard/dotnet/runs/f756af9f-1171-41e9-bc96-239c11ec018c/evidence.json>).

### Newtonsoft.Json

- Commit: `09bb545d72969ad7fb4ea07db0d5c34f4fc07877`.
- Current audit/run: `aed84326-87fb-4482-99cc-7f2773d019ae` / `ae88fabf-05ae-4ef4-b421-3f8d8cab6f71`.
- Baseline audit/run: `1176e4d4-c957-4d52-9583-3ea5b4268862` / `402c8b82-1f1f-40a3-ba2e-6639e082b255`.
- Configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- Population: `{"types": 1707, "members": 25381}`; analyzed units 9/16.
- Nonblocking analysis diagnostics: 1; see current evidence for messages.
- Current evidence: [Newtonsoft.Json evidence](<E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/ae88fabf-05ae-4ef4-b421-3f8d8cab6f71/evidence.json>).

### SimplCommerce

- Commit: `3472ba02a6f2d9b6bdca7f7fb84957176aa799dc`.
- Current audit/run: `cbd6a62a-dc4a-466c-af6d-be35dd4fab42` / `7e683382-c607-4b4d-afe7-555452bf6a97`.
- Baseline audit/run: `b012ccfe-5092-4bd8-a808-9fc9a69b7886` / `117c4170-8cf2-4fac-9e3c-012faf802d2a`.
- Configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- Population: `{"types": 715, "members": 3823}`; analyzed units 42/49.
- Nonblocking analysis diagnostics: 0; see current evidence for messages.
- Current evidence: [SimplCommerce evidence](<E:/repos/SimplCommerce/.scorecard/dotnet/runs/7e683382-c607-4b4d-afe7-555452bf6a97/evidence.json>).

### OrchardCore

- Commit: `4c101f5c6a6e6aca073a800797a223a958b28c0b`.
- Current audit/run: `6ebbbebe-d657-4f09-8fc5-d840b28d7cc6` / `b2302e87-aebc-40d8-9b84-172b7bd7f17e`.
- Baseline audit/run: `b8642cdb-508f-4a8b-8b71-355d021fd0ce` / `9b559530-3aec-4a3c-8269-7d10d0c76af9`.
- Configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- Population: `{"types": 5568, "members": 24989}`; analyzed units 213/239.
- Nonblocking analysis diagnostics: 3; see current evidence for messages.
- Current evidence: [OrchardCore evidence](<E:/repos/OrchardCore/.scorecard/dotnet/runs/b2302e87-aebc-40d8-9b84-172b7bd7f17e/evidence.json>).

Package SHA-256: `80cae1ca56b2684e9141c129d6420c7bb90875567d03aa5d47aec10e087d7f07`. The shared validator uses the local codemetrics-ai 0.3.0 package. All clones, SDK/cache work and generated artifacts stayed on E:.

Full machine-readable comparison: [comparison.json](<E:/repos/CodeMetrics.AI/TestResults/executable-functions/comparison.json>). Run results and logs are under `TestResults/executable-functions/`. These are local artifacts; the report retains IDs and package identity for reproducibility.
