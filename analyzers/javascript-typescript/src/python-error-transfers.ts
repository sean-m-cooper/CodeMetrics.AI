import { isDeepStrictEqual } from "node:util";
import { hash } from "./evidence.js";
import { fingerprint, type PythonErrorContext } from "./python-error-context.js";

const policy = "python/exception-raise-chaining-v1";
type Context = PythonErrorContext["contexts"][number];
interface Expression { kind: string; astKind: string | null; symbol: string | null; matchesHandlerNameSyntax: boolean }
interface Raise {
  id: string; line: number; column: number; endLine: number; direct: boolean; exception: Expression; cause: Expression;
  containsNamedExpression: boolean; containers: { kind: string; line: number }[]; containersTruncated: boolean;
}
interface Handler extends Pick<Context, "handlerId" | "file" | "line" | "sourceSha256" | "population" | "owner" | "disposition"> {
  handlerNameSyntax: string | null; raises: Raise[];
  originalClassification: Context["classification"]; classification: Context["classification"];
}
export interface PythonTransfers extends Omit<PythonErrorContext, "handlerReview" | "handlerRowsSha256" | "contexts"> {
  contextReview: PythonErrorContext; contextRowsSha256: string; handlers: Handler[];
}
function requireValue(value: unknown, message: string): asserts value {
  if (!value) throw new Error(`Invalid Python raise/chaining evidence: ${message}.`);
}
function keys(value: unknown, names: string[]) {
  requireValue(value !== null && typeof value === "object" && !Array.isArray(value), "object required");
  requireValue(isDeepStrictEqual(Object.keys(value).sort(), names.sort()), "unexpected or missing fields");
}
const integer = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0;
const nonempty = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const name = (value: unknown): value is string => typeof value === "string" && /^[_\p{ID_Start}][_\p{ID_Continue}]*$/u.test(value);
function expression(value: Expression, handlerName: string | null) {
  keys(value, ["kind", "astKind", "symbol", "matchesHandlerNameSyntax"]);
  requireValue(["absent", "none", "name", "attribute", "call", "other"].includes(value.kind) && typeof value.matchesHandlerNameSyntax === "boolean", "invalid expression kind");
  const expected = { absent: null, none: "Constant", name: "Name", attribute: "Attribute", call: "Call" };
  requireValue(value.kind === "other" ? nonempty(value.astKind) && !["Name", "Attribute", "Call"].includes(value.astKind)
    : value.astKind === expected[value.kind as keyof typeof expected], "AST kind mismatch");
  if (value.symbol !== null) {
    requireValue(["name", "attribute", "call"].includes(value.kind) && typeof value.symbol === "string" && [...value.symbol].length <= 200 &&
      value.symbol.split(".").length <= 9 && value.symbol.split(".").every(name), "invalid symbol path");
    requireValue(value.kind !== "name" || !value.symbol.includes("."), "name cannot be a path");
    requireValue(value.kind !== "attribute" || value.symbol.includes("."), "attribute needs a path");
  }
  // Long names intentionally have no bounded symbol, but can still match the original handler name.
  requireValue(!value.matchesHandlerNameSyntax || value.kind === "name" && handlerName !== null && (value.symbol === null ? [...handlerName].length > 200 : value.symbol === handlerName), "handler-name correspondence mismatch");
  if (value.kind === "name" && value.symbol !== null) requireValue(value.matchesHandlerNameSyntax === (value.symbol === handlerName), "handler-name match omitted");
}
function classification(row: Handler, original: Context) {
  const prior = original.classification;
  if (prior.status === "recognized-shape" || !isDeepStrictEqual(prior.unresolvedReasons, ["no-supported-local-shape"]) ||
      original.handlerBody.statementCount !== 1 || original.handlerBody.statements[0].kind !== "Raise") return prior;
  requireValue(row.raises.length === 1 && row.raises[0].direct, "single raise suite mismatch");
  const item = row.raises[0]; let pattern: string | null = null;
  if (!item.containsNamedExpression && item.exception.symbol !== null) {
    if (item.cause.kind === "none") pattern = "direct-raise-from-none";
    else if (item.cause.matchesHandlerNameSyntax) pattern = "direct-raise-from-handler-name";
    else if (item.cause.kind === "absent") pattern = item.exception.matchesHandlerNameSyntax ? "direct-handler-name-raise" : "direct-symbol-or-call-raise";
  }
  return pattern ? { status: "recognized-shape", pattern, evidenceLines: [...new Set([row.line, item.line])].sort((a, b) => a - b), unresolvedReasons: [] }
    : { status: "unresolved", pattern: null, evidenceLines: [row.line], unresolvedReasons: ["raise-expression-or-cause-unresolved"] };
}
const histogram = (values: string[]) => Object.fromEntries([...new Set(values)].sort().map(key => [key, values.filter(value => value === key).length]));
function summary(rows: Handler[]) {
  const counts = (items: Handler[]) => {
    const raises = items.flatMap(row => row.raises);
    return { handlers: items.length, recognizedShapes: items.filter(row => row.classification.status === "recognized-shape").length,
      unresolved: items.filter(row => row.classification.status === "unresolved").length,
      byPattern: histogram(items.flatMap(row => row.classification.pattern ? [row.classification.pattern] : [])),
      unresolvedReasons: histogram(items.flatMap(row => row.classification.unresolvedReasons)), raiseStatements: raises.length,
      handlersWithRaises: items.filter(row => row.raises.length).length,
      newlyRecognizedShapes: items.filter(row => row.originalClassification.status === "unresolved" && row.classification.status === "recognized-shape").length,
      byExceptionSyntax: histogram(raises.map(row => row.exception.kind)), byCauseSyntax: histogram(raises.map(row => row.cause.kind)),
      handlerNameRaiseSyntax: raises.filter(row => row.exception.matchesHandlerNameSyntax).length,
      handlerNameCauseSyntax: raises.filter(row => row.cause.matchesHandlerNameSyntax).length };
  };
  return { ...counts(rows), populations: Object.fromEntries([...new Set(rows.map(row => row.population))].sort().map(kind => [kind, counts(rows.filter(row => row.population === kind))])) };
}

