import { isDeepStrictEqual } from "node:util";
import type { SourceFacts } from "#source-facts/contract.mjs";
import { hash, type Evidence } from "./evidence.js";

const policy = "python/coveragepy-line-crap-review-v1";
const formula = "CC^2 * (1 - coveredLines / executableLines)^3 + CC";
type ObjectValue = Record<string, unknown>;
interface CoverageRow {
  id: string; file: string; member: string; line: number; endLine: number; population: string;
  sourceSha256: string; complexity: number;
  coverage: { status: string; reason?: string; basis?: string; coveredLines?: number; executableLines?: number;
    executedLines?: number[]; missingLines?: number[]; excludedLines?: number[]; fraction?: number };
  crapEstimate: { value: number; disposition: string; basis: string; formula: string } | null;
}
export interface PythonCoverage {
  schemaVersion: number; artifactKind: string; producer: { name: string; version: string };
  generatedAtUtc: string; runId: string; auditId: string; status: string; policy: string;
  configuration: ObjectValue; configurationFingerprint: string; subject: { root: string; entryPoint: string };
  sourceFactsProducer: SourceFacts["producer"]; selection: SourceFacts["selection"];
  coverageInput: ObjectValue | null; collectionProvenance: ObjectValue | null;
  diagnostics: SourceFacts["diagnostics"]; scope: string; functions: CoverageRow[]; summary: ObjectValue | null;
}
function requireValue(value: unknown, message: string): asserts value {
  if (!value) throw new Error(`Invalid Python coverage evidence: ${message}.`);
}
function object(value: unknown): asserts value is ObjectValue {
  requireValue(value !== null && typeof value === "object" && !Array.isArray(value), "expected object");
}
function keys(value: unknown, fields: string[]) {
  object(value); requireValue(isDeepStrictEqual(Object.keys(value).sort(), [...fields].sort()), "unexpected or missing fields");
}
const text = (value: unknown): value is string => typeof value === "string" && value.length > 0;
const hex = (value: unknown) => typeof value === "string" && /^[a-f0-9]{64}$/.test(value);
const integer = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0;
const timestamp = (value: unknown): value is string => text(value) && Number.isFinite(Date.parse(value));
const uuid = (value: unknown) => typeof value === "string" && /^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(value);
const relative = (value: unknown): value is string => text(value) && !value.includes("\\") && !value.includes(":") && !value.startsWith("/") && value.split("/").every(part => part && part !== "." && part !== "..");
function sameRoot(value: unknown, expected: string) {
  if (!text(value)) return false;
  const normalize = (name: string) => {
    const result = name.replaceAll("\\", "/").replace(/\/$/, "");
    return /^[a-z]:\//i.test(result) ? result.toLowerCase() : result;
  };
  return normalize(value) === normalize(expected);
}

/** Python's sorted, ASCII JSON convention used for source snapshot digests. */
function canonical(value: unknown): string {
  if (Array.isArray(value)) return `[${value.map(canonical).join(",")}]`;
  if (value !== null && typeof value === "object") return `{${Object.entries(value).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0)
    .map(([key, item]) => `${canonical(key)}:${canonical(item)}`).join(",")}}`;
  return JSON.stringify(value).replace(/[\u007f-\uffff]/g, char => `\\u${char.charCodeAt(0).toString(16).padStart(4, "0")}`);
}

