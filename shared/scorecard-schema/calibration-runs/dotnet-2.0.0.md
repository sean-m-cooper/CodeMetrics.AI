# dotnet 2.0.0 regression baseline

Recorded from the pinned local fixture corpus on 2026-09-05. This is a regression reference, not public-repository cross-ecosystem calibration.

Corpus SHA-256: `2e439e89ff5833b0567c2f74faabe6b4cafb5bcaf3482778ae004fa34197d1b0`.
Ruleset: `dotnet-2026-09-05`.

| Dimension | Scored cases | Median | p25 | p10 |
|---|---:|---:|---:|---:|
| codeQuality | 6 | 10.0 | 10.0 | 9.3 |
| maintainability | 6 | 10.0 | 10.0 | 10.0 |
| errorHandling | 6 | 10.0 | 5.5 | 3.0 |
| performanceAsync | 6 | 10.0 | 10.0 | 6.0 |
| dependencyManagement | 0 | unavailable | unavailable | unavailable |
| security | 6 | 10.0 | 10.0 | 10.0 |
| testing | 6 | 0.0 | 0.0 | 0.0 |
| documentation | 6 | 0.0 | 0.0 | 0.0 |
| architecture | 6 | 10.0 | 10.0 | 10.0 |

All labeled positive and negative expectations passed. Only the rules listed in the manifest are labeled; other findings are tracked for regression without claiming measured precision.

See `../../calibration/README.md` for replay and review instructions, and `../../calibration/baselines/dotnet.json` for case-level evidence.
