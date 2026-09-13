# Executed scoring decisions

.NET 2.2.0 and JavaScript/TypeScript 0.3.0 add optional `dimension.scoringDecision` to schema v3. Existing scores, thresholds, scopes and rulesets are unchanged. Older evidence remains valid without this field; aggregate finding counts alone cannot establish the active scoring condition.

`version: 1` identifies this decision format. `policy` identifies the executed dimension policy. `inputs` records measured values, thresholds, rounding or clamping when relevant. `finalScore` equals the dimension score. Failed and skipped dimensions never carry a decision.

## Operations and steps

| Operation | Meaning of step score | Dispositions |
| --- | --- | --- |
| `firstMatch` | Candidate score for an ordered condition | The first true condition is `selected`; later true conditions are `shadowed`; false conditions are `notMatched`. |
| `minimum` | Component score or ceiling | Every tied minimum is `selected`; higher values are `notLimiting`. Final rounding is recorded in inputs. |
| `mean` | Component score | Components are `contributing`. Inputs record rounding; nested decisions preserve intermediate rounding. |
| `deductions` | Deduction amount, not a candidate final score | True deductions are `applied`; false ones are `notMatched`. Inputs record starting score and clamping. |

Steps have a unique ID within the root decision, a `kind` (`rule`, `component`, `cap`), input observations, and relevant finding categories. Rules record a condition and its evaluated `matched` boolean. Components/caps may contain a nested `decision`. A selected child of a nonbinding parent does not limit the root score. A threshold ladder evaluates overlapping conditions in order; `shadowed` is not a separate unresolved penalty. Numeric component values can exist without emitted findings.

### .NET score rounding

Ruleset `dotnet-2026-09-12-score-rounding` rounds score midpoints away from zero: for nonnegative scores, .5 goes up (2.65 → 2.7 and 8.65 → 8.7 at one decimal). Mean inputs are converted to decimal before averaging; minimum scores use decimal rounding after selecting the limiting component. Decisions record `roundingMode: AwayFromZero`, `roundingArithmetic: decimal`, the precision and the unrounded score. Nested means retain their intermediate rounding. This is half-up score rounding, not banker's ties-to-even rounding.

The earlier type-distribution C&D and maintainability policies record `percentileInterpolation: linear-decimal-v1`. Rank and interpolation use decimal arithmetic on integer CC/MI and finite decimal ratio inputs before threshold comparisons. For 59 types with six maxima at CC 16 and the rest at CC 1, p90 is exactly 4, qualifying for the existing `<= 4` band. Measurements genuinely above a boundary still use the next band; display rounding does not determine threshold membership. Thresholds, weights and raw metric/CSV calculations are unchanged. Earlier rulesets can differ numerically and must not be used as compatible baselines.

## Function-based maintainability

Unpublished .NET 2.3.0 adds `dotnet/maintainability/source-functions-quintile-40-60-v1` with measurement policy `source-function-own-mi-v1`. It combines 40% of the mean individual score of the weakest `ceil(N/5)` distinct functions with 60% of the remaining mean. Each function belongs to one group. The `deductions` steps `weakest` and `remaining` express equivalent weighted shortfalls from 10, not additional penalties on top of the weighted means. One function uses weights 1/0; an empty population is skipped and unmeasured.

Decision inputs and `metrics` record MI anchors, interpolation, group counts/means/weights, source observation counts, minimum-MI variant aggregation, unrounded score and half-up rounding. `functionContributions` records every function's score, group and rational weight (`weightNumerator / weightDenominator`). `topOffenders` is only a five-function sample with owned MI measurements and variant details. Distribution statistics (`populationPercentBelow60`, `populationPercentBelow40`, `p10Mi`) are diagnostic only and add no deductions or caps. There is no entry-point MI bonus. The earlier percentile/three-component policy applies only to legacy evidence or explicitly labeled legacy input; raw CSV MI cannot replace owned function MI. See [maintainability-policy.md](maintainability-policy.md) for measurement and compatibility limits.

## Finding attribution

Root `findingEffects` identifies each emitted finding exactly once by fingerprint and category, with links to relevant step IDs:

- `policyInput`: its category participates in the active decision path. This is not a separately measurable deduction, a counterfactual impact, or proof that removing this one finding changes the score.
- `excluded`: the occurrence is excluded by recorded policy, such as an incompatible target framework or an excluded Aspire package.
- `noAdditionalReduction`: the occurrence does not participate in an active score-reducing path for this run. It can be advisory, below a threshold, or shadowed by a stricter condition/component. It is not evidence that the issue is harmless or resolved.

For example, a dependency run with six deprecated occurrences and two included outdated occurrences selects `deprecatedPackage` at 4. The outdated findings remain visible but cause no additional reduction. Six occurrences may represent one package across projects and TFMs. Consumers should group the presentation without altering the deterministic counts or score.

The analyzer constructs decision records while executing the policy, rather than reconstructing them from the final score. The evidence validator checks the optional structure, final score equality, unique step identifiers, and finding/step references. It does not recalculate policy conditions from source. Source and behavior review remain necessary for recommendations.

