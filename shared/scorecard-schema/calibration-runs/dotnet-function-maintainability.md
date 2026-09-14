# Function-based maintainability: implementation verification

Local unpublished CodeMetrics.AI 2.3.0, schema v3, Release, ruleset `dotnet-2026-09-12-function-maintainability`. The implemented [policy](../maintainability-policy.md) measures owned executable-function MI and combines 40% of the weakest fifth mean with 60% of the remaining mean. Each source function belongs to exactly one group. Low-MI distribution statistics add no deductions or caps.

## Fixed-source public corpus

All four pinned checkouts remain unchanged. Fresh runs are complete and usable, with matching run/audit IDs and the packaged rule catalog. Raw CSV is byte-identical to the prior method-population runs; source populations, filters, configuration fingerprints, findings, C&D metrics and all non-Maintainability scores are unchanged. These are incompatible-policy comparisons on identical source, not evidence of code improvement or a ranking of project quality.

| Repository | Former type MI score | New function MI score | Distinct functions / observations | Weakest count | Weakest mean | Remaining mean |
|---|---:|---:|---:|---:|---:|---:|
| Polly | 7.3 | 8.2 | 2218 / 9651 | 444 | 5.68 | 9.90 |
| Newtonsoft.Json | 8.7 | 6.9 | 3448 / 21125 | 690 | 3.01 | 9.52 |
| SimplCommerce | 6 | 7.3 | 2890 / 2890 | 578 | 3.62 | 9.68 |
| OrchardCore | 6.7 | 7.3 | 20079 / 20081 | 4016 | 3.76 | 9.70 |

## Self-scorecard

The current working codebase scores Maintainability **6.7**, with 1072 distinct functions: 215 in the weakest group (mean 2.7169) and 857 in the remaining group (mean 9.4147). Its unrounded weighted result is 6.735620344544. The earlier self-run scored 4.7 under the former policy, but both the implementation source and scoring policy have changed, so this is not an isolated policy comparison.

| Dimension | Score |
|---|---:|
| codeQuality | 5.9 |
| maintainability | 6.7 |
| errorHandling | 10 |
| performanceAsync | 10 |
| dependencyManagement | unmeasured |
| security | 10 |
| testing | 10 |
| documentation | 8 |
| architecture | 8.5 |

## Measurement review and limits

Individual own MI anchors 40/52/58/65/70/75 map linearly to scores 0/2/4/6/8/10. These initial anchors retain former reference points; no thresholds were adjusted to reach desired self or public scores. They are product choices pending broader labeled calibration, not an empirical model of defect probability, readability or change cost.

The fixed examples cover enum and storage neutrality, exclusive nested-body ownership including multiline changes, partial-class preservation, moves between classes, runtime initializer ownership, constructor initializer ownership, no entry-point bonus, missing measurements and identities, repeated/changed framework variants, group boundaries, rational weights, half-up ties and fixed-population monotonicity. An assignment constructor is explicit executable code; pure state declarations do not qualify. An explicitly implemented empty body is measured, while an absent body is not fabricated as MI 100.

Review samples illustrate the remaining interpretation boundary. Polly retry orchestration and telemetry, Newtonsoft.Json primitive-value dispatch and decimal parsing, and our architecture/evidence construction methods can have low own MI for different reasons. Some have substantial branching; others have low CC and high token/line volume. The measurements identify review leads and do not decide whether domain or compatibility constraints justify the code. Ordinary body formatting and changes to the authored function population can affect MI. Weakest-fifth weighting can dilute a fixed weak group as the population grows; it is not the separate method-complexity worst-function ceiling.

Dependency probes were skipped, no coverage file was supplied, and public target test suites were not executed. Nonfatal workspace diagnostics remain visible in evidence. These four development repositories are not a held-out or cross-ecosystem calibration corpus.

## Verification

- 728 .NET tests passed; full formatting verification and whitespace checks passed.
- Six pinned .NET calibration fixtures passed after previewing, reviewing, recording and independently rerunning the baseline. Only the declaration-only payload changes from Maintainability 10 to null/unmeasured; other scores, populations, findings, labels and fixture hashes are unchanged. The ruleset and corresponding distribution metadata change intentionally.
- An independent old/new package comparison on a fixed source/coverage fixture preserves all non-Maintainability dimension objects, nine findings and byte-identical raw CSV.
- Every emitted function contribution in all five fresh scorecards was checked for unique identity and disjoint groups; summing its rational weighted contribution reproduces the unrounded score and final half-up result.
- The CMAI9001 package catalog and generated Markdown match. The working `ai_tools-scorecard-integration` scorecard skill passed structural validation and explains the active policy, diagnostic-only statistics, legacy boundaries and unsupported comment exemption.

## Provenance

Tested NuGet SHA-256: `be0e7ee145f020c5038423ed35c019396c7a3fd71afce8a46add6d090265093c`. The candidate is local and unpublished; packages, caches, temporary files and run artifacts are on E:.

### CodeMetrics.AI

- Source: `working tree on codex/public-corpus-fidelity`.
- Audit/run: `60eaee69-3039-45dc-afb1-5dd1bb917ff7` / `fb2dfb6a-dbbd-421e-86e1-f4a10810a88b`.
- Configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- Evidence: [CodeMetrics.AI](<E:/repos/CodeMetrics.AI/.scorecard/dotnet/runs/fb2dfb6a-dbbd-421e-86e1-f4a10810a88b/evidence.json>).

### Polly

- Source: `1a80392b1f093f40e59c515f4aeb989bea5db857`.
- Audit/run: `4d878766-1c2b-4c05-b2e1-ccda846cdea1` / `254e7c25-9db6-4a1e-b802-f77e2ae59734`.
- Configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- Evidence: [Polly](<E:/repos/Polly/.scorecard/dotnet/runs/254e7c25-9db6-4a1e-b802-f77e2ae59734/evidence.json>).

### Newtonsoft.Json

- Source: `09bb545d72969ad7fb4ea07db0d5c34f4fc07877`.
- Audit/run: `c8a79a0d-5d90-4f17-b9f1-f8e6bc702da2` / `bd1dea25-ebc8-4204-a180-44e2fde5d887`.
- Configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- Evidence: [Newtonsoft.Json](<E:/repos/Newtonsoft.Json/.scorecard/dotnet/runs/bd1dea25-ebc8-4204-a180-44e2fde5d887/evidence.json>).

### SimplCommerce

- Source: `3472ba02a6f2d9b6bdca7f7fb84957176aa799dc`.
- Audit/run: `fb2e820e-6e72-4176-b606-35002b8e18ab` / `7a3878ed-b381-475f-85cf-da8f2a490728`.
- Configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- Evidence: [SimplCommerce](<E:/repos/SimplCommerce/.scorecard/dotnet/runs/7a3878ed-b381-475f-85cf-da8f2a490728/evidence.json>).

### OrchardCore

- Source: `4c101f5c6a6e6aca073a800797a223a958b28c0b`.
- Audit/run: `19f58e59-09ec-4d10-a067-e84d52c63ddb` / `9f985397-25cc-4352-aecb-85ea76e4cfb1`.
- Configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- Evidence: [OrchardCore](<E:/repos/OrchardCore/.scorecard/dotnet/runs/9f985397-25cc-4352-aecb-85ea76e4cfb1/evidence.json>).

Machine-readable checks, prior/fresh identities and function samples: [comparison.json](<E:/repos/CodeMetrics.AI/TestResults/function-maintainability/comparison.json>). Test, calibration, package and contract logs are in the same local directory.
