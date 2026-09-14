# Maintainability scoring product decision

Accepted weighting and population principles: 2026-09-12.

**Status: implemented in .NET 2.3.0.** Scoring policy `dotnet/maintainability/source-functions-quintile-40-60-v1`, measurement policy `source-function-own-mi-v1`, ruleset `dotnet-2026-09-14-dependency-availability`. The initial ladder retains existing MI reference points; it is a product calibration, not an empirical readability or defect model. Earlier `dotnet/maintainability/v1` results retain their historical interpretation.

## Purpose and rationale

Maintainability should reward consistently understandable executable code while giving additional influence to the weakest portion of the codebase. MI responds to size and token volume as well as branching, so this dimension emphasizes a group of weaker functions rather than letting one necessarily substantial function dominate. Method complexity retains its separate, accepted worst-function policy.

The former maintainability components describe different but overlapping aspects of the same low-MI population. A low-MI type can affect both prevalence and the lower percentile, and an extreme case can affect all three components. The accepted replacement assigns every eligible function to exactly one weighted group. Distribution statistics remain visible diagnostics without additional score adjustments.

The self-refactor exposed a separate measurement problem: a nested enum contributed MI 100 to its containing type's member average. Moving the enum out removed that credit even though all ten executable methods retained identical individual MI values. Enum placement must be neutral in scored maintainability.

## Population and ownership

- Count distinct authored executable functions, not type instances or raw member rows. Repeated observations of the same physical source across projects/target frameworks must not multiply credit or penalties.
- Enum declarations, fields as declarations, bodyless members and other non-executable declarations contribute neither credit nor penalty. Code that uses an enum remains measurable.
- Each executable operation belongs to one function. A local function or lambda's body must not also contribute its complexity, source lines or token measurements to its enclosing function's MI.
- Moving unchanged functions between classes, splitting a class into partial declarations, or relocating an enum must not change the scored population or contributions merely because containment changed.
- Preserve raw MI and CSV compatibility separately. Correcting scored maintainability does not silently redefine existing raw metrics.

Methods, constructors, operators, named local functions, callbacks, implemented accessors and expression-bodied properties/indexers qualify. Runtime field/property initializers qualify even without branching. Compile-time constant initializers and lambda-only initializer wrappers do not supply extra credit; their callback bodies count separately. Pure state declarations are neutral, but an assignment constructor is still executable and measured. This population can differ from the C&D population.

Source identity is the normalized absolute physical file, defining-token offset and function kind. Repeated project/TFM observations use the **lowest unrounded own MI** once and retain observation counts and sampled variant details. Separate conditional declarations remain distinct. Missing identities remain separate observations and are disclosed. Missing function measurements fail the dimension without a score. Explicit legacy or mixed `TypeMetrics` inputs lacking complete executable metadata retain the former type policy, labeled `legacy-type-member-mi-v1` with `legacyInputTypes`; production collection supplies complete metadata.

### Own-function MI measurement

The standard MI expression is evaluated on the function's owned body: `clamp((171 - 5.2*ln(max(volume, 1)) - 0.23*ownCC - 16.2*ln(sourceLines)) * 100/171, 0, 100)`. An explicitly implemented empty body has MI 100; absence of an implemented function does not. The logarithms use floating-point arithmetic, then the unrounded MI is converted to decimal for scoring.

Halstead volume uses the existing token classification on body tokens, excluding signatures and nested function/type declarations. Constructor initializer arguments belong to the constructor. Each function's own CC excludes nested bodies. Source lines count token-bearing physical lines, ignoring comments, blank lines and brace/semicolon-only lines; multiline literal tokens retain their occupied lines. Line breaks inside removed nested declarations are projected out so expanding a nested body cannot inflate its parent's line count. Ordinary body formatting can still affect MI.

There is no `Program`/`Startup` MI bonus under this policy (`entryPointMiAdjustment: 0`). Exclusive body measurement replaces the former type-density adjustment. Supported documented exceptions retain their advertised rule-specific behavior; the aggregate CMAI9001 code does not implement a comment exemption.

## Accepted aggregation

Convert each eligible function's MI into an individual score using linear interpolation between the following anchors, clamped to 0–10. The initial anchors retain 52/58/65/70/75 from the former MI reference bands and add MI 40 as zero. They are explicitly versioned product choices pending broader labeled calibration.

| Own-function MI | Individual score |
|---:|---:|
| 40 or below | 0 |
| 52 | 2 |
| 58 | 4 |
| 65 | 6 |
| 70 | 8 |
| 75 or above | 10 |

