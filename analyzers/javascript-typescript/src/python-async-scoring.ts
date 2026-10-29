import { isDeepStrictEqual } from "node:util";
import { fingerprint } from "./python-error-context.js";
import { keys, extent, type Extent } from "./python-async-context.js";
import type { PythonAsyncContext } from "./python-async-context.js";
import type { PythonAsyncAdapters } from "./python-async-adapters.js";
import type { PythonAsyncUsage } from "./python-async-usage.js";
import { scored, hash, type Dimension, type Finding } from "./evidence.js";
import { firstMatch } from "./scoring-decision.js";

export const asyncHelperPolicy = "python/async-local-helper-paths-review-v1";
export const pythonAsyncPolicy = "python/async-blocking-owner-rate-v1";
export const asyncIntentPolicy = "adjacent-simple-statement-declared-blocking-prose-v1";
const locationKeys = ["line", "column", "endLine", "endColumn"] as const;
const identityKeys = ["id", "file", "member", "line", "population", "sourceSha256"] as const;
const contexts = ["async-body", "thread-helper-callback", "wrapper-callback-reference", "deferred-generator-body"];
const blocking = ["time.sleep", "subprocess.run", "subprocess.call", "subprocess.check_call", "subprocess.check_output", "os.system", "urllib.request.urlopen"];
export const asyncLadder = [
  { percent: 1, inclusive: false, score: 10 }, { percent: 2, inclusive: false, score: 9 },
  { percent: 5, inclusive: false, score: 8 }, { percent: 10, inclusive: true, score: 6 },
  { percent: 15, inclusive: true, score: 4 }, { percent: 20, inclusive: true, score: 2 }
];
interface Step extends Extent { ownerId: string; callId: string; file: string; member: string }
interface Trace { path: Step[]; executionContext: string; status: string; reason: string; target: string | null; targetOwnerId?: string }
interface HelperOwner { id: string; file: string; member: string; line: number; population: string; sourceSha256: string; traces: Trace[]; disposition: string }
export interface PythonAsyncHelpers extends Omit<PythonAsyncUsage, "functions"> { asyncRowsSha256: string; functions: HelperOwner[] }
interface Documentation { file: string; callId: string; line: number; text: string }
interface Observation extends Trace { documentation: Documentation[]; classification: string }
interface Contribution extends Omit<HelperOwner, "traces" | "disposition"> {
  eligible: boolean; eligibilityReason: string; affected: boolean; observations: Observation[]; unresolvedPaths: Trace[];
}
export interface PythonAsyncScoring extends Omit<PythonAsyncHelpers, "asyncRowsSha256" | "functions"> {
  helperRowsSha256: string; functions: Contribution[];
}
function check(condition: unknown, message: string): asserts condition {
  if (!condition) throw new Error(`Invalid Python async scoring evidence: ${message}.`);
}
const histogram = (items: string[]) => {
  const counts: Record<string, number> = {};
  for (const item of items) counts[item] = (counts[item] ?? 0) + 1;
  return Object.fromEntries(Object.entries(counts).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0));
};
const nonempty = (value: unknown): value is string => typeof value === "string" && value.trim().length > 0;
const same = (a: unknown, b: unknown, message: string) => check(isDeepStrictEqual(a, b), message);
const location = (step: Extent) => Object.fromEntries(locationKeys.map(key => [key, step[key]]));
const commonKeys = ["schemaVersion", "artifactKind", "producer", "generatedAtUtc", "runId", "auditId", "policy", "configuration", "configurationFingerprint", "subject", "selection", "sourceFactsProducer", "sourceFactsPolicy", "status", "diagnostics", "scope", "functions", "summary"];

