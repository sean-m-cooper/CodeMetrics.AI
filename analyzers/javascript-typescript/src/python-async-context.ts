import { isDeepStrictEqual } from "node:util";
import { hash } from "./evidence.js";
import { fingerprint } from "./python-error-context.js";
import type { PythonAsyncUsage } from "./python-async-usage.js";

export const asyncContextPolicy = "python/async-call-context-review-v1";
type Owner = PythonAsyncUsage["functions"][number];
type Call = NonNullable<Owner["measurement"]>["calls"][number];
interface Extent { line: number; column: number; endLine: number; endColumn: number }
interface Definition extends Extent { ownerId: string; file: string; member: string; functionKind: string }
interface WrapperDefinition { name: string; assignment: Extent; constructionCallId: string; construction: Extent; importTarget: string }
interface Reference { status: string; reason: string; target?: string; definition: Definition | null }
interface Wrapper { status: string; reason: string; definition: WrapperDefinition | null }
interface Callback { kind: string; evaluation: string; reference: Reference }
interface ContextCall {
  callId: string; executionContext: string; shape: string; unresolvedReason: string | null;
  localCallee: Reference | null; storedWrapper: Wrapper | null; callback: Callback | null;
}
interface ContextOwner {
  id: string; file: string; member: string; line: number; endLine: number; population: string; sourceSha256: string;
  status: string; reason: string | null; disposition: string; calls: ContextCall[];
}
export interface PythonAsyncContext extends Omit<PythonAsyncUsage, "functions"> {
  asyncRowsSha256: string; functions: ContextOwner[];
}
const referenceReasons = ["non-name-reference", "comprehension-local-binding", "reflective-or-cross-scope-binding",
  "rebound-or-ambiguous-name", "not-a-direct-function-definition", "decorated-definition", "definition-not-before-reference",
  "definition-outside-selected-owners", "no-local-definition"];
const wrapperReasons = ["comprehension-local-binding", "reflective-or-cross-scope-binding", "deferred-execution-context",
  "not-a-unique-same-owner-binding", "not-a-direct-wrapper-assignment", "assignment-not-before-use", "unmatched-wrapper-constructor", "unresolved-argument-layout"];
const shapes = ["name", "attribute-path", "attribute-on-expression", "call-result", "subscript", "lambda", "other-expression"];
const contexts = ["async-body", "deferred-generator-body"];
function requireValue(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(`Invalid Python async context evidence: ${message}.`);
}
function keys(value: unknown, names: string[]) {
  requireValue(value !== null && typeof value === "object" && !Array.isArray(value), "object required");
  requireValue(isDeepStrictEqual(Object.keys(value).sort(), [...names].sort()), "unexpected or missing fields");
}
const integer = (n: number) => Number.isSafeInteger(n) && n >= 0;
const nonempty = (s: unknown): s is string => typeof s === "string" && s.trim().length > 0;
const before = (a: Extent, b: Extent) => a.endLine < b.line || a.endLine === b.line && a.endColumn < b.column;
const contains = (a: Extent, b: Extent) => (a.line < b.line || a.line === b.line && a.column <= b.column) &&
  (a.endLine > b.endLine || a.endLine === b.endLine && a.endColumn >= b.endColumn);
function extent(value: Extent) {
  requireValue([value.line, value.column, value.endLine, value.endColumn].every(integer) && value.line > 0 &&
    (value.endLine > value.line || value.endLine === value.line && value.endColumn > value.column), "invalid extent");
}