## Packaged CMAI rule catalog

Development .NET 2.3.0 ships catalog version 1 with 44 permanent CMAI codes: 38 finding identities and six aggregate metric components across nine dimensions. `code-metrics rules --format json` returns the installed package version and definitions. The package also contains `Rules/rules.json` and generated `Rules/rules.md`. New meanings receive unused codes; existing codes and long rule IDs must not be reassigned.

Findings retain existing `ruleId` values and fingerprints and add `observations.diagnosticCode`. Dimensions include `ruleCatalog` with version and code/identity/kind references. Metric entries describe components without inventing per-site findings or independent deductions. These fields fit existing schema-v3 extension points; scores, thresholds and finding populations are unchanged for unannotated source.

The first catalog enables comments for CMAI5001 (empty catch), CMAI5005 (error-handling sync block), CMAI8001 (performance sync-over-async), and CMAI8006 (awaited I/O in a loop). CMAI directives require a nonempty rationale and exclude only the matched rule occurrence before scoring. The rationale is accepted without a business-judgment review. Existing category directives retain legacy behavior. Other codes explicitly advertise that comment exclusion is not implemented. A suppression declaration alone does not prove applicability or a score change.

The scorecard helper obtains definitions from the same isolated analyzer executable used for a fresh run and validates the catalog against evidence. It exposes `artifacts.ruleCatalog` only after validation. Historical imports and older packages do not borrow a current catalog. Missing catalog guidance cannot alter otherwise validated scores.

Verification: 605 analyzer tests (21 new catalog cases), 27 skill/runtime/integration tests, six unchanged .NET calibration fixtures, formatting and whitespace checks passed. Package contents and CLI lookup/error paths were checked without a solution. Tested NuGet SHA-256: `fa19507a8cebdba9e09a19f83d8235be8aa2ace83f1175ac9536b9d8890c8f38`. Local logs are under `E:/repos/CodeMetrics.AI/TestResults/rule-catalog/`. This checkpoint is unpublished.

## Error-handling source populations

Development .NET 2.3.0 uses `dotnet/errorHandling/source-findings-v1`. Its ladder thresholds are unchanged, but inputs count distinct source findings instead of project/framework repetitions. Identity uses the physical file, exact syntax span, rule category and severity. Two catches on the same line remain distinct; the same catch included in multiple frameworks or linked into multiple projects counts once. Separate conditional branches retain their source identities. A finding emitted in only one variant lists only that variant. Sources without a file identity remain unmerged.

Each finding's `observations` includes `sourceSpanStart`, `sourceSpanLength` (Roslyn zero-based character offsets), `countingUnit`, `observationCount`, `affectedProjects`, and `projectFrameworkObservations` containing project labels and original messages. The legacy `project` field is a deterministic representative; consumers should use `affectedProjects` for full applicability. Decision inputs record `sourceFindings` and `projectFrameworkObservations` totals. Report the unique count as the scoring population and the repeated count as provenance, without restoring the old penalty. This policy does not change how other dimensions count type or package instances.

The subsequent development correction records `inputs.handlingRecognition: exception-propagation-v1`. It recognizes a caught exception passed into a returned factory/constructor, returned in an initializer/tuple or directly, or delivered to a void/awaited delegate invocation. An outcome stored by a direct catch statement and later returned as a local or through an operation returning that same outcome type qualifies when no intervening assignment or ref/out use is found. It also exempts those handling paths from the missing-logger advisory. Symbol resolution and catch-variable write checks prevent unrelated/reassigned exception values from qualifying; deferred lambda/local-function bodies within the catch and discarded results do not establish handling. Existing post-catch deferred-logging recognition is preserved. Recognition describes an observable handling path, including optional callbacks, rather than proving exhaustive control flow or inspecting a factory's implementation. Exception aliases, arbitrary custom reporting methods and interprocedural mutations are outside this bounded recognition. Existing rule IDs, schema, source-counting policy and ladder thresholds remain unchanged; unknown handling produces a review warning rather than a definitive swallowing claim. Compare runs using this recognition marker and package provenance as well as the version.

## Complexity and decomposition presentation

### Source-function population policy

Ruleset `dotnet-2026-09-12-method-population` introduces `dotnet/codeQuality/method-population-v3`, whose complexity child is `dotnet/codeQuality/complexity/source-functions-40-60-v1`. The root retains `measurementPolicy: executable-function-ownership-v1` for executable ownership/decomposition and adds `complexityMeasurementPolicy: source-function-own-cc-v1`. This newer complexity policy takes precedence over the earlier type-maximum description below.

Each distinct authored executable function receives an individual score interpolated through own CC 3/5/10/20/40 → 10/8/6/4/0, clamped to 0–10. The aggregate is 40% of one worst score plus 60% of the mean of the remaining scores. Exclude exactly one worst function, including when several tie. A single-function scope uses its individual score; an empty population is unmeasured despite a policy default. Decimal arithmetic precedes one half-up rounding to one decimal; the combined C&D score retains the mean of its rounded components. Decomposition bands and raw CSV calculations are unchanged.

