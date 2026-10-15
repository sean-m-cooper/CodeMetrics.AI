import { isDeepStrictEqual } from "node:util";
import { hash } from "./evidence.js";
import type { PythonErrorHandling } from "./python-error-handling.js";

export const contextPolicy = "python/exception-context-patterns-v1";
type Handler = PythonErrorHandling["handlers"][number];
interface Statement {
  kind: string; line: number; endLine: number; literalReturnKind: string | null;
  literalAssignment: { targets: string[]; valueKind: string } | null;
}
interface Suite { statementCount: number; statements: Statement[]; truncated: boolean }
interface Context {
  handlerId: string; file: string; line: number; sourceSha256: string; population: string; owner: Handler["owner"];
  tryStatement: { line: number; endLine: number; kind: string; body: Suite; else: Suite; finally: Suite; handlerLines: number[] };
  handlerBody: Suite; followingInSameSuite: Suite & { suite: string };
  enclosingConstructs: { kind: string; line: number }[];
  surroundingDocumentation: { items: { kind: string; line: number; text: string; truncated: boolean }[]; truncated: boolean };
  classification: { status: string; pattern: string | null; evidenceLines: number[]; unresolvedReasons: string[] };
  disposition: string;
}
export interface PythonErrorContext {
  schemaVersion: number; artifactKind: string; producer: { name: string; version: string }; generatedAtUtc: string;
  runId: string; auditId: string; policy: string; configuration: Record<string, string | number>; configurationFingerprint: string;
  handlerReview: PythonErrorHandling; handlerRowsSha256: string; status: string; diagnostics: PythonErrorHandling["diagnostics"];
  contexts: Context[]; summary: unknown; scope: string;
}
function requireValue(value: unknown, message: string): asserts value {
  if (!value) throw new Error(`Invalid Python exception context: ${message}.`);
}
function keys(value: unknown, names: string[]) {
  requireValue(value !== null && typeof value === "object" && !Array.isArray(value), "object required");
  requireValue(isDeepStrictEqual(Object.keys(value).sort(), names.sort()), "unexpected or missing fields");
}
const integer = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0;
const nonempty = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const literalKinds = ["none", "bool", "int", "float", "complex", "str", "bytes"];
const statementKinds = ["FunctionDef", "AsyncFunctionDef", "ClassDef", "Return", "Delete", "Assign", "TypeAlias", "AugAssign", "AnnAssign", "For", "AsyncFor", "While", "If", "With", "AsyncWith", "Match", "Raise", "Try", "TryStar", "Assert", "Import", "ImportFrom", "Global", "Nonlocal", "Expr", "Pass", "Break", "Continue"];
const patterns = ["locally-documented-no-op", "direct-literal-fallback-return", "direct-bare-rethrow", "guarded-import-shape", "guarded-import-literal-default", "no-op-then-literal-return"];
const reasons = ["exception-group-semantics", "enclosing-finally", "enclosing-context-manager", "nested-handler", "import-exception-binding-unresolved", "no-supported-local-shape"];

