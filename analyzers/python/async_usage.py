"""Offline async-body call evidence; no Async/Blocking Usage score or execution."""

import argparse
import ast
from collections import Counter, defaultdict
from datetime import datetime, timezone
import json
from pathlib import Path
import sys
import uuid

from async_bindings import Bindings, COMPREHENSIONS, DEFINITIONS, dotted
from duplication import digest, read_verified
import source_facts


POLICY = "python/async-owned-calls-review-v1"
MAX_OWNERS = 100000
MAX_CALLS = 100000
BLOCKING = {"time.sleep", "subprocess.run", "subprocess.call", "subprocess.check_call",
            "subprocess.check_output", "os.system", "urllib.request.urlopen"}
OFFLOAD = {"asyncio.to_thread", "anyio.to_thread.run_sync", "starlette.concurrency.run_in_threadpool"}
WRAPPER = "asgiref.sync.sync_to_async"


def fingerprint(value):
    return digest(json.dumps(value, sort_keys=True, separators=(",", ":")))


def extent(node):
    return {"line": node.lineno, "column": node.col_offset, "endLine": node.end_lineno, "endColumn": node.end_col_offset}


class Calls:
    def __init__(self, bindings, node, owner):
        self.bindings, self.scope, self.owner = bindings, bindings.scopes[node], owner
        self.rows = []
        self.awaits = Counter()
        self.yields = 0

    def resolve(self, node, shadowed):
        target, reason = self.bindings.resolve(node, self.scope, shadowed=shadowed)
        return {"syntax": dotted(node), "target": target, "status": "import-matched" if target else "unresolved", "reason": reason}

    def callback(self, call, shadowed):
        value = call.args[0] if call.args and not isinstance(call.args[0], ast.Starred) else None
        if value is None:
            return {"kind": "unresolved-argument-layout", "binding": None}
        return {"kind": type(value).__name__, "binding": self.resolve(value, shadowed),
                "evaluation": "eager-call-expression" if isinstance(value, ast.Call) else "callback-expression-only"}

    def record(self, node, context, shadowed, parent):
        binding = self.resolve(node.func, shadowed)
        target = binding["target"]
        category = "other-import-call" if target else "unresolved-call"
        callback = None
        if target in BLOCKING:
            category = "blocking-call-candidate"
        elif target in OFFLOAD or target == WRAPPER:
            category = "offload-call" if target in OFFLOAD else "offload-wrapper-construction"
            callback = self.callback(node, shadowed)
        elif isinstance(node.func, ast.Call):
            wrapped = self.resolve(node.func.func, shadowed)
            if wrapped["target"] == WRAPPER and node.func.args and not isinstance(node.func.args[0], ast.Starred):
                category = "offload-wrapped-invocation"
                binding = {**wrapped, "reason": "immediate-import-matched-wrapper-syntax"}
                callback = self.callback(node.func, shadowed)
        location = extent(node)
        self.rows.append({"id": fingerprint([self.owner["id"], location]), **location,
                          "executionContext": context, "category": category, "binding": binding,
                          "directlyAwaited": isinstance(parent, ast.Await), "callback": callback,
                          "disposition": "review-only"})
        if len(self.rows) > MAX_CALLS:
            raise ValueError("Async call population exceeds the call budget.")

    def comprehension(self, node, context, shadowed):
        # Only the first iterable is evaluated when a generator is constructed.
        first, *rest = node.generators
        self.visit(first.iter, context, shadowed, node)
        inner_context = "deferred-generator-body" if isinstance(node, ast.GeneratorExp) else context
        names = {child.id for generator in node.generators for child in ast.walk(generator.target) if isinstance(child, ast.Name)}
        inner_shadowed = shadowed | names
        for condition in first.ifs:
            self.visit(condition, inner_context, inner_shadowed, node)
        for generator in rest:
            self.visit(generator.iter, inner_context, inner_shadowed, node)
            for condition in generator.ifs:
                self.visit(condition, inner_context, inner_shadowed, node)
        for field in ("elt", "key", "value"):
            if hasattr(node, field):
                self.visit(getattr(node, field), inner_context, inner_shadowed, node)

    def visit(self, node, context="async-body", shadowed=frozenset(), parent=None):
        if isinstance(node, DEFINITIONS):
            return
        if isinstance(node, COMPREHENSIONS):
            self.comprehension(node, context, shadowed)
            return
        if isinstance(node, ast.Call):
            self.record(node, context, shadowed, parent)
        elif isinstance(node, ast.Await):
            self.awaits[context] += 1
        elif isinstance(node, (ast.Yield, ast.YieldFrom)):
            self.yields += 1
        # Type aliases and annotations are declaration syntax, not body calls.
        if isinstance(node, ast.TypeAlias):
            return
        if isinstance(node, ast.AnnAssign):
            self.visit(node.target, context, shadowed, node)
            if node.value:
                self.visit(node.value, context, shadowed, node)
            return
        for child in ast.iter_child_nodes(node):
            self.visit(child, context, shadowed, node)

    def measure(self, node):
        for statement in node.body:
            self.visit(statement)
        self.rows.sort(key=lambda row: (row["line"], row["column"], row["endLine"], row["endColumn"]))
        return {"kind": "async-generator" if self.yields else "coroutine",
                "awaitExpressions": dict(sorted(self.awaits.items())), "calls": self.rows}


