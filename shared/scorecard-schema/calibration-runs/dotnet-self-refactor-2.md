# .NET self-refactor: fan-out analysis and framework parsing

This September 12, 2026 follow-up separates responsibilities in two reviewed hotspots. Scoring rules, thresholds and suppression declarations are unchanged. The [preceding pass](dotnet-self-refactor.md) is the comparison baseline.

## Changes

- `ConcurrentFanOutProbe` separates tree traversal, captured-mutation discovery, captured-argument matching and finding construction. Explicit returns replace the cross-loop `goto`, preserving the first demonstrated mutation and one finding per recognized fan-out site.
- Parameter aliases are computed once per authored method and reused throughout fixed-point mutation propagation. The aliases depend only on source syntax; summaries evolve independently. This removes repeated syntax scans at the cost of retaining those alias maps for the duration of summary construction. No runtime speedup percentage is claimed.
- `TargetFrameworkParser` owns normalization of framework names into the internal `ParsedFramework` representation. `TargetFrameworkCompatibility` owns platform and version compatibility decisions, including unknown results. Supported syntax and compatibility behavior remain unchanged.
- ExecutableFunctionCollector and WebTypeClassifier were reviewed and retained: their explicit classifications are already compact, and splitting their decision tables solely to lower a metric would not establish an improvement.

## Verification

All **696 .NET tests passed**, including ten added cases: seven framework-name/platform combinations, compatible assets mixed with unknown alternatives, transitive fan-out mutation with first-call/one-finding-per-site assertions, and mutation limited to the iteration's own item. Full solution formatting verification and `git diff --check` passed.

All six pinned .NET calibration fixtures passed without updating baselines. An old/new CLI contract comparison on identical source includes interface and transitive fan-out mutation, noncaptured item mutation, completion guards, repeated catches and partially matched coverage. All nine findings and complete evidence match after removing only generation timestamps and fresh run/audit IDs. Raw CSV is byte-identical. This establishes preservation for the exercised cases, not an exhaustive semantic proof.

## Fresh self-assessment

Entry point: `analyzers/dotnet/CodeMetrics.AI.slnx`, Release. Both runs are complete and usable, with matching invocation/evidence IDs. Analyzer and validator exit codes are zero. There are no analysis diagnostics. Production scope is one analyzed project; the test project is excluded from production metrics.

| Dimension | Previous | Current | Source and scope |
|---|---:|---:|---|
| Architecture & SOLID | 8.5 | 8.5 | Deterministic static coupling/project structure |
| Complexity & Decomposition | 6.3 | 6.3 | Deterministic method complexity 7.8; decomposition 4.7 |
| Testing | 10 | 10 | Deterministic test signals; no coverage report supplied |
| Security | 10 | 10 | Deterministic static patterns; dependency vulnerability checks skipped |
| Error Handling | 10 | 10 | Deterministic static exception patterns |
| Documentation | 8 | 8 | Deterministic presence/content signals |
| Dependency Management | N/A | N/A | Feed checks deliberately skipped |
| Performance & Async | 10 | 10 | Deterministic static async/blocking patterns |
| Maintainability | 5.3 | **4.7** | Deterministic type MI; lower percentile crossed a score boundary |
| **Overall — partial assessment, 8/9 scored** | **8.5** | **8.4** | Unweighted mean; dependencies excluded |

Every scored dimension has partial scope. Neither comprehensive human review nor runtime performance is measured. The testing/security scores do not establish test effectiveness or absence of vulnerabilities. JavaScript and the skill were not rescored.

Method complexity (CMAI2002, Method complexity distribution) remains 7.8: `0.4 × 5.2 + 0.6 × 9.493153526970953 = 7.775892116182573`, rounded half up. Maximum own CC remains 14, high functions decrease from 20 to 19, and severe functions remain zero. The population is 965 distinct functions: 863 low, 83 moderate and 19 high. No repeated or unidentified observations are present.

Decomposition (CMAI2001, Decomposition distribution) remains 4.7. Its population above ratio 4 changes from 28.85% to 30.19%; p90 changes from 4.95 to 5.0, and none exceed ratio 15. These CMAI entries identify aggregate components, not independent per-site deductions. Neither supports comment exclusion in this package.

The maintainability tradeoff is explicit. ConcurrentFanOutProbe's raw type MI improves from 60 to 61. The former combined TargetFrameworkCompatibility type had MI 60; the separated compatibility and parser types measure 56 and 57. The eligible population grows from 66 to 67 types, its below-60 rate rises from 10.61% to 13.43%, and p10 MI moves from 59 to 57.6. The p10 component falls from 4 to 2, lowering the final score. Clearer responsibility boundaries do not guarantee a higher type-distribution score; the separation is retained for its design benefit and the measured reduction is reported without changing policy.

## Remaining work

Continue source review before further decomposition. Flat syntax/compatibility tables may be appropriate even with elevated CC. The highest remaining methods still have CC 14; unchanged counts do not revalidate prior explanations of their design. Future optimization claims should include representative timing and memory evidence. No further implementation recommendation is inferred from the score alone.

## Provenance

- Tool: local unpublished CodeMetrics.AI 2.3.0; schema 3; ruleset `dotnet-2026-09-12-method-population`; calibration `baseline` (regression fixtures).
- auditId: `4139901c-a171-4590-ade5-85771324c0c5`.
- runId: `21337e32-a775-45c2-ab2b-7774cb469ee5`.
- Previous runId: `a15f8647-c0a2-4cf7-a93a-36c2e0a10a3e`; previous auditId: `b9bba52f-d9bb-4c53-8aa3-260a974cf1c0`.
- Configuration fingerprint unchanged: `a65fbbb8a7c140f7d52cb50486e7d414a7a67a0e5186a80a79f0e9b4807371c4`.
- Current package SHA256: `fb8505cf8d42df02605f8d18376aac6ffbd5cb88a88298ca469870563e3ab243`; previous package: `dfa8210cf6da29abbc20976c9f9d4c57d4e92e78d49e5aa8eef53173530b2a7f`.
- Current production population: 105 types / 628 raw members. New collaborators change the population; raw metrics are expected to change on modified source.
- Evidence, validated inspection and packaged rule catalog: `.scorecard/dotnet/runs/21337e32-a775-45c2-ab2b-7774cb469ee5/`.
- Invocation, package, test/format/calibration logs and contract verification: `TestResults/self-refactor-2/`.

The contract comparison has separate fresh IDs recorded in its `contract-verification.json`. All generated evidence remains local audit output. Changes are local and uncommitted.
