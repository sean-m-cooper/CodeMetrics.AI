import { describe, expect, it } from "vitest";
import { firstMatch, thresholdDecision } from "../dist/scoring-decision.js";
import { scored, hash, skippedDimensions, type Evidence, type Finding } from "../dist/evidence.js";
import { validateEvidence } from "../dist/evidence-tools.js";

describe("executed scoring decisions", () => {
  it.each([[5, 10], [5.01, 8], [10, 8], [10.01, 6], [20, 6], [20.01, 4], [40, 4], [40.01, 2]])("preserves complexity boundary %s", (measured, expected) => {
    const decision = thresholdDecision("test", measured, [5, 10, 20, 40], false);
    expect(decision.finalScore).toBe(expected);
    expect(decision.steps.filter(step => step.disposition === "selected")).toHaveLength(1);
  });
  it.each([[85, 10], [84.99, 8], [65, 8], [64.99, 6], [40, 6], [39.99, 4], [20, 4], [19.99, 2]])("preserves MI boundary %s", (measured, expected) => {
    expect(thresholdDecision("test", measured, [85, 65, 40, 20], true).finalScore).toBe(expected);
  });
  function fixture() {
    const findings: Finding[] = ["high", "low"].map(confidence => ({ category: "reactEffect", ruleId: "react/effect", fingerprint: hash(confidence), severity: "warning",
      confidence: confidence as "high" | "low", message: "Review the effect.", observations: {} }));
    const dimension = scored(firstMatch("react", { actionableFindings: 1 }, [
      { id: "actionable", condition: "actionableFindings > 0", matched: true, score: 6, categories: ["reactEffect"] },
      { id: "otherwise", condition: "otherwise", matched: true, score: 10 }
    ]), "React hooks only", findings, {}, finding => finding.confidence !== "low");
    const evidence: Evidence = { schemaVersion: 3, generatedAtUtc: new Date().toISOString(), tool: { name: "codemetrics-ai", version: "0.3.0", ecosystem: "javascript-typescript" },
      subject: { root: "/sample", entryPoint: "/sample/package.json" }, filters: { totalUnits: 1, analyzedUnits: 1, skipped: [] }, population: { types: 1, members: 1 },
      analysis: { status: "complete", ruleset: "test", calibration: "uncalibrated", configurationFingerprint: "test", diagnostics: [], suppressions: [] },
      dimensions: { ...skippedDimensions(), performanceAsync: dimension } };
    return evidence;
  }
  it("keeps advisory findings without attributing them to the selected penalty", () => {
    const evidence = fixture(); validateEvidence(evidence);
    const effects = evidence.dimensions.performanceAsync.scoringDecision!.findingEffects;
    expect(effects.find(effect => effect.fingerprint === hash("low"))!.effect).toBe("noAdditionalReduction");
    expect(effects.find(effect => effect.fingerprint === hash("high"))!.effect).toBe("policyInput");
    const legacy = structuredClone(evidence); delete legacy.dimensions.performanceAsync.scoringDecision;
    expect(() => validateEvidence(legacy)).not.toThrow();
  });
  it("rejects invalid decision score, status and finding references", () => {
    const original = fixture();
    for (const mutate of [
      (e: Evidence) => { e.dimensions.performanceAsync.scoringDecision!.finalScore = 4; },
      (e: Evidence) => { e.dimensions.performanceAsync.status = "failed"; delete e.dimensions.performanceAsync.score; },
      (e: Evidence) => { e.dimensions.performanceAsync.scoringDecision!.findingEffects.pop(); },
      (e: Evidence) => { e.dimensions.performanceAsync.scoringDecision!.findingEffects[0].fingerprint = hash("unknown"); },
      (e: Evidence) => { e.dimensions.performanceAsync.scoringDecision!.findingEffects[0].stepIds = ["unknown"]; },
      (e: Evidence) => { e.dimensions.performanceAsync.scoringDecision!.steps.push(e.dimensions.performanceAsync.scoringDecision!.steps[0]); }
    ]) { const evidence = structuredClone(original); mutate(evidence); expect(() => validateEvidence(evidence)).toThrow(); }
  });
});