For `N >= 2` distinct functions:

1. Sort the unrounded individual scores from weakest to strongest.
2. Set `K = ceil(N / 5)`.
3. Assign the first `K` functions to the weakest group and every remaining function to the other group. A stable source-identity tie-breaker makes evidence reproducible; exchanging equal scores at the boundary cannot change the result.
4. Calculate:

```text
maintainability =
    0.40 × mean(weakest K individual scores)
  + 0.60 × mean(remaining N − K individual scores)
```

Each function contributes exactly once: with weight `0.40 / K` or `0.60 / (N − K)`. The weights sum to one. Low-MI percentages, percentiles, worst cases and type-level summaries do not add deductions, bonuses or caps.

For exactly 100 functions, each of the weakest 20 has weight 0.02 and each of the remaining 80 has weight 0.0075. A weak-group function therefore has approximately 2.67 times the influence of an individual function in the other group. This is deliberate weighting, not duplicate inclusion.

For one function, use its individual score and disclose the small population. For no eligible functions, report unmeasured rather than measured excellence. Use decimal arithmetic, retain unrounded intermediate values and round the final aggregate once to one decimal with midpoint ties up.

## Accepted examples

These examples start with already-converted individual scores. They validate aggregation only; a function scoring 2 here is not an assertion that raw MI 20 maps to 2.

| Population of 100 functions | Weakest-group mean | Remaining mean | Aggregate |
|---|---:|---:|---:|
| All score 10 | 10 | 10 | 10.0 |
| 99 score 10; one scores 2 | 9.6 | 10 | 9.8 |
| 90 score 10; ten score 2 | 6 | 10 | 8.4 |
| 80 score 10; twenty score 2 | 2 | 10 | 6.8 |
| 50 score 10; fifty score 2 | 2 | 7 | 5.0 |
| All score 8 | 8 | 8 | 8.0 |

## Limits and acceptance checks

At a fixed population size, lowering any individual score must not improve the unrounded aggregate; increasing it must not worsen the aggregate. Functions can change groups, but none can belong to both. Presentation rounding can make distinct aggregates display the same value.

The weakest group retains 40% of the aggregate while it represents the weakest fifth. This is **not an absolute worst-function ceiling**: adding enough high-scoring functions can dilute a fixed weak group as `K` grows. The separate method-complexity policy preserves its own severe-hotspot ceiling. Do not claim this maintainability formula is immune to padding or guarantees invariance when executable functions are added or extracted.

`ceil(N / 5)` creates group-size steps in small populations. Six functions place two in the weakest group, rather than exactly 20%. Record both group sizes and weights, disclose small scope and test these boundaries. A source-preserving move between classes is different from changing the actual function population.

Required verification includes enum addition/relocation neutrality; unchanged function contributions across class and partial-class boundaries; no overlapping ownership of nested bodies; repeated-framework deduplication; the examples above; group-boundary ties; populations of zero, one, two, five and six; and monotonicity at fixed population size. Preserve unchanged raw CSV output on fixed source fixtures.

## Calibration and rollout

Validate the initial MI-to-score ladder using controlled examples and the representative corpus. Review necessary orchestration, parsers, compact decision tables and metadata-heavy functions as well as problematic examples. Repository reputation or a desired self-score must not determine thresholds. The ladder and weights are product decisions, not estimates of defect probability or proof of readability.

Evidence exposes population counts, group sizes, weights, individual-score mapping, unrounded aggregate and rounding policy. `functionContributions` lists every distinct function's MI, individual score, disjoint group and rational weight (`weightNumerator / weightDenominator`). `topOffenders` includes the five weakest functions with owned source lines, Halstead volume, own CC and variant MI observations. The decision uses the existing `deductions` operation to express equivalent weighted shortfalls from 10; the two step scores are deductions, not the group means. Low-MI percentages and p10 are explicitly `diagnosticOnly` and never add a third deduction or cap.

The packaged CMAI9001 catalog entry and working code-scorecard skill describe this policy. Review baseline changes explicitly, then rerun regression checks without recording. Earlier type-distribution scores and new executable-function scores must not be treated as compatible baselines or as evidence of code improvement by themselves.

Raw metrics and CSV remain compatible; schema v3 extension fields carry the new evidence. This work does not publish packages. See the [implementation verification](calibration-runs/dotnet-function-maintainability.md) for the tested candidate and limits.
