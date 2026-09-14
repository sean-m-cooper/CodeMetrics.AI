# Accuracy and score regression corpus

The [maintainability policy](../scorecard-schema/maintainability-policy.md), implemented in the unpublished .NET 2.3.0 candidate, specifies exclusive executable-function ownership and 40/60 weighting of the weakest fifth and remaining functions. Its initial MI ladder preserves former reference points and requires broader labeled calibration. The [verification record](../scorecard-schema/calibration-runs/dotnet-function-maintainability.md) distinguishes this policy change from code improvement and documents baseline review.

The [executable-function scoring verification](../scorecard-schema/calibration-runs/dotnet-executable-functions.md) records the corrected C&D measurements across the four pinned public repositories, exact raw-CSV preservation and the decision to retain thresholds pending labeled calibration.

The accepted [complexity scoring product decision](../scorecard-schema/calibration.md#complexity-scoring-product-decision) classifies own method CC as 1–5 low, 6–10 moderate, 11–20 high and 21+ severe. Individual-method assessment anchors do not directly cap the aggregate: 10 represents exceptional simplicity, while 8 is a strong target that allows limited moderate complexity. A severe method keeps the reported aggregate below 8; additional hotspots worsen it, and trivial-code padding preserves the severe hotspot's ceiling. The accepted formula is 40% of one worst individual-function score plus 60% of the remaining mean, with a single-function exception; the decision describes the product's expectations rather than asserting that high CC proves defects.

The [method-population verification](../scorecard-schema/calibration-runs/dotnet-method-population.md) records fresh validated runs of the four pinned repositories, the 40/60 policy results, exact raw-CSV and decomposition preservation, regression checks and package provenance. Scores reflect an explicit policy change on unchanged source; this corpus does not establish a held-out or cross-ecosystem calibration result.

`corpus.json` pins 12 local fixtures by SHA-256 (LF-normalized): six .NET and six JavaScript/TypeScript/React cases. The cases exercise clean code, decisions, async misuse, nested functions, passive payloads, and intentional exceptions. Each entry labels positive counts and negative cases for specific rules. Unlabeled rules are recorded but excluded from precision/recall accounting.

Build both analyzers, then run:

```sh
dotnet build analyzers/dotnet/CodeMetrics.AI.slnx -c Release
npm ci --prefix analyzers/javascript-typescript
npm run build --prefix analyzers/javascript-typescript
python shared/calibration/run.py --output TestResults/calibration/report.json
```

The runner verifies fixture hashes, copies each fixture into its own temporary directory, invokes the real CLI, validates emitted evidence, and compares results with `baselines/`. No source fixture is restored or built in place. Every external process has a timeout. The report includes per-rule true positives, false positives, false negatives, negative cases, per-case scores, and per-dimension median/p25/p10.

CI fails on a hash mismatch, analyzer failure, invalid evidence, changed scores/findings, or a labeled false positive/negative. To accept a deliberate rule or threshold change, inspect the behavior, update labels or source pins when appropriate, and run `python shared/calibration/run.py --record` on the review branch. Review the resulting baseline diff alongside the rule change. Never record baselines automatically in CI.

These are small, labeled regression fixtures. They establish a reproducible .NET reference distribution for these cases and exercise JS/TS against the same release mechanism. They do **not** satisfy the 6–10 public-repository calibration procedure in `../scorecard-schema/calibration.md`. Cross-ecosystem comparability remains uncalibrated until that separate evidence exists. Distribution matching alone is insufficient: add labeled defect and intentional-pattern cases whenever a corpus run exposes a false positive or missed finding.

The [2.3.0 public-corpus verification](../scorecard-schema/calibration-runs/dotnet-2.3.0-public-corpus.md) records pinned Polly, Newtonsoft.Json, SimplCommerce and OrchardCore snapshots, corrected scope/classification behavior, run identities and remaining limits. These four repositories extend fidelity testing; they do not establish a general quality ranking. Their confirmed failures are covered by the .NET probe/CLI regressions and the skill runner's integration tests.

The [factory coupling source review](../scorecard-schema/calibration-runs/dotnet-factory-coupling-review.md) records method-level Polly evidence and four labeled .NET test cases contrasting independent construction, orchestration, mixed factory/decision logic, and local-helper extraction. These protect interpretation boundaries without changing scoring or the pinned baseline fixtures.
