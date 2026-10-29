import { isDeepStrictEqual } from "node:util";
import { hash } from "./evidence.js";
import { fingerprint, type PythonErrorContext } from "./python-error-context.js";
import { expression, type Expression, type PythonTransfers } from "./python-error-transfers.js";

const policy = "python/exception-call-return-v1";
const loggingNames = ["critical", "debug", "error", "exception", "fatal", "info", "log", "warn", "warning"];
type Transfer = PythonTransfers["handlers"][number];
interface Location { id: string; line: number; column: number; endLine: number; endColumn: number; ancestors: { kind: string; line: number }[]; ancestorsTruncated: boolean; containsNamedExpression: boolean }
interface Call extends Location { callee: Expression; loggingNameSyntax: string | null; directStatement: boolean; immediatelyAwaited: boolean; positionalArguments: number; keywordArguments: number; starredArguments: number; keywordUnpacks: number }
interface Return extends Location { direct: boolean; expression: Expression; awaitedExpression: Expression | null }
interface Handler extends Pick<Transfer, "handlerId" | "file" | "line" | "sourceSha256" | "population" | "owner" | "originalClassification" | "classification" | "disposition"> {
  calls: Call[]; returns: Return[]; suite: PythonErrorContext["contexts"][number]["handlerBody"];
}
export interface PythonOperations extends Omit<PythonTransfers, "contextReview" | "contextRowsSha256" | "handlers"> {
  transferReview: PythonTransfers; transferRowsSha256: string; handlers: Handler[];
}
function requireValue(value: unknown, message: string): asserts value {
  if (!value) throw new Error(`Invalid Python call/return evidence: ${message}.`);
}
function keys(value: unknown, names: string[]) {
  requireValue(value !== null && typeof value === "object" && !Array.isArray(value), "object required");
  requireValue(isDeepStrictEqual(Object.keys(value).sort(), names.sort()), "unexpected or missing fields");
}
const integer = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0;
const nonempty = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const histogram = (values: string[]) => Object.fromEntries([...new Set(values)].sort().map(key => [key, values.filter(value => value === key).length]));
function inventory(rows: Handler[]) {
  const items = rows.filter(row => row.originalClassification.status === "unresolved");
  const shapes = new Map<string, { kinds: string[]; truncated: boolean; handlers: number }>();
  for (const row of items) {
    const kinds = row.suite.statements.map(item => item.kind), truncated = row.suite.truncated;
    const key = JSON.stringify([kinds, truncated]);
    const entry = shapes.get(key) ?? { kinds, truncated, handlers: 0 }; entry.handlers++; shapes.set(key, entry);
  }
  const compareKinds = (left: string[], right: string[]) => {
    for (let index = 0; index < Math.min(left.length, right.length); index++)
      if (left[index] !== right[index]) return left[index] < right[index] ? -1 : 1;
    return left.length - right.length;
  };
  return { handlers: items.length, singleStatement: items.filter(row => row.suite.statementCount === 1).length,
    multiStatement: items.filter(row => row.suite.statementCount > 1).length, withCalls: items.filter(row => row.calls.length).length,
    withReturns: items.filter(row => row.returns.length).length, withLoggingNameSyntax: items.filter(row => row.calls.some(call => call.loggingNameSyntax)).length,
    byReason: histogram(items.flatMap(row => row.originalClassification.unresolvedReasons)),
    suiteShapes: [...shapes.values()].sort((a, b) => b.handlers - a.handlers || compareKinds(a.kinds, b.kinds) || Number(a.truncated) - Number(b.truncated)) };
}
function summary(rows: Handler[]) {
  const counts = (items: Handler[]) => {
    const calls = items.flatMap(row => row.calls), returns = items.flatMap(row => row.returns);
    return { handlers: items.length, recognizedShapes: items.filter(row => row.classification.status === "recognized-shape").length,
      unresolved: items.filter(row => row.classification.status === "unresolved").length,
      byPattern: histogram(items.flatMap(row => row.classification.pattern ? [row.classification.pattern] : [])),
      unresolvedReasons: histogram(items.flatMap(row => row.classification.unresolvedReasons)), callExpressions: calls.length, returnStatements: returns.length,
      handlersWithCalls: items.filter(row => row.calls.length).length, handlersWithReturns: items.filter(row => row.returns.length).length,
      loggingNameSyntaxCalls: calls.filter(row => row.loggingNameSyntax !== null).length, immediatelyAwaitedCalls: calls.filter(row => row.immediatelyAwaited).length,
      byReturnExpression: histogram(returns.map(row => row.expression.kind)),
      newlyRecognizedShapes: items.filter(row => row.originalClassification.status === "unresolved" && row.classification.status === "recognized-shape").length,
      inputUnresolvedInventory: inventory(items) };
  };
  return { ...counts(rows), populations: Object.fromEntries([...new Set(rows.map(row => row.population))].sort().map(kind => [kind, counts(rows.filter(row => row.population === kind))])) };
}
function classification(row: Handler, prior: Transfer) {
  if (prior.classification.status === "recognized-shape" || !isDeepStrictEqual(prior.classification.unresolvedReasons, ["no-supported-local-shape"]) ||
      row.calls.some(call => call.containsNamedExpression) || row.returns.some(item => item.containsNamedExpression)) return prior.classification;
  let pattern: string | null = null; let lines: number[] = [];
  const returned = row.returns.find(item => item.direct);
  if (row.suite.statementCount === 1 && returned && ["name", "attribute", "call"].includes(returned.expression.kind) && returned.expression.symbol !== null) {
    pattern = "direct-symbol-or-call-return"; lines = [returned.line];
  } else {
    const calls = row.calls.filter(call => call.directStatement);
    const raises = prior.raises.filter(item => item.direct);
    const rethrow = raises.length === 1 && raises[0].exception.kind === "absent" && row.suite.statementCount > 1;
    if (calls.length > 0 && calls.length + Number(rethrow) === row.suite.statementCount && !returned &&
        calls.every(call => !call.immediatelyAwaited && ["name", "attribute"].includes(call.callee.kind) && call.callee.symbol !== null) &&
        (!rethrow || calls.every(call => call.line < raises[0].line || call.line === raises[0].line && call.column < raises[0].column))) {
      pattern = rethrow ? "direct-calls-then-bare-raise" : "direct-call-statement-sequence";
      lines = calls.map(call => call.ancestors[0].line).concat(rethrow ? [raises[0].line] : []);
    }
  }
  return pattern ? { status: "recognized-shape", pattern, evidenceLines: [...new Set([row.line, ...lines])].sort((a, b) => a - b), unresolvedReasons: [] } : prior.classification;
}
const locationKeys = ["id", "line", "column", "endLine", "endColumn", "ancestors", "ancestorsTruncated", "containsNamedExpression"];
function location(item: Location, previous: Location | undefined, row: Handler, endLine: number, kind: string, seen: Set<string>) {
  requireValue(integer(item.line) && item.line >= row.line && integer(item.column) && integer(item.endLine) && item.endLine >= item.line && item.endLine <= endLine && integer(item.endColumn) && (item.endLine > item.line || item.endColumn > item.column) &&
    (!previous || item.line > previous.line || item.line === previous.line && item.column > previous.column || item.line === previous.line && item.column === previous.column && (item.endLine < previous.endLine || item.endLine === previous.endLine && item.endColumn < previous.endColumn)), "invalid location/order");
  requireValue(item.id === hash(`${row.file}|${item.line}|${item.column}|${item.endLine}|${item.endColumn}|${kind}`) && !seen.has(item.id), "operation identity mismatch"); seen.add(item.id);
  requireValue(typeof item.containsNamedExpression === "boolean" && typeof item.ancestorsTruncated === "boolean" && Array.isArray(item.ancestors) &&
    item.ancestors.length <= 32 && (!item.ancestorsTruncated || item.ancestors.length === 32), "invalid ancestor accounting");
  for (const [index, ancestor] of item.ancestors.entries()) {
    keys(ancestor, ["kind", "line"]);
    requireValue(typeof ancestor.kind === "string" && /^[A-Z][A-Za-z]*$/.test(ancestor.kind) && integer(ancestor.line) && ancestor.line >= row.line &&
      ancestor.line <= item.line && (!index || ancestor.line <= item.ancestors[index - 1].line), "invalid ancestor metadata");
  }
}