function reference(value: Reference, spelling: string | null, target: string | null, kind: string, use: Call, owner: Owner, owners: Map<string, Owner>, bindingReason: string) {
  keys(value, value.status === "import-spelling" ? ["status", "reason", "target", "definition"] : ["status", "reason", "definition"]);
  if (target !== null) {
    requireValue(value.status === "import-spelling" && value.target === target && value.reason === "import-spelling-only" && value.definition === null, "callback import mismatch");
    return;
  }
  if (kind === "Name" && ["comprehension-local-binding", "reflective-or-cross-scope-binding"].includes(bindingReason))
    requireValue(value.status === "unresolved" && value.reason === bindingReason, "source binding veto must be preserved");
  if (value.status === "unresolved") {
    requireValue(value.definition === null && referenceReasons.includes(value.reason), "invalid unresolved reference");
    requireValue(kind === "Name" || value.reason === "non-name-reference", "non-name reference cannot resolve locally");
    return;
  }
  requireValue(value.status === "local-definition" && value.reason === "unique-direct-lexical-definition" && kind === "Name", "invalid local reference");
  keys(value.definition, ["ownerId", "file", "member", "functionKind", "line", "column", "endLine", "endColumn"]);
  const d = value.definition!, selected = owners.get(d.ownerId);
  extent(d);
  requireValue(selected && selected.file === owner.file && d.file === selected.file && d.member === selected.member &&
    d.line === selected.line && d.endLine === selected.endLine && d.functionKind === selected.functionKind &&
    ["FunctionDef", "AsyncFunctionDef"].includes(d.functionKind) && spelling === d.member.split(".").at(-1), "unknown local definition");
  requireValue(d.ownerId === hash(`${d.file}|${d.line}|${d.column}|${d.functionKind}`) && before(d, use), "definition identity or source order mismatch");
}

function wrapper(value: Wrapper, call: Call, owner: Owner, calls: Map<string, Call>): Call | null {
  keys(value, ["status", "reason", "definition"]);
  if (["comprehension-local-binding", "reflective-or-cross-scope-binding"].includes(call.binding.reason))
    requireValue(value.status === "unresolved" && value.reason === call.binding.reason, "wrapper binding veto must be preserved");
  if (value.status === "unresolved") {
    requireValue(value.definition === null && wrapperReasons.includes(value.reason), "invalid unresolved wrapper");
    return null;
  }
  requireValue(value.status === "stored-wrapper-syntax" && value.reason === "unique-direct-same-owner-assignment" && call.executionContext === "async-body", "invalid stored wrapper");
  keys(value.definition, ["name", "assignment", "constructionCallId", "construction", "importTarget"]);
  const d = value.definition!;
  keys(d.assignment, ["line", "column", "endLine", "endColumn"]); extent(d.assignment);
  keys(d.construction, ["line", "column", "endLine", "endColumn"]); extent(d.construction);
  const construction = calls.get(d.constructionCallId);
  requireValue(d.name === call.binding.syntax && d.importTarget === "asgiref.sync.sync_to_async" &&
    d.assignment.line >= owner.line && d.assignment.endLine <= owner.endLine && contains(d.assignment, d.construction) && before(d.assignment, call), "wrapper extent/name mismatch");
  requireValue(construction && construction.category === "offload-wrapper-construction" && construction.executionContext === "async-body" &&
    construction.callback && construction.callback.kind !== "unresolved-argument-layout" && !construction.directlyAwaited &&
    d.constructionCallId === fingerprint([owner.id, d.construction]) &&
    ["line", "column", "endLine", "endColumn"].every(key => construction[key as keyof Extent] === d.construction[key as keyof Extent]), "missing wrapper construction");
  return construction;
}