function envelope(report: PythonAsyncHelpers | PythonAsyncScoring, base: PythonAsyncUsage | PythonAsyncHelpers, policy: string, kind: string, producer: string, configuration: Record<string, unknown>) {
  check(report.schemaVersion === 1 && report.artifactKind === kind && report.policy === policy, "unsupported policy/kind");
  same(report.producer, { name: producer, version: "0.1.0-experimental" }, "producer mismatch");
  for (const key of ["runId", "auditId", "subject", "selection", "sourceFactsProducer", "sourceFactsPolicy"] as const)
    same(report[key], base[key], "invocation/source mismatch");
  same(report.configuration, configuration, "configuration mismatch");
  check(report.configurationFingerprint === fingerprint(configuration), "configuration fingerprint mismatch");
  check(nonempty(report.scope) && typeof report.generatedAtUtc === "string" && Number.isFinite(Date.parse(report.generatedAtUtc)), "scope/time required");
  check(Array.isArray(report.functions) && Array.isArray(report.diagnostics), "collections required");
  for (const diagnostic of report.diagnostics) {
    keys(diagnostic, ["code", "message"]);
    check(nonempty(diagnostic.code) && typeof diagnostic.message === "string", "invalid diagnostic");
  }
  if (report.status === "incomplete") {
    check(report.diagnostics.length > 0 && !report.functions.length && report.summary === null, "incomplete report must withhold rows/score");
    return false;
  }
  check(report.status === "complete" && base.status === "complete" && !report.diagnostics.length, "completion mismatch");
  return true;
}

function helperCounts(rows: HelperOwner[]) {
  const traces = rows.flatMap(row => row.traces), candidates = traces.filter(row => row.status === "blocking-candidate");
  return { asyncFunctions: rows.length, availability: rows.length ? "available" : "unavailable",
    functionsWithDirectOrHelperCandidates: rows.length ? rows.filter(row => row.traces.some(trace => trace.status === "blocking-candidate" && trace.executionContext === "async-body")).length : null,
    candidatePathsByContext: histogram(candidates.map(trace => trace.executionContext)),
    unresolvedPathsByReason: histogram(traces.filter(trace => trace.status === "unresolved").map(trace => trace.reason)),
    candidatePaths: candidates.length, localHelperLinks: traces.filter(trace => trace.status === "local-helper-link").length,
    distinctBlockingSourceCalls: new Set(candidates.map(trace => trace.path.at(-1)!.callId)).size };
}

function validatePath(trace: Trace, row: HelperOwner, owners: Map<string, PythonAsyncUsage["functions"][number]>, links: Map<string, Trace>) {
  keys(trace, ["path", "executionContext", "status", "reason", "target", ...(trace.status === "local-helper-link" ? ["targetOwnerId"] : [])]);
  check(contexts.includes(trace.executionContext) && nonempty(trace.reason), "invalid execution context/reason");
  check(Array.isArray(trace.path) && trace.path.length > 0 && trace.path.length <= 4, "helper depth exceeded");
  const used = new Set<string>();
  for (const [index, step] of trace.path.entries()) {
    keys(step, ["ownerId", "callId", "file", "member", ...locationKeys]); extent(step);
    const owner = owners.get(step.ownerId);
    check(owner && owner.file === row.file && step.file === owner.file && step.member === owner.member &&
      step.line >= owner.line && step.endLine <= owner.endLine && !used.has(owner.id), "path owner/location mismatch");
    used.add(owner.id);
    check(step.callId === fingerprint([step.ownerId, location(step)]), "path call identity mismatch");
    if (!index) check(step.ownerId === row.id, "path must start at async owner");
    else {
      const prefix = trace.path.slice(0, index);
      const link = links.get(fingerprint(prefix));
      check(link?.targetOwnerId === step.ownerId && (trace.executionContext === link.executionContext || trace.executionContext === "deferred-generator-body" ||
        link.executionContext === "async-body" && ["thread-helper-callback", "wrapper-callback-reference"].includes(trace.executionContext)), "missing or inconsistent helper path prefix");
      check(owner.functionKind === "FunctionDef", "helper must be synchronous");
    }
  }
  if (trace.status === "blocking-candidate") check(blocking.includes(trace.target!) && ["catalogue-import-spelling", "catalogue-callback-import-spelling"].includes(trace.reason), "invalid blocking catalogue observation");
  else if (trace.status === "local-helper-link") {
    const target = owners.get(trace.targetOwnerId!);
    check(trace.target === null && trace.reason === "bounded-lexical-definition" && target?.functionKind === "FunctionDef" &&
      target.file === row.file && !used.has(target.id) && target.line < trace.path.at(-1)!.line && trace.path.length <= 3, "invalid local helper link");
  } else check(trace.status === "unresolved" && trace.target === null, "invalid unresolved trace");
}

