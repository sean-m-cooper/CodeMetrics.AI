import { isDeepStrictEqual } from "node:util";
import type { SourceFacts } from "#source-facts/contract.mjs";
import type { Evidence } from "./evidence.js";
import type { PythonDecomposition } from "./python-decomposition.js";
import { fingerprint } from "./python-error-context.js";

export const asyncUsagePolicy = "python/async-owned-calls-review-v1";
const blocking = ["os.system", "subprocess.call", "subprocess.check_call", "subprocess.check_output", "subprocess.run", "time.sleep", "urllib.request.urlopen"];
const offload = ["anyio.to_thread.run_sync", "asyncio.to_thread", "starlette.concurrency.run_in_threadpool"];
const wrapper = "asgiref.sync.sync_to_async";
const unresolved = ["dynamic-callee-expression", "comprehension-local-binding", "reflective-or-cross-scope-binding", "non-import-or-ambiguous-binding", "import-not-before-use", "imported-module-attribute-write", "no-lexical-import"];
const expressionKinds = ["BoolOp", "NamedExpr", "BinOp", "UnaryOp", "Lambda", "IfExp", "Dict", "Set", "ListComp", "SetComp", "DictComp", "GeneratorExp", "Await", "Yield", "YieldFrom", "Compare", "Call", "FormattedValue", "JoinedStr", "Constant", "Attribute", "Subscript", "Name", "List", "Tuple"];
interface Binding { syntax: string | null; target: string | null; status: string; reason: string }
interface Callback { kind: string; binding: Binding | null; evaluation?: string }
interface Call {
  id: string; line: number; column: number; endLine: number; endColumn: number;
  executionContext: string; category: string; binding: Binding; directlyAwaited: boolean;
  callback: Callback | null; disposition: string;
}
interface Owner {
  id: string; file: string; member: string; line: number; endLine: number; population: string;
  sourceSha256: string; functionKind: string; disposition: string; status: string; reason: string | null;
  measurement: { kind: string; awaitExpressions: Record<string, number>; calls: Call[] } | null;
}
export interface PythonAsyncUsage extends Omit<PythonDecomposition, "configuration" | "functions"> {
  configuration: Record<string, unknown>; functions: Owner[];
}
function requireValue(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(`Invalid Python async evidence: ${message}.`);
}
function keys(value: unknown, names: string[]) {
  requireValue(value !== null && typeof value === "object" && !Array.isArray(value), "object required");
  requireValue(isDeepStrictEqual(Object.keys(value).sort(), names.sort()), "unexpected or missing fields");
}
const integer = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0;
const nonempty = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const pathSyntax = (value: unknown): value is string => nonempty(value) && value.split(".").every(part => part.trim() === part && part.length > 0 && !/[\s()\[\]]/u.test(part));
const histogram = (items: string[]) => Object.fromEntries([...new Set(items)].sort().map(key => [key, items.filter(value => value === key).length]));

function binding(value: Binding, wrapped = false) {
  keys(value, ["syntax", "target", "status", "reason"]);
  requireValue(value.syntax === null || pathSyntax(value.syntax), "invalid callee spelling");
  if (value.status === "import-matched") {
    requireValue(pathSyntax(value.target) && value.syntax !== null, "matched import must have a target and spelling");
    requireValue(value.reason === (wrapped ? "immediate-import-matched-wrapper-syntax" : "import-spelling-only"), "invalid match reason");
  } else {
    requireValue(!wrapped && value.status === "unresolved" && value.target === null && unresolved.includes(value.reason), "invalid unresolved binding");
    requireValue((value.syntax === null) === (value.reason === "dynamic-callee-expression"), "binding syntax/reason mismatch");
  }
}

function callback(value: Callback) {
  requireValue(value !== null, "helper callback evidence required");
  if (value.kind === "unresolved-argument-layout") {
    keys(value, ["kind", "binding"]);
    requireValue(value.binding === null, "unresolved arguments cannot resolve a callback");
    return;
  }
  keys(value, ["kind", "binding", "evaluation"]);
  requireValue(expressionKinds.includes(value.kind) && value.binding !== null, "invalid callback expression");
  binding(value.binding);
  requireValue(value.evaluation === (value.kind === "Call" ? "eager-call-expression" : "callback-expression-only"), "callback evaluation mismatch");
  requireValue((value.binding.syntax !== null) === ["Name", "Attribute"].includes(value.kind) ||
    value.kind === "Attribute" && value.binding.syntax === null, "callback spelling/kind mismatch");
}