function collection(value: unknown, input: ObjectValue | null, facts: SourceFacts, subject: Evidence["subject"]) {
  keys(value, ["path", "sha256", "collectionId", "policy", "startedAtUtc", "finishedAtUtc", "sourceBinding", "freshness", "command", "receipt", "limitations"]);
  const bound = value as ObjectValue;
  requireValue(input && text(bound.path) && hex(bound.sha256) && uuid(bound.collectionId) && bound.policy === "python/coverage-collection-v1" &&
    bound.sourceBinding === "matched-pre-post-snapshots" && bound.freshness === "source-matched" && text(bound.limitations), "invalid collection binding");
  keys(bound.receipt, ["schemaVersion", "policy", "collectionId", "startedAtUtc", "finishedAtUtc", "root", "status", "configuration", "sourceSelection",
    "beforeSourceSha256", "afterSourceSha256", "sourceFactsPolicy", "sourceFactsProducer", "reportSha256", "command", "runtime", "testExitCode", "diagnostics"]);
  const receipt = bound.receipt as ObjectValue;
  requireValue(receipt.schemaVersion === 1 && receipt.status === "complete" && receipt.testExitCode === 0 &&
    sameRoot(receipt.root, subject.root) && receipt.reportSha256 === input.sha256 && input.branchDataPresent === true &&
    isDeepStrictEqual(receipt.diagnostics, []), "failed or mismatched collection receipt");
  for (const key of ["policy", "collectionId", "startedAtUtc", "finishedAtUtc", "command"])
    requireValue(isDeepStrictEqual(bound[key], receipt[key]), "collection summary disagrees with receipt");
  requireValue(timestamp(receipt.startedAtUtc) && timestamp(receipt.finishedAtUtc) &&
    /(?:Z|[+-]\d\d:\d\d)$/.test(receipt.startedAtUtc) && /(?:Z|[+-]\d\d:\d\d)$/.test(receipt.finishedAtUtc) &&
    Date.parse(receipt.finishedAtUtc) >= Date.parse(receipt.startedAtUtc), "invalid collection times");
  requireValue(Array.isArray(receipt.command) && receipt.command.length > 0 && receipt.command.every(text), "missing collection command");
  requireValue(isDeepStrictEqual(receipt.configuration, { branch: true, configFile: false, processScope: "single-process", freshBytecodeCache: true }), "collection configuration mismatch");
  keys(receipt.runtime, ["coverageVersion", "pythonVersion", "testExitCode"]);
  const runtime = receipt.runtime as ObjectValue;
  requireValue(runtime.coverageVersion === input.version && text(runtime.pythonVersion) && /^3\.12\./.test(runtime.pythonVersion) && runtime.testExitCode === 0, "collection runtime mismatch");
  const selected = facts.selection!;
  const snapshot = { policy: selected.policy, configurationFingerprint: selected.configurationFingerprint, options: selected.options,
    files: selected.files.filter(row => row.disposition === "analyzed"), excludedFunctions: selected.excludedFunctions };
  const digest = hash(canonical(snapshot));
  requireValue(receipt.beforeSourceSha256 === digest && receipt.afterSourceSha256 === digest && isDeepStrictEqual(receipt.sourceSelection, snapshot) &&
    receipt.sourceFactsPolicy === facts.measurementPolicy && isDeepStrictEqual(receipt.sourceFactsProducer, facts.producer), "collection source snapshot mismatch");
}

function inputs(report: PythonCoverage, facts: SourceFacts, subject: Evidence["subject"]) {
  const input = report.coverageInput;
  if (input !== null) {
    keys(input, ["path", "sha256", "producer", "version", "format", "reportedAt", "branchDataPresent", "sourceBinding", "freshness"]);
    requireValue(text(input.path) && hex(input.sha256) && input.producer === "coverage.py" && text(input.version) &&
      [2, 3].includes(input.format as number) && typeof input.reportedAt === "string" && typeof input.branchDataPresent === "boolean", "invalid coverage input");
    // Failed reviews may retain verified receipt diagnostics, but never promote input status.
    const bound = report.status === "complete" && report.collectionProvenance !== null;
    requireValue(input.sourceBinding === (bound ? "matched-pre-post-snapshots" : "unverified") &&
      input.freshness === (bound ? "source-matched" : "unverified"), "unjustified coverage freshness");
  }
  if (report.collectionProvenance !== null) collection(report.collectionProvenance, input, facts, subject);
}