/** Validate provenance, path/source identities and accounting; this is not independent AST replay. */
export function validateAsyncHelpers(value: unknown, base: PythonAsyncUsage, context: PythonAsyncContext, adapters: PythonAsyncAdapters): asserts value is PythonAsyncHelpers {
  keys(value, [...commonKeys, "asyncRowsSha256"]);
  const report = value as PythonAsyncHelpers;
  check(report.asyncRowsSha256 === fingerprint(base.functions), "async rows hash mismatch");
  if (!envelope(report, base, asyncHelperPolicy, "experimental-python-async-helper-paths", "codemetrics-ai/python-async-helper-paths",
    { policy: asyncHelperPolicy, asyncConfigurationFingerprint: base.configurationFingerprint, maxHelperDepth: 3, maxSteps: 100000 })) return;
  const observed = base.functions.filter(owner => owner.status === "observed");
  same(report.functions.map(row => row.id), observed.map(row => row.id), "async owner population/order mismatch");
  const owners = new Map(base.functions.map(row => [row.id, row]));
  let total = 0;
  for (const row of report.functions) {
    keys(row, [...identityKeys, "traces", "disposition"]);
    const original = owners.get(row.id)!;
    for (const key of identityKeys) same(row[key], original[key], "owner provenance mismatch");
    check(row.disposition === "review-only" && Array.isArray(row.traces), "invalid helper row");
    total += row.traces.length; check(total <= 200000, "trace budget exceeded");
    const links = new Map(row.traces.filter(trace => trace.status === "local-helper-link").map(trace => [fingerprint(trace.path), trace]));
    const unique = new Set<string>();
    const calls = new Map(original.measurement!.calls.map(call => [call.id, call]));
    const contextual = new Map(context.functions.find(owner => owner.id === row.id)!.calls.map(call => [call.callId, call]));
    const adapted = new Map(adapters.functions.find(owner => owner.id === row.id)!.calls.map(call => [call.callId, call]));
    for (const trace of row.traces) {
      validatePath(trace, row, owners, links);
      const identity = fingerprint(trace); check(!unique.has(identity), "duplicate trace"); unique.add(identity);
      const root = trace.path[0], call = calls.get(root.callId);
      check(call && isDeepStrictEqual(location(root), location(call)), "unknown root call");
      if (trace.path.length !== 1) continue;
      if (call.category === "blocking-call-candidate") {
        check(trace.status === "blocking-candidate" && trace.target === call.binding.target && trace.executionContext === call.executionContext && trace.reason === "catalogue-import-spelling", "direct blocking observation changed");
      } else if (trace.status === "blocking-candidate") {
        const old = contextual.get(call.id)!, adapter = adapted.get(call.id)!;
        const partial = adapter.partialCallee.partial?.callback?.reference;
        const callback = adapter.callbackPartial?.partial?.callback?.reference ?? old.callback?.reference;
        const callbackMode = call.executionContext === "async-body" ? "thread-helper-callback" : call.executionContext;
        check(trace.reason === "catalogue-callback-import-spelling" && [
          { reference: partial, mode: call.executionContext },
          { reference: callback, mode: call.category === "offload-wrapper-construction" ? "wrapper-callback-reference" : callbackMode }
        ].some(item => item.reference?.status === "import-spelling" && item.reference.target === trace.target && item.mode === trace.executionContext), "callback catalogue observation has no matching reference/context");
      } else if (trace.status === "local-helper-link") {
        const old = contextual.get(call.id)!, adapter = adapted.get(call.id)!;
        const partial = adapter.partialCallee.partial?.callback?.reference;
        const callback = adapter.callbackPartial?.partial?.callback?.reference ?? old.callback?.reference;
        const callbackMode = call.executionContext === "async-body" ? "thread-helper-callback" : call.executionContext;
        const references = [
          { reference: old.localCallee, mode: call.executionContext }, { reference: partial, mode: call.executionContext },
          { reference: adapter.decoratedCallee, mode: callbackMode },
          { reference: callback, mode: call.category === "offload-wrapper-construction" ? "wrapper-callback-reference" : callbackMode }
        ];
        check(references.some(item => item.reference?.definition?.ownerId === trace.targetOwnerId && item.mode === trace.executionContext), "root helper link has no prior source reference");
      }
    }
    for (const call of calls.values()) if (call.category === "blocking-call-candidate")
      check(row.traces.filter(trace => trace.path.length === 1 && trace.path[0].callId === call.id).length === 1, "missing direct blocking trace");
    else if (call.category === "unresolved-call")
      check(row.traces.some(trace => trace.path.length === 1 && trace.path[0].callId === call.id), "missing unresolved or linked root call");
  }
  const expected = { ...helperCounts(report.functions), populations: Object.fromEntries([...new Set(report.functions.map(row => row.population))].sort()
    .map(population => [population, helperCounts(report.functions.filter(row => row.population === population))])) };
  same(report.summary, expected, "helper summary mismatch");
}

