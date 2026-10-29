import { scored, type Dimension, type Finding } from "./evidence.js";
import type { FunctionMeasurement } from "./function-measurements.js";
import { aggregatePopulation, complexityPopulation, complexityAnchors } from "#source-facts/population.mjs";
import { functionSourceFacts } from "./source-facts.js";
import type { ScoringDecision } from "./scoring-decision.js";

export { complexityAnchors };
export const maintainabilityAnchors = [[40, 0], [52, 2], [58, 4], [65, 6], [70, 8], [75, 10]] as const;

export function scoreFunctions(functions: FunctionMeasurement[], dimension: "codeQuality" | "maintainability", findings: Finding[] = []): Dimension {
  const complexity = dimension === "codeQuality";
  const anchors = complexity ? complexityAnchors : maintainabilityAnchors;
  const measured = (item: FunctionMeasurement) => complexity ? item.ownComplexity : item.ownMaintainabilityIndex;
  const scope = { id: `javascript-typescript/${dimension}/v2`, coverage: "partial" as const,
    includes: [complexity ? "owned-function-cyclomatic-complexity" : "owned-function-maintainability-index"],
    excludes: ["module-and-field-initializers", "parameter-initializers", "runtime-behavior", complexity ? "decomposition" : "change-cost"] };
  if (!functions.length) return { status: "skipped", basis: "No implemented functions; function score is unmeasured.", findings, scope };
  const population = complexity ? complexityPopulation(functionSourceFacts(functions))!
    : aggregatePopulation(functions.map(item => ({ id: item.id, value: measured(item) })), anchors, false, Math.ceil(functions.length / 5))!;
  const { weakCount, restCount, losses } = population;
  const byFunctionId = new Map(functions.map(item => [item.id, item]));
  const contributions = population.contributions.map(({ id, ...contribution }) => ({ ...byFunctionId.get(id)!, ...contribution }));
  const decision: ScoringDecision = { version: 1, policy: `javascript-typescript/${dimension}/owned-functions-v2`, operation: "deductions",
    finalScore: population.score, inputs: { baseline: 10, eligibleFunctions: functions.length, weakCount, restCount,
      anchors, aggregation: complexity ? "worst-40-remainder-60" : "weakest-quintile-40-remainder-60",
      rounding: "decimal-rational-half-up-one-decimal", unroundedScore: population.unroundedScore },
    steps: (restCount ? ["weakest", "remainder"] : ["weakest"]).map(group => ({ id: group, kind: "component", score: losses[group],
      disposition: "applied", inputs: { weightedScoreLoss: losses[group], functionCount: group === "weakest" ? weakCount : restCount },
      findingCategories: complexity ? ["highFunctionComplexity"] : [] })), findingEffects: [] };
  const result = scored(decision, `${complexity ? "Method complexity: one worst function" : "Maintainability: weakest ceil(N/5) functions"} receives 40%; remaining functions receive 60%. Single-function scopes use their own score. JS/TS remains uncalibrated across ecosystems.`,
    findings, { ...decision.inputs, measurement: "owned-function-body-v2-type-erasure", functionContributions: contributions, topOffenders: contributions.slice(0, 5) });
  // Findings identify population inputs, never additional independent penalties.
  const byId = new Map(contributions.map(item => [item.id, item.group]));
  decision.findingEffects = result.findings.map(finding => {
    const group = byId.get(String(finding.observations.functionId));
    return { fingerprint: finding.fingerprint, category: finding.category,
      effect: group ? "policyInput" : "noAdditionalReduction", stepIds: group ? [group] : [] };
  });
  return { ...result, scope, ...(complexity ? { displayName: "Method Complexity", componentDetails: {
    methodComplexity: { score: result.score, eligibleFunctions: functions.length, topOffenders: contributions.slice(0, 5) },
    decomposition: { score: null, status: "unsupported", description: "JS/TS decomposition is not measured by this policy." }
  } } : {}) };
}