function callContext(value: ContextCall, call: Call, owner: Owner, owners: Map<string, Owner>, calls: Map<string, Call>, rows: Map<string, ContextCall>) {
  keys(value, ["callId", "executionContext", "shape", "unresolvedReason", "localCallee", "storedWrapper", "callback"]);
  requireValue(value.callId === call.id && value.executionContext === call.executionContext && shapes.includes(value.shape), "call identity/context/shape mismatch");
  const spelling = call.binding.syntax;
  if (call.category === "offload-wrapped-invocation") requireValue(value.shape === "call-result", "immediate wrapper shape mismatch");
  else if (spelling !== null) requireValue(value.shape === (spelling.includes(".") ? "attribute-path" : "name"), "callee shape differs from spelling");
  else requireValue(!["name", "attribute-path"].includes(value.shape), "dynamic callee cannot be a path");
  requireValue(value.unresolvedReason === (call.binding.status === "unresolved" ? call.binding.reason : null), "unresolved reason mismatch");
  let construction: Call | null = null;
  if (call.binding.status === "unresolved" && value.shape === "name") {
    requireValue(value.localCallee !== null && value.storedWrapper !== null, "name context required");
    reference(value.localCallee, spelling, null, "Name", call, owner, owners, call.binding.reason);
    construction = wrapper(value.storedWrapper, call, owner, calls);
    if (construction) requireValue(value.localCallee.status === "unresolved" && value.localCallee.reason === "not-a-direct-function-definition", "wrapper is not a function definition");
  } else requireValue(value.localCallee === null && value.storedWrapper === null, "non-name cannot claim local links");
  const callback = construction?.callback ?? call.callback;
  if (!callback || callback.kind === "unresolved-argument-layout") {
    requireValue(value.callback === null, "unexpected callback context");
    return;
  }
  keys(value.callback, ["kind", "evaluation", "reference"]);
  requireValue(value.callback!.kind === callback.kind && value.callback!.evaluation === callback.evaluation, "callback expression mismatch");
  reference(value.callback!.reference, callback.binding!.syntax, callback.binding!.target, callback.kind, construction ?? call, owner, owners, callback.binding!.reason);
  if (construction) requireValue(isDeepStrictEqual(value.callback, rows.get(construction.id)?.callback), "stored callback differs from construction");
  if (call.category === "offload-wrapped-invocation") {
    const inner = [...calls.values()].find(candidate => candidate.category === "offload-wrapper-construction" && candidate.line === call.line &&
      candidate.column === call.column && contains(call, candidate) && candidate.id !== call.id);
    requireValue(inner && isDeepStrictEqual(value.callback, rows.get(inner.id)?.callback), "immediate callback differs from construction");
  }
}

const histogram = (values: string[]) => {
  const counts = new Map<string, number>();
  for (const value of values) counts.set(value, (counts.get(value) ?? 0) + 1);
  return Object.fromEntries([...counts.entries()].sort(([a], [b]) => a.localeCompare(b, "en")));
};
function inventory(rows: ContextOwner[], context: string) {
  const groups = new Map<string, { reason: string; shape: string; callExpressions: number; examples: { ownerId: string; callId: string }[] }>();
  for (const row of rows) for (const call of row.calls) if (call.executionContext === context && call.unresolvedReason !== null) {
    const key = `${call.unresolvedReason}\0${call.shape}`;
    const group = groups.get(key) ?? { reason: call.unresolvedReason, shape: call.shape, callExpressions: 0, examples: [] };
    group.callExpressions++;
    if (group.examples.length < 3) group.examples.push({ ownerId: row.id, callId: call.callId });
    groups.set(key, group);
  }
  return [...groups.entries()].sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0).map(([, group]) => group);
}
function counts(rows: ContextOwner[]) {
  const eligible = rows.filter(row => row.status === "observed"), calls = eligible.flatMap(row => row.calls);
  return { selectedFunctions: rows.length, asyncFunctions: eligible.length, excludedSynchronousFunctions: rows.length - eligible.length,
    availability: eligible.length ? "available" : "unavailable", contexts: Object.fromEntries(contexts.map(context => {
      const selected = calls.filter(call => call.executionContext === context), unmatched = selected.filter(call => call.unresolvedReason !== null);
      return [context, eligible.length ? { callExpressions: selected.length, unresolvedImportCallExpressions: unmatched.length,
        unresolvedReasons: histogram(unmatched.map(call => call.unresolvedReason!)), unresolvedShapes: histogram(unmatched.map(call => call.shape)),
        unresolvedInventory: inventory(eligible, context), localCalleeLinks: selected.filter(call => call.localCallee?.status === "local-definition").length,
        storedWrapperInvocationLinks: selected.filter(call => call.storedWrapper?.status === "stored-wrapper-syntax").length,
        localCallbackReferenceLinks: selected.filter(call => call.callback?.reference.status === "local-definition").length } : null];
    })) };
}

