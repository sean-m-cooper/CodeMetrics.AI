# Cross-Ecosystem Calibration

The .NET 2.3.0 release uses `dotnet-2026-09-14-dependency-availability`. Missing dependency compatibility evidence now withholds the dependency score; a failed vulnerability query also withholds Security. See [release notes](../../docs/releases/2.3.0.md).
Performance and shared blocking observations separate actionable signals from unscored
review leads under the [classification policy](performance-async-policy.md). Numeric
ladders and catch-population weights are unchanged. Population/severity recalibration
is deferred until these classification results have been reviewed.

The [OrchardCore wait review](calibration-runs/dotnet-orchard-wait-review.md) supplies the
source evidence for completed-return proofs, conditional guards and consistent synchronous
contract propagation. The [wait-boundary corpus comparison](calibration-runs/dotnet-public-corpus-wait-boundaries.md)
records the subsequent fresh runs; it does not change the numerical calibration.

The shared contract promises a stable 0-10 scale per dimension. That promise is only meaningful across ecosystems if each analyzer's thresholds are tuned so that comparable codebases earn comparable scores. This document defines how an ecosystem earns "calibrated" status. The `dotnet` analyzer is the baseline: its thresholds define the reference distribution.

## Complexity scoring product decision

Accepted 2026-09-12. Complexity scores should express the product's expectations for understandable control flow and manageable verification effort. These expectations are product policy, not an empirically established probability of defects. More branching creates more opportunities for mistakes in implementing or interpreting business logic; high cyclomatic complexity alone does not establish that a method contains a bug.

A single severe hotspot must prevent an excellent **method-complexity** score. Increasing its severity must not improve the score. Additional distinct hotspots must make the score worse, with other conditions held constant, until the score floor is reached. Many simple methods surrounding a difficult method do not remove its reading and verification burden. Adding trivial methods or unrelated clean types must not relax the ceiling imposed by that severe hotspot.

The intended policy combines population-wide complexity with a severity-based ceiling. Prevalence describes how widespread the burden is; the ceiling preserves the influence of an isolated severe method. These are complementary scoring roles, not independent confirmations that defects exist. A combined C&D score must retain the method-complexity component and hotspot detail so a favorable decomposition score cannot support a claim of uniformly simple control flow. This decision does not impose a ceiling on the entire nine-dimension overall score.

Documented constraints can explain why a team accepts complexity. Preserve the raw measurements and contextual explanation, and honor the analyzer's supported annotation rules. Neither necessary complexity nor an accepted business decision establishes a bug. This decision introduces no new comment exclusions or judgment-based exceptions during analysis.

### Accepted method-CC bands

| Method's own CC | Product classification |
|---|---|
| 1–5 | Low branching complexity |
| 6–10 | Moderate complexity |
| 11–20 | High complexity; a hotspot worth reviewing |
| 21+ | Severe complexity; must prevent an excellent method-complexity score |

CC 10 is the upper end of moderate complexity; high begins at 11 and severe begins at 21. Apply these classifications to each executable function's own decisions, with local functions and callbacks measured separately under the declared measurement policy. These bands classify the measurement, not the presence of defects. They are accepted product choices, not universal or empirically established defect boundaries.

The bands do not prescribe abrupt score steps. The score penalty should grow progressively with severity, so crossing from 10 to 11 has a small effect and moving from 11 to 30 has a substantially larger effect. A flat switch and deeply nested conditions can receive the same CC; the classification acknowledges that limitation and introduces no automatic switch exemption or additional nesting penalty.

The product aims to guide human and AI developers toward better, more maintainable and performant code. Complexity policy addresses control-flow comprehension and verification effort; a lower CC alone does not establish better runtime performance. Developer feedback and evidence from real usage can motivate revisions to these product choices. Record the rationale, version the changed policy and revalidate its behavior rather than silently adjusting thresholds to improve particular repositories' scores.

### Individual-method anchors and aggregate expectations

The accepted individual-method assessment anchors are CC 3 → 10, CC 5 → 8, CC 10 → 6 and CC 20 → 4. These express increasingly demanding expectations for a single method. They do **not** directly set the codebase's method-complexity ceiling. Using the worst individual assessment as that ceiling would require every method to stay at CC 5 or below to achieve an aggregate 8, which is not the intended product standard.