// Match Python json.dumps(sort_keys=True, ensure_ascii=True, separators=(",", ":")) for this integer/string contract.
export function fingerprint(value: unknown) {
  const json = JSON.stringify(value, (_key, item) => item !== null && typeof item === "object" && !Array.isArray(item)
    ? Object.fromEntries(Object.entries(item).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0)) : item);
  return hash(json.replace(/[\u007f-\uffff]/g, character => `\\u${character.charCodeAt(0).toString(16).padStart(4, "0")}`));
}
function suite(value: Suite, maximum: number, start: number, end: number, following = false) {
  keys(value, ["statementCount", "statements", "truncated", ...(following ? ["suite"] : [])]);
  requireValue(integer(value.statementCount) && Array.isArray(value.statements) && value.statements.length === Math.min(maximum, value.statementCount) &&
    value.truncated === (value.statementCount > maximum), "suite accounting mismatch");
  for (const [index, item] of value.statements.entries()) {
    keys(item, ["kind", "line", "endLine", "literalReturnKind", "literalAssignment"]);
    requireValue(statementKinds.includes(item.kind) && integer(item.line) && item.line >= start && integer(item.endLine) && item.endLine >= item.line && item.endLine <= end &&
      (!index || item.line >= value.statements[index - 1].endLine), "invalid statement location or kind");
    requireValue(item.literalReturnKind === null || item.kind === "Return" && literalKinds.includes(item.literalReturnKind), "invalid literal return");
    if (item.literalAssignment !== null) {
      keys(item.literalAssignment, ["targets", "valueKind"]);
      requireValue(item.kind === "Assign" && literalKinds.includes(item.literalAssignment.valueKind) && Array.isArray(item.literalAssignment.targets) &&
        item.literalAssignment.targets.length > 0 && item.literalAssignment.targets.every(nonempty), "invalid literal assignment");
    }
  }
}
function documentation(value: Context["surroundingDocumentation"], row: Context) {
  keys(value, ["items", "truncated"]);
  requireValue(Array.isArray(value.items) && typeof value.truncated === "boolean", "documentation required");
  let length = 0;
  for (const item of value.items) {
    keys(item, ["kind", "line", "text", "truncated"]);
    requireValue(["preceding-try-comment", "owner-docstring"].includes(item.kind) && integer(item.line) && item.line > 0 && item.line < row.tryStatement.line &&
      typeof item.text === "string" && item.text.length > 0 && typeof item.truncated === "boolean", "invalid surrounding prose");
    length += [...item.text].length;
  }
  requireValue(length <= 2000 && (!value.items.some(item => item.truncated) || value.truncated), "documentation budget mismatch");
}
function classification(row: Context, handler: Handler) {
  const value = row.classification;
  keys(value, ["status", "pattern", "evidenceLines", "unresolvedReasons"]);
  requireValue(Array.isArray(value.evidenceLines) && value.evidenceLines.length > 0 && value.evidenceLines.every(line => integer(line) && line > 0) &&
    Array.isArray(value.unresolvedReasons) && value.unresolvedReasons.every(reason => reasons.includes(reason)) && new Set(value.unresolvedReasons).size === value.unresolvedReasons.length, "invalid classification evidence");
  const blockers = [
    ...(handler.exceptionGroup ? ["exception-group-semantics"] : []),
    ...(handler.enclosingFinallyLines.length ? ["enclosing-finally"] : []),
    ...(row.enclosingConstructs.some(item => ["With", "AsyncWith"].includes(item.kind)) ? ["enclosing-context-manager"] : []),
    ...(row.enclosingConstructs.some(item => item.kind === "ExceptHandler") ? ["nested-handler"] : [])];
  if (value.status === "unresolved") {
    requireValue(value.pattern === null && value.unresolvedReasons.length > 0 && isDeepStrictEqual(value.evidenceLines, [row.line]) &&
      (blockers.length ? isDeepStrictEqual(value.unresolvedReasons, blockers) : value.unresolvedReasons.length === 1 && ["import-exception-binding-unresolved", "no-supported-local-shape"].includes(value.unresolvedReasons[0])), "unresolved classification mismatch");
    requireValue(handler.classification !== "documented-no-op", "local documentation pattern missing");
    return;
  }
  requireValue(value.status === "recognized-shape" && patterns.includes(value.pattern!) && !value.unresolvedReasons.length, "invalid pattern");
  const first = row.handlerBody.statements[0];
  const imported = row.tryStatement.body;
  let lines: number[] = [];
  if (value.pattern === "locally-documented-no-op") {
    requireValue(handler.classification === "documented-no-op", "documentation pattern mismatch");
    lines = handler.documentation.map(item => item.line);
  } else {
    requireValue(!blockers.length && handler.classification !== "documented-no-op", "pattern bypasses context or documentation precedence");
    if (value.pattern === "direct-literal-fallback-return") {
      requireValue(row.handlerBody.statementCount === 1 && first.literalReturnKind !== null && handler.classification === "terminal-return", "fallback pattern mismatch");
      lines = [row.line, first.line];
    } else if (value.pattern === "direct-bare-rethrow") {
      requireValue(row.handlerBody.statementCount === 1 && first.kind === "Raise" && handler.bodyObservations.raises.length === 1 && handler.bodyObservations.raises[0].bare, "rethrow pattern mismatch");
      lines = [row.line, first.line];
    } else if (value.pattern === "no-op-then-literal-return") {
      requireValue(handler.classification === "undocumented-no-op" && row.followingInSameSuite.statements[0]?.literalReturnKind != null, "fallthrough pattern mismatch");
      lines = [row.line, row.followingInSameSuite.statements[0].line];
    } else {
      requireValue(imported.statementCount === 1 && ["Import", "ImportFrom"].includes(imported.statements[0].kind) &&
        /^(?:ImportError|ModuleNotFoundError|\((?:ImportError|ModuleNotFoundError)(?:, (?:ImportError|ModuleNotFoundError))*,?\))$/.test(handler.exceptionTypeSyntax ?? ""), "import pattern mismatch");
      lines = [imported.statements[0].line, row.line];
      if (value.pattern === "guarded-import-shape") requireValue(handler.classification === "undocumented-no-op", "import no-op mismatch");
      else {
        requireValue(row.handlerBody.statementCount === 1 && first.literalAssignment !== null, "import default mismatch");
        lines.push(first.line);
      }
    }
    lines = [...new Set(lines)].sort((a, b) => a - b);
  }
  requireValue(isDeepStrictEqual(value.evidenceLines, lines), "pattern evidence locations mismatch");
}
function histogram(values: string[]) {
  return Object.fromEntries([...new Set(values)].sort().map(key => [key, values.filter(value => value === key).length]));
}
function summary(rows: Context[]) {
  const counts = (items: Context[]) => ({ handlers: items.length, recognizedShapes: items.filter(row => row.classification.status === "recognized-shape").length,
    unresolved: items.filter(row => row.classification.status === "unresolved").length,
    byPattern: histogram(items.flatMap(row => row.classification.pattern ? [row.classification.pattern] : [])),
    unresolvedReasons: histogram(items.flatMap(row => row.classification.unresolvedReasons)) });
  return { ...counts(rows), populations: Object.fromEntries([...new Set(rows.map(row => row.population))].sort().map(kind => [kind, counts(rows.filter(row => row.population === kind))])) };
}