def population_summary(rows):
    eligible = [row for row in rows if row["status"] == "observed"]
    calls = [call for row in eligible for call in row["measurement"]["calls"]]
    direct = [call for call in calls if call["executionContext"] == "async-body"]
    return {"selectedFunctions": len(rows), "asyncFunctions": len(eligible), "excludedSynchronousFunctions": len(rows) - len(eligible),
            "availability": "available" if eligible else "unavailable",
            "asyncBodyCallExpressions": len(direct) if eligible else None,
            "asyncBodyCallCategories": dict(sorted(Counter(call["category"] for call in direct).items())) if eligible else None,
            "deferredGeneratorCallExpressions": len(calls) - len(direct) if eligible else None,
            "functionsWithBlockingCandidates": sum(any(call["category"] == "blocking-call-candidate" and
                call["executionContext"] == "async-body" for call in row["measurement"]["calls"]) for row in eligible) if eligible else None}


def collect(root, facts):
    if len(facts["functions"]) > MAX_OWNERS:
        raise ValueError("Selected function population exceeds the async owner budget.")
    by_file = defaultdict(dict)
    for function in facts["functions"]:
        by_file[function["file"]][function["id"]] = function
    rows, call_count = [], 0
    for record in facts["selection"]["files"]:
        if record["disposition"] != "analyzed":
            continue
        tree = ast.parse(read_verified(root, record), filename=record["file"])
        bindings = Bindings(tree)
        for node in bindings.scopes:
            if not isinstance(node, source_facts.OWNERS):
                continue
            key = digest(f"{record['file']}|{node.lineno}|{node.col_offset}|{type(node).__name__}")
            owner = by_file[record["file"]].get(key)
            if owner is None:
                continue
            row = {name: owner[name] for name in ("id", "file", "member", "line", "population")}
            row.update(sourceSha256=record["contentSha256"], endLine=node.end_lineno, functionKind=type(node).__name__,
                       disposition="review-only", status="excluded", reason="synchronous-owner", measurement=None)
            if isinstance(node, ast.AsyncFunctionDef):
                row.update(status="observed", reason=None, measurement=Calls(bindings, node, owner).measure(node))
                call_count += len(row["measurement"]["calls"])
                if call_count > MAX_CALLS:
                    raise ValueError("Async call population exceeds the call budget.")
            rows.append(row)
    if {row["id"] for row in rows} != {row["id"] for row in facts["functions"]} or len(rows) != len(facts["functions"]):
        raise ValueError("Selected function population changed during async review.")
    rows.sort(key=lambda row: (row["file"], row["line"], row["id"]))
    return rows, {**population_summary(rows), "populations": {
        population: population_summary([row for row in rows if row["population"] == population])
        for population in sorted({row["population"] for row in rows})}}