| Aggregate method-complexity score | Intended meaning |
|---|---|
| 10 | Exceptional simplicity throughout the measured code; not the expected result for most codebases. |
| 8 | A strong, generally achievable engineering target: predominantly straightforward code, limited moderate complexity and no severe hotspots. |
| Below 8 | Branching is sufficiently widespread or severe to warrant attention. |

An isolated moderate method at CC 6–10 must not by itself force otherwise straightforward code below the target of 8. A severe method at CC 21+ must keep the reported aggregate below 8, including after half-up presentation rounding. More severe or more numerous hotspots must worsen the assessment. The aggregate must therefore combine population burden with a separately defined severity ceiling, rather than copy the worst method's assessment or let an average hide it.

The purpose is disciplined, understandable control flow. Universal tiny-method decomposition is not a prerequisite for an 8, and extracting helpers solely to improve a number is not evidence of better maintainability. Business constraints and supported documented exceptions retain the treatment described above.

### Acceptance anchors

| Controlled change | Required behavior |
|---|---|
| Introduce one severe method into otherwise simple code | The reported aggregate method-complexity score is below 8, including after rounding. |
| Introduce an isolated moderate method at CC 6–10 into otherwise straightforward code | That method alone does not force the aggregate below 8. |
| Increase that method's own CC | The score does not improve; the severity ceiling does not rise. |
| Introduce additional distinct hotspots, holding population size and other hotspot severities fixed | The underlying score decreases until the floor; displayed scores may tie because of rounding. |
| Add trivial methods around the severe method or clean types elsewhere | The severe hotspot and its ceiling remain; neither is averaged away. |
| Observe the same authored method through several target frameworks | Repeated observations do not become additional distinct hotspots for this policy. |
| Actually reduce a hotspot's own branching without introducing equivalent hotspots elsewhere | Its severity contribution does not worsen; helper counts alone do not establish improvement. |

### Accepted aggregation formula

The accepted method assessment interpolates linearly through CC 3/5/10/20/40 → score 10/8/6/4/0, holding at 10 below CC 3 and at zero above CC 40. For two or more distinct functions:

`aggregate = 0.4 * worstIndividualScore + 0.6 * mean(remainingIndividualScores)`

Exclude exactly one worst function from the remaining mean; other tied worst functions stay in it. The severity ceiling is `0.4 * worstIndividualScore + 6`, reached only when the remaining mean is 10. This preserves the effect of a severe method while additional hotspots reduce the population contribution. It does not set the aggregate equal to the worst individual assessment. A single-function scope uses its individual score and is disclosed as a small population. An empty population is unmeasured, even if a policy supplies a default.

Use decimal arithmetic and round the final aggregate once to one decimal, with midpoint ties up. The combined C&D dimension retains its existing mean of the rounded method-complexity and decomposition components. Decomposition thresholds and raw metrics are unchanged.

Count distinct authored executable functions by physical file, function name/defining-token offset and kind. Repeated project/TFM observations count once at their maximum own CC, retaining the variant details; distinct conditional declarations remain separate. Missing source identities cannot establish duplicates and remain separate observations. This is a method-complexity counting policy, not a change to decomposition's type-instance population.

For 100-function populations, accepted examples include: all CC 5 → 8.0; 99 at CC 3 and one at CC 10 → 8.4; 90 at CC 3 and ten at CC 10 → 8.2; all CC 10 → 6.0; one CC 21 with 99 at CC 3 → 7.5; ten CC 21 with 90 at CC 3 → 7.2; twenty CC 21 with 80 at CC 3 → 6.8.

The .NET development implementation uses complexity policy `dotnet/codeQuality/complexity/source-functions-40-60-v1` within C&D `dotnet/codeQuality/method-population-v3`, ruleset `dotnet-2026-09-12-method-population`. These weights and thresholds are documented product choices. Validate them against acceptance tests and pinned/held-out source examples; repository reputation or a desired score distribution must not silently change them. Subsequent revisions require a rationale, updated policy/ruleset provenance and reviewed regression baselines. This does not establish cross-ecosystem calibration or publish a package.

## Maintainability scoring product decision

The [accepted maintainability design](maintainability-policy.md) uses 40% of the mean individual score of the weakest fifth of distinct executable functions and 60% of the remaining mean. Every function belongs to one group; enums and non-executable declarations provide no credit or penalty, and low-MI distribution statistics remain diagnostics rather than additional deductions. The decision documents examples, small populations, ownership rules and dilution limits.