/** Validate source-bound consistency; syntax is not independently remeasured or authenticated. */
export function validateErrorContext(value: unknown, base: PythonErrorHandling): asserts value is PythonErrorContext {
  keys(value, ["schemaVersion", "artifactKind", "producer", "generatedAtUtc", "runId", "auditId", "policy", "configuration", "configurationFingerprint", "handlerReview", "handlerRowsSha256", "status", "diagnostics", "contexts", "summary", "scope"]);
  const report = value as PythonErrorContext;
  requireValue(report.schemaVersion === 1 && report.artifactKind === "experimental-python-exception-context-review" && report.policy === contextPolicy &&
    isDeepStrictEqual(report.producer, { name: "codemetrics-ai/python-exception-context", version: "0.1.0-experimental" }), "unsupported producer or policy");
  requireValue(report.runId === base.runId && report.auditId === base.auditId && isDeepStrictEqual(report.handlerReview, base) && report.handlerRowsSha256 === fingerprint(base.handlers), "handler provenance mismatch");
  const configuration = { policy: contextPolicy, handlerPolicy: base.policy, maxStatementsPerSuite: 32, maxFollowingStatements: 3,
    maxDocumentationCharacters: 2000, selectionFingerprint: base.selection!.configurationFingerprint, parserVersion: base.sourceFactsProducer.parserVersion };
  requireValue(isDeepStrictEqual(report.configuration, configuration) && report.configurationFingerprint === fingerprint(configuration), "configuration mismatch");
  requireValue(nonempty(report.scope) && typeof report.generatedAtUtc === "string" && Number.isFinite(Date.parse(report.generatedAtUtc)) && Array.isArray(report.contexts) && Array.isArray(report.diagnostics), "missing metadata or collections");
  for (const diagnostic of report.diagnostics) {
    keys(diagnostic, diagnostic.file === undefined ? ["code", "message"] : ["code", "message", "file"]);
    requireValue(nonempty(diagnostic.code) && nonempty(diagnostic.message) && (diagnostic.file === undefined || nonempty(diagnostic.file)), "invalid diagnostic");
  }
  if (report.status === "incomplete") {
    requireValue(report.diagnostics.length > 0 && !report.contexts.length && report.summary === null, "incomplete results must withhold contexts");
    return;
  }
  requireValue(report.status === "complete" && base.status === "complete" && !report.diagnostics.length && report.contexts.length === base.handlers.length, "status or population mismatch");
  const handlers = new Map(base.handlers.map(row => [row.id, row]));
  const used = new Set<string>();
  for (const row of report.contexts) {
    keys(row, ["handlerId", "file", "line", "sourceSha256", "population", "owner", "tryStatement", "handlerBody", "followingInSameSuite", "enclosingConstructs", "surroundingDocumentation", "classification", "disposition"]);
    const handler = handlers.get(row.handlerId);
    requireValue(handler && !used.has(row.handlerId), "missing or repeated handler identity"); used.add(row.handlerId);
    requireValue(row.file === handler.file && row.line === handler.line && row.sourceSha256 === handler.sourceSha256 && row.population === handler.population &&
      isDeepStrictEqual(row.owner, handler.owner) && row.disposition === "review-only", "handler location/source mismatch");
    const attempt = row.tryStatement;
    keys(attempt, ["line", "endLine", "kind", "body", "else", "finally", "handlerLines"]);
    requireValue(integer(attempt.line) && attempt.line > 0 && attempt.line < row.line && integer(attempt.endLine) && attempt.endLine >= handler.endLine &&
      attempt.kind === (handler.exceptionGroup ? "TryStar" : "Try") && Array.isArray(attempt.handlerLines) && attempt.handlerLines.includes(row.line) &&
      attempt.handlerLines.every((line, index, list) => integer(line) && line > attempt.line && line <= attempt.endLine && (!index || line > list[index - 1])), "invalid containing try");
    for (const key of ["body", "else", "finally"] as const) suite(attempt[key], 32, attempt.line, attempt.endLine);
    requireValue(attempt.body.statementCount > 0 && (attempt.finally.statementCount > 0) === handler.enclosingFinallyLines.includes(attempt.line), "try/finally mismatch");
    suite(row.handlerBody, 32, handler.line, handler.endLine); requireValue(row.handlerBody.statementCount > 0, "empty handler suite");
    suite(row.followingInSameSuite, 3, attempt.endLine, Number.MAX_SAFE_INTEGER, true);
    requireValue(["body", "orelse", "finalbody"].includes(row.followingInSameSuite.suite), "invalid containing suite");
    requireValue(Array.isArray(row.enclosingConstructs) && row.enclosingConstructs.every(item => {
      keys(item, ["kind", "line"]); return [...statementKinds, "ExceptHandler"].includes(item.kind) && integer(item.line) && item.line > 0 && item.line < row.line;
    }), "invalid ancestors");
    documentation(row.surroundingDocumentation, row); classification(row, handler);
  }
  requireValue(isDeepStrictEqual(report.summary, summary(report.contexts)), "summary accounting mismatch");
}