function counts(rows: Owner[]) {
  const eligible = rows.filter(row => row.status === "observed");
  const calls = eligible.flatMap(row => row.measurement!.calls);
  const direct = calls.filter(call => call.executionContext === "async-body");
  return { selectedFunctions: rows.length, asyncFunctions: eligible.length, excludedSynchronousFunctions: rows.length - eligible.length,
    availability: eligible.length ? "available" : "unavailable", asyncBodyCallExpressions: eligible.length ? direct.length : null,
    asyncBodyCallCategories: eligible.length ? histogram(direct.map(call => call.category)) : null,
    deferredGeneratorCallExpressions: eligible.length ? calls.length - direct.length : null,
    functionsWithBlockingCandidates: eligible.length ? eligible.filter(row => row.measurement!.calls.some(call =>
      call.category === "blocking-call-candidate" && call.executionContext === "async-body")).length : null };
}

function callEvidence(call: Call, row: Owner, used: Set<string>) {
  keys(call, ["id", "line", "column", "endLine", "endColumn", "executionContext", "category", "binding", "directlyAwaited", "callback", "disposition"]);
  requireValue([call.line, call.column, call.endLine, call.endColumn].every(integer) && call.line >= row.line && call.endLine <= row.endLine &&
    (call.endLine > call.line || call.endLine === call.line && call.endColumn > call.column), "invalid call extent");
  const location = { line: call.line, column: call.column, endLine: call.endLine, endColumn: call.endColumn };
  requireValue(call.id === fingerprint([row.id, location]) && !used.has(call.id), "call identity mismatch or duplicate");
  used.add(call.id);
  requireValue(["async-body", "deferred-generator-body"].includes(call.executionContext) && typeof call.directlyAwaited === "boolean" && call.disposition === "review-only", "call context/disposition mismatch");
  const wrapped = call.category === "offload-wrapped-invocation";
  binding(call.binding, wrapped);
  const target = call.binding.target;
  const category = target === null ? "unresolved-call" : blocking.includes(target) ? "blocking-call-candidate" :
    offload.includes(target) ? "offload-call" : target === wrapper ? (wrapped ? "offload-wrapped-invocation" : "offload-wrapper-construction") : "other-import-call";
  requireValue(call.category === category, "call category does not match the catalogue");
  if (call.category.startsWith("offload-")) {
    requireValue(call.callback !== null, "helper callback required");
    callback(call.callback);
    if (wrapped) {
      requireValue(call.callback.kind !== "unresolved-argument-layout", "wrapped invocation requires a direct positional callback");
      requireValue(row.measurement!.calls.some(inner => inner.category === "offload-wrapper-construction" &&
        inner.line === call.line && inner.column === call.column &&
        (inner.endLine < call.endLine || inner.endLine === call.endLine && inner.endColumn < call.endColumn) &&
        inner.executionContext === call.executionContext && !inner.directlyAwaited && isDeepStrictEqual(inner.callback, call.callback)), "missing immediate wrapper construction");
    }
  } else requireValue(call.callback === null, "non-helper cannot claim an offloaded callback");
}