/** Internal/source-bound consistency only, not AST remeasurement or authentication. Base must be validated first. */
export function validateTransfers(value: unknown, base: PythonErrorContext): asserts value is PythonTransfers {
  keys(value, ["schemaVersion", "artifactKind", "producer", "generatedAtUtc", "runId", "auditId", "policy", "configuration", "configurationFingerprint", "contextReview", "contextRowsSha256", "status", "diagnostics", "handlers", "summary", "scope"]);
  const report = value as PythonTransfers;
  requireValue(report.schemaVersion === 1 && report.artifactKind === "experimental-python-exception-transfer-review" && report.policy === policy &&
    isDeepStrictEqual(report.producer, { name: "codemetrics-ai/python-exception-transfers", version: "0.1.0-experimental" }), "unsupported producer or policy");
  requireValue(report.runId === base.runId && report.auditId === base.auditId && isDeepStrictEqual(report.contextReview, base) && report.contextRowsSha256 === fingerprint(base.contexts), "context provenance mismatch");
  const configuration = { policy, contextPolicy: base.policy, contextConfigurationFingerprint: base.configurationFingerprint,
    maxRaises: 100000, maxSymbolCharacters: 200, maxContainers: 32, maxSymbolAttributes: 8 };
  requireValue(isDeepStrictEqual(report.configuration, configuration) && report.configurationFingerprint === fingerprint(configuration), "configuration mismatch");
  requireValue(nonempty(report.scope) && typeof report.generatedAtUtc === "string" && Number.isFinite(Date.parse(report.generatedAtUtc)) && Array.isArray(report.handlers) && Array.isArray(report.diagnostics), "invalid metadata or collections");
  for (const item of report.diagnostics) {
    keys(item, item.file === undefined ? ["code", "message"] : ["code", "message", "file"]);
    requireValue(nonempty(item.code) && nonempty(item.message) && (item.file === undefined || nonempty(item.file)), "invalid diagnostic");
  }
  if (report.status === "incomplete") {
    requireValue(report.diagnostics.length > 0 && !report.handlers.length && report.summary === null, "incomplete report must withhold rows"); return;
  }
  requireValue(report.status === "complete" && base.status === "complete" && !report.diagnostics.length && report.handlers.length === base.contexts.length, "status or population mismatch");
  const contexts = new Map(base.contexts.map(row => [row.handlerId, row]));
  const handlers = new Map(base.handlerReview.handlers.map(row => [row.id, row]));
  const used = new Set<string>(), raiseIds = new Set<string>();
  for (const row of report.handlers) {
    keys(row, ["handlerId", "file", "line", "sourceSha256", "population", "owner", "handlerNameSyntax", "raises", "originalClassification", "classification", "disposition"]);
    const original = contexts.get(row.handlerId), handler = handlers.get(row.handlerId);
    requireValue(original && handler && !used.has(row.handlerId), "missing or repeated handler identity"); used.add(row.handlerId);
    requireValue(["file", "line", "sourceSha256", "population", "owner"].every(key => isDeepStrictEqual(row[key as keyof Handler], original[key as keyof Context])) &&
      row.disposition === "review-only" && isDeepStrictEqual(row.originalClassification, original.classification) &&
      (row.handlerNameSyntax === null || name(row.handlerNameSyntax)) && Array.isArray(row.raises), "handler/context mismatch");
    for (const [index, item] of row.raises.entries()) {
      keys(item, ["id", "line", "column", "endLine", "direct", "exception", "cause", "containsNamedExpression", "containers", "containersTruncated"]);
      requireValue(integer(item.line) && item.line >= row.line && integer(item.endLine) && item.endLine >= item.line && item.endLine <= handler.endLine && integer(item.column) &&
        (!index || item.line > row.raises[index - 1].line || item.line === row.raises[index - 1].line && item.column > row.raises[index - 1].column), "invalid raise location/order");
      requireValue(item.id === hash(`${row.file}|${item.line}|${item.column}|Raise`) && !raiseIds.has(item.id), "raise identity mismatch"); raiseIds.add(item.id);
      expression(item.exception, row.handlerNameSyntax); expression(item.cause, row.handlerNameSyntax);
      requireValue(item.exception.kind !== "absent" || item.cause.kind === "absent", "bare raise cannot have a cause");
      requireValue(typeof item.direct === "boolean" && typeof item.containsNamedExpression === "boolean" && typeof item.containersTruncated === "boolean" &&
        Array.isArray(item.containers) && item.containers.length <= 32 && (!item.containersTruncated || item.containers.length === 32) && item.direct === !item.containers.length, "invalid container accounting");
      for (const [position, container] of item.containers.entries()) {
        keys(container, ["kind", "line"]);
        requireValue(["If", "For", "AsyncFor", "While", "Try", "TryStar", "With", "AsyncWith", "Match"].includes(container.kind) && integer(container.line) &&
          container.line >= row.line && container.line <= item.line && (!position || container.line <= item.containers[position - 1].line), "invalid enclosing statement");
      }
      const direct = original.handlerBody.statements.find(statement => statement.kind === "Raise" && statement.line === item.line && statement.endLine === item.endLine);
      requireValue(!direct || item.direct, "direct raise marked nested");
      if (item.direct && !original.handlerBody.truncated) requireValue(direct, "direct raise absent from suite");
    }
    const projected = row.raises.map(item => ({ line: item.line, bare: item.exception.kind === "absent", explicitCause: item.cause.kind !== "absent" }));
    requireValue(isDeepStrictEqual(projected, handler.bodyObservations.raises), "owned raises disagree with original observations");
    requireValue(isDeepStrictEqual(row.classification, classification(row, original)), "classification mismatch");
  }
  requireValue(raiseIds.size <= 100000 && isDeepStrictEqual(report.summary, summary(report.handlers)), "summary or budget mismatch");
}
