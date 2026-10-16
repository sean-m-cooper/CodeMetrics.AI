import { validateAsyncUsage, asyncUsagePolicy, type PythonAsyncUsage } from "./python-async-usage.js";
import { isDeepStrictEqual } from "node:util";
import { complexityPopulation } from "#source-facts/population.mjs";
import { validateSourceFacts, type SourceFacts } from "#source-facts/contract.mjs";
import { dimensionKeys } from "./scorecard-contract.js";
import { hash, type Evidence, type Dimension, type Finding } from "./evidence.js";
import { duplicationFindings, validateDuplication, type PythonDuplication } from "./python-duplication.js";
import { validateCoverage, type PythonCoverage } from "./python-coverage.js";
import { validateDecomposition, type PythonDecomposition } from "./python-decomposition.js";
import { validateErrorHandling, errorHandlingPolicy, type PythonErrorHandling } from "./python-error-handling.js";
import { validateErrorContext, type PythonErrorContext } from "./python-error-context.js";
import { validateOperations, type PythonOperations } from "./python-error-operations.js";
import { validateTransfers, type PythonTransfers } from "./python-error-transfers.js";
import { scoreMethodComplexity, pythonCalibration } from "./python-complexity.js";

export const pythonVersion = "0.12.0";
export const pythonRuleset = "python-2026-10-16-async-usage-review-v1";
const currentProfile = { version: pythonVersion, ruleset: pythonRuleset, decomposition: true, scored: true, errorHandling: true, context: true, transfers: true, operations: true, asyncUsage: true };
const operationProfile = { version: "0.11.0", ruleset: "python-2026-10-15-exception-operations-review-v1", decomposition: true, scored: true, errorHandling: true, context: true, transfers: true, operations: true, asyncUsage: false };
const transferProfile = { version: "0.10.0", ruleset: "python-2026-10-15-exception-transfers-review-v1", decomposition: true, scored: true, errorHandling: true, context: true, transfers: true, operations: false, asyncUsage: false };
const contextProfile = { version: "0.9.0", ruleset: "python-2026-10-15-exception-context-review-v1", decomposition: true, scored: true, errorHandling: true, context: true, transfers: false, operations: false, asyncUsage: false };
const handlerProfile = { version: "0.8.0", ruleset: "python-2026-10-15-error-handling-review-v1", decomposition: true, scored: true, errorHandling: true, context: false, transfers: false, operations: false, asyncUsage: false };
const complexityProfile = { version: "0.7.0", ruleset: "python-2026-10-13-method-complexity-v1", decomposition: true, scored: true, errorHandling: false, context: false, transfers: false, operations: false, asyncUsage: false };
const decompositionProfile = { version: "0.6.0-experimental", ruleset: "python-2026-10-13-decomposition-preview", decomposition: true, scored: false, errorHandling: false, context: false, transfers: false, operations: false, asyncUsage: false };
const coverageProfile = { version: "0.5.0-experimental", ruleset: "python-2026-10-12-coverage-preview", decomposition: false, scored: false, errorHandling: false, context: false, transfers: false, operations: false, asyncUsage: false };
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

export function pythonEvidence(facts: SourceFacts | null, subject: Evidence["subject"], ids: { runId: string; auditId: string }, error?: string, duplication: PythonDuplication | null = null, coverage: PythonCoverage | null = null, decomposition: PythonDecomposition | null = null, errorHandling: PythonErrorHandling | null = null, context: PythonErrorContext | null = null, transfers: PythonTransfers | null = null, operations: PythonOperations | null = null, asyncReview: PythonAsyncUsage | null = null): Evidence {
  return buildEvidence(currentProfile, facts, subject, ids, error, duplication, coverage, decomposition, errorHandling, context, transfers, operations, asyncReview);
}

