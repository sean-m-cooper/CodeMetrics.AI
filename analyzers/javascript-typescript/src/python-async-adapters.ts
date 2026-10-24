import { isDeepStrictEqual } from "node:util";
import { hash } from "./evidence.js";
import { fingerprint } from "./python-error-context.js";
import { before, contains, extent, keys, reference, type Extent, type Reference, type PythonAsyncContext } from "./python-async-context.js";
import type { PythonAsyncUsage } from "./python-async-usage.js";

export const asyncAdaptersPolicy = "python/async-callback-adapters-review-v1";
type Owner = PythonAsyncUsage["functions"][number];
type Call = NonNullable<Owner["measurement"]>["calls"][number];
type ContextCall = PythonAsyncContext["functions"][number]["calls"][number];
interface Expression extends Extent { kind: string; shape: string }
interface Partial {
  status: string; importTarget: string; construction: Extent; argumentLayout: string;
  callback: (Expression & { reference: Reference }) | null;
  boundPositionalExpressions: Expression[] | null;
  keywordExpressions: { name: string | null; unpacking: boolean; value: Expression }[];
}
interface PartialLink {
  status: string; reason?: string; definition?: null; name?: string; assignment?: Extent;
  constructionCallId?: string; partial?: Partial;
}
interface DecoratorLink {
  status: string; reason?: string; importTarget?: string;
  definition: (Extent & { ownerId: string; file: string; member: string; functionKind: string }) | null;
  decorator?: Extent & { form: string; options: { name: string; value: boolean }[] };
}
interface AdapterCall {
  callId: string; executionContext: string; partialConstruction: Partial | null;
  partialCallee: PartialLink; decoratedCallee: DecoratorLink;
  callbackPartial: PartialLink | null; callbackDecorator: DecoratorLink | null;
}
interface AdapterOwner extends Omit<PythonAsyncContext["functions"][number], "calls"> { calls: AdapterCall[] }
export interface PythonAsyncAdapters extends Omit<PythonAsyncContext, "functions"> { functions: AdapterOwner[] }
const locationKeys = ["line", "column", "endLine", "endColumn"];
const partialReasons = ["not-a-name-or-matched-partial", "comprehension-local-binding", "reflective-or-cross-scope-binding",
  "deferred-execution-context", "not-a-unique-same-owner-binding", "no-direct-assignment-before-use", "not-a-matched-partial-assignment", "unresolved-argument-layout"];
const decoratorReasons = ["non-name-reference", "comprehension-local-binding", "reflective-or-cross-scope-binding",
  "rebound-or-ambiguous-name", "no-direct-definition-before-reference", "unsupported-decorated-definition",
  "unmatched-decorator-import", "unsupported-decorator-arguments", "definition-outside-selected-owners", "no-local-definition"];