export function asyncOwnerScore(affected: number, eligible: number): number | null {
  check(Number.isSafeInteger(affected) && Number.isSafeInteger(eligible) && affected >= 0 && affected <= eligible, "invalid owner counts");
  if (!eligible) return null;
  for (const band of asyncLadder) if (100 * affected < band.percent * eligible || band.inclusive && 100 * affected === band.percent * eligible) return band.score;
  return 0;
}

export function asyncScoreSummary(rows: Contribution[]) {
  const eligible = rows.filter(row => row.eligible), affected = eligible.filter(row => row.affected).length;
  const observations = eligible.flatMap(row => row.observations), unresolved = eligible.flatMap(row => row.unresolvedPaths);
  return { availability: eligible.length ? "available" : "unavailable", eligibleOwners: eligible.length, affectedOwners: affected,
    score: asyncOwnerScore(affected, eligible.length), affectedRate: eligible.length ? { numerator: affected, denominator: eligible.length } : null,
    excludedOwnersByPopulation: histogram(rows.filter(row => !row.eligible).map(row => row.population)),
    observationPathsByClassification: histogram(observations.map(item => item.classification)),
    distinctScoredSourceCalls: new Set(observations.filter(item => item.classification === "scored-signal").map(item => item.path.at(-1)!.callId)).size,
    ownersWithUnresolvedPaths: eligible.filter(row => row.unresolvedPaths.length).length,
    unresolvedPathsByReason: histogram(unresolved.map(item => item.reason)) };
}

export function validateAsyncScoring(value: unknown, base: PythonAsyncHelpers, usage: PythonAsyncUsage): asserts value is PythonAsyncScoring {
  keys(value, [...commonKeys, "helperRowsSha256"]);
  const report = value as PythonAsyncScoring;
  check(report.helperRowsSha256 === fingerprint(base.functions), "helper rows hash mismatch");
  if (!envelope(report, base, pythonAsyncPolicy, "experimental-python-async-scoring", "codemetrics-ai/python-async-scoring",
    { policy: pythonAsyncPolicy, helperConfigurationFingerprint: base.configurationFingerprint, intentPolicy: asyncIntentPolicy,
      maxSourceIndexNodes: 1000000, ladder: asyncLadder, population: "selected-production-async-implementations", unresolvedTreatment: "visible-without-deduction" })) return;
  same(report.functions.map(row => row.id), base.functions.map(row => row.id), "scoring owner population/order mismatch");
  const owners = new Map(usage.functions.map(row => [row.id, row]));
  for (const [index, row] of report.functions.entries()) {
    keys(row, [...identityKeys, "eligible", "eligibilityReason", "affected", "observations", "unresolvedPaths"]);
    const original = base.functions[index];
    for (const key of identityKeys) same(row[key], original[key], "scoring owner provenance mismatch");
    check(row.eligible === (row.population === "production") && row.eligibilityReason === (row.eligible ? "production-async-implementation" : "nonproduction"), "scoring eligibility mismatch");
    check(Array.isArray(row.observations), "observations required");
    same(row.unresolvedPaths, original.traces.filter(trace => trace.status === "unresolved"), "unresolved evidence changed");
    const candidates = original.traces.filter(trace => trace.status === "blocking-candidate");
    check(row.observations.length === candidates.length, "candidate population mismatch");
    for (const [at, observation] of row.observations.entries()) {
      keys(observation, ["path", "executionContext", "status", "reason", "target", "documentation", "classification"]);
      const { documentation, classification, ...trace } = observation;
      same(trace, candidates[at], "candidate path changed");
      check(Array.isArray(documentation), "documentation required");
      const seen = new Set<string>();
      for (const item of documentation) {
        keys(item, ["file", "callId", "line", "text"]);
        const step = trace.path.find(part => part.callId === item.callId && part.file === item.file);
        const owner = step && owners.get(step.ownerId);
        check(owner && Number.isSafeInteger(item.line) && item.line > owner.line && item.line <= owner.endLine && nonempty(item.text), "documentation source mismatch");
        check(/\b(blocking|synchronous|sync-over-async|sleep)\b/i.test(item.text) && /\b(intentional(ly)?|deliberate(ly)?|required|necessary|unavoidable)\b/i.test(item.text) &&
          !/\b(TODO|FIXME|HACK|XXX|not|never|avoid|remove|replace|shouldn't|mustn't|unnecessary)\b/i.test(item.text) && !/^(noqa\b|type:|pragma:|fmt:|pylint:|ruff:)/i.test(item.text), "documentation convention mismatch");
        const key = fingerprint(item); check(!seen.has(key), "duplicate rationale"); seen.add(key);
      }
      if (trace.executionContext !== "async-body") check(!documentation.length && classification === "excluded-execution-context", "callback/deferred evidence cannot be scored");
      else check(classification === (documentation.length ? "documented-choice" : "scored-signal"), "classification mismatch");
    }
    check(row.affected === (row.eligible && row.observations.some(item => item.classification === "scored-signal")), "affected owner mismatch");
  }
  same(report.summary, asyncScoreSummary(report.functions), "async score/summary mismatch");
}