function buildEvidence(profile: typeof currentProfile, facts: SourceFacts | null, subject: Evidence["subject"], ids: { runId: string; auditId: string }, error: string | undefined, duplication: PythonDuplication | null, coverage: PythonCoverage | null, decomposition: PythonDecomposition | null, errorHandling: PythonErrorHandling | null, context: PythonErrorContext | null, transfers: PythonTransfers | null, operations: PythonOperations | null, asyncReview: PythonAsyncUsage | null): Evidence {
  if (facts) validateSourceFacts(facts);
  if (facts && duplication) validateDuplication(duplication, facts, subject, ids);
  if (facts && coverage) validateCoverage(coverage, facts, subject, ids);
  if (facts && decomposition) validateDecomposition(decomposition, facts, subject, ids);
  if (facts && errorHandling) validateErrorHandling(errorHandling, facts, subject, ids);
  if (context && errorHandling) validateErrorContext(context, errorHandling);
  if (transfers && context) validateTransfers(transfers, context);
  if (operations && transfers) validateOperations(operations, transfers);
  if (asyncReview && facts && decomposition) validateAsyncUsage(asyncReview, facts, subject, ids, decomposition);
  const complete = facts?.status === "complete" && duplication?.status === "complete" && coverage?.status === "complete" && (!profile.decomposition || decomposition?.status === "complete") && (!profile.errorHandling || errorHandling?.status === "complete") && (!profile.context || context?.status === "complete") && (!profile.transfers || transfers?.status === "complete") && (!profile.operations || operations?.status === "complete") && (!profile.asyncUsage || asyncReview?.status === "complete") && !error;
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
  if (profile.errorHandling) dimensions.errorHandling = {
    status: complete ? "skipped" : "failed", basis: complete
      ? "Exception-handler syntax and local documentation are review evidence only; no handling-quality score."
      : "Python analysis incomplete; handler observations are diagnostic only.",
    scope: { id: errorHandlingPolicy, coverage: "partial", includes: ["selected-exception-suites", "module-and-class-body-handlers", "local-no-op-documentation", ...(profile.context ? ["bounded-lexical-context", "conservative-handler-patterns"] : []), ...(profile.transfers ? ["owned-raise-cause-syntax", "conservative-raise-patterns"] : []), ...(profile.operations ? ["owned-call-return-syntax", "unresolved-suite-inventory"] : [])],
      excludes: ["calibrated-dimension-score", "resolved-exception-types", "business-correctness", "context-manager-suppression", "path-completeness"] },
    findings: [], review: errorHandling, ...(profile.context ? { contextReview: context } : {}), ...(profile.transfers ? { transferReview: transfers } : {}), ...(profile.operations ? { operationReview: operations } : {})
  };
  if (profile.asyncUsage) dimensions.performanceAsync = {
    status: complete ? "skipped" : "failed", basis: complete
      ? "Async-owned calls, import-matched blocking candidates, and thread-helper syntax are unscored review evidence. Unresolved calls are not clean-async evidence."
      : "Python analysis incomplete; async observations are diagnostic only.",
    scope: { id: asyncUsagePolicy, coverage: "partial", includes: ["selected-async-bodies", "import-spelling-matches", "thread-helper-syntax", "separate-deferred-generator-observations"],
      excludes: ["calibrated-dimension-score", "synchronous-owners", "runtime-performance", "resolved-receiver-types", "offloading-success", "complete-blocking-detection"] },
    findings: [], review: asyncReview
  };
  return { schemaVersion: 3, generatedAtUtc: new Date().toISOString(),
    tool: { name: "CodeMetrics.AI.Python", version: profile.version, ecosystem: "python" }, subject,
    ...measurements(facts), dimensions,
    analysis: { ...ids, status: complete ? "complete" : "incomplete", ruleset: profile.ruleset, calibration: profile.scored ? pythonCalibration : "uncalibrated",
      configurationFingerprint: hash(JSON.stringify([profile.ruleset, facts?.producer, facts?.measurementPolicy, facts?.selection?.configurationFingerprint, duplication?.configurationFingerprint, coverage?.configurationFingerprint, ...(profile.decomposition ? [decomposition?.configurationFingerprint] : []), ...(profile.errorHandling ? [errorHandling?.configurationFingerprint] : []), ...(profile.context ? [context?.configurationFingerprint] : []), ...(profile.transfers ? [transfers?.configurationFingerprint] : []), ...(profile.operations ? [operations?.configurationFingerprint] : []), ...(profile.asyncUsage ? [asyncReview?.configurationFingerprint] : [])])),
      diagnostics: [...(facts?.diagnostics ?? []).map(item => ({ kind: item.code, message: item.message, ...(item.file ? { project: item.file } : {}) })),
        ...(duplication?.diagnostics ?? []).filter(item => !facts?.diagnostics.some(other => isDeepStrictEqual(item, other)))
          .map(item => ({ kind: item.code, message: item.message, ...(item.file ? { project: item.file } : {}) })),
        ...(coverage?.diagnostics ?? []).filter(item => !facts?.diagnostics.some(other => isDeepStrictEqual(item, other)))
          .map(item => ({ kind: item.code, message: item.message, ...(item.file ? { project: item.file } : {}) })),
        ...(decomposition?.diagnostics ?? []).filter(item => !facts?.diagnostics.some(other => isDeepStrictEqual(item, other)))
          .map(item => ({ kind: item.code, message: item.message, ...(item.file ? { project: item.file } : {}) })),
        ...(errorHandling?.diagnostics ?? []).filter(item => !facts?.diagnostics.some(other => isDeepStrictEqual(item, other)))
          .map(item => ({ kind: item.code, message: item.message, ...(item.file ? { project: item.file } : {}) })),
        ...(context?.diagnostics ?? []).filter(item => !facts?.diagnostics.some(other => isDeepStrictEqual(item, other)))
          .map(item => ({ kind: item.code, message: item.message, ...(item.file ? { project: item.file } : {}) })),
        ...(transfers?.diagnostics ?? []).filter(item => !facts?.diagnostics.some(other => isDeepStrictEqual(item, other)))
          .map(item => ({ kind: item.code, message: item.message, ...(item.file ? { project: item.file } : {}) })),
        ...(operations?.diagnostics ?? []).filter(item => !facts?.diagnostics.some(other => isDeepStrictEqual(item, other)))
          .map(item => ({ kind: item.code, message: item.message, ...(item.file ? { project: item.file } : {}) })),
        ...(asyncReview?.diagnostics ?? []).filter(item => !facts?.diagnostics.some(other => isDeepStrictEqual(item, other)))
          .map(item => ({ kind: item.code, message: item.message, ...(item.file ? { project: item.file } : {}) })),
        ...(error ? [{ kind: "pythonExecution", message: error }] : [])], suppressions: [] }
  };
}