/** Base must already be validated. Checks consistency, not independent AST measurement or authenticity. */
export function validateOperations(value: unknown, base: PythonTransfers): asserts value is PythonOperations {
  keys(value, ["schemaVersion", "artifactKind", "producer", "generatedAtUtc", "runId", "auditId", "policy", "configuration", "configurationFingerprint", "transferReview", "transferRowsSha256", "status", "diagnostics", "handlers", "summary", "scope"]);
  const report = value as PythonOperations;
  requireValue(report.schemaVersion === 1 && report.artifactKind === "experimental-python-exception-operation-review" && report.policy === policy &&
    isDeepStrictEqual(report.producer, { name: "codemetrics-ai/python-exception-operations", version: "0.2.0-experimental" }), "unsupported producer/policy");
  requireValue(report.runId === base.runId && report.auditId === base.auditId && isDeepStrictEqual(report.transferReview, base) && report.transferRowsSha256 === fingerprint(base.handlers), "transfer provenance mismatch");
  const configuration = { policy, transferPolicy: base.policy, transferConfigurationFingerprint: base.configurationFingerprint,
    maxOperations: 100000, maxAncestors: 32, maxSuiteStatements: 32, maxSymbolCharacters: 200, maxSymbolAttributes: 8, loggingNames };
  requireValue(isDeepStrictEqual(report.configuration, configuration) && report.configurationFingerprint === fingerprint(configuration), "configuration mismatch");
  requireValue(nonempty(report.scope) && typeof report.generatedAtUtc === "string" && Number.isFinite(Date.parse(report.generatedAtUtc)) && Array.isArray(report.handlers) && Array.isArray(report.diagnostics), "invalid metadata/collections");
  for (const item of report.diagnostics) {
    keys(item, item.file === undefined ? ["code", "message"] : ["code", "message", "file"]);
    requireValue(nonempty(item.code) && nonempty(item.message) && (item.file === undefined || nonempty(item.file)), "invalid diagnostic");
  }
  if (report.status === "incomplete") {
    requireValue(report.diagnostics.length > 0 && !report.handlers.length && report.summary === null, "incomplete report must withhold rows"); return;
  }
  requireValue(report.status === "complete" && base.status === "complete" && !report.diagnostics.length && report.handlers.length === base.handlers.length, "status/population mismatch");
  const prior = new Map(base.handlers.map(row => [row.handlerId, row]));
  const contexts = new Map(base.contextReview.contexts.map(row => [row.handlerId, row]));
  const originals = new Map(base.contextReview.handlerReview.handlers.map(row => [row.id, row]));
  const used = new Set<string>(), seen = new Set<string>();
  for (const row of report.handlers) {
    keys(row, ["handlerId", "file", "line", "sourceSha256", "population", "owner", "calls", "returns", "suite", "originalClassification", "classification", "disposition"]);
    const old = prior.get(row.handlerId), context = contexts.get(row.handlerId), original = originals.get(row.handlerId);
    requireValue(old && context && original && !used.has(row.handlerId), "missing or repeated handler"); used.add(row.handlerId);
    requireValue(["file", "line", "sourceSha256", "population", "owner"].every(key => isDeepStrictEqual(row[key as keyof Handler], old[key as keyof Transfer])) &&
      row.disposition === "review-only" && isDeepStrictEqual(row.originalClassification, old.classification) && isDeepStrictEqual(row.suite, context.handlerBody) &&
      Array.isArray(row.calls) && Array.isArray(row.returns), "handler provenance or suite mismatch");
    for (const [index, call] of row.calls.entries()) {
      keys(call, [...locationKeys, "callee", "loggingNameSyntax", "directStatement", "immediatelyAwaited", "positionalArguments", "keywordArguments", "starredArguments", "keywordUnpacks"]);
      location(call, row.calls[index - 1], row, original.endLine, "Call", seen); expression(call.callee, old.handlerNameSyntax);
      const terminal = ["name", "attribute"].includes(call.callee.kind) ? call.callee.symbol?.split(".").at(-1) : undefined;
      requireValue(call.loggingNameSyntax === (terminal && loggingNames.includes(terminal) ? terminal : null), "logging-name hint mismatch");
      requireValue(call.immediatelyAwaited === (call.ancestors[0]?.kind === "Await") && call.directStatement === (!call.ancestorsTruncated &&
        (call.ancestors.length === 1 && call.ancestors[0].kind === "Expr" || call.ancestors.length === 2 && call.ancestors[0].kind === "Await" && call.ancestors[1].kind === "Expr")), "call context mismatch");
      requireValue([call.positionalArguments, call.keywordArguments, call.starredArguments, call.keywordUnpacks].every(integer) &&
        call.starredArguments <= call.positionalArguments && call.keywordUnpacks <= call.keywordArguments, "invalid argument accounting");
      if (call.directStatement && !row.suite.truncated) requireValue(row.suite.statements.some(item => item.kind === "Expr" && item.line === call.ancestors.at(-1)!.line && item.endLine >= call.endLine), "direct call absent from suite");
    }
    for (const [index, item] of row.returns.entries()) {
      keys(item, [...locationKeys, "direct", "expression", "awaitedExpression"]);
      location(item, row.returns[index - 1], row, original.endLine, "Return", seen); expression(item.expression, old.handlerNameSyntax);
      requireValue(item.direct === (item.ancestors.length === 0), "return context mismatch");
      if (item.expression.astKind === "Await") expression(item.awaitedExpression!, old.handlerNameSyntax);
      else requireValue(item.awaitedExpression === null, "unexpected awaited expression");
      if (item.direct && !row.suite.truncated) requireValue(row.suite.statements.some(statement => statement.kind === "Return" && statement.line === item.line && statement.endLine === item.endLine), "direct return absent from suite");
    }
    requireValue(seen.size <= 100000 && row.calls.length === original.bodyObservations.calls &&
      isDeepStrictEqual(row.returns.map(item => ({ line: item.line, hasValue: item.expression.kind !== "absent" })), original.bodyObservations.returns), "owned call/return population mismatch");
    requireValue(isDeepStrictEqual(row.classification, classification(row, old)), "classification mismatch");
  }
  requireValue(isDeepStrictEqual(report.summary, summary(report.handlers)), "summary/inventory mismatch");
}