export function scorePythonAsync(dimension: Dimension, report: PythonAsyncScoring | null, complete: boolean): Dimension {
  const scope: Dimension["scope"] = { id: pythonAsyncPolicy, coverage: "partial",
    includes: ["selected-production-async-implementations", "blocking-import-catalogue", "bounded-eager-local-helpers", "documented-choice-exclusions", "explicit-unresolved-paths"],
    excludes: ["test-support-generated-scoring", "runtime-performance", "complete-blocking-detection", "resolved-receiver-types", "cross-file-call-graphs", "business-correctness"] };
  const details = complete && report?.status === "complete" ? asyncScoreSummary(report.functions) : null;
  if (details?.score === null || !details) return { ...dimension, scope,
    status: complete ? "skipped" : "failed", basis: complete ? "Async/Blocking Usage unavailable: no selected production async implementations." : "Python analysis incomplete; no async score is usable." };
  const findings: Finding[] = report!.functions.filter(row => row.observations.length).map(row => ({
    category: "pythonAsyncBlockingReview", ruleId: pythonAsyncPolicy, fingerprint: hash(`${pythonAsyncPolicy}:${row.id}`),
    severity: row.affected ? "warning" : "info", confidence: "medium", file: row.file,
    line: row.observations[0].path[0].line, member: row.member,
    message: row.affected ? "Supported blocking-call syntax appears in this async owner's eager path. Inspect required behavior and source context before changing execution."
      : "Blocking-call syntax is retained as excluded context or documented intent; no score deduction.",
    observations: { ownerId: row.id, population: row.population, eligible: row.eligible, affected: row.affected,
      paths: row.observations, classification: row.affected ? "actionableSignal" : "reviewLead" }
  }));
  const decision = firstMatch(pythonAsyncPolicy, { eligibleOwners: details.eligibleOwners, affectedOwners: details.affectedOwners,
    ownersWithUnresolvedPaths: details.ownersWithUnresolvedPaths, unresolvedPathsByReason: details.unresolvedPathsByReason,
    ladder: asyncLadder, counting: "distinct-selected-production-async-owner", intentPolicy: asyncIntentPolicy },
    [{ id: "affectedOwnerRate", condition: "nonempty selected production async implementation population", matched: true, score: details.score, categories: ["pythonAsyncBlockingReview"] }]);
  return { ...dimension, ...scored(decision, "Partial static blocking-catalogue usage score. Unresolved paths are not nonblocking evidence; score does not measure runtime performance.",
    findings, details, finding => finding.observations.affected === true), scope };
}
