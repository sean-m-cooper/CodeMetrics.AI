import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { Ajv2020 } from "ajv/dist/2020.js";
import addFormats from "ajv-formats";
import type { Evidence, Finding } from "./evidence.js";
import { dimensionKeys } from "./scorecard-contract.js";

const ajv = new Ajv2020({ allErrors: true, strict: false });
(addFormats as unknown as (instance: Ajv2020) => void)(ajv);
let validator: ReturnType<Ajv2020["compile"]> | undefined;
export function validateEvidence(value: unknown): asserts value is Evidence {
  if ((value as Evidence)?.schemaVersion !== 3) throw new Error("Schema-v3 evidence is required. Re-run the analyzer; v2 lacks comparison provenance.");
  validator ??= ajv.compile(JSON.parse(fs.readFileSync(new URL("./evidence.schema.v3.json", import.meta.url), "utf8")));
  if (!validator(value)) throw new Error("Invalid evidence: " + ajv.errorsText(validator.errors));
  const evidence = value as Evidence;
  if (evidence.filters.analyzedUnits > evidence.filters.totalUnits) throw new Error("Analyzed units exceed total units.");
  const ids = findings(evidence).map(finding => finding.fingerprint);
  if (new Set(ids).size !== ids.length) throw new Error("Duplicate finding fingerprints.");
  for (const dimension of Object.values(evidence.dimensions)) {
    const decision = dimension.scoringDecision;
    if (!decision) continue; // Older schema-v3 producers remain supported.
    if (decision.finalScore !== dimension.score) throw new Error("Scoring decision differs from dimension score.");
    const steps = (items: typeof decision.steps): typeof decision.steps => items.flatMap(step => [step, ...steps(step.decision?.steps ?? [])]);
    const stepIds = steps(decision.steps).map(step => step.id);
    if (new Set(stepIds).size !== stepIds.length) throw new Error("Duplicate scoring step identifiers.");
    const effectIds = decision.findingEffects.map(effect => effect.fingerprint);
    if (new Set(effectIds).size !== effectIds.length || effectIds.length !== dimension.findings.length)
      throw new Error("Scoring effects must identify each finding exactly once.");
    for (const effect of decision.findingEffects) {
      if (!dimension.findings.some(finding => finding.fingerprint === effect.fingerprint && finding.category === effect.category))
        throw new Error("Scoring effect references an unknown finding.");
      if (effect.stepIds.some(id => !stepIds.includes(id))) throw new Error("Scoring effect references an unknown step.");
    }
  }
}
export function readEvidence(file: string): Evidence {
  const value: unknown = JSON.parse(fs.readFileSync(file, "utf8")); validateEvidence(value); return value;
}
export function findings(evidence: Evidence): Finding[] { return dimensionKeys.flatMap(key => evidence.dimensions[key].findings); }
const ranks: Record<string, number> = { info: 0, warning: 1, error: 2 };
function entryPoint(evidence: Evidence) {
  const root = evidence.subject.root.replaceAll("\\", "/").replace(/\/$/, "") + "/";
  const entry = evidence.subject.entryPoint.replaceAll("\\", "/");
  return entry.startsWith(root) ? entry.slice(root.length) : entry;
}
export function compare(current: Evidence, baseline: Evidence, allowIncompatible = false) {
  const reasons: string[] = [];
  for (const key of ["ecosystem", "version", "name"] as const)
    if (current.tool[key] !== baseline.tool[key]) reasons.push("tool." + key + " differs");
  for (const key of ["ruleset", "configurationFingerprint"] as const)
    if (current.analysis[key] !== baseline.analysis[key]) reasons.push("analysis." + key + " differs");
  for (const key of ["name", "variant"] as const)
    if (current.subject[key] !== baseline.subject[key]) reasons.push("subject." + key + " differs");
  if (entryPoint(current) !== entryPoint(baseline)) reasons.push("subject.entryPoint differs");
  if (current.analysis.status !== "complete" || baseline.analysis.status !== "complete") reasons.push("Analysis is incomplete");
  if (dimensionKeys.some(key => current.dimensions[key].status === "failed" || baseline.dimensions[key].status === "failed")) reasons.push("A dimension failed");
  if (dimensionKeys.some(key => current.dimensions[key].status !== baseline.dimensions[key].status)) reasons.push("Dimension availability differs");
  const scopeIdentity = (scope: Evidence["dimensions"]["codeQuality"]["scope"]) => scope === undefined ? "unknown" :
    JSON.stringify([scope.id, scope.coverage, [...scope.includes].sort(), [...scope.excludes].sort()]);
  if (dimensionKeys.some(key => scopeIdentity(current.dimensions[key].scope) !== scopeIdentity(baseline.dimensions[key].scope))) reasons.push("Dimension scope differs");
  if (reasons.length && !allowIncompatible) throw new Error("Incompatible baseline: " + reasons.join("; "));
  const before = new Map(findings(baseline).map(finding => [finding.fingerprint, finding]));
  const after = new Map(findings(current).map(finding => [finding.fingerprint, finding]));
  const changed = findings(current).filter(finding => before.has(finding.fingerprint) &&
    (before.get(finding.fingerprint)!.severity !== finding.severity || before.get(finding.fingerprint)!.confidence !== finding.confidence));
  return { compatible: reasons.length === 0, reasons,
    currentRun: { runId: current.analysis.runId ?? null, auditId: current.analysis.auditId ?? null },
    baselineRun: { runId: baseline.analysis.runId ?? null, auditId: baseline.analysis.auditId ?? null },
    new: findings(current).filter(finding => !before.has(finding.fingerprint)),
    resolved: findings(baseline).filter(finding => !after.has(finding.fingerprint)), changed,
    severityIncreases: changed.filter(finding => ranks[finding.severity] > ranks[before.get(finding.fingerprint)!.severity]),
    unchanged: findings(current).filter(finding => before.has(finding.fingerprint) && !changed.includes(finding)),
    dimensions: Object.fromEntries(dimensionKeys.map(key => [key, { before: baseline.dimensions[key].score ?? null,
      after: current.dimensions[key].score ?? null,
      delta: reasons.length || current.dimensions[key].score === undefined || baseline.dimensions[key].score === undefined ? null :
        current.dimensions[key].score! - baseline.dimensions[key].score! }])) };
}
export function gate(comparison: ReturnType<typeof compare>, severity?: string, maxScoreDrop?: number): boolean {
  if (!comparison.compatible) throw new Error("Cannot apply quality gates to incompatible evidence.");
  if (severity !== undefined && !(severity in ranks)) throw new Error("--fail-on-new must be info, warning, or error.");
  if (maxScoreDrop !== undefined && (!Number.isFinite(maxScoreDrop) || maxScoreDrop < 0)) throw new Error("--max-score-drop must be a nonnegative number.");
  return (severity !== undefined && [...comparison.new, ...comparison.severityIncreases].some(finding => ranks[finding.severity] >= ranks[severity])) ||
    (maxScoreDrop !== undefined && Object.values(comparison.dimensions).some(dimension => dimension.delta !== null && dimension.delta < -maxScoreDrop));
}
export function sarif(evidence: Evidence) {
  const all = findings(evidence);
  const ids = [...new Set(all.map(finding => finding.ruleId))].sort();
  return { $schema: "https://json.schemastore.org/sarif-2.1.0.json", version: "2.1.0",
    runs: [{ properties: { runId: evidence.analysis.runId ?? null, auditId: evidence.analysis.auditId ?? null },
      tool: { driver: { name: evidence.tool.name, version: evidence.tool.version,
      rules: ids.map(id => ({ id, shortDescription: { text: id.split("/").at(-1)! },
        help: { text: "Review observations and source context. Scores use aggregate dimension policies; findings are not independent deductions." } })) } },
      invocations: [{ workingDirectory: { uri: pathToFileURL(evidence.subject.root + path.sep).href },
        executionSuccessful: evidence.analysis.status === "complete" && !dimensionKeys.some(key => evidence.dimensions[key].status === "failed") }],
      results: all.map(finding => ({ ruleId: finding.ruleId, ruleIndex: ids.indexOf(finding.ruleId),
        level: finding.severity === "info" ? "note" : finding.severity, message: { text: finding.message },
        partialFingerprints: { "codemetrics/v1": finding.fingerprint }, properties: { confidence: finding.confidence, observations: finding.observations },
        ...(finding.file && !finding.file.startsWith("../") && !/^(?:[A-Za-z]:|\/)/.test(finding.file) ? {
          locations: [{ physicalLocation: { artifactLocation: { uri: finding.file.split("/").map(encodeURIComponent).join("/") },
            ...(finding.line && finding.line > 0 ? { region: { startLine: finding.line } } : {}) } }] } : {}) })) }] };
}