The .NET 2.3.0 implementation uses `source-function-own-mi-v1` and scoring policy `dotnet/maintainability/source-functions-quintile-40-60-v1`, ruleset `dotnet-2026-09-14-dependency-availability`. Own MI 40/52/58/65/70/75 maps linearly to individual scores 0/2/4/6/8/10. The initial ladder preserves former MI reference points and remains a product choice pending broader labeled calibration. Raw metrics and CSV stay compatible. Earlier `dotnet/maintainability/v1` scores are not comparable baselines.

## Performance & Async source identity

The .NET correction accepted on 2026-09-13 counts authored finding sites rather than
project/framework rows. Policy `dotnet/performanceAsync/source-findings-v1` groups by
normalized physical path, exact syntax span and rule category, using the highest observed
severity per site. All framework observations remain available. Conditional source spans,
different operations on one line and different rules at the same span remain distinct;
missing identity is never merged. Pattern recognition, exclusions and ladder thresholds
are unchanged. This follows the product decision that compiling authored code for more
frameworks must not multiply its penalty.

Ruleset `dotnet-2026-09-13-async-source-findings` prevents an old row-counting run from
being used as a compatible baseline gate. The six pinned fixture expectations retain
their scores, findings, labels and source hashes; only baseline ruleset metadata changes.
Public-corpus before/after results must be labeled as a counting correction on fixed
source, not source-code improvement or evidence that every finding is a defect.

## Prerequisites: metric parity

Before distribution tuning, the new analyzer must demonstrate formula parity with the baseline on the shared raw metrics:

1. **Maintainability index** — identical formula: `MAX(0, (171 - 5.2*ln(HalsteadVolume) - 0.23*CyclomaticComplexity - 16.2*ln(LinesOfCode)) * 100 / 171)`. The language-specific inputs must be documented in the analyzer README, with fixture files whose expected MI values are hand-computed and asserted in tests.
2. **Cyclomatic complexity** — a construct parity table mapping baseline constructs to the ecosystem's equivalents, asserted by fixture tests. The counting policy is: start at 1 per member; +1 per runtime decision point including logical `&&`/`||` and null-coalescing operators; type-only declarations add nothing.
3. **Population definitions** — what counts as a "type" and "member" for `population` and CSV rows must be documented.

## Calibration procedure

1. **Assemble a reference corpus** of 6-10 public repositories for the ecosystem, selected for spread, not fame: at least two widely accepted as high quality, at least two with known quality problems, sizes spanning roughly 10k-500k LOC, and at least one of each dominant framework flavor.
2. **Establish the baseline distribution** once: run the `dotnet` analyzer over its own corpus and record per-dimension score median, p25, and p10.
3. **Run and compare**: run the candidate analyzer over its corpus. For each dimension, compare its score distribution to the baseline distribution.
4. **Tune thresholds** until per-dimension medians are within +/-0.5 and p10 within +/-1.0 of the baseline, subject to the accepted product anchors above. Distribution similarity cannot justify violating those anchors or establish equal score meaning on its own. Ordinary distribution tuning changes thresholds, not formulas; implementing the accepted complexity policy is a separately versioned scoring change.
5. **Record the run** under `shared/scorecard-schema/calibration-runs/<ecosystem>-<tool-version>.md` listing the corpus, per-dimension distributions, and frozen threshold values. Update the status table in `dimensions.md`.
6. **Recalibrate** whenever a dimension's rules or thresholds change. `tool.version` ties every scorecard to a frozen threshold set.

## Interim rule for uncalibrated ecosystems

Until an ecosystem completes this procedure:

- Its analyzer README must state that scores are uncalibrated across ecosystems.
- Consumers render its scores with an explicit caveat and never average them with other ecosystems' scores.
- The dotnet CSV fallback procedure must not be applied to its CSV output.

## Executable regression checks

See `../calibration/README.md` and `../calibration/baselines/` for the pinned labeled fixture corpus, measured .NET reference distribution, JS/TS distributions, and release checks. These fixtures supplement the public-repository procedure above; they do not establish cross-ecosystem comparability. Always evaluate labeled false positives and false negatives alongside distribution similarity.

## Security and error-handling context correction

The .NET 2.3.0 release uses ruleset `dotnet-2026-09-14-dependency-availability`. It distinguishes descriptive identifiers from credential candidates, recognizes bounded CORS rejecting guards, accepts local catch rationale and diagnostic output parameters, and scores the severity-weighted source catch population. See the [policy and limitations](error-handling-policy.md). Earlier absolute-count scores are incompatible baseline gates.