def verify_facts(target, facts):
    options = facts["selection"]["options"]
    current = source_facts.analyze(target, include=options["include"], exclude=options["exclude"],
        test_paths=options["testPaths"], production_paths=options["productionPaths"], include_tests=options["includeTests"],
        include_generated=options["includeGenerated"], include_support=options["includeSupport"])
    if current != facts:
        raise ValueError("Source facts or selection changed; collect a fresh source snapshot.")


def review(target, facts, *, run_id=None, audit_id=None):
    target = Path(target).absolute()
    root = target if target.is_dir() else target.parent
    configuration = {"policy": POLICY, "selectionFingerprint": facts["selection"]["configurationFingerprint"],
                     "parserVersion": facts["producer"]["parserVersion"], "maxOwners": MAX_OWNERS, "maxCalls": MAX_CALLS,
                     "blockingCatalogue": sorted(BLOCKING), "offloadCatalogue": sorted(OFFLOAD), "wrapperCatalogue": [WRAPPER]}
    report = {"schemaVersion": 1, "artifactKind": "experimental-python-async-usage-review",
              "producer": {"name": "codemetrics-ai/python-async-usage", "version": "0.1.0-experimental"},
              "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
              "runId": str(uuid.UUID(run_id)) if run_id else str(uuid.uuid4()),
              "auditId": str(uuid.UUID(audit_id)) if audit_id else str(uuid.uuid4()),
              "policy": POLICY, "configuration": configuration, "configurationFingerprint": fingerprint(configuration),
              "subject": {"root": str(root), "entryPoint": str(target)},
              "sourceFactsProducer": facts["producer"], "sourceFactsPolicy": facts["measurementPolicy"],
              "selection": facts["selection"], "status": facts["status"], "diagnostics": list(facts["diagnostics"]),
              "scope": "Selected async bodies, exclusive of nested definitions and declaration expressions. Import spelling only; no runtime identity, reachability, duration, offload-success, safety, defect, or score claim. Synchronous owners excluded; deferred generator calls separate. Unmatched calls unresolved.",
              "functions": [], "summary": None}
    if facts["status"] != "complete":
        return report
    try:
        verify_facts(target, facts)
        report["functions"], report["summary"] = collect(root, facts)
    except (OSError, SyntaxError, UnicodeError, ValueError, RecursionError, KeyError, TypeError) as error:
        report["status"] = "incomplete"
        report["diagnostics"].append({"code": "async-usage-analysis", "message": str(error)})
    return report


def analyze(target, *, run_id=None, audit_id=None, **selection):
    return review(target, source_facts.analyze(target, **selection), run_id=run_id, audit_id=audit_id)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target")
    parser.add_argument("--output", type=Path, help="New .json file with an existing parent")
    parser.add_argument("--run-id")
    parser.add_argument("--audit-id")
    for name in ("include", "exclude", "test-path", "production-path"):
        parser.add_argument(f"--{name}", action="append", default=[])
    for name in ("include-tests", "include-generated", "include-support"):
        parser.add_argument(f"--{name}", action="store_true")
    args = parser.parse_args()
    try:
        report = analyze(args.target, run_id=args.run_id, audit_id=args.audit_id, include=args.include, exclude=args.exclude,
            test_paths=args.test_path, production_paths=args.production_path, include_tests=args.include_tests,
            include_generated=args.include_generated, include_support=args.include_support)
        payload = json.dumps(report, indent=2, ensure_ascii=True) + "\n"
        if args.output:
            if args.output.suffix != ".json":
                raise ValueError("Output must be a new .json file.")
            with args.output.open("x", encoding="utf-8") as stream:
                stream.write(payload)
        else:
            sys.stdout.write(payload)
        return 0 if report["status"] == "complete" else 2
    except (OSError, ValueError) as error:
        print(f"Cannot produce async review: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