const nonempty = (v: unknown): v is string => typeof v === "string" && v.trim().length > 0;
const path = (v: unknown): v is string => typeof v === "string" && /^[\p{ID_Start}_][\p{ID_Continue}]*(\.[\p{ID_Start}_][\p{ID_Continue}]*)*$/u.test(v);
function check(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(`Invalid Python async adapter evidence: ${message}.`);
}
function location(value: Extent) { keys(value, locationKeys); extent(value); }
function sameExtent(a: Extent, b: Extent) { return locationKeys.every(key => a[key as keyof Extent] === b[key as keyof Extent]); }
function expression(value: Expression, container: Extent, extra: string[] = []) {
  keys(value, [...locationKeys, "kind", "shape", ...extra]); extent(value);
  check(contains(container, value), "expression outside construction");
  const kinds = ["Name", "Attribute", "Call", "Subscript", "Lambda", "Constant", "Starred", "BoolOp", "NamedExpr", "BinOp", "UnaryOp", "IfExp", "Dict", "Set", "ListComp", "SetComp", "DictComp", "GeneratorExp", "Await", "Yield", "YieldFrom", "Compare", "FormattedValue", "JoinedStr", "List", "Tuple"];
  check(kinds.includes(value.kind), "unknown expression kind");
  const expected = ({ Name: ["name"], Attribute: ["attribute-path", "attribute-on-expression"], Call: ["call-result"], Subscript: ["subscript"], Lambda: ["lambda"] } as Record<string, string[]>)[value.kind] ?? ["other-expression"];
  check(expected.includes(value.shape), "expression kind/shape mismatch");
}
function construction(value: Partial, call: Call, owner: Owner, owners: Map<string, Owner>) {
  keys(value, ["status", "importTarget", "construction", "argumentLayout", "callback", "boundPositionalExpressions", "keywordExpressions"]);
  check(value.status === "partial-construction-syntax" && value.importTarget === "functools.partial" && call.binding.target === value.importTarget, "unmatched partial constructor");
  location(value.construction); check(sameExtent(value.construction, call), "construction extent mismatch");
  check(Array.isArray(value.keywordExpressions), "keyword collection required");
  if (value.argumentLayout === "unresolved") {
    check(value.callback === null && value.boundPositionalExpressions === null, "unresolved argument layout must withhold callback");
  } else {
    check(value.argumentLayout === "first-positional-callback" && value.callback && value.callback.kind !== "Starred" && Array.isArray(value.boundPositionalExpressions), "invalid callback layout");
    const callback = value.callback;
    expression(callback, call, ["reference"]);
    const r = callback.reference;
    if (r.status === "import-spelling") check(path(r.target) && ["Name", "Attribute"].includes(callback.kind), "invalid callback import spelling");
    reference(r, r.status === "local-definition" ? r.definition?.member.split(".").at(-1) ?? null : null,
      r.status === "import-spelling" ? r.target! : null, callback.kind, { ...call, ...callback }, owner, owners, "");
    let previous: Extent = callback;
    for (const arg of value.boundPositionalExpressions) {
      expression(arg, call); check(before(previous, arg), "positional arguments out of order"); previous = arg;
    }
  }
  let previousKeyword: Extent | null = null;
  for (const kw of value.keywordExpressions) {
    keys(kw, ["name", "unpacking", "value"]);
    check(kw.name === null || path(kw.name) && !kw.name.includes("."), "invalid keyword name");
    check(kw.unpacking === (kw.name === null), "keyword unpacking mismatch");
    expression(kw.value, call);
    check(!previousKeyword || before(previousKeyword, kw.value), "keywords out of order"); previousKeyword = kw.value;
  }
}
function unresolvedLink(value: PartialLink | DecoratorLink, reasons: string[]) {
  keys(value, ["status", "reason", "definition"]);
  check(value.status === "unresolved" && value.definition === null && reasons.includes(value.reason!), "invalid unresolved link");
}
function partialLink(value: PartialLink, use: Call, owner: Owner, calls: Map<string, Call>, rows: Map<string, AdapterCall>, kind: string, spelling: string | null, bindingReason: string) {
  if (kind === "Name" && ["comprehension-local-binding", "reflective-or-cross-scope-binding"].includes(bindingReason))
    check(value.status === "unresolved" && value.reason === bindingReason, "partial binding veto lost");
  if (value.status === "unresolved") { unresolvedLink(value, partialReasons); return; }
  const stored = value.status === "stored-partial-syntax";
  keys(value, stored ? ["status", "name", "assignment", "constructionCallId", "partial"] : ["status", "constructionCallId", "partial"]);
  check(stored || value.status === "inline-partial-syntax", "unsupported partial link");
  const call = calls.get(value.constructionCallId!), original = rows.get(value.constructionCallId!)?.partialConstruction;
  check(call && original && isDeepStrictEqual(value.partial, original) && value.constructionCallId === fingerprint([owner.id, original.construction]), "missing or inconsistent construction link");
  if (stored) {
    check(kind === "Name" && value.name === spelling && use.executionContext === "async-body" && call.executionContext === "async-body" && !call.directlyAwaited && original.argumentLayout === "first-positional-callback", "invalid stored partial context");
    location(value.assignment!);
    check(value.assignment!.line >= owner.line && value.assignment!.endLine <= owner.endLine && contains(value.assignment!, call) && before(value.assignment!, use), "invalid partial assignment/source order");
  } else check(kind === "Call" && call.id !== use.id && contains(use, call) && use.executionContext === call.executionContext, "invalid inline partial context");
}
function decoratorLink(value: DecoratorLink, use: Call, owner: Owner, owners: Map<string, Owner>, kind: string, spelling: string | null, bindingReason: string, old: Reference | null) {
  if (kind === "Name" && ["comprehension-local-binding", "reflective-or-cross-scope-binding"].includes(bindingReason))
    check(value.status === "unresolved" && value.reason === bindingReason, "decorator binding veto lost");
  if (value.status === "unresolved") { unresolvedLink(value, decoratorReasons); return; }
  keys(value, ["status", "importTarget", "definition", "decorator"]);
  check(value.status === "decorated-wrapper-syntax" && value.importTarget === "asgiref.sync.sync_to_async" && kind === "Name" &&
    old?.status === "unresolved" && old.reason === "decorated-definition", "unsupported decorated reference");
  const d = value.definition!;
  keys(d, [...locationKeys, "ownerId", "file", "member", "functionKind"]); extent(d);
  const selected = owners.get(d.ownerId);
  check(selected && selected.functionKind === "FunctionDef" && d.functionKind === "FunctionDef" && d.file === owner.file && d.file === selected.file && d.member === selected.member &&
    d.line === selected.line && d.endLine === selected.endLine && spelling === d.member.split(".").at(-1) &&
    d.ownerId === hash(`${d.file}|${d.line}|${d.column}|FunctionDef`) && before(d, use), "decorated definition mismatch");
  const decorator = value.decorator!;
  keys(decorator, [...locationKeys, "form", "options"]); extent(decorator);
  check(before(decorator, d) && ["bare", "factory"].includes(decorator.form) && Array.isArray(decorator.options) &&
    decorator.options.length <= (decorator.form === "bare" ? 0 : 1), "invalid decorator header");
  for (const option of decorator.options) { keys(option, ["name", "value"]); check(option.name === "thread_sensitive" && typeof option.value === "boolean", "unsupported decorator option"); }
}
function validateCall(row: AdapterCall, call: Call, owner: Owner, owners: Map<string, Owner>, calls: Map<string, Call>, rows: Map<string, AdapterCall>, old: ContextCall) {
  keys(row, ["callId", "executionContext", "partialConstruction", "partialCallee", "decoratedCallee", "callbackPartial", "callbackDecorator"]);
  check(row.callId === call.id && row.executionContext === call.executionContext, "call identity/context mismatch");
  if (call.binding.target === "functools.partial") { check(row.partialConstruction, "missing partial construction"); construction(row.partialConstruction, call, owner, owners); }
  else check(row.partialConstruction === null, "unexpected partial construction");
  const kind = old.shape === "name" ? "Name" : old.shape === "call-result" ? "Call" : "Other";
  partialLink(row.partialCallee, call, owner, calls, rows, kind, call.binding.syntax, call.binding.reason);
  decoratorLink(row.decoratedCallee, call, owner, owners, kind, call.binding.syntax, call.binding.reason, old.localCallee);
  let use = call;
  if (old.storedWrapper?.status === "stored-wrapper-syntax") use = calls.get(old.storedWrapper.definition!.constructionCallId)!;
  const callback = use.callback;
  if (!old.callback) { check(row.callbackPartial === null && row.callbackDecorator === null, "unexpected callback adapter"); return; }
  check(callback?.binding && row.callbackPartial && row.callbackDecorator, "missing callback adapter");
  partialLink(row.callbackPartial, use, owner, calls, rows, callback.kind, callback.binding.syntax, callback.binding.reason);
  decoratorLink(row.callbackDecorator, use, owner, owners, callback.kind, callback.binding.syntax, callback.binding.reason, old.callback.reference);
  if (use !== call) {
    check(isDeepStrictEqual(row.callbackPartial, rows.get(use.id)?.callbackPartial) && isDeepStrictEqual(row.callbackDecorator, rows.get(use.id)?.callbackDecorator), "stored wrapper callback differs from construction");
  }
  if (call.category === "offload-wrapped-invocation") {
    const inner = [...calls.values()].find(c => c.category === "offload-wrapper-construction" && c.line === call.line && c.column === call.column && contains(call, c) && c.id !== call.id);
    check(inner && isDeepStrictEqual(row.callbackPartial, rows.get(inner.id)?.callbackPartial) && isDeepStrictEqual(row.callbackDecorator, rows.get(inner.id)?.callbackDecorator), "immediate wrapper callback differs from construction");
  }
}
function counts(rows: AdapterOwner[]) {
  const eligible = rows.filter(row => row.status === "observed"), calls = eligible.flatMap(row => row.calls);
  return { selectedFunctions: rows.length, asyncFunctions: eligible.length, excludedSynchronousFunctions: rows.length - eligible.length,
    availability: eligible.length ? "available" : "unavailable", contexts: Object.fromEntries(["async-body", "deferred-generator-body"].map(context => {
      const selected = calls.filter(call => call.executionContext === context);
      const links = (field: "partialConstruction" | "partialCallee" | "callbackPartial" | "decoratedCallee" | "callbackDecorator", statuses: string[]) => selected.filter(call => call[field] && statuses.includes(call[field]!.status)).length;
      return [context, eligible.length ? { callExpressions: selected.length,
        partialConstructions: links("partialConstruction", ["partial-construction-syntax"]),
        partialCalleeReferences: links("partialCallee", ["inline-partial-syntax", "stored-partial-syntax"]),
        partialCallbackReferences: links("callbackPartial", ["inline-partial-syntax", "stored-partial-syntax"]),
        decoratedCalleeReferences: links("decoratedCallee", ["decorated-wrapper-syntax"]),
        decoratedCallbackReferences: links("callbackDecorator", ["decorated-wrapper-syntax"]) } : null];
    })) };
}
/** Internal consistency checks against validated source evidence, not AST remeasurement or authentication. */
export function validateAsyncAdapters(value: unknown, base: PythonAsyncUsage, context: PythonAsyncContext): asserts value is PythonAsyncAdapters {
  keys(value, ["schemaVersion", "artifactKind", "producer", "generatedAtUtc", "runId", "auditId", "policy", "configuration", "configurationFingerprint", "subject", "selection", "sourceFactsProducer", "sourceFactsPolicy", "asyncRowsSha256", "status", "diagnostics", "functions", "summary", "scope"]);
  const r = value as PythonAsyncAdapters;
  check(r.schemaVersion === 1 && r.artifactKind === "experimental-python-async-callback-adapters-review" && r.policy === asyncAdaptersPolicy &&
    isDeepStrictEqual(r.producer, { name: "codemetrics-ai/python-async-callback-adapters", version: "0.1.0-experimental" }), "unsupported producer/policy");
  for (const key of ["runId", "auditId", "subject", "selection", "sourceFactsProducer", "sourceFactsPolicy"] as const)
    check(isDeepStrictEqual(r[key], base[key]), "invocation/source mismatch");
  const configuration = { policy: asyncAdaptersPolicy, asyncConfigurationFingerprint: base.configurationFingerprint };
  check(isDeepStrictEqual(r.configuration, configuration) && r.configurationFingerprint === fingerprint(configuration) && r.asyncRowsSha256 === fingerprint(base.functions), "configuration/original rows mismatch");
  check(nonempty(r.scope) && typeof r.generatedAtUtc === "string" && Number.isFinite(Date.parse(r.generatedAtUtc)), "scope/time required");
  check(Array.isArray(r.functions) && Array.isArray(r.diagnostics), "collections required");
  for (const d of r.diagnostics) { keys(d, ["code", "message"]); check(d.code === "async-adapter-analysis" && nonempty(d.message), "invalid diagnostic"); }
  if (r.status === "incomplete") { check(r.diagnostics.length > 0 && r.functions.length === 0 && r.summary === null, "incomplete report must withhold rows"); return; }
  check(r.status === "complete" && base.status === "complete" && !r.diagnostics.length && r.functions.length === base.functions.length, "status/owner population mismatch");
  // A context-only failure can coexist with valid adapter syntax, but cannot restore a score.
  const owners = new Map(base.functions.map(row => [row.id, row]));
  for (let i = 0; i < r.functions.length; i++) {
    const row = r.functions[i], owner = base.functions[i];
    keys(row, ["id", "file", "member", "line", "endLine", "population", "sourceSha256", "status", "reason", "disposition", "calls"]);
    for (const key of ["id", "file", "member", "line", "endLine", "population", "sourceSha256", "status", "reason", "disposition"] as const) check(row[key] === owner[key], "owner metadata/order mismatch");
    const original = owner.measurement?.calls ?? [];
    check(Array.isArray(row.calls) && row.calls.length === original.length, "call population mismatch");
    const calls = new Map(original.map(call => [call.id, call])), rows = new Map(row.calls.map(call => [call.callId, call]));
    check(context.status === "complete" || !row.calls.length, "complete adapter calls require validated context");
    for (let j = 0; j < row.calls.length; j++) validateCall(row.calls[j], original[j], owner, owners, calls, rows, context.functions[i].calls[j]);
  }
  const expected = { ...counts(r.functions), populations: Object.fromEntries([...new Set(r.functions.map(row => row.population))].sort()
    .map(population => [population, counts(r.functions.filter(row => row.population === population))])) };
  check(isDeepStrictEqual(r.summary, expected), "summary mismatch");
}
