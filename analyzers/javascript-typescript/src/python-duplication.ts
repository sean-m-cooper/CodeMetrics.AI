import { isDeepStrictEqual } from "node:util";
import type { SourceFacts } from "#source-facts/contract.mjs";
import { hash, type Evidence, type Finding } from "./evidence.js";

export const duplicationPolicy = "python/exact-function-body-v1";
interface Location {
  id: string; file: string; member: string; line: number; endLine: number;
  population: string; statementCount: number | null; sourceSha256: string;
}
interface Group {
  id: string; population: string; functionKind: string; bodySha256: string;
  statementCount: number; occurrences: Location[]; disposition: "review-only";
}
export interface PythonDuplication {
  schemaVersion: number; artifactKind: string; producer: { name: string; version: string };
  generatedAtUtc: string; runId: string; auditId: string; status: "complete" | "incomplete";
  policy: string; configuration: Record<string, string | number>; configurationFingerprint: string;
  subject: { root: string; entryPoint: string }; sourceFactsProducer: SourceFacts["producer"];
  sourceFactsPolicy: string; selection: SourceFacts["selection"]; scope: string;
  diagnostics: SourceFacts["diagnostics"]; groups: Group[];
  excludedFunctions: (Location & { reason: string })[];
  summary: { selectedFunctions: number; eligibleFunctions: number; excludedFunctions: number;
    exclusionReasons: Record<string, number>; duplicateGroups: number; matchedFunctions: number } | null;
}

function requireValue(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(`Invalid Python duplication evidence: ${message}.`);
}
function keys(value: unknown, expected: string[]) {
  requireValue(value !== null && typeof value === "object" && !Array.isArray(value), "object required");
  requireValue(isDeepStrictEqual(Object.keys(value).sort(), expected.sort()), "unexpected or missing fields");
}
const hex = (value: unknown) => typeof value === "string" && /^[a-f0-9]{64}$/.test(value);
const integer = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0;

