"""Trace bounded local synchronous helpers with explicit callback execution context."""

import ast
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

from async_adapters import Adapters, AdapterCalls
from async_provenance import ContextCalls
import async_usage as usage
from review_cli import run

POLICY = "python/async-local-helper-paths-review-v1"
MAX_DEPTH = 3
MAX_STEPS = 100000


def partial_callback(value):
    if value and value["status"] in {"inline-partial-syntax", "stored-partial-syntax"}:
        callback = value["partial"]["callback"]
        return callback["reference"] if callback else None
    return None


class FilePaths:
    def __init__(self, tree, owners, file, budget):
        self.bindings = usage.Bindings(tree)
        self.owners, self.file, self.budget = owners, file, budget
        self.provenance = Adapters(self.bindings, owners, file)
        self.nodes, self.cache = {}, {}
        for node in self.bindings.scopes:
            if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
                key = usage.digest(f"{file}|{node.lineno}|{node.col_offset}|{type(node).__name__}")
                if key in owners:
                    self.nodes[key] = node

    def spend(self, amount=1):
        self.budget[0] += amount
        if self.budget[0] > MAX_STEPS:
            raise ValueError("Local helper measurement/traversal budget exceeded.")

    def measure(self, key):
        if key not in self.cache:
            node, owner = self.nodes[key], self.owners[key]
            contextual = ContextCalls(self.bindings, node, owner, self.provenance)
            measurement, contexts = contextual.measure(node)
            adapter = AdapterCalls(self.bindings, node, owner, self.provenance)
            if adapter.measure(node) != measurement:
                raise ValueError("Local helper call measurements disagree.")
            self.spend(len(measurement["calls"]))
            self.cache[key] = measurement, {row["callId"]: row for row in contexts}, adapter.observations
        return self.cache[key]

    def follow(self, reference, path, execution, visited, traces):
        if reference and reference["status"] == "import-spelling" and reference.get("target") in usage.BLOCKING:
            traces.append({"path": path, "executionContext": execution, "status": "blocking-candidate",
                           "reason": "catalogue-callback-import-spelling", "target": reference["target"]})
            return
        if not reference or reference["status"] not in {"local-definition", "decorated-wrapper-syntax"}:
            traces.append({"path": path, "executionContext": execution, "status": "unresolved",
                           "reason": (reference or {}).get("reason", "unsupported-callback"), "target": None})
            return
        key = reference["definition"]["ownerId"]
        reason = None
        if key in visited:
            reason = "recursive-or-repeated-owner"
        elif len(visited) - 1 >= MAX_DEPTH:
            reason = "helper-depth-limit"
        elif not isinstance(self.nodes.get(key), ast.FunctionDef):
            reason = "async-or-unselected-callee"
        elif self.measure(key)[0]["kind"] == "async-generator":
            # Calls.measure also notices yields in synchronous functions. Calling
            # such a function constructs a generator; it does not run its body.
            reason = "generator-body-deferred"
        if reason:
            traces.append({"path": path, "executionContext": execution, "status": "unresolved", "reason": reason, "target": None})
            return
        traces.append({"path": path, "executionContext": execution, "status": "local-helper-link",
                       "reason": "bounded-lexical-definition", "target": None, "targetOwnerId": key})
        self.walk(key, path, execution, visited | {key}, traces)

    def walk(self, key, prefix, execution, visited, traces):
        measurement, contexts, adapters = self.measure(key)
        for call in measurement["calls"]:
            self.spend()
            lexical, adapter = contexts[call["id"]], adapters[call["id"]]
            mode = "deferred-generator-body" if call["executionContext"] == "deferred-generator-body" else execution
            owner = self.owners[key]
            path = prefix + [{"ownerId": key, "callId": call["id"], "file": self.file,
                              "member": owner["member"], **{name: call[name] for name in ("line", "column", "endLine", "endColumn")}}]
            if call["category"] == "blocking-call-candidate":
                traces.append({"path": path, "executionContext": mode, "status": "blocking-candidate",
                               "reason": "catalogue-import-spelling", "target": call["binding"]["target"]})
                continue
            decorated = adapter["decoratedCallee"]
            if decorated["status"] == "decorated-wrapper-syntax":
                self.follow(decorated, path, self.callback_mode(mode), visited, traces)
                continue
            local = lexical["localCallee"]
            partial = partial_callback(adapter["partialCallee"])
            if local and local["status"] == "local-definition":
                self.follow(local, path, mode, visited, traces)
            elif partial:
                self.follow(partial, path, mode, visited, traces)
            elif call["category"] == "unresolved-call" and not (lexical["storedWrapper"] and
                    lexical["storedWrapper"]["status"] == "stored-wrapper-syntax"):
                traces.append({"path": path, "executionContext": mode, "status": "unresolved",
                               "reason": (local or {}).get("reason", lexical["unresolvedReason"]), "target": None})
            if lexical["callback"]:
                callback = partial_callback(adapter["callbackPartial"]) or lexical["callback"]["reference"]
                # Constructor callbacks are only references, never evidence of an invocation.
                callback_mode = "wrapper-callback-reference" if call["category"] == "offload-wrapper-construction" else self.callback_mode(mode)
                self.follow(callback, path, callback_mode, visited, traces)

    @staticmethod
    def callback_mode(mode):
        return "thread-helper-callback" if mode == "async-body" else mode