function lines(value: unknown, row: CoverageRow): asserts value is number[] {
  requireValue(Array.isArray(value) && value.every((line, index) => integer(line) && line > row.line && line <= row.endLine &&
    (index === 0 || line > value[index - 1])), "invalid body line attribution");
}

function observations(row: CoverageRow, input: ObjectValue | null, bound: boolean) {
  const coverage = row.coverage;
  if (coverage.status === "unavailable") {
    keys(coverage, ["status", "reason", ...(coverage.excludedLines === undefined ? [] : ["excludedLines"])]);
    requireValue(row.crapEstimate === null && (input === null ? coverage.reason === "coverage-not-supplied" :
      ["lambda-line-attribution", "body-shares-definition-line", "body-shares-docstring-line", "nested-definition", "no-reportable-body-lines", "file-not-in-report"].includes(coverage.reason!)), "invalid unavailable reason");
    if (coverage.excludedLines !== undefined) { lines(coverage.excludedLines, row); requireValue(coverage.reason === "no-reportable-body-lines", "unexpected excluded lines"); }
    return;
  }
  keys(coverage, ["status", "basis", "coveredLines", "executableLines", "executedLines", "missingLines", "excludedLines", "fraction"]);
  requireValue(input !== null && coverage.status === (bound ? "reported-source-matched" : "reported-unverified") && coverage.basis === "executable-line", "unjustified coverage status");
  lines(coverage.executedLines, row); lines(coverage.missingLines, row); lines(coverage.excludedLines, row);
  const all = [...coverage.executedLines, ...coverage.missingLines, ...coverage.excludedLines];
  const covered = coverage.executedLines.length, total = covered + coverage.missingLines.length;
  requireValue(new Set(all).size === all.length && total > 0 && coverage.coveredLines === covered && coverage.executableLines === total &&
    coverage.fraction === covered / total, "invalid coverage arithmetic");
  keys(row.crapEstimate, ["value", "disposition", "basis", "formula"]);
  const estimate = row.crapEstimate!;
  const expected = row.complexity ** 2 * ((total - covered) / total) ** 3 + row.complexity;
  requireValue(Number.isFinite(estimate.value) && Math.abs(estimate.value - expected) <= 1e-12 * Math.max(1, expected) &&
    estimate.disposition === (bound ? "review-only" : "conditional-review-only") && estimate.basis === "line-coverage-proxy" && estimate.formula === formula, "invalid CRAP estimate");
}