/** Check consistency against validated async evidence; not independent AST resolution or authentication. */
export function validateAsyncContext(value: unknown, base: PythonAsyncUsage): asserts value is PythonAsyncContext {
  keys(value, ["schemaVersion", "artifactKind", "producer", "generatedAtUtc", "runId", "auditId", "policy", "configuration", "configurationFingerprint", "subject", "selection", "sourceFactsProducer", "sourceFactsPolicy", "asyncRowsSha256", "status", "diagnostics", "functions", "summary", "scope"]);
  const r = value as PythonAsyncContext;
  requireValue(r.schemaVersion === 1 && r.artifactKind === "experimental-python-async-call-context-review" && r.policy === asyncContextPolicy &&
    isDeepStrictEqual(r.producer, { name: "codemetrics-ai/python-async-call-context", version: "0.1.0-experimental" }), "unsupported producer/policy");
  for (const key of ["runId", "auditId", "subject", "selection", "sourceFactsProducer", "sourceFactsPolicy"] as const)
    requireValue(isDeepStrictEqual(r[key], base[key]), "invocation or source provenance mismatch");
  const configuration = { policy: asyncContextPolicy, asyncConfigurationFingerprint: base.configurationFingerprint, maxExamplesPerGroup: 3 };
  requireValue(isDeepStrictEqual(r.configuration, configuration) && r.configurationFingerprint === fingerprint(configuration) &&
    r.asyncRowsSha256 === fingerprint(base.functions), "configuration or original async rows mismatch");
  requireValue(nonempty(r.scope) && typeof r.generatedAtUtc === "string" && Number.isFinite(Date.parse(r.generatedAtUtc)), "scope/time required");
  requireValue(Array.isArray(r.functions) && Array.isArray(r.diagnostics), "collections required");
  for (const d of r.diagnostics) { keys(d, ["code", "message"]); requireValue(d.code === "async-context-analysis" && nonempty(d.message), "invalid diagnostic"); }
  if (r.status === "incomplete") {
    requireValue(r.diagnostics.length > 0 && r.functions.length === 0 && r.summary === null, "incomplete report must withhold rows");
    return;
  }
  requireValue(r.status === "complete" && base.status === "complete" && !r.diagnostics.length && r.functions.length === base.functions.length, "status/owner population mismatch");
  const owners = new Map(base.functions.map(row => [row.id, row]));
  for (let index = 0; index < r.functions.length; index++) {
    const row = r.functions[index], owner = base.functions[index];
    keys(row, ["id", "file", "member", "line", "endLine", "population", "sourceSha256", "status", "reason", "disposition", "calls"]);
    for (const key of ["id", "file", "member", "line", "endLine", "population", "sourceSha256", "status", "reason", "disposition"] as const)
      requireValue(row[key] === owner[key], "owner metadata/order mismatch");
    const original = owner.measurement?.calls ?? [];
    requireValue(Array.isArray(row.calls) && row.calls.length === original.length, "owned call population mismatch");
    const calls = new Map(original.map(call => [call.id, call])), rows = new Map(row.calls.map(call => [call.callId, call]));
    for (let i = 0; i < row.calls.length; i++) callContext(row.calls[i], original[i], owner, owners, calls, rows);
  }
  const expected = { ...counts(r.functions), populations: Object.fromEntries([...new Set(r.functions.map(row => row.population))].sort()
    .map(population => [population, counts(r.functions.filter(row => row.population === population))])) };
  requireValue(isDeepStrictEqual(r.summary, expected), "summary or inventory mismatch");
}
