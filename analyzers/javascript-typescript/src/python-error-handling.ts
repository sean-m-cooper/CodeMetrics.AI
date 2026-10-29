import { isDeepStrictEqual } from "node:util";
import type { SourceFacts } from "#source-facts/contract.mjs";
import { hash, type Evidence } from "./evidence.js";
import type { PythonDecomposition } from "./python-decomposition.js";

export const errorHandlingPolicy = "python/exception-handler-observations-v1";
const documentationPolicy = "suite-comments-or-string-statements/tool-directives-excluded/content-not-adjudicated-v1";
const classifications = ["documented-no-op", "undocumented-no-op", "terminal-raise", "terminal-return", "terminal-loop-control", "other-body"];
interface Annotation { kind: string; line: number; text: string }
interface Handler {
  id: string; file: string; line: number; endLine: number; sourceSha256: string; population: string;
  owner: { kind: string; member: string; functionId: string | null };
  exceptionTypeSyntax: string | null; bareExcept: boolean; exceptionGroup: boolean; enclosingFinallyLines: number[];
  classification: string; documentation: Annotation[]; directiveComments: Annotation[]; disposition: string;
  bodyObservations: {
    raises: { line: number; bare: boolean; explicitCause: boolean }[];
    returns: { line: number; hasValue: boolean }[];
    loopTransfers: { line: number; kind: string }[];
    calls: number; nestedHandlersExcluded: number;
  };
}
export interface PythonErrorHandling extends Omit<PythonDecomposition, "functions"> { handlers: Handler[] }
function requireValue(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(`Invalid Python error-handling evidence: ${message}.`);
}
function keys(value: unknown, expected: string[]) {
  requireValue(value !== null && typeof value === "object" && !Array.isArray(value), "object required");
  requireValue(isDeepStrictEqual(Object.keys(value).sort(), expected.sort()), "unexpected or missing fields");
}
const integer = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0;
const nonempty = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
function histogram(values: string[]) {
  return Object.fromEntries([...new Set(values)].sort().map(value => [value, values.filter(item => item === value).length]));
}
function summary(rows: Handler[]) {
  const counts = (items: Handler[]) => ({ handlers: items.length, byClassification: histogram(items.map(item => item.classification)) });
  return { ...counts(rows), functionsWithHandlers: new Set(rows.map(row => row.owner.functionId).filter(Boolean)).size,
    byOwnerKind: histogram(rows.map(row => row.owner.kind)),
    populations: Object.fromEntries([...new Set(rows.map(row => row.population))].sort().map(kind => [kind, counts(rows.filter(row => row.population === kind))])) };
}
function annotations(value: Annotation[], row: Handler, directives: boolean) {
  requireValue(Array.isArray(value), "annotations required");
  for (const item of value) {
    keys(item, ["kind", "line", "text"]);
    requireValue((directives ? ["comment"] : ["comment", "string-statement"]).includes(item.kind) &&
      integer(item.line) && item.line >= row.line && nonempty(item.text) && /[\p{L}\p{N}]/u.test(item.text), "invalid annotation");
    // Trailing suite comments can extend beyond the AST handler's endLine.
    const directive = /^(?:noqa\b|type:\s*ignore\b|pragma:\s*no cover\b|fmt:|pylint:|ruff:)/i.test(item.text);
    requireValue(item.kind !== "comment" || directive === directives, "directive/documentation mismatch");
    requireValue(item.kind !== "string-statement" || item.line <= row.endLine, "string outside handler");
  }
}
function body(row: Handler) {
  const value = row.bodyObservations;
  keys(value, ["raises", "returns", "loopTransfers", "calls", "nestedHandlersExcluded"]);
  requireValue(integer(value.calls) && integer(value.nestedHandlersExcluded), "invalid owned counts");
  for (const [kind, fields] of [["raises", ["line", "bare", "explicitCause"]], ["returns", ["line", "hasValue"]], ["loopTransfers", ["line", "kind"]]] as const) {
    requireValue(Array.isArray(value[kind]), "owned transfers required");
    for (const item of value[kind]) {
      keys(item, [...fields]);
      requireValue(integer(item.line) && item.line >= row.line && item.line <= row.endLine, "transfer outside handler");
      if ("bare" in item) requireValue(typeof item.bare === "boolean" && typeof item.explicitCause === "boolean", "invalid raise");
      if ("hasValue" in item) requireValue(typeof item.hasValue === "boolean", "invalid return");
      if ("kind" in item) requireValue(["break", "continue"].includes(item.kind), "invalid loop transfer");
    }
  }
  const inert = row.classification.endsWith("no-op");
  if (inert) requireValue(!value.raises.length && !value.returns.length && !value.loopTransfers.length && !value.calls && !value.nestedHandlersExcluded, "no-op has active syntax");
  else requireValue(!row.documentation.length && !row.directiveComments.length, "non-inert annotations outside policy");
  requireValue(row.classification !== "documented-no-op" || row.documentation.length > 0, "documented no-op needs annotation");
  requireValue(row.classification !== "undocumented-no-op" || !row.documentation.length, "undocumented no-op has annotation");
  for (const [kind, count] of [["terminal-raise", value.raises.length], ["terminal-return", value.returns.length], ["terminal-loop-control", value.loopTransfers.length]])
    requireValue(row.classification !== kind || Number(count) > 0, "terminal classification lacks transfer");
}

