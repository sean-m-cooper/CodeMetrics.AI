# Method-complexity population policy: pinned corpus verification

Fresh local CodeMetrics.AI development 2.3.0 package, schema v3, Release, ruleset `dotnet-2026-09-12-method-population`. Method complexity uses `source-functions-40-60-v1`; decomposition retains its previous executable-function/type-instance policy. These are partial static assessments and a same-source policy comparison, not source improvements or a general ranking of repository quality.

All four runs are complete, fresh and validated with matching run/audit IDs and the shipped rule catalog. Pinned commits and tracked source are unchanged. Raw CSV cells, source populations, filters, configuration fingerprints, findings, decomposition metrics and all non-C&D scores match the earlier executable-function runs. The earlier runs predate the half-up rounding correction as well as this method-complexity policy; combined C&D differences include that final rounding change. Incompatible rulesets are compared as a policy experiment, not accepted as compatible baseline gates.

| Repository | Method complexity before → after | Decomposition | Combined C&D before → after | Distinct functions / observations | High / severe functions | Worst own CC |
|---|---:|---:|---:|---:|---:|---:|
| Polly | 9.3 → 7.9 | 8 | 8.6 → 8 | 2079 / 8964 | 3 / 0 | 15 |
| Newtonsoft.Json | 0 → 5.6 | 4 | 2 → 4.8 | 3356 / 20529 | 108 / 64 | 69 |
| SimplCommerce | 8.7 → 7.2 | 7.3 | 8 → 7.3 | 2758 / 2758 | 3 / 2 | 24 |
| OrchardCore | 6.7 → 5.8 | 4.7 | 5.7 → 5.3 | 18764 / 18766 | 233 / 56 | 52 |

## Interpretation

The aggregate combines 40% of one worst individual-function assessment with 60% of the remaining mean. Individual own CC anchors 3/5/10/20/40 correspond to 10/8/6/4/0, interpolated linearly and clamped; single-function scopes use their individual assessment. Exactly one worst function is removed, including ties. Calculations use decimal arithmetic and final half-up rounding. High means CC 11–20; severe means 21+. Repeated source/function-start/kind observations count once at maximum variant CC.

A higher score under this policy does not remove branching or establish correctness. A severe hotspot keeps the method-complexity aggregate below 8, while additional hotspots lower its remaining-population contribution. Raw methods, classes and decomposition retain their separate definitions. The code-scorecard skill presents the new populations and the weighted arithmetic without substituting type maxima or applying these weights to historical runs.

## Validation

- 674 .NET tests passed, including accepted populations, individual anchors, half-up rounding, ties, severity/prevalence monotonicity, padding ceilings, class regrouping, repeated/changed variants, missing source identity, same-line callbacks and conditional declarations and conditional attributes/return types.
- Full .NET formatting verification and whitespace checks passed. The shipped CMAI2002 description matches generated Markdown; the updated skill passed structural validation.
- Six .NET calibration fixtures passed after reviewing and then independently verifying the baseline update. Only the branching fixture changes C&D (8.7 → 7.7); fixture hashes, populations, findings, accuracy labels and non-C&D scores are unchanged.
- Dependency probes were skipped and no coverage file was supplied. Target repository test suites were not run. These checks validate the accepted policy on four development repositories, not a held-out or cross-ecosystem calibration claim.

## Provenance

NuGet package SHA-256: `319f27c2c35aa918c2df06b18ce1af834b52ab686a5e703a5215bec86cc4e986`. This package remains local and unpublished. All checkouts, packages, caches and run artifacts are on E:.

### Polly

- Source commit: `1a80392b1f093f40e59c515f4aeb989bea5db857`.
- Current audit/run: `bff46587-6fb2-4e68-8385-82e0907836d8` / `d57df3d8-76b2-4829-af90-c8c6c97b76dd`.
- Prior audit/run: `c8f2768f-addc-4c8b-8a56-a15d8fec46f9` / `f756af9f-1171-41e9-bc96-239c11ec018c`.
- Configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- Evidence: [Polly](<E:/repos/Polly/.scorecard/dotnet/runs/d57df3d8-76b2-4829-af90-c8c6c97b76dd/evidence.json>).

### Newtonsoft.Json

- Source commit: `09bb545d72969ad7fb4ea07db0d5c34f4fc07877`.
- Current audit/run: `7a6b667a-93ce-41a5-82e3-86c4a86636e6` / `95a5081c-2044-413c-9b62-8aaebd340286`.
- Prior audit/run: `aed84326-87fb-4482-99cc-7f2773d019ae` / `ae88fabf-05ae-4ef4-b421-3f8d8cab6f71`.
- Configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- Evidence: [Newtonsoft.Json](<E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/95a5081c-2044-413c-9b62-8aaebd340286/evidence.json>).

### SimplCommerce

- Source commit: `3472ba02a6f2d9b6bdca7f7fb84957176aa799dc`.
- Current audit/run: `b6dcff4b-ad60-48a9-bb60-825e9ad7b20d` / `4a041946-3ce6-4c81-93d2-b522394a665f`.
- Prior audit/run: `cbd6a62a-dc4a-466c-af6d-be35dd4fab42` / `7e683382-c607-4b4d-afe7-555452bf6a97`.
- Configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- Evidence: [SimplCommerce](<E:/repos/SimplCommerce/.scorecard/dotnet/runs/4a041946-3ce6-4c81-93d2-b522394a665f/evidence.json>).

### OrchardCore

- Source commit: `4c101f5c6a6e6aca073a800797a223a958b28c0b`.
- Current audit/run: `d3f380e5-e5ee-43a4-bbca-d743c3f17d80` / `f317966a-481a-4484-a1e8-9f179861f87d`.
- Prior audit/run: `6ebbbebe-d657-4f09-8fc5-d840b28d7cc6` / `b2302e87-aebc-40d8-9b84-172b7bd7f17e`.
- Configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- Evidence: [OrchardCore](<E:/repos/OrchardCore/.scorecard/dotnet/runs/f317966a-481a-4484-a1e8-9f179861f87d/evidence.json>).

The README-refreshed local release candidate has SHA-256 `0aaec208e1092c62a5b51a8c796734384d24f46346aaada23e150886994980a8`. All 128 tool runtime/catalog files are byte-identical to the package used for the runs; only packaging/documentation was refreshed. See [package verification](<E:/repos/CodeMetrics.AI/TestResults/method-population/package-verification.json>).

Local machine-readable details: [comparison.json](<E:/repos/CodeMetrics.AI/TestResults/method-population/comparison.json>). Logs and the comparison script are in the same directory.
