import { aggregatePopulation, complexityAnchors } from "#source-facts/population.mjs";
import type { SourceFacts } from "#source-facts/contract.mjs";
import { scored, type Dimension } from "./evidence.js";
import { firstMatch } from "./scoring-decision.js";

export const pythonComplexityPolicy = "python/method-complexity-worst-40-remainder-60/v1";
export const pythonCalibration = "baseline";

/** Score each selected production implementation once; other populations remain review evidence. */
export function methodComplexity(facts: SourceFacts | null) {
  const available = facts?.status === "complete" && facts.capabilities.functionComplexity === "available";
  const selected = available ? facts.functions : [];
  const eligible = selected.filter(item => item.population === "production");
  const population = available ? aggregatePopulation(eligible.map(item => ({ id: item.id, value: item.complexity!.value })), complexityAnchors, true, 1) : null;
  return { status: population ? "scored" : "unavailable", scoringPolicy: pythonComplexityPolicy,
    measurementPolicy: "python/owned-function-complexity-v2", comparableAcrossLanguages: false,
    selectedFunctions: available ? selected.length : null, eligibleFunctions: available ? eligible.length : null,
    excludedPopulations: available ? Object.fromEntries(["test", "support", "generated"].map(kind => [kind, selected.filter(item => item.population === kind).length])) : null,
    population };
}

export function scoreMethodComplexity(dimension: Dimension, facts: SourceFacts | null, complete: boolean): Dimension {
  const details = methodComplexity(complete ? facts : null);
  const { experimentalPreview: _preview, ...base } = dimension;
  const findings = base.findings.map(finding => finding.category === "methodComplexityReview" ? { ...finding,
    observations: { ...finding.observations, scoringDisposition: finding.observations.population === "production" ? "aggregate-policy-input" : "unscored" }
  } : finding);
  const scope: Dimension["scope"] = { id: pythonComplexityPolicy, coverage: "partial",
    includes: ["selected-production-function-complexity"],
    excludes: ["test-support-generated-scoring", "decomposition-scoring", "duplication-scoring", "coverage-crap-scoring", "runtime-performance", "semantic-validation"] };
  if (!details.population) return { ...base, scope, findings, methodComplexity: details,
    basis: complete ? "Method Complexity unavailable: no selected production implementations. Other measurements are unscored review evidence." : base.basis };
  const decision = firstMatch(pythonComplexityPolicy, { measurementPolicy: facts!.measurementPolicy,
    eligibleFunctions: details.eligibleFunctions, excludedPopulations: details.excludedPopulations,
    anchors: complexityAnchors, weighting: "one-worst-40-percent/remainder-mean-60-percent; singleton-100-percent", rounding: "decimal-half-up-one-place" },
  [{ id: "methodComplexity", condition: "nonempty selected production implementation population", matched: true,
    score: details.population.score, categories: ["methodComplexityReview"] }]);
  return { ...base, ...scored(decision,
    "Method Complexity of selected production implementations. Decomposition, duplication and coverage/CRAP are unscored review evidence.",
    findings, { population: details.population }, finding => finding.category === "methodComplexityReview" && finding.observations.population === "production"),
    scope, methodComplexity: details };
}
