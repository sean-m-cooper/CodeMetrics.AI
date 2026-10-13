import { isDeepStrictEqual } from "node:util";
import type { SourceFacts } from "#source-facts/contract.mjs";
import { hash, type Evidence } from "./evidence.js";

export const decompositionPolicy = "python/owned-statements-block-nesting-v1";
interface Measurement {
  ownedStatements: number; maxBlockNesting: number;
  statementKinds: Record<string, number>; excludedSyntax: Record<string, number>;
}
interface Owner {
  id: string; file: string; member: string; line: number; endLine: number; population: string;
  sourceSha256: string; functionKind: string; complexity: number; disposition: string;
  status: string; measurement: Measurement | null; reason?: string;
}
export interface PythonDecomposition {
  schemaVersion: number; artifactKind: string; producer: { name: string; version: string };
  generatedAtUtc: string; runId: string; auditId: string; status: string; policy: string;
  configuration: Record<string, string | number>; configurationFingerprint: string;
  subject: { root: string; entryPoint: string }; sourceFactsProducer: SourceFacts["producer"];
  sourceFactsPolicy: string; selection: SourceFacts["selection"]; scope: string;
  diagnostics: SourceFacts["diagnostics"]; functions: Owner[]; summary: unknown;
}
const blocks = ["If", "For", "AsyncFor", "While", "With", "AsyncWith", "Try", "TryStar", "Match"];
const statements = [...blocks, "Return", "Delete", "Assign", "AugAssign", "AnnAssign", "Raise", "Assert", "Import", "ImportFrom", "Expr", "Break", "Continue"];
const excluded = ["FunctionDef", "AsyncFunctionDef", "ClassDef", "Lambda", "Pass", "Global", "Nonlocal", "TypeAlias", "AnnAssign", "Expr", "docstring"];
function requireValue(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(`Invalid Python decomposition evidence: ${message}.`);
}
function keys(value: unknown, expected: string[]) {
  requireValue(value !== null && typeof value === "object" && !Array.isArray(value), "object required");
  requireValue(isDeepStrictEqual(Object.keys(value).sort(), expected.sort()), "unexpected or missing fields");
}
const integer = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0;
function histogram(value: Record<string, number>, allowed: string[]) {
  requireValue(value !== null && typeof value === "object" && !Array.isArray(value), "histogram required");
  const entries = Object.entries(value);
  requireValue(entries.every(([key, count]) => allowed.includes(key) && integer(count) && count > 0), "invalid histogram");
  const total = entries.reduce((sum, [, count]) => sum + count, 0);
  requireValue(integer(total), "histogram overflow");
  return total;
}
function distribution(values: number[]) {
  if (!values.length) return null;
  values.sort((a, b) => a - b);
  const rank = (percent: number) => values[Math.ceil(percent * values.length / 100) - 1];
  return { count: values.length, p50: rank(50), p90: rank(90), p95: rank(95), max: values.at(-1) };
}
function summary(rows: Owner[]) {
  const measured = rows.filter(row => row.status === "measured");
  const populations = Object.fromEntries([...new Set(rows.map(row => row.population))].map(population => {
    const selected = rows.filter(row => row.population === population);
    const eligible = selected.filter(row => row.status === "measured");
    return [population, { selectedFunctions: selected.length, measuredFunctions: eligible.length,
      unavailableFunctions: selected.length - eligible.length,
      ownedStatements: distribution(eligible.map(row => row.measurement!.ownedStatements)),
      maxBlockNesting: distribution(eligible.map(row => row.measurement!.maxBlockNesting)) }];
  }));
  const unavailable = rows.length - measured.length;
  return { selectedFunctions: rows.length, measuredFunctions: measured.length, unavailableFunctions: unavailable,
    unavailableReasons: unavailable ? { "lambda-expression-body": unavailable } : {}, populations };
}