/** Check provenance, population membership and accounting; source equality is a producer measurement. */
export function validateDuplication(value: unknown, facts: SourceFacts, subject: Evidence["subject"], ids: { runId: string; auditId: string }): asserts value is PythonDuplication {
  keys(value, ["schemaVersion", "artifactKind", "producer", "generatedAtUtc", "runId", "auditId", "status", "policy", "configuration",
    "configurationFingerprint", "subject", "sourceFactsProducer", "sourceFactsPolicy", "selection", "diagnostics", "scope", "groups", "excludedFunctions", "summary"]);
  const report = value as PythonDuplication;
  requireValue(report.schemaVersion === 1 && report.artifactKind === "experimental-python-duplication-review" && report.policy === duplicationPolicy, "unsupported policy");
  requireValue(isDeepStrictEqual(report.producer, { name: "codemetrics-ai/python-duplication", version: "0.1.0-experimental" }), "producer mismatch");
  requireValue(report.runId === ids.runId && report.auditId === ids.auditId, "stale invocation identities");
  const target = subject.entryPoint.replaceAll("\\", "/").endsWith("/pyproject.toml") ? subject.root : subject.entryPoint;
  requireValue(isDeepStrictEqual(report.subject, { root: subject.root, entryPoint: target }), "subject mismatch");
  requireValue(isDeepStrictEqual(report.sourceFactsProducer, facts.producer) && report.sourceFactsPolicy === facts.measurementPolicy &&
    isDeepStrictEqual(report.selection, facts.selection), "source selection/provenance mismatch");
  const configuration = { policy: duplicationPolicy, minimumStatements: 5, selectionFingerprint: facts.selection!.configurationFingerprint,
    parserVersion: facts.producer.parserVersion, maxOwners: 100000, maxNormalizedBytes: 67108864 };
  requireValue(isDeepStrictEqual(report.configuration, configuration), "configuration mismatch");
  requireValue(report.configurationFingerprint === hash(JSON.stringify(configuration, Object.keys(configuration).sort())), "configuration fingerprint mismatch");
  requireValue(typeof report.scope === "string" && report.scope.length > 0 && typeof report.generatedAtUtc === "string" &&
    Number.isFinite(Date.parse(report.generatedAtUtc)), "missing scope or generation time");
  requireValue(Array.isArray(report.groups) && Array.isArray(report.excludedFunctions) && Array.isArray(report.diagnostics), "collections required");
  for (const diagnostic of report.diagnostics) {
    keys(diagnostic, diagnostic.file === undefined ? ["code", "message"] : ["code", "message", "file"]);
    requireValue(typeof diagnostic.code === "string" && typeof diagnostic.message === "string" &&
      (diagnostic.file === undefined || typeof diagnostic.file === "string"), "invalid diagnostic");
  }
  if (report.status === "incomplete") {
    requireValue(report.diagnostics.length > 0 && report.groups.length === 0 && report.excludedFunctions.length === 0 && report.summary === null,
      "incomplete results must withhold groups and summary");
    return;
  }
  requireValue(report.status === "complete" && facts.status === "complete" && report.diagnostics.length === 0, "status mismatch");
  const owners = new Map(facts.functions.map(item => [item.id, item]));
  const files = new Map(facts.selection!.files.map(item => [item.file, item]));
  const used = new Set<string>(), groupIds = new Set<string>(), bodies = new Set<string>();
  function location(item: Location, excluded: boolean) {
    keys(item, ["id", "file", "member", "line", "endLine", "population", "statementCount", "sourceSha256", ...(excluded ? ["reason"] : [])]);
    const owner = owners.get(item.id);
    requireValue(owner && !used.has(item.id), "unknown or repeated function");
    used.add(item.id);
    requireValue(item.file === owner.file && item.member === owner.member && item.line === owner.line && item.population === owner.population &&
      item.sourceSha256 === files.get(item.file)?.contentSha256 && integer(item.endLine) && item.endLine >= item.line, "location/source mismatch");
  }
  const reasons: Record<string, number> = {};
  for (const item of report.excludedFunctions) {
    location(item, true);
    requireValue(item.reason === "below-statement-minimum" ? integer(item.statementCount) && item.statementCount < 5 :
      ["lambda", "nested-definition"].includes(item.reason) && item.statementCount === null, "exclusion mismatch");
    reasons[item.reason] = (reasons[item.reason] ?? 0) + 1;
  }
  let matched = 0;
  for (const group of report.groups) {
    keys(group, ["id", "population", "functionKind", "bodySha256", "statementCount", "occurrences", "disposition"]);
    requireValue(hex(group.id) && !groupIds.has(group.id) && hex(group.bodySha256), "invalid or repeated group identity");
    groupIds.add(group.id);
    const body = JSON.stringify([group.population, group.functionKind, group.bodySha256]);
    requireValue(!bodies.has(body), "repeated body group"); bodies.add(body);
    requireValue(["FunctionDef", "AsyncFunctionDef"].includes(group.functionKind) && group.disposition === "review-only" &&
      integer(group.statementCount) && group.statementCount >= 5 && Array.isArray(group.occurrences) && group.occurrences.length >= 2, "invalid group");
    for (const item of group.occurrences) {
      location(item, false);
      requireValue(item.population === group.population && item.statementCount === group.statementCount, "mixed group population/count");
    }
    matched += group.occurrences.length;
  }
  requireValue(isDeepStrictEqual(report.summary, { selectedFunctions: facts.functions.length, eligibleFunctions: facts.functions.length - report.excludedFunctions.length,
    excludedFunctions: report.excludedFunctions.length, exclusionReasons: reasons, duplicateGroups: report.groups.length, matchedFunctions: matched }), "summary accounting mismatch");
}

export function duplicationFindings(report: PythonDuplication): Finding[] {
  return report.groups.map(group => {
    const first = group.occurrences[0];
    return { category: "exactBodyDuplicationReview", ruleId: "python/duplication/exact-function-body-v1",
      fingerprint: group.id, severity: "info", confidence: "high", file: first.file, line: first.line, member: first.member,
      message: `${group.occurrences.length} matching whole-function bodies (${group.statementCount} statements each); review context before suggesting extraction.`,
      observations: { groupId: group.id, policy: report.policy, occurrences: group.occurrences, statementCount: group.statementCount,
        scoringDisposition: "unscored", semanticEquivalence: "not-established" } };
  });
}
