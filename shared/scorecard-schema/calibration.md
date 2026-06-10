# Cross-Ecosystem Calibration

The shared contract promises a stable 0-10 scale per dimension. That promise is only meaningful across ecosystems if each analyzer's thresholds are tuned so that comparable codebases earn comparable scores. This document defines how an ecosystem earns "calibrated" status. The `dotnet` analyzer is the baseline: its thresholds define the reference distribution.

## Prerequisites: metric parity

Before distribution tuning, the new analyzer must demonstrate formula parity with the baseline on the shared raw metrics:

1. **Maintainability index** — identical formula: `MAX(0, (171 - 5.2*ln(HalsteadVolume) - 0.23*CyclomaticComplexity - 16.2*ln(LinesOfCode)) * 100 / 171)`. The language-specific inputs must be documented in the analyzer README, with fixture files whose expected MI values are hand-computed and asserted in tests.
2. **Cyclomatic complexity** — a construct parity table mapping baseline constructs to the ecosystem's equivalents, asserted by fixture tests. The counting policy is: start at 1 per member; +1 per runtime decision point including logical `&&`/`||` and null-coalescing operators; type-only declarations add nothing.
3. **Population definitions** — what counts as a "type" and "member" for `population` and CSV rows must be documented.

## Calibration procedure

1. **Assemble a reference corpus** of 6-10 public repositories for the ecosystem, selected for spread, not fame: at least two widely accepted as high quality, at least two with known quality problems, sizes spanning roughly 10k-500k LOC, and at least one of each dominant framework flavor.
2. **Establish the baseline distribution** once: run the `dotnet` analyzer over its own corpus and record per-dimension score median, p25, and p10.
3. **Run and compare**: run the candidate analyzer over its corpus. For each dimension, compare its score distribution to the baseline distribution.
4. **Tune thresholds** until per-dimension medians are within +/-0.5 and p10 within +/-1.0 of the baseline. Tuning changes thresholds, never the formulas.
5. **Record the run** under `shared/scorecard-schema/calibration-runs/<ecosystem>-<tool-version>.md` listing the corpus, per-dimension distributions, and frozen threshold values. Update the status table in `dimensions.md`.
6. **Recalibrate** whenever a dimension's rules or thresholds change. `tool.version` ties every scorecard to a frozen threshold set.

## Interim rule for uncalibrated ecosystems

Until an ecosystem completes this procedure:

- Its analyzer README must state that scores are uncalibrated across ecosystems.
- Consumers render its scores with an explicit caveat and never average them with other ecosystems' scores.
- The dotnet CSV fallback procedure must not be applied to its CSV output.