/** Validate provenance and internal accounting; AST measurements remain the producer's observations. */
export function validateDecomposition(value: unknown, facts: SourceFacts, subject: Evidence["subject"], ids: { runId: string; auditId: string }): asserts value is PythonDecomposition {
  keys(value, ["schemaVersion", "artifactKind", "producer", "generatedAtUtc", "runId", "auditId", "status", "policy", "configuration",
    "configurationFingerprint", "subject", "sourceFactsProducer", "sourceFactsPolicy", "selection", "diagnostics", "scope", "functions", "summary"]);
  const report = value as PythonDecomposition;
  requireValue(report.schemaVersion === 1 && report.artifactKind === "experimental-python-decomposition-review" && report.policy === decompositionPolicy, "unsupported policy");
  requireValue(isDeepStrictEqual(report.producer, { name: "codemetrics-ai/python-decomposition", version: "0.1.0-experimental" }), "producer mismatch");
  requireValue(report.runId === ids.runId && report.auditId === ids.auditId, "stale invocation identities");
  const target = subject.entryPoint.replaceAll("\\", "/").endsWith("/pyproject.toml") ? subject.root : subject.entryPoint;
  requireValue(isDeepStrictEqual(report.subject, { root: subject.root, entryPoint: target }), "subject mismatch");
  requireValue(isDeepStrictEqual(report.sourceFactsProducer, facts.producer) && report.sourceFactsPolicy === facts.measurementPolicy &&
    isDeepStrictEqual(report.selection, facts.selection), "source selection/provenance mismatch");
  const configuration = { policy: decompositionPolicy, selectionFingerprint: facts.selection!.configurationFingerprint,
    parserVersion: facts.producer.parserVersion, maxOwners: 100000, quantiles: "nearest-rank", complexityPolicy: facts.measurementPolicy };
  requireValue(isDeepStrictEqual(report.configuration, configuration), "configuration mismatch");
  requireValue(report.configurationFingerprint === hash(JSON.stringify(configuration, Object.keys(configuration).sort())), "configuration fingerprint mismatch");
  requireValue(typeof report.scope === "string" && report.scope.length > 0 && typeof report.generatedAtUtc === "string" &&
    Number.isFinite(Date.parse(report.generatedAtUtc)), "missing scope or generation time");
  requireValue(Array.isArray(report.functions) && Array.isArray(report.diagnostics), "collections required");
  for (const diagnostic of report.diagnostics) {
    keys(diagnostic, diagnostic.file === undefined ? ["code", "message"] : ["code", "message", "file"]);
    requireValue(typeof diagnostic.code === "string" && typeof diagnostic.message === "string" &&
      (diagnostic.file === undefined || typeof diagnostic.file === "string"), "invalid diagnostic");
  }
  if (report.status === "incomplete") {
    requireValue(report.diagnostics.length > 0 && report.functions.length === 0 && report.summary === null, "incomplete results must withhold measurements");
    return;
  }
  requireValue(report.status === "complete" && facts.status === "complete" && !report.diagnostics.length && report.functions.length <= 100000, "status or budget mismatch");
  const owners = new Map(facts.functions.map(item => [item.id, item]));
  const files = new Map(facts.selection!.files.map(item => [item.file, item]));
  const used = new Set<string>();
  for (const row of report.functions) {
    keys(row, ["id", "file", "member", "line", "endLine", "population", "sourceSha256", "functionKind", "complexity", "disposition", "status", "measurement",
      ...(row.status === "unavailable" ? ["reason"] : [])]);
    const owner = owners.get(row.id);
    requireValue(owner && !used.has(row.id), "unknown or repeated function");
    used.add(row.id);
    requireValue(row.file === owner.file && row.member === owner.member && row.line === owner.line && row.population === owner.population &&
      row.sourceSha256 === files.get(row.file)?.contentSha256 && row.complexity === owner.complexity?.value &&
      integer(row.endLine) && row.endLine >= row.line && row.disposition === "review-only", "location/source mismatch");
    if (row.functionKind === "Lambda") {
      requireValue(row.status === "unavailable" && row.measurement === null && row.reason === "lambda-expression-body", "lambda must be unavailable");
      continue;
    }
    requireValue(["FunctionDef", "AsyncFunctionDef"].includes(row.functionKind) && row.status === "measured", "function measurement required");
    keys(row.measurement, ["ownedStatements", "maxBlockNesting", "statementKinds", "excludedSyntax"]);
    const measurement = row.measurement!;
    requireValue(integer(measurement.ownedStatements) && integer(measurement.maxBlockNesting), "invalid measurement");
    requireValue(histogram(measurement.statementKinds, statements) === measurement.ownedStatements, "statement accounting mismatch");
    histogram(measurement.excludedSyntax, excluded);
    requireValue((measurement.excludedSyntax.docstring ?? 0) <= 1, "repeated leading docstring");
    const blockCount = blocks.reduce((sum, kind) => sum + (measurement.statementKinds[kind] ?? 0), 0);
    requireValue(measurement.maxBlockNesting <= blockCount && (measurement.maxBlockNesting === 0) === (blockCount === 0), "block accounting mismatch");
  }
  requireValue(used.size === owners.size, "missing selected functions");
  requireValue(isDeepStrictEqual(report.summary, summary(report.functions)), "summary accounting mismatch");
}
