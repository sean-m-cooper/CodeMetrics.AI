import type { Finding } from "./evidence.js";

export interface ScoringStep {
  id: string; kind: "rule" | "component" | "cap"; score: number;
  condition?: string | null; matched?: boolean | null;
  disposition: "selected" | "shadowed" | "notMatched" | "contributing" | "notLimiting" | "applied";
  inputs: Record<string, unknown>; findingCategories: string[]; decision?: ScoringDecision | null;
}
export interface ScoringDecision {
  version: 1; policy: string; operation: "firstMatch" | "minimum" | "mean" | "deductions";
  finalScore: number; inputs: Record<string, unknown>; steps: ScoringStep[];
  findingEffects: { fingerprint: string; category: string; effect: "policyInput" | "excluded" | "noAdditionalReduction"; stepIds: string[] }[];
}

export function firstMatch(policy: string, inputs: Record<string, unknown>, rules: { id: string; condition: string; matched: boolean; score: number; categories?: string[] }[]): ScoringDecision {
  const selected = rules.findIndex(rule => rule.matched);
  if (selected < 0) throw new Error("Scoring ladder requires a matching default.");
  return { version: 1, policy, operation: "firstMatch", finalScore: rules[selected].score, inputs, findingEffects: [],
    steps: rules.map((rule, index) => ({ id: rule.id, kind: "rule", condition: rule.condition, matched: rule.matched, score: rule.score,
      disposition: index === selected ? "selected" : rule.matched ? "shadowed" : "notMatched", inputs: {}, findingCategories: rule.categories ?? [] })) };
}

export function thresholdDecision(policy: string, measured: number, thresholds: number[], descending: boolean, categories: string[] = []): ScoringDecision {
  return firstMatch(policy, { measured, thresholds }, [
    ...thresholds.map((threshold, index) => ({ id: `band${index}`, condition: `measured ${descending ? ">=" : "<="} threshold[${index}]`,
      matched: descending ? measured >= threshold : measured <= threshold, score: 10 - index * 2, categories })),
    { id: "otherwise", condition: "otherwise", matched: true, score: 2, categories }
  ]);
}

export function attachFindings(decision: ScoringDecision, findings: Finding[], eligible: (finding: Finding) => boolean = () => true): void {
  decision.findingEffects = findings.map(finding => {
    const steps = decision.steps.filter(step => step.findingCategories.includes(finding.category));
    const excluded = String(finding.observations.scoreDisposition ?? "").startsWith("excluded");
    return { fingerprint: finding.fingerprint, category: finding.category,
      effect: excluded ? "excluded" as const : eligible(finding) && steps.some(step => step.disposition === "selected") ? "policyInput" as const : "noAdditionalReduction" as const,
      stepIds: steps.map(step => step.id).sort() };
  }).sort((a, b) => a.fingerprint.localeCompare(b.fingerprint, "en"));
}