/** Offline integrity checks; this does not rerun tests or authenticate unsigned receipts. */
export function validateCoverage(value: unknown, facts: SourceFacts, subject: Evidence["subject"], ids: { runId: string; auditId: string }): asserts value is PythonCoverage {
  keys(value, ["schemaVersion", "artifactKind", "producer", "generatedAtUtc", "runId", "auditId", "policy", "configuration", "configurationFingerprint", "status",
    "subject", "sourceFactsProducer", "selection", "coverageInput", "collectionProvenance", "diagnostics", "scope", "functions", "summary"]);
  const report = value as PythonCoverage;
  requireValue(report.schemaVersion === 2 && report.artifactKind === "experimental-python-coverage-review" && report.policy === policy &&
    isDeepStrictEqual(report.producer, { name: "codemetrics-ai/python-coverage-review", version: "0.3.0-experimental" }), "unsupported coverage producer or policy");
  requireValue(report.runId === ids.runId && report.auditId === ids.auditId, "stale coverage identities");
  const target = subject.entryPoint.replaceAll("\\", "/").endsWith("/pyproject.toml") ? subject.root : subject.entryPoint;
  requireValue(isDeepStrictEqual(report.subject, { root: subject.root, entryPoint: target }) && isDeepStrictEqual(report.selection, facts.selection) &&
    isDeepStrictEqual(report.sourceFactsProducer, facts.producer), "source selection/provenance mismatch");
  const configuration = { policy, selectionFingerprint: facts.selection!.configurationFingerprint, complexityPolicy: facts.measurementPolicy,
    parserVersion: facts.producer.parserVersion, maxReportBytes: 33554432, maxOwners: 100000 };
  requireValue(isDeepStrictEqual(report.configuration, configuration) && report.configurationFingerprint === hash(canonical(configuration)), "configuration mismatch");
  requireValue(timestamp(report.generatedAtUtc) && text(report.scope) && Array.isArray(report.diagnostics) && Array.isArray(report.functions), "missing report metadata");
  for (const item of report.diagnostics) {
    keys(item, ["code", "message", ...(item.file === undefined ? [] : ["file"])]);
    requireValue(text(item.code) && typeof item.message === "string" && (item.file === undefined || text(item.file)), "invalid diagnostic");
  }
  inputs(report, facts, subject);
  if (report.status === "incomplete") {
    requireValue(report.functions.length === 0 && report.summary === null && report.diagnostics.length > 0, "incomplete review must withhold estimates");
    return;
  }
  requireValue(report.status === "complete" && facts.status === "complete" && report.diagnostics.length === 0, "status mismatch");
  const owners = new Map(facts.functions.map(row => [row.id, row])), used = new Set<string>(), usedLines = new Set<string>();
  const files = new Map(facts.selection!.files.map(row => [row.file, row]));
  const reasons: Record<string, number> = {};
  let estimates = 0;
  for (const row of report.functions) {
    keys(row, ["id", "file", "member", "line", "population", "endLine", "complexity", "coverage", "crapEstimate", "sourceSha256"]);
    const owner = owners.get(row.id);
    requireValue(owner && !used.has(row.id) && row.file === owner.file && row.member === owner.member && row.line === owner.line &&
      row.population === owner.population && row.complexity === owner.complexity?.value && row.sourceSha256 === files.get(row.file)?.contentSha256 &&
      integer(row.endLine) && row.endLine >= row.line, "unknown, repeated, or mismatched function");
    used.add(row.id); object(row.coverage);
    observations(row, report.coverageInput, report.collectionProvenance !== null);
    if (row.crapEstimate === null) reasons[row.coverage.reason!] = (reasons[row.coverage.reason!] ?? 0) + 1;
    else {
      estimates++;
      for (const line of [...row.coverage.executedLines!, ...row.coverage.missingLines!]) {
        const key = `${row.file}:${line}`; requireValue(!usedLines.has(key), "coverage line credited to multiple owners"); usedLines.add(key);
      }
    }
  }
  requireValue(used.size === owners.size, "incomplete function population");
  keys(report.summary, ["selectedFunctions", "conditionalEstimates", "sourceBoundEstimates", "unavailableFunctions", "unavailableReasons", "matchedFiles", "unmatchedReportFiles"]);
  const summary = report.summary!;
  requireValue(summary.selectedFunctions === owners.size && summary.conditionalEstimates === (report.collectionProvenance ? 0 : estimates) &&
    summary.sourceBoundEstimates === (report.collectionProvenance ? estimates : 0) && summary.unavailableFunctions === owners.size - estimates &&
    isDeepStrictEqual(summary.unavailableReasons, reasons), "summary accounting mismatch");
  requireValue(integer(summary.matchedFiles) && summary.matchedFiles <= facts.selection!.files.filter(file => file.disposition === "analyzed").length &&
    Array.isArray(summary.unmatchedReportFiles) && summary.unmatchedReportFiles.every(file => relative(file) && files.get(file)?.disposition !== "analyzed") &&
    new Set(summary.unmatchedReportFiles).size === summary.unmatchedReportFiles.length, "invalid file accounting");
  if (report.coverageInput === null) requireValue(summary.matchedFiles === 0 && summary.unmatchedReportFiles.length === 0, "missing coverage cannot contain file observations");
}
