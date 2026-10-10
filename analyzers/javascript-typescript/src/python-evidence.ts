import { isDeepStrictEqual } from "node:util";
import { complexityPopulation } from "#source-facts/population.mjs";
import { validateSourceFacts, type SourceFacts } from "#source-facts/contract.mjs";
import { dimensionKeys } from "./scorecard-contract.js";
import { hash, type Evidence, type Dimension, type Finding } from "./evidence.js";

export const pythonVersion = "0.3.0-experimental";
export const pythonRuleset = "python-2026-10-10-match-membership-preview";
const previewPolicy = "experimental/shared-complexity-worst-40-remainder-60/v1";

function complexityDetails(facts: SourceFacts | null) {
  const population = facts ? complexityPopulation(facts) : null;
  return { status: population ? "experimental-preview" : "unavailable", scoringPolicy: previewPolicy,
    comparableAcrossLanguages: false, population };
}

function reviewFindings(facts: SourceFacts | null): Finding[] {
  if (facts?.status !== "complete") return [];
  return facts.functions.filter(item => item.complexity && item.complexity.value >= 10).map(item => ({
    category: "methodComplexityReview", ruleId: "python/method-complexity/review-v1",
    fingerprint: hash(`${facts.measurementPolicy}:${item.id}`), severity: "info", confidence: "high",
    file: item.file, line: item.line, member: item.member,
    message: `Measured CC ${item.complexity!.value}; inspect control flow and required behavior before recommending decomposition.`,
    observations: { functionId: item.id, complexity: item.complexity, population: item.population,
      measurementPolicy: facts.measurementPolicy, reviewThreshold: 10, scoringDisposition: "unscored" }
  }));
}

function measurements(facts: SourceFacts | null) {
  const files = facts?.selection?.files ?? [];
  return { filters: { totalUnits: files.length, analyzedUnits: files.filter(item => item.disposition === "analyzed").length,
    skipped: files.filter(item => item.disposition === "excluded").map(item => ({ name: item.file, reason: item.reason })) },
  population: { types: 0, members: facts?.functions.length ?? 0 } };
}

export function pythonEvidence(facts: SourceFacts | null, subject: Evidence["subject"], ids: { runId: string; auditId: string }, error?: string): Evidence {
  if (facts) validateSourceFacts(facts);
  const complete = facts?.status === "complete" && !error;
  const dimensions = Object.fromEntries(dimensionKeys.map(key => [key, {
    status: "skipped", basis: "Not measured by the experimental Python analyzer.", findings: [],
    scope: { id: `python/${key}/unsupported`, coverage: "unsupported", includes: [], excludes: [key] }
  }])) as unknown as Record<typeof dimensionKeys[number], Dimension>;
  dimensions.codeQuality = {
    status: complete ? "skipped" : "failed",
    basis: complete ? "Function complexity measured; Python dimension scoring awaits calibration. Preview is experimental, not a dimension score."
      : "Python analysis incomplete; partial measurements are diagnostic only.",
    scope: { id: "python/owned-function-complexity-v2", coverage: "partial", includes: ["selected-function-complexity"],
      excludes: ["calibrated-dimension-score", "decomposition", "runtime-performance", "semantic-validation"] },
    findings: complete ? reviewFindings(facts) : [],
    sourceFacts: facts, experimentalPreview: complete ? complexityDetails(facts) : complexityDetails(null),
    populationNotes: "Units are observed Python files; members are selected implementation bodies. Types are not measured (the required types field is a placeholder zero). Pruned directories do not enumerate unseen files."
  };
  return { schemaVersion: 3, generatedAtUtc: new Date().toISOString(),
    tool: { name: "CodeMetrics.AI.Python", version: pythonVersion, ecosystem: "python" }, subject,
    ...measurements(facts), dimensions,
    analysis: { ...ids, status: complete ? "complete" : "incomplete", ruleset: pythonRuleset, calibration: "uncalibrated",
      configurationFingerprint: hash(JSON.stringify([pythonRuleset, facts?.producer, facts?.measurementPolicy, facts?.selection?.configurationFingerprint])),
      diagnostics: [...(facts?.diagnostics ?? []).map(item => ({ kind: item.code, message: item.message, ...(item.file ? { project: item.file } : {}) })),
        ...(error ? [{ kind: "pythonExecution", message: error }] : [])], suppressions: [] }
  };
}

/** Validate the experimental extension as well as the shared v3 envelope. */
export function validatePythonEvidence(evidence: Evidence) {
  if (evidence.tool.ecosystem !== "python" || evidence.analysis.ruleset !== pythonRuleset) return;
  if (!evidence.analysis.runId || !evidence.analysis.auditId || evidence.analysis.calibration !== "uncalibrated" ||
      evidence.tool.version !== pythonVersion || evidence.tool.name !== "CodeMetrics.AI.Python")
    throw new Error("Python evidence requires invocation IDs and experimental provenance.");
  if (Object.values(evidence.dimensions).some(item => item.status === "scored")) throw new Error("Experimental Python dimensions cannot be scored.");
  const facts = evidence.dimensions.codeQuality.sourceFacts as SourceFacts | null;
  if (facts !== null) {
    validateSourceFacts(facts);
    if (facts.language !== "python" || facts.schemaVersion !== 2 || facts.producer.version !== pythonVersion ||
        facts.producer.name !== "codemetrics-ai/python-source-facts" || facts.producer.parser !== "python-ast" ||
        facts.measurementPolicy !== "python/owned-function-complexity-v2" || facts.selection?.policy !== "python/source-selection-v1" ||
        (facts.status === "complete" && !/^3\.12\./.test(facts.producer.parserVersion)))
      throw new Error("Unexpected Python source-facts producer or version.");
  }
  if (evidence.analysis.status === "complete" && facts?.status !== "complete") throw new Error("Complete Python evidence requires complete source facts.");
  if (evidence.analysis.status === "incomplete" && !evidence.analysis.diagnostics.length) throw new Error("Incomplete Python evidence requires diagnostics.");
  const expected = pythonEvidence(facts, evidence.subject, { runId: evidence.analysis.runId, auditId: evidence.analysis.auditId },
    evidence.analysis.status === "incomplete" ? "Validation placeholder" : undefined);
  for (const key of ["filters", "population", "dimensions"] as const)
    if (!isDeepStrictEqual(evidence[key], expected[key])) throw new Error(`Python ${key} do not match measured facts and experimental policy.`);
  if (evidence.analysis.configurationFingerprint !== expected.analysis.configurationFingerprint)
    throw new Error("Python configuration fingerprint does not match source facts.");
}
