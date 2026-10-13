import { isDeepStrictEqual } from "node:util";
import { complexityPopulation } from "#source-facts/population.mjs";
import { validateSourceFacts, type SourceFacts } from "#source-facts/contract.mjs";
import { dimensionKeys } from "./scorecard-contract.js";
import { hash, type Evidence, type Dimension, type Finding } from "./evidence.js";
import { duplicationFindings, validateDuplication, type PythonDuplication } from "./python-duplication.js";
import { validateCoverage, type PythonCoverage } from "./python-coverage.js";
import { validateDecomposition, type PythonDecomposition } from "./python-decomposition.js";
import { scoreMethodComplexity, pythonCalibration } from "./python-complexity.js";

export const pythonVersion = "0.7.0";
export const pythonRuleset = "python-2026-10-13-method-complexity-v1";
const currentProfile = { version: pythonVersion, ruleset: pythonRuleset, decomposition: true, scored: true };
const decompositionProfile = { version: "0.6.0-experimental", ruleset: "python-2026-10-13-decomposition-preview", decomposition: true, scored: false };
const coverageProfile = { version: "0.5.0-experimental", ruleset: "python-2026-10-12-coverage-preview", decomposition: false, scored: false };
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

export function pythonEvidence(facts: SourceFacts | null, subject: Evidence["subject"], ids: { runId: string; auditId: string }, error?: string, duplication: PythonDuplication | null = null, coverage: PythonCoverage | null = null, decomposition: PythonDecomposition | null = null): Evidence {
  return buildEvidence(currentProfile, facts, subject, ids, error, duplication, coverage, decomposition);
}

function buildEvidence(profile: typeof currentProfile, facts: SourceFacts | null, subject: Evidence["subject"], ids: { runId: string; auditId: string }, error: string | undefined, duplication: PythonDuplication | null, coverage: PythonCoverage | null, decomposition: PythonDecomposition | null): Evidence {
  if (facts) validateSourceFacts(facts);
  if (facts && duplication) validateDuplication(duplication, facts, subject, ids);
  if (facts && coverage) validateCoverage(coverage, facts, subject, ids);
  if (facts && decomposition) validateDecomposition(decomposition, facts, subject, ids);
  const complete = facts?.status === "complete" && duplication?.status === "complete" && coverage?.status === "complete" && (!profile.decomposition || decomposition?.status === "complete") && !error;
  const dimensions = Object.fromEntries(dimensionKeys.map(key => [key, {
    status: "skipped", basis: profile.scored ? "Not measured by this Python analyzer." : "Not measured by the experimental Python analyzer.", findings: [],
    scope: { id: `python/${key}/unsupported`, coverage: "unsupported", includes: [], excludes: [key] }
  }])) as unknown as Record<typeof dimensionKeys[number], Dimension>;
  dimensions.codeQuality = {
    status: complete ? "skipped" : "failed",
    basis: complete ? profile.decomposition
      ? "Complexity, owned statements, block nesting, duplication and optional coverage/CRAP review evidence; all unscored. Missing coverage and lambda decomposition are unavailable."
      : "Complexity, duplication and optional coverage/CRAP review evidence; all unscored. Complexity preview is experimental. Missing coverage is unavailable."
      : "Python analysis incomplete; partial measurements are diagnostic only.",
    scope: { id: "python/owned-function-complexity-v2", coverage: "partial", includes: ["selected-function-complexity", "exact-function-body-duplication", "optional-line-coverage-crap-review", ...(profile.decomposition ? ["owned-function-statements", "function-block-nesting"] : [])],
      excludes: ["calibrated-dimension-score", ...(profile.decomposition ? [] : ["decomposition"]), "runtime-performance", "semantic-validation"] },
    findings: complete ? [...reviewFindings(facts), ...duplicationFindings(duplication!)] : [],
    duplication, coverageReview: coverage, ...(profile.decomposition ? { decomposition } : {}),
    sourceFacts: facts, experimentalPreview: complete ? complexityDetails(facts) : complexityDetails(null),
    populationNotes: "Units are observed Python files; members are selected implementation bodies. Types are not measured (the required types field is a placeholder zero). Pruned directories do not enumerate unseen files."
  };
  if (profile.scored) dimensions.codeQuality = scoreMethodComplexity(dimensions.codeQuality, facts, complete);
  return { schemaVersion: 3, generatedAtUtc: new Date().toISOString(),
    tool: { name: "CodeMetrics.AI.Python", version: profile.version, ecosystem: "python" }, subject,
    ...measurements(facts), dimensions,
    analysis: { ...ids, status: complete ? "complete" : "incomplete", ruleset: profile.ruleset, calibration: profile.scored ? pythonCalibration : "uncalibrated",
      configurationFingerprint: hash(JSON.stringify([profile.ruleset, facts?.producer, facts?.measurementPolicy, facts?.selection?.configurationFingerprint, duplication?.configurationFingerprint, coverage?.configurationFingerprint, ...(profile.decomposition ? [decomposition?.configurationFingerprint] : [])])),
      diagnostics: [...(facts?.diagnostics ?? []).map(item => ({ kind: item.code, message: item.message, ...(item.file ? { project: item.file } : {}) })),
        ...(duplication?.diagnostics ?? []).filter(item => !facts?.diagnostics.some(other => isDeepStrictEqual(item, other)))
          .map(item => ({ kind: item.code, message: item.message, ...(item.file ? { project: item.file } : {}) })),
        ...(coverage?.diagnostics ?? []).filter(item => !facts?.diagnostics.some(other => isDeepStrictEqual(item, other)))
          .map(item => ({ kind: item.code, message: item.message, ...(item.file ? { project: item.file } : {}) })),
        ...(decomposition?.diagnostics ?? []).filter(item => !facts?.diagnostics.some(other => isDeepStrictEqual(item, other)))
          .map(item => ({ kind: item.code, message: item.message, ...(item.file ? { project: item.file } : {}) })),
        ...(error ? [{ kind: "pythonExecution", message: error }] : [])], suppressions: [] }
  };
}