/** Checks provenance and internal consistency, not independent AST remeasurement or authentication. */
export function validateErrorHandling(value: unknown, facts: SourceFacts, subject: Evidence["subject"], ids: { runId: string; auditId: string }): asserts value is PythonErrorHandling {
  keys(value, ["schemaVersion", "artifactKind", "producer", "generatedAtUtc", "runId", "auditId", "status", "policy", "configuration",
    "configurationFingerprint", "subject", "sourceFactsProducer", "sourceFactsPolicy", "selection", "diagnostics", "scope", "handlers", "summary"]);
  const report = value as PythonErrorHandling;
  requireValue(report.schemaVersion === 1 && report.artifactKind === "experimental-python-error-handling-review" && report.policy === errorHandlingPolicy, "unsupported policy");
  requireValue(isDeepStrictEqual(report.producer, { name: "codemetrics-ai/python-error-handling", version: "0.1.0-experimental" }), "producer mismatch");
  requireValue(report.runId === ids.runId && report.auditId === ids.auditId, "stale invocation identities");
  const target = subject.entryPoint.replaceAll("\\", "/").endsWith("/pyproject.toml") ? subject.root : subject.entryPoint;
  requireValue(isDeepStrictEqual(report.subject, { root: subject.root, entryPoint: target }), "subject mismatch");
  requireValue(isDeepStrictEqual(report.sourceFactsProducer, facts.producer) && report.sourceFactsPolicy === facts.measurementPolicy &&
    isDeepStrictEqual(report.selection, facts.selection), "source selection/provenance mismatch");
  const configuration = { policy: errorHandlingPolicy, selectionFingerprint: facts.selection!.configurationFingerprint,
    parserVersion: facts.producer.parserVersion, maxHandlers: 100000, documentationPolicy };
  requireValue(isDeepStrictEqual(report.configuration, configuration), "configuration mismatch");
  requireValue(report.configurationFingerprint === hash(JSON.stringify(configuration, Object.keys(configuration).sort())), "configuration fingerprint mismatch");
  requireValue(nonempty(report.scope) && typeof report.generatedAtUtc === "string" && Number.isFinite(Date.parse(report.generatedAtUtc)), "missing scope or generation time");
  requireValue(Array.isArray(report.handlers) && Array.isArray(report.diagnostics), "collections required");
  for (const diagnostic of report.diagnostics) {
    keys(diagnostic, diagnostic.file === undefined ? ["code", "message"] : ["code", "message", "file"]);
    requireValue(nonempty(diagnostic.code) && nonempty(diagnostic.message) && (diagnostic.file === undefined || nonempty(diagnostic.file)), "invalid diagnostic");
  }
  if (report.status === "incomplete") {
    requireValue(report.diagnostics.length > 0 && report.handlers.length === 0 && report.summary === null, "incomplete results must withhold measurements");
    return;
  }
  requireValue(report.status === "complete" && facts.status === "complete" && !report.diagnostics.length && report.handlers.length <= 100000, "status or budget mismatch");
  const owners = new Map(facts.functions.map(item => [item.id, item]));
  const files = new Map(facts.selection!.files.map(item => [item.file, item]));
  const used = new Set<string>();
  for (const row of report.handlers) {
    keys(row, ["id", "file", "line", "endLine", "sourceSha256", "population", "owner", "exceptionTypeSyntax", "bareExcept", "exceptionGroup",
      "enclosingFinallyLines", "classification", "documentation", "directiveComments", "bodyObservations", "disposition"]);
    const file = files.get(row.file);
    requireValue(typeof row.id === "string" && /^[a-f0-9]{64}$/.test(row.id) && !used.has(row.id), "invalid or repeated handler identity");
    used.add(row.id);
    requireValue(file?.disposition === "analyzed" && row.sourceSha256 === file.contentSha256 && row.population === file.category &&
      integer(row.line) && row.line > 0 && integer(row.endLine) && row.endLine >= row.line && row.disposition === "review-only", "location/source mismatch");
    keys(row.owner, ["kind", "member", "functionId"]);
    requireValue(nonempty(row.owner.member), "owner member required");
    if (row.owner.kind === "function") {
      const owner = owners.get(row.owner.functionId!);
      requireValue(owner && owner.file === row.file && owner.member === row.owner.member && owner.population === row.population && row.line >= owner.line, "function owner mismatch");
    } else requireValue(["module", "class-body"].includes(row.owner.kind) && row.owner.functionId === null &&
      (row.owner.kind !== "module" || row.owner.member === "<module>"), "invalid nonfunction owner");
    requireValue(typeof row.bareExcept === "boolean" && typeof row.exceptionGroup === "boolean" &&
      (row.bareExcept ? row.exceptionTypeSyntax === null && !row.exceptionGroup : nonempty(row.exceptionTypeSyntax)), "exception syntax mismatch");
    requireValue(Array.isArray(row.enclosingFinallyLines) && row.enclosingFinallyLines.every((line, index, lines) =>
      integer(line) && line > 0 && line < row.line && (index === 0 || line > lines[index - 1])), "invalid finally context");
    requireValue(classifications.includes(row.classification), "unknown classification");
    annotations(row.documentation, row, false); annotations(row.directiveComments, row, true); body(row);
  }
  requireValue(isDeepStrictEqual(report.summary, summary(report.handlers)), "summary accounting mismatch");
}
