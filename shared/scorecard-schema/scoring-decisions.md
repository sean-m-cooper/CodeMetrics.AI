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

## Finding attribution

Root `findingEffects` identifies each emitted finding exactly once by fingerprint and category, with links to relevant step IDs:

- `policyInput`: its category participates in the active decision path. This is not a separately measurable deduction, a counterfactual impact, or proof that removing this one finding changes the score.
- `excluded`: the occurrence is excluded by recorded policy, such as an incompatible target framework or an excluded Aspire package.
- `noAdditionalReduction`: the occurrence does not participate in an active score-reducing path for this run. It can be advisory, below a threshold, or shadowed by a stricter condition/component. It is not evidence that the issue is harmless or resolved.

For example, a dependency run with six deprecated occurrences and two included outdated occurrences selects `deprecatedPackage` at 4. The outdated findings remain visible but cause no additional reduction. Six occurrences may represent one package across projects and TFMs. Consumers should group the presentation without altering the deterministic counts or score.

The analyzer constructs decision records while executing the policy, rather than reconstructing them from the final score. The evidence validator checks the optional structure, final score equality, unique step identifiers, and finding/step references. It does not recalculate policy conditions from source. Source and behavior review remain necessary for recommendations.