/** Validate supported scoring and historical review contracts within the shared v3 envelope. */
export function validatePythonEvidence(evidence: Evidence) {
  if (evidence.tool.ecosystem !== "python") return;
  const profile = [currentProfile, decompositionProfile, coverageProfile].find(item => item.ruleset === evidence.analysis.ruleset);
  if (!profile) {
    if ([pythonVersion, decompositionProfile.version, coverageProfile.version].includes(evidence.tool.version) || evidence.dimensions.codeQuality.coverageReview !== undefined || evidence.dimensions.codeQuality.decomposition !== undefined ||
        evidence.dimensions.codeQuality.methodComplexity !== undefined || Object.values(evidence.dimensions).some(item => item.status === "scored"))
      throw new Error("Python review evidence requires its recorded supported ruleset.");
    return; // Historical producers without these extensions retain their original contract.
  }
  if (!profile.decomposition && Object.hasOwn(evidence.dimensions.codeQuality, "decomposition"))
    throw new Error("Historical Python coverage evidence cannot contain decomposition measurements.");
  if (!evidence.analysis.runId || !evidence.analysis.auditId || evidence.analysis.calibration !== (profile.scored ? pythonCalibration : "uncalibrated") ||
      evidence.tool.version !== profile.version || evidence.tool.name !== "CodeMetrics.AI.Python")
    throw new Error("Python evidence requires invocation IDs and matching versioned provenance.");
  if (Object.entries(evidence.dimensions).some(([key, item]) => item.status === "scored" && (!profile.scored || key !== "codeQuality"))) throw new Error("Only supported Python Method Complexity can be scored.");
  const facts = evidence.dimensions.codeQuality.sourceFacts as SourceFacts | null;
  const duplication = evidence.dimensions.codeQuality.duplication as PythonDuplication | null;
  const coverage = evidence.dimensions.codeQuality.coverageReview as PythonCoverage | null;
  const decomposition = profile.decomposition ? evidence.dimensions.codeQuality.decomposition as PythonDecomposition | null : null;
  if (facts === null && (duplication !== null || coverage !== null || decomposition !== null)) throw new Error("Python measurements require source facts.");
  if (facts !== null) {
    validateSourceFacts(facts);
    if (facts.language !== "python" || facts.schemaVersion !== 2 || facts.producer.version !== "0.4.0-experimental" ||
        facts.producer.name !== "codemetrics-ai/python-source-facts" || facts.producer.parser !== "python-ast" ||
        facts.measurementPolicy !== "python/owned-function-complexity-v2" || facts.selection?.policy !== "python/source-selection-v1" ||
        (facts.status === "complete" && !/^3\.12\./.test(facts.producer.parserVersion)))
      throw new Error("Unexpected Python source-facts producer or version.");
    validateDuplication(duplication, facts, evidence.subject, { runId: evidence.analysis.runId, auditId: evidence.analysis.auditId });
    validateCoverage(coverage, facts, evidence.subject, { runId: evidence.analysis.runId, auditId: evidence.analysis.auditId });
    if (profile.decomposition) validateDecomposition(decomposition, facts, evidence.subject, { runId: evidence.analysis.runId, auditId: evidence.analysis.auditId });
  }
  if (evidence.analysis.status === "complete" && (facts?.status !== "complete" || duplication?.status !== "complete" || coverage?.status !== "complete" || (profile.decomposition && decomposition?.status !== "complete"))) throw new Error("Complete Python evidence requires complete measurements.");
  if (evidence.analysis.status === "incomplete" && facts?.status === "complete" && duplication?.status === "complete" && coverage?.status === "complete" && (!profile.decomposition || decomposition?.status === "complete")) throw new Error("Incomplete Python evidence must identify an incomplete measurement.");
  if (evidence.analysis.status === "incomplete" && !evidence.analysis.diagnostics.length) throw new Error("Incomplete Python evidence requires diagnostics.");
  const expected = buildEvidence(profile, facts, evidence.subject, { runId: evidence.analysis.runId, auditId: evidence.analysis.auditId },
    evidence.analysis.status === "incomplete" ? "Validation placeholder" : undefined, duplication, coverage, decomposition);
  for (const key of ["filters", "population", "dimensions"] as const)
    if (!isDeepStrictEqual(evidence[key], expected[key])) throw new Error(`Python ${key} do not match measured facts and experimental policy.`);
  if (evidence.analysis.configurationFingerprint !== expected.analysis.configurationFingerprint)
    throw new Error("Python configuration fingerprint does not match source facts.");
}

/** Historical previews and unavailable populations cannot participate in score comparisons. */
export function supportsPythonComparison(evidence: Evidence) {
  return evidence.tool.version === pythonVersion && evidence.analysis.ruleset === pythonRuleset &&
    evidence.analysis.calibration === pythonCalibration && evidence.dimensions.codeQuality.status === "scored";
}
