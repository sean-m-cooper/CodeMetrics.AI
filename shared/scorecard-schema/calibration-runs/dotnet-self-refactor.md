# .NET self-refactor verification

The September 12, 2026 self-assessment identified concentrated branching in completion proofs, evidence enrichment and coverage parsing. This refactor separates those responsibilities without changing scoring thresholds, policy IDs or rule declarations.

## Measured result

Both assessments use Release, schema 3, CodeMetrics.AI 2.3.0 and ruleset `dotnet-2026-09-12-method-population`. These are different local package builds of the same development version; package hashes are recorded below. Both are complete, usable runs with matching invocation/evidence IDs and no analysis diagnostics. The entry point is `analyzers/dotnet/CodeMetrics.AI.slnx`.

| Dimension | Before | After | Source and scope |
|---|---:|---:|---|
| Architecture & SOLID | 8.5 | 8.5 | Deterministic static coupling/project structure |
| Complexity & Decomposition | 5.1 | **6.3** | Deterministic own-function CC and type decomposition |
| Testing | 10 | 10 | Deterministic test signals; no coverage report supplied |
| Security | 10 | 10 | Deterministic static patterns; dependency vulnerability checks skipped |
| Error Handling | 10 | 10 | Deterministic static exception patterns |
| Documentation | 8 | 8 | Deterministic presence/content signals |
| Dependency Management | N/A | N/A | Feed checks deliberately skipped in both runs |
| Performance & Async | 10 | 10 | Deterministic static async/blocking patterns |
| Maintainability | 4.7 | **5.3** | Deterministic production-type MI |
| **Overall — partial assessment, 8/9 scored** | **8.3** | **8.5** | Unweighted mean, decimal half-up; dependencies excluded |

Every scored dimension has partial scope. No runtime performance, comprehensive human review or absence of security vulnerabilities is established. The .NET solution is the assessment scope; JavaScript and the skill were not rescored here.

| C&D measurement | Before | After |
|---|---:|---:|
| Method complexity (CMAI2002) | 6.1 | **7.8** |
| Decomposition (CMAI2001) | 4.0 | **4.7** |
| Maximum own function CC | 35 | **14** |
| Severe functions, CC 21+ | 2 | **0** |
| High functions, CC 11–20 | 20 | 20 |
| Moderate functions, CC 6–10 | 80 | 82 |
| Low functions, CC 1–5 | 838 | 858 |
| Distinct functions | 940 | 960 |
| Decomposition-eligible types | 50 | 52 |
| Types above decomposition ratio 4 | 30% | 28.85% |
| p90 decomposition ratio | 6.25 | 4.95 |
| Types above decomposition ratio 15 | 0% | 0% |

The method population has no unidentified or repeated observations in either run. The new calculation is `0.4 × 5.2 + 0.6 × 9.492179353493222 = 7.775307612095934 → 7.8`. Its recorded ceiling is 8.08. Decomposition component scores are 0, 4 and 10, averaging 4.7 after rounding. The combined dimension rounds `(7.8 + 4.7) / 2 = 6.25` to 6.3.

CMAI2002, Method complexity distribution, measures the worst individual function and the mean of the remaining functions. CMAI2001, Decomposition distribution, measures executable complexity per qualifying function within each type. Both are aggregate metric components, not per-site deductions. Neither supports comment exclusion in this package. Better component scores alone do not establish better cohesion, correctness or readability; the source changes and contract checks below support this interpretation.

## Changes and preserved behavior

- `CompletedTaskAccess` keeps Boolean proof composition explicit and separates Task properties, helper call validation, single-return extraction and helper-body verification. The asymmetric AND/OR proof rules, receiver-symbol checks, write invalidation and restrictions on helper calls remain intact.
- `EvidenceEnricher` coordinates catalog metadata, normalized package paths, source resolution, confidence, identity and scoring attachment. `FindingSourceResolver` owns source/member lookup and relative paths. Fingerprint identity fields, source anchors, occurrence ordering, final sorting and scoring attachment are preserved.
- `CoverageReport` owns I/O, hashing, guarded parsing and invalid-report handling. `CoverageSourceAccumulator` resolves production files and merges repeated line observations. XML protections, invalid-line filtering, hit union and unknown branch coverage for partially matched reports are preserved.

The production population changed from 101 types / 599 raw members to 103 / 625. Two focused internal collaborators and meaningful operation boundaries account for the structural changes. Raw metrics on the modified analyzer naturally change; raw output on unchanged contract-fixture source is byte-identical.

## Verification

- Full .NET suite: 686 tests passed, including 12 added cases covering Boolean guards, block-bodied helpers, helper calls/side effects, duplicate coverage lines, invalid observations and unsafe XML.
- Full solution formatting verification and `git diff --check` passed.
- All six pinned .NET calibration fixtures passed without recording or changing baselines.
- Old and refactored analyzers ran on identical contract-fixture source containing repeated empty catches, proven/unproven task access and duplicated/partially matched coverage lines. Their complete evidence documents are equal after removing only generation time and fresh run/audit IDs. All six findings, fingerprints, member locations, confidence, scoring traces and coverage results are included in that equality check. Raw CSV is byte-identical.
- The fresh packaged self-run completed with analyzer exit 0, validator exit 0 and `inspection.usable=true`.

## Remaining review leads

This completes the three selected refactors, not an assertion that the whole codebase is exemplary. The remaining highest method CC is 14. The decomposition sample now starts with ExecutableFunctionCollector (8.75), WebTypeClassifier (7), ConcurrentFanOutProbe (6.1667), TargetFrameworkCompatibility (5.8) and CompletedTaskAccess (5.3125). Except for CompletedTaskAccess, these are metric review leads whose current implementations were not re-reviewed during this change. Review their responsibilities and required decision tables before recommending further extraction. Do not split straightforward syntax switches merely to improve the ratio.

Maintainability still warrants attention: 10.61% of 66 eligible types have MI below 60, p10 MI is 59, and none are below 40. These measurements guide the next source review; they are not independent proof of defects.

## Provenance

| Field | Before | After |
|---|---|---|
| auditId | `6dd74e96-fdb5-415d-8cf1-a6bcfbb64e72` | `b9bba52f-d9bb-4c53-8aa3-260a974cf1c0` |
| runId | `321a1d05-009a-41b6-9219-119b9c0e095b` | `a15f8647-c0a2-4cf7-a93a-36c2e0a10a3e` |
| Package SHA256 | `0aaec208e1092c62a5b51a8c796734384d24f46346aaada23e150886994980a8` | `dfa8210cf6da29abbc20976c9f9d4c57d4e92e78d49e5aa8eef53173530b2a7f` |

The configuration fingerprint is unchanged: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`. Calibration is `baseline`, meaning regression fixtures, not broad external calibration. Both runs analyze one production project and exclude the test project from production metrics. The after-run records 529 test methods and 1,016 assertions; parameterized test execution produces a different test-case count.

Local artifacts are retained under `.scorecard/dotnet/runs/<runId>/` and `TestResults/self-refactor/` (invocation, test/format/calibration logs, package and contract comparison). Generated scorecard evidence remains local audit output and is not committed. The contract comparison retains its own before/after IDs in `contract-verification.json`; those IDs are separate from the self-assessment IDs above.