/** Validate identity, catalogue coherence and accounting, not runtime resolution or independent AST semantics. */
export function validateAsyncUsage(value: unknown, facts: SourceFacts, subject: Evidence["subject"], ids: { runId: string; auditId: string }, decomposition: PythonDecomposition): asserts value is PythonAsyncUsage {
  keys(value, ["schemaVersion", "artifactKind", "producer", "generatedAtUtc", "runId", "auditId", "policy", "configuration", "configurationFingerprint", "subject", "sourceFactsProducer", "sourceFactsPolicy", "selection", "status", "diagnostics", "scope", "functions", "summary"]);
  const report = value as PythonAsyncUsage;
  requireValue(report.schemaVersion === 1 && report.artifactKind === "experimental-python-async-usage-review" && report.policy === asyncUsagePolicy &&
    isDeepStrictEqual(report.producer, { name: "codemetrics-ai/python-async-usage", version: "0.1.0-experimental" }), "unsupported producer/policy");
  requireValue(report.runId === ids.runId && report.auditId === ids.auditId, "stale invocation identities");
  const target = subject.entryPoint.replaceAll("\\", "/").endsWith("/pyproject.toml") ? subject.root : subject.entryPoint;
  requireValue(isDeepStrictEqual(report.subject, { root: subject.root, entryPoint: target }) && isDeepStrictEqual(report.selection, facts.selection) &&
    isDeepStrictEqual(report.sourceFactsProducer, facts.producer) && report.sourceFactsPolicy === facts.measurementPolicy, "source provenance mismatch");
  const configuration = { policy: asyncUsagePolicy, selectionFingerprint: facts.selection!.configurationFingerprint, parserVersion: facts.producer.parserVersion,
    maxOwners: 100000, maxCalls: 100000, blockingCatalogue: blocking, offloadCatalogue: offload, wrapperCatalogue: [wrapper] };
  requireValue(isDeepStrictEqual(report.configuration, configuration) && report.configurationFingerprint === fingerprint(configuration), "configuration mismatch");
  requireValue(nonempty(report.scope) && typeof report.generatedAtUtc === "string" && Number.isFinite(Date.parse(report.generatedAtUtc)), "missing scope/time");
  requireValue(Array.isArray(report.functions) && Array.isArray(report.diagnostics), "collections required");
  for (const item of report.diagnostics) {
    keys(item, item.file === undefined ? ["code", "message"] : ["code", "message", "file"]);
    requireValue(nonempty(item.code) && typeof item.message === "string" && (item.file === undefined || typeof item.file === "string"), "invalid diagnostic");
  }
  if (report.status === "incomplete") {
    requireValue(report.diagnostics.length > 0 && report.functions.length === 0 && report.summary === null, "incomplete review must withhold rows");
    return;
  }
  requireValue(report.status === "complete" && facts.status === "complete" && !report.diagnostics.length && report.functions.length <= 100000, "status/owner budget mismatch");
  const owners = new Map(facts.functions.map(row => [row.id, row]));
  const bodies = new Map(decomposition.functions.map(row => [row.id, row]));
  const files = new Map(facts.selection!.files.map(row => [row.file, row]));
  const used = new Set<string>(), calls = new Set<string>();
  for (const row of report.functions) {
    keys(row, ["id", "file", "member", "line", "population", "sourceSha256", "endLine", "functionKind", "disposition", "status", "reason", "measurement"]);
    const owner = owners.get(row.id), body = bodies.get(row.id);
    requireValue(owner && !used.has(row.id), "unknown or repeated owner"); used.add(row.id);
    requireValue(row.file === owner.file && row.member === owner.member && row.line === owner.line && row.population === owner.population &&
      row.sourceSha256 === files.get(row.file)?.contentSha256 && integer(row.endLine) && row.endLine >= row.line && row.disposition === "review-only", "owner location/source mismatch");
    if (decomposition.status === "complete") requireValue(body && row.functionKind === body.functionKind && row.endLine === body.endLine, "owner differs from decomposition");
    if (["FunctionDef", "Lambda"].includes(row.functionKind)) {
      requireValue(row.status === "excluded" && row.reason === "synchronous-owner" && row.measurement === null, "synchronous owner must be excluded");
      continue;
    }
    requireValue(row.functionKind === "AsyncFunctionDef" && row.status === "observed" && row.reason === null, "async owner required");
    keys(row.measurement, ["kind", "awaitExpressions", "calls"]);
    const measurement = row.measurement!;
    requireValue(["coroutine", "async-generator"].includes(measurement.kind) && Array.isArray(measurement.calls), "invalid async measurement");
    requireValue(measurement.awaitExpressions !== null && typeof measurement.awaitExpressions === "object" && !Array.isArray(measurement.awaitExpressions), "await histogram required");
    for (const [key, count] of Object.entries(measurement.awaitExpressions)) requireValue(["async-body", "deferred-generator-body"].includes(key) && integer(count) && count > 0, "invalid await count");
    for (const call of measurement.calls) callEvidence(call, row, calls);
    for (const context of ["async-body", "deferred-generator-body"]) requireValue(measurement.calls.filter(call => call.executionContext === context && call.directlyAwaited).length <= (measurement.awaitExpressions[context] ?? 0), "direct-await accounting mismatch");
    requireValue(calls.size <= 100000, "call budget exceeded");
  }
  requireValue(used.size === owners.size, "missing selected owners");
  const expected = { ...counts(report.functions), populations: Object.fromEntries([...new Set(report.functions.map(row => row.population))].sort()
    .map(population => [population, counts(report.functions.filter(row => row.population === population))])) };
  requireValue(isDeepStrictEqual(report.summary, expected), "summary accounting mismatch");
}
