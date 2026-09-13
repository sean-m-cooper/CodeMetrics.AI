# Performance & Async: distinct source findings

Implemented 2026-09-13 in the local unpublished CodeMetrics.AI 2.3.0 candidate following the [public-corpus regression run](dotnet-public-corpus-refactor-15.md). Compiling the same authored operation for additional frameworks no longer multiplies its Performance & Async score input.

## Counting contract

- Identity is normalized physical file path, exact syntax span start/length and rule category. Windows paths compare without case sensitivity. Linked references to the same path merge; different files do not.
- One site uses its highest observed severity across variants. The original project, severity and message for every observation remain under `projectFrameworkObservations`, with `affectedProjects` and `observationCount`.
- Distinct operations on one line, distinct conditional-compilation spans and different rules at the same operation remain separate. Missing physical file/span identity is conservatively left unmerged.
- All eight Performance & Async finding producers record syntax identity, including ConcurrentFanOutProbe. Recognition, annotations and score-ladder thresholds are unchanged.
- Policy `dotnet/performanceAsync/source-findings-v1` and ruleset `dotnet-2026-09-13-async-source-findings` identify the correction. Old framework-row runs are incompatible baseline gates; this comparison isolates a deliberate counting correction on fixed source.
- The README, architecture guide, calibration policy and packaged CMAI8001-CMAI8008 JSON/Markdown catalog document the behavior.

## Polly replay

| Measurement | Before | After |
|---|---:|---:|
| Findings used by the async ladder | 100 | 24 |
| Retained project/framework observations | 100 | 100 |
| Performance & Async score | 0 | 0 |

| Category | Before | After |
|---|---:|---:|
| missingCancellationToken | 46 | 11 |
| syncOverAsync | 53 | 12 |
| threadSleep | 1 | 1 |

After aggregation, severity counts are {"error": 11, "warning": 12, "info": 1}. The unchanged score reflects the existing ladder, not a failure of source counting. Findings remain static review observations, not confirmed runtime defects.

Expanding the grouped framework observations reproduces every original category/file/line/project/severity/message tuple, with multiplicity. All eight other dimension objects, source populations, filters and raw CSV are unchanged. This is a counting correction, not an improvement to Polly source.

## Validation

- 837 analyzer tests pass, including 21 new source-counting regression cases.
- One warning across 1/5/10 frameworks remains score 8; one error remains score 2. Five separate errors on one line remain score 0.
- Tests also cover all eight producers, same-span different rules, conditional branches, findings in only one variant, different severity at a shared site, reversed project ordering, linked paths, filesystem casing and absent identity.
- Six pinned calibration fixtures pass with scores, findings, labels, distributions and source hashes unchanged. The only baseline diff is ruleset metadata; no automatic baseline recording was used.
- Formatting verification and whitespace checks pass. Generated Markdown matches the packaged catalog.
- Fresh helper/validator exits are zero, inspection is usable, run/audit IDs match the invocation, and the packaged catalog is available.

## Provenance and scope

- Polly commit: `1a80392b1f093f40e59c515f4aeb989bea5db857`; tracked source remains unchanged.
- Before audit/run: `f3a5d714-4f08-4c82-95b3-b9540c6d31b7` / `182f3ba6-37e1-4c9b-8e34-9f38693eb5c1`.
- After audit/run: `c7c823e2-e0cc-4263-9358-6cc6dca5e6be` / `6063b10d-eac7-458a-86fb-4302adb94e9c`.
- Package SHA-256: `97488adaed562f4bbb5f4dfb8420b43d01d576c1ddb6277df302b2a8013f0263`.
- Entry point: `E:\repos\Polly\Polly.slnx`; Release; schema v3; calibration `baseline`.
- Configuration fingerprint: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- Analyzed units: 21 of 53; population: {"types": 1858, "members": 10988}; workspace diagnostics: [].
- Dependency probes were deliberately skipped to match the baseline configuration. No coverage was supplied and Polly tests were not executed. This is a partial static assessment; no new full overall score is claimed.
- SDKs, packages, caches, temporary files and artifacts remain on E:. The package is local and unpublished.
- [Fresh evidence](<E:/repos/Polly/.scorecard/dotnet/runs/6063b10d-eac7-458a-86fb-4302adb94e9c/evidence.json>), [inspection](<E:/repos/Polly/.scorecard/dotnet/runs/6063b10d-eac7-458a-86fb-4302adb94e9c/inspection.json>), [machine-readable comparison](<E:/repos/CodeMetrics.AI/TestResults/async-source-findings/comparison.json>).