def summarize(rows):
    def counts(items):
        traces = [trace for row in items for trace in row["traces"]]
        blocking = [trace for trace in traces if trace["status"] == "blocking-candidate"]
        return {"asyncFunctions": len(items), "availability": "available" if items else "unavailable",
            "functionsWithDirectOrHelperCandidates": sum(any(trace["status"] == "blocking-candidate" and
                trace["executionContext"] == "async-body" for trace in row["traces"]) for row in items) if items else None,
            "candidatePathsByContext": dict(sorted(Counter(trace["executionContext"] for trace in blocking).items())),
            "unresolvedPathsByReason": dict(sorted(Counter(trace["reason"] for trace in traces if trace["status"] == "unresolved").items())),
            "candidatePaths": len(blocking),
            "localHelperLinks": sum(trace["status"] == "local-helper-link" for trace in traces),
            "distinctBlockingSourceCalls": len({trace["path"][-1]["callId"] for trace in blocking})}
    return {**counts(rows), "populations": {population: counts([row for row in rows if row["population"] == population])
        for population in sorted({row["population"] for row in rows})}}


def review(target, facts, base):
    configuration = {"policy": POLICY, "asyncConfigurationFingerprint": base["configurationFingerprint"],
                     "maxHelperDepth": MAX_DEPTH, "maxSteps": MAX_STEPS}
    report = {"schemaVersion": 1, "artifactKind": "experimental-python-async-helper-paths",
        "producer": {"name": "codemetrics-ai/python-async-helper-paths", "version": "0.1.0-experimental"},
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(), "runId": base["runId"], "auditId": base["auditId"],
        "policy": POLICY, "configuration": configuration, "configurationFingerprint": usage.fingerprint(configuration),
        "asyncRowsSha256": usage.fingerprint(base["functions"]), "subject": base["subject"], "selection": facts["selection"],
        "sourceFactsProducer": facts["producer"], "sourceFactsPolicy": facts["measurementPolicy"],
        "status": "incomplete", "diagnostics": [], "functions": [], "summary": None,
        "scope": "Bounded same-file lexical synchronous helper paths and callback context. Import spelling, not runtime identity, reachability, successful offloading or performance. Both dimensions remain unscored."}
    try:
        fresh = usage.review(target, facts, run_id=base["runId"], audit_id=base["auditId"])
        stable = lambda value: {key: item for key, item in value.items() if key != "generatedAtUtc"}
        if fresh["status"] != "complete" or stable(fresh) != stable(base):
            raise ValueError("Async input is incomplete, stale, or inconsistent.")
        target = Path(target).absolute()
        root = target if target.is_dir() else target.parent
        owners = {row["id"]: row for row in facts["functions"]}
        originals = {row["id"]: row for row in base["functions"] if row["status"] == "observed"}
        rows, budget = [], [0]
        for record in facts["selection"]["files"]:
            if record["disposition"] != "analyzed":
                continue
            paths = FilePaths(ast.parse(usage.read_verified(root, record)), owners, record["file"], budget)
            for key, node in paths.nodes.items():
                if not isinstance(node, ast.AsyncFunctionDef):
                    continue
                if paths.measure(key)[0] != originals[key]["measurement"]:
                    raise ValueError("Original async calls changed during helper analysis.")
                traces = []
                paths.walk(key, [], "async-body", {key}, traces)
                row = {name: originals[key][name] for name in ("id", "file", "member", "line", "population", "sourceSha256")}
                rows.append({**row, "traces": traces, "disposition": "review-only"})
        if {row["id"] for row in rows} != set(originals):
            raise ValueError("Async owners changed during helper analysis.")
        rows.sort(key=lambda row: (row["file"], row["line"], row["id"]))
        report.update(status="complete", functions=rows, summary=summarize(rows))
    except (OSError, SyntaxError, UnicodeError, ValueError, RecursionError, KeyError, TypeError) as error:
        report["diagnostics"].append({"code": "async-helper-analysis", "message": str(error)})
    return report


def analyze(target, *, run_id=None, audit_id=None, **selection):
    facts = usage.source_facts.analyze(target, **selection)
    base = usage.review(target, facts, run_id=run_id, audit_id=audit_id)
    return review(target, facts, base)


if __name__ == "__main__":
    raise SystemExit(run(analyze, __doc__))
