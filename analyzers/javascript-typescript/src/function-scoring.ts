import { scored, type Dimension, type Finding } from "./evidence.js";
import type { FunctionMeasurement } from "./function-measurements.js";
import { add, decimal, fraction, interpolate, multiply, numeric, roundScore, subtract, type Fraction } from "./score-arithmetic.js";
import type { ScoringDecision } from "./scoring-decision.js";

export const complexityAnchors = [[3, 10], [5, 8], [10, 6], [20, 4], [40, 0]] as const;
export const maintainabilityAnchors = [[40, 0], [52, 2], [58, 4], [65, 6], [70, 8], [75, 10]] as const;

export function scoreFunctions(functions: FunctionMeasurement[], dimension: "codeQuality" | "maintainability", findings: Finding[] = []): Dimension {
  const complexity = dimension === "codeQuality";
  const anchors = complexity ? complexityAnchors : maintainabilityAnchors;
  const measured = (item: FunctionMeasurement) => complexity ? item.ownComplexity : item.ownMaintainabilityIndex;
  const scope = { id: `javascript-typescript/${dimension}/v2`, coverage: "partial" as const,
    includes: [complexity ? "owned-function-cyclomatic-complexity" : "owned-function-maintainability-index"],
    excludes: ["module-and-field-initializers", "parameter-initializers", "runtime-behavior", complexity ? "decomposition" : "change-cost"] };
  if (!functions.length) return { status: "skipped", basis: "No implemented functions; function score is unmeasured.", findings, scope };
  const ordered = functions.map(item => ({ item, score: interpolate(measured(item), anchors) }))
    .sort((a, b) => (complexity ? measured(b.item) - measured(a.item) : measured(a.item) - measured(b.item)) ||
      (a.item.id < b.item.id ? -1 : a.item.id > b.item.id ? 1 : 0));
  const weakCount = complexity ? 1 : Math.ceil(ordered.length / 5);
  const restCount = ordered.length - weakCount;
  const weakWeight = restCount ? fraction(2n, 5n * BigInt(weakCount)) : fraction(1n, BigInt(weakCount));
  const restWeight = restCount ? fraction(3n, 5n * BigInt(restCount)) : decimal(0);
  const losses: Record<string, Fraction> = { weakest: decimal(0), remainder: decimal(0) };
  const contributions = ordered.map(({ item, score }, index) => {
    const group = index < weakCount ? "weakest" : "remainder";
    const weight = index < weakCount ? weakWeight : restWeight;
    const weightedLoss = multiply(subtract(decimal(10), score), weight);
    losses[group] = add(losses[group], weightedLoss);
    return { ...item, group, individualScore: numeric(score), weightNumerator: Number(weight.numerator),
      weightDenominator: Number(weight.denominator), weightedScoreLoss: numeric(weightedLoss) };
  });
  const total = subtract(decimal(10), add(losses.weakest, losses.remainder));
  const decision: ScoringDecision = { version: 1, policy: `javascript-typescript/${dimension}/owned-functions-v2`, operation: "deductions",
    finalScore: roundScore(total), inputs: { baseline: 10, eligibleFunctions: ordered.length, weakCount, restCount,
      anchors, aggregation: complexity ? "worst-40-remainder-60" : "weakest-quintile-40-remainder-60",
      rounding: "decimal-rational-half-up-one-decimal", unroundedScore: numeric(total) },
    steps: (restCount ? ["weakest", "remainder"] : ["weakest"]).map(group => ({ id: group, kind: "component", score: numeric(losses[group]),
      disposition: "applied", inputs: { weightedScoreLoss: numeric(losses[group]), functionCount: group === "weakest" ? weakCount : restCount },
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
    methodComplexity: { score: result.score, eligibleFunctions: ordered.length, topOffenders: contributions.slice(0, 5) },
    decomposition: { score: null, status: "unsupported", description: "JS/TS decomposition is not measured by this policy." }
  } } : {}) };
}
