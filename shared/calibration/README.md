# Accuracy and score regression corpus

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