/** Validate supported scoring and historical review contracts within the shared v3 envelope. */
export function validatePythonEvidence(evidence: Evidence) {
  if (evidence.tool.ecosystem !== "python") return;
  const profile = [currentProfile, operationProfile, transferProfile, contextProfile, handlerProfile, complexityProfile, decompositionProfile, coverageProfile].find(item => item.ruleset === evidence.analysis.ruleset);
  if (!profile) {
    if ([pythonVersion, operationProfile.version, transferProfile.version, contextProfile.version, handlerProfile.version, complexityProfile.version, decompositionProfile.version, coverageProfile.version].includes(evidence.tool.version) || evidence.dimensions.codeQuality.coverageReview !== undefined || evidence.dimensions.codeQuality.decomposition !== undefined ||
        evidence.dimensions.performanceAsync.review !== undefined || evidence.dimensions.errorHandling.operationReview !== undefined || evidence.dimensions.errorHandling.transferReview !== undefined || evidence.dimensions.errorHandling.contextReview !== undefined || evidence.dimensions.errorHandling.review !== undefined || evidence.dimensions.codeQuality.methodComplexity !== undefined || Object.values(evidence.dimensions).some(item => item.status === "scored"))
      throw new Error("Python review evidence requires its recorded supported ruleset.");
    return; // Historical producers without these extensions retain their original contract.
  }
  if (!profile.asyncUsage && Object.hasOwn(evidence.dimensions.performanceAsync, "review"))
    throw new Error("Historical Python evidence cannot contain async measurements.");
  if (!profile.operations && Object.hasOwn(evidence.dimensions.errorHandling, "operationReview"))
    throw new Error("Historical Python evidence cannot contain call/return measurements.");
  if (!profile.transfers && Object.hasOwn(evidence.dimensions.errorHandling, "transferReview"))
    throw new Error("Historical Python evidence cannot contain raise/chaining measurements.");
  if (!profile.context && Object.hasOwn(evidence.dimensions.errorHandling, "contextReview"))
    throw new Error("Historical Python evidence cannot contain exception context measurements.");
  if (!profile.errorHandling && Object.hasOwn(evidence.dimensions.errorHandling, "review"))
    throw new Error("Historical Python evidence cannot contain handler measurements.");
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
  const errorHandling = profile.errorHandling ? evidence.dimensions.errorHandling.review as PythonErrorHandling | null : null;
  const context = profile.context ? evidence.dimensions.errorHandling.contextReview as PythonErrorContext | null : null;
  const transfers = profile.transfers ? evidence.dimensions.errorHandling.transferReview as PythonTransfers | null : null;
  const operations = profile.operations ? evidence.dimensions.errorHandling.operationReview as PythonOperations | null : null;
  const asyncReview = profile.asyncUsage ? evidence.dimensions.performanceAsync.review as PythonAsyncUsage | null : null;
  if (facts === null && (duplication !== null || coverage !== null || decomposition !== null || errorHandling !== null || context !== null || transfers !== null || operations !== null || asyncReview !== null)) throw new Error("Python measurements require source facts.");
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
    if (profile.errorHandling) validateErrorHandling(errorHandling, facts, evidence.subject, { runId: evidence.analysis.runId, auditId: evidence.analysis.auditId });
    if (profile.context) validateErrorContext(context, errorHandling!);
    if (profile.transfers) validateTransfers(transfers, context!);
    if (profile.operations) validateOperations(operations, transfers!);
    if (profile.asyncUsage) validateAsyncUsage(asyncReview, facts, evidence.subject, { runId: evidence.analysis.runId, auditId: evidence.analysis.auditId }, decomposition!);
  }
  if (evidence.analysis.status === "complete" && (facts?.status !== "complete" || duplication?.status !== "complete" || coverage?.status !== "complete" || (profile.decomposition && decomposition?.status !== "complete") || (profile.errorHandling && errorHandling?.status !== "complete") || (profile.context && context?.status !== "complete") || (profile.transfers && transfers?.status !== "complete") || (profile.operations && operations?.status !== "complete") || (profile.asyncUsage && asyncReview?.status !== "complete"))) throw new Error("Complete Python evidence requires complete measurements.");
  if (evidence.analysis.status === "incomplete" && facts?.status === "complete" && duplication?.status === "complete" && coverage?.status === "complete" && (!profile.decomposition || decomposition?.status === "complete") && (!profile.errorHandling || errorHandling?.status === "complete") && (!profile.context || context?.status === "complete") && (!profile.transfers || transfers?.status === "complete") && (!profile.operations || operations?.status === "complete") && (!profile.asyncUsage || asyncReview?.status === "complete")) throw new Error("Incomplete Python evidence must identify an incomplete measurement.");
  if (evidence.analysis.status === "incomplete" && !evidence.analysis.diagnostics.length) throw new Error("Incomplete Python evidence requires diagnostics.");
  const expected = buildEvidence(profile, facts, evidence.subject, { runId: evidence.analysis.runId, auditId: evidence.analysis.auditId },
    evidence.analysis.status === "incomplete" ? "Validation placeholder" : undefined, duplication, coverage, decomposition, errorHandling, context, transfers, operations, asyncReview);
  for (const key of ["filters", "population", "dimensions"] as const)
    if (!isDeepStrictEqual(evidence[key], expected[key])) throw new Error(`Python ${key} do not match measured facts and experimental policy.`);
  if (evidence.analysis.configurationFingerprint !== expected.analysis.configurationFingerprint)
    throw new Error("Python configuration fingerprint does not match source facts.");
}

/** Historical previews and unavailable populations cannot participate in score comparisons. */
export function supportsPythonComparison(evidence: Evidence) {
  return [currentProfile, operationProfile, transferProfile, contextProfile, handlerProfile, complexityProfile].some(profile => evidence.tool.version === profile.version && evidence.analysis.ruleset === profile.ruleset) &&
    evidence.analysis.calibration === pythonCalibration && evidence.dimensions.codeQuality.status === "scored";
}