The complexity decision uses the existing `deductions` operation to represent the equivalent formula `10 - worstWeight*(10-worstMethodScore) - remainingWeight*(10-remainingMeanScore)`. Step scores are the weighted shortfalls from 10, not the individual function assessments. Inputs record anchors, interpolation, weights, `worstMethodScore`, `remainingMeanScore`, `aggregateCeiling`, `unroundedScore`, rounding and population details. A single-function scope records weights 1/0, a null remaining mean and an inactive remaining-population step. No schema operation was added.

`metrics.methodComplexity` records the score and its inputs instead of emitting the former `maxMemberCyclomaticComplexity` prevalence/percentile ladder. `componentDetails.methodComplexity` includes the score, `eligibleFunctions`, `functionObservations`, `singleFunctionScope`, `worstFunction` and up to five function `topOffenders`. Those samples retain name/kind/file/line/source span, `ownCc`, `individualScore`, classification, affected projects and per-variant observations. `highFunctions` means CC 11–20; `severeFunctions` means CC 21+. These disjoint counts describe measured complexity, not confirmed defects.

Count physical file + function name/defining-token offset + function kind once across project/TFM observations, using maximum own CC across variants. Identity starts at the function name or defining token (for example, a lambda arrow), so conditional attributes and return types do not duplicate an authored function. Different callbacks on one line and separate conditional declarations remain distinct. Normalized absolute source paths compare without case on Windows and ordinally on other hosts; observations lacking a file or start remain unmerged and are disclosed by `unidentifiedObservations`. Inputs include repeated-observation and variant-complexity-difference counts. The root `executableFunctions` total still counts observations, and decomposition still uses type instances; neither is the new complexity denominator. Explicit legacy/mixed `TypeMetrics` input lacking complete executable measurements retains the previous type-maximum complexity policy with `complexityMeasurementPolicy: legacy-type-maxima-v1` and `legacyInputTypes`; production collection supplies complete measurements.

The policy implements the [accepted product expectations](calibration.md#individual-method-anchors-and-aggregate-expectations). Interpret 10 as exceptional and 8 as a strong target. Preserve documented context and raw observations without inferring defects or runtime performance from CC. Earlier policy scores remain historical and cannot be relabeled with these populations or weights.

### Earlier executable-function ownership policy

For `measurementPolicy: executable-function-ownership-v1` (C&D policy IDs ending in `executable-functions-v2`), method complexity uses each type's maximum **own executable-function CC**, measuring local functions and callbacks separately from enclosing bodies. Decomposition uses `decompositionComplexity / decompositionFunctionCount`; fields, bodyless members and branch-free callbacks/initializers do not add denominator units, while named local functions count like private methods. The population remains type instances. The table below describes the earlier member-based measurements; do not apply its denominator to the new policy.

New hotspot fields distinguish these scoring inputs from preserved raw `classCc`, `memberCount`, `rawMaxMemberCc` and `rawDecompositionRatio`. Sampled `functions` retain names, kinds, files, lines and own CC. The score decision records executable/decomposition function totals and `legacyInputTypes`; nonzero legacy inputs lack the new measurements and retain member-based inputs. Production collection supplies the new measurements for every type. The ruleset changes to prevent cross-policy baseline gates, even for development packages that share a version label. Schema v3 and raw CSV remain compatible.

The .NET `codeQuality` key remains stable for schema, comparison and overall-score compatibility. Its display label is **Complexity & Decomposition**. It is one dimension with two component scores, not two additional dimensions or deductions. Preserve the recorded combined score and existing overall weighting.

| Component | Recorded score | Interpretation |
|---|---|---|
| Method complexity | `scoringDecision.steps[id=complexity].score` | Distribution of each eligible type's maximum member cyclomatic complexity. Percentages describe type instances, not all methods. |
| Decomposition | `scoringDecision.steps[id=decomposition].score` | Distribution of class complexity divided by member count, restricted to types with at least two members. It does not measure cohesion or establish that helper extraction improves readability. |

.NET 2.3.0 also supplies `displayName` and `componentDetails.methodComplexity` / `componentDetails.decomposition`: recorded scores, eligible populations, descriptions, limitations and separately ranked type hotspot lists. Hotspots retain project/TFM identity and are samples, not denominators or automatic refactoring instructions. The existing `topOffenders` field remains the decomposition-ranked list for older consumers. Empty overall populations have null component scores; an empty decomposition population within a nonempty run retains the existing policy default of 10, with zero eligible types. Neither establishes measured excellence.

Older .NET evidence may expose the scores only as `metrics.maxMemberCyclomaticComplexity.ccScore` and `metrics.decomposition.decompScore`. If neither representation exists, report component scores as unavailable rather than deriving them from the combined score. Failed-run diagnostic detail cannot restore component scores. JS/TS currently implements only method complexity; do not invent a decomposition score or apply the .NET combination policy to it.
