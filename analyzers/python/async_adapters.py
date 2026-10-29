"""Review partial construction and sync_to_async decorator syntax without scoring."""

import argparse
import ast
from datetime import datetime, timezone
import json
from pathlib import Path
import sys

import async_usage as usage
from async_provenance import Provenance, before, shape, unresolved


POLICY = "python/async-callback-adapters-review-v1"
PARTIAL = "functools.partial"


def expression(node):
    return {"kind": type(node).__name__, "shape": shape(node), **usage.extent(node)}


class Adapters(Provenance):
    def partial(self, node, scope, shadowed):
        if not isinstance(node, ast.Call):
            return None
        target, _ = self.bindings.resolve(node.func, scope, shadowed=shadowed)
        if target != PARTIAL:
            return None
        callback = node.args[0] if node.args and not isinstance(node.args[0], ast.Starred) else None
        return {"status": "partial-construction-syntax", "importTarget": target,
                "construction": usage.extent(node),
                "argumentLayout": "first-positional-callback" if callback else "unresolved",
                "callback": None if callback is None else {
                    **expression(callback), "reference": self.reference(callback, scope, shadowed)},
                "boundPositionalExpressions": [expression(arg) for arg in node.args[1:]] if callback else None,
                "keywordExpressions": [{"name": kw.arg, "unpacking": kw.arg is None,
                                        "value": expression(kw.value)} for kw in node.keywords]}

    def partial_reference(self, node, scope, shadowed, execution, owner_id):
        inline = self.partial(node, scope, shadowed)
        if inline:
            return {"status": "inline-partial-syntax", "constructionCallId":
                    usage.fingerprint([owner_id, inline["construction"]]), "partial": inline}
        if not isinstance(node, ast.Name):
            return unresolved("not-a-name-or-matched-partial")
        veto = self.veto(node.id, shadowed)
        if veto:
            return unresolved(veto)
        if execution != "async-body":
            return unresolved("deferred-execution-context")
        if len(scope.bindings.get(node.id, [])) != 1:
            return unresolved("not-a-unique-same-owner-binding")
        assignment = self.assignments[scope].get(node.id)
        if assignment is None or not before(assignment, node):
            return unresolved("no-direct-assignment-before-use")
        partial = self.partial(assignment.value, scope, frozenset())
        if partial is None:
            return unresolved("not-a-matched-partial-assignment")
        if partial["argumentLayout"] != "first-positional-callback":
            return unresolved("unresolved-argument-layout")
        return {"status": "stored-partial-syntax", "name": node.id,
                "assignment": usage.extent(assignment), "constructionCallId":
                usage.fingerprint([owner_id, partial["construction"]]), "partial": partial}

    def decorated(self, node, scope, shadowed):
        if not isinstance(node, ast.Name):
            return unresolved("non-name-reference")
        veto = self.veto(node.id, shadowed)
        if veto:
            return unresolved(veto)
        while scope:
            if node.id not in scope.bindings:
                scope = scope.parent
                continue
            if len(scope.bindings[node.id]) != 1:
                return unresolved("rebound-or-ambiguous-name")
            definition = self.definitions[scope].get(node.id)
            if definition is None or not before(definition, node):
                return unresolved("no-direct-definition-before-reference")
            if not isinstance(definition, ast.FunctionDef) or len(definition.decorator_list) != 1:
                return unresolved("unsupported-decorated-definition")
            decorator = definition.decorator_list[0]
            constructor = decorator.func if isinstance(decorator, ast.Call) else decorator
            target, _ = self.bindings.resolve(constructor, scope)
            if target != usage.WRAPPER:
                return unresolved("unmatched-decorator-import")
            options = []
            if isinstance(decorator, ast.Call):
                if decorator.args or any(kw.arg != "thread_sensitive" or
                    not isinstance(kw.value, ast.Constant) or type(kw.value.value) is not bool
                    for kw in decorator.keywords) or len(decorator.keywords) > 1:
                    return unresolved("unsupported-decorator-arguments")
                options = [{"name": kw.arg, "value": kw.value.value} for kw in decorator.keywords]
            key = usage.digest(f"{self.file}|{definition.lineno}|{definition.col_offset}|FunctionDef")
            owner = self.owners.get(key)
            if owner is None:
                return unresolved("definition-outside-selected-owners")
            return {"status": "decorated-wrapper-syntax", "importTarget": target,
                    "definition": {"ownerId": key, "file": self.file, "member": owner["member"],
                                   "functionKind": "FunctionDef", **usage.extent(definition)},
                    "decorator": {**usage.extent(decorator), "form": "factory" if isinstance(decorator, ast.Call)
                                  else "bare", "options": options}}
        return unresolved("no-local-definition")


class AdapterCalls(usage.Calls):
    def __init__(self, bindings, node, owner, adapters):
        super().__init__(bindings, node, owner)
        self.adapters, self.observations = adapters, {}

    def record(self, node, execution, shadowed, parent):
        super().record(node, execution, shadowed, parent)
        call = self.rows[-1]
        callback = None
        if call["category"] in {"offload-call", "offload-wrapper-construction", "offload-wrapped-invocation"}:
            helper = node.func if call["category"] == "offload-wrapped-invocation" else node
            if helper.args and not isinstance(helper.args[0], ast.Starred):
                callback = helper.args[0]
        # Existing stored wrapper support supplies the construction-time callback.
        callback_shadowed = shadowed
        if isinstance(node.func, ast.Name):
            wrapper, stored = self.adapters.wrapper(node.func, self.scope, shadowed, execution, self.owner["id"])
            if wrapper["status"] == "stored-wrapper-syntax":
                callback, callback_shadowed = stored, frozenset()
        partial_reference = lambda value, names: self.adapters.partial_reference(
            value, self.scope, names, execution, self.owner["id"])
        self.observations[call["id"]] = {
            "callId": call["id"], "executionContext": execution,
            "partialConstruction": self.adapters.partial(node, self.scope, shadowed),
            "partialCallee": partial_reference(node.func, shadowed),
            "decoratedCallee": self.adapters.decorated(node.func, self.scope, shadowed),
            "callbackPartial": None if callback is None else partial_reference(callback, callback_shadowed),
            "callbackDecorator": None if callback is None else self.adapters.decorated(callback, self.scope, callback_shadowed)}


def summary(rows):
    eligible = [row for row in rows if row["status"] == "observed"]
    contexts = {}
    for execution in ("async-body", "deferred-generator-body"):
        calls = [call for row in eligible for call in row["calls"] if call["executionContext"] == execution]
        linked = lambda field, statuses: sum(bool(call[field] and call[field]["status"] in statuses) for call in calls)
        contexts[execution] = None if not eligible else {
            "callExpressions": len(calls),
            "partialConstructions": linked("partialConstruction", {"partial-construction-syntax"}),
            "partialCalleeReferences": linked("partialCallee", {"inline-partial-syntax", "stored-partial-syntax"}),
            "partialCallbackReferences": linked("callbackPartial", {"inline-partial-syntax", "stored-partial-syntax"}),
            "decoratedCalleeReferences": linked("decoratedCallee", {"decorated-wrapper-syntax"}),
            "decoratedCallbackReferences": linked("callbackDecorator", {"decorated-wrapper-syntax"})}
    return {"selectedFunctions": len(rows), "asyncFunctions": len(eligible),
            "excludedSynchronousFunctions": len(rows) - len(eligible),
            "availability": "available" if eligible else "unavailable", "contexts": contexts}


def collect(root, facts, base):
    owners = {row["id"]: row for row in facts["functions"]}
    originals = {row["id"]: row for row in base["functions"]}
    rows = []
    for record in facts["selection"]["files"]:
        if record["disposition"] != "analyzed":
            continue
        bindings = usage.Bindings(ast.parse(usage.read_verified(root, record), filename=record["file"]))
        adapters = Adapters(bindings, owners, record["file"])
        for node in bindings.scopes:
            key = usage.digest(f"{record['file']}|{getattr(node, 'lineno', 0)}|{getattr(node, 'col_offset', 0)}|{type(node).__name__}")
            if key not in owners:
                continue
            original = originals[key]
            row = {name: original[name] for name in ("id", "file", "member", "line", "endLine", "population", "sourceSha256", "status", "reason")}
            row.update(disposition="review-only", calls=[])
            if isinstance(node, ast.AsyncFunctionDef):
                visitor = AdapterCalls(bindings, node, owners[key], adapters)
                measurement = visitor.measure(node)
                if measurement != original["measurement"]:
                    raise ValueError("Async call evidence changed during adapter review.")
                row["calls"] = [visitor.observations[call["id"]] for call in measurement["calls"]]
            rows.append(row)
    if len(rows) != len(owners) or {row["id"] for row in rows} != set(owners):
        raise ValueError("Selected owners changed during adapter review.")
    rows.sort(key=lambda row: (row["file"], row["line"], row["id"]))
    return rows, {**summary(rows), "populations": {population: summary([row for row in rows if row["population"] == population])
        for population in sorted({row["population"] for row in rows})}}


def review(target, facts, base):
    configuration = {"policy": POLICY, "asyncConfigurationFingerprint": base["configurationFingerprint"]}
    report = {"schemaVersion": 1, "artifactKind": "experimental-python-async-callback-adapters-review",
              "producer": {"name": "codemetrics-ai/python-async-callback-adapters", "version": "0.1.0-experimental"},
              "generatedAtUtc": datetime.now(timezone.utc).isoformat(), "runId": base["runId"], "auditId": base["auditId"],
              "policy": POLICY, "configuration": configuration, "configurationFingerprint": usage.fingerprint(configuration),
              "subject": base["subject"], "selection": facts["selection"], "sourceFactsProducer": facts["producer"],
              "sourceFactsPolicy": facts["measurementPolicy"], "asyncRowsSha256": usage.fingerprint(base["functions"]),
              "status": "incomplete", "diagnostics": [], "functions": [], "summary": None,
              "scope": "Partial construction and decorator syntax only. No runtime identity, execution, callback-body traversal, successful offload, safety, performance or score claim. Async/Blocking Usage and Error Handling remain unscored."}
    try:
        fresh = usage.review(target, facts, run_id=base["runId"], audit_id=base["auditId"])
        stable = lambda value: {key: item for key, item in value.items() if key != "generatedAtUtc"}
        if fresh["status"] != "complete" or stable(fresh) != stable(base):
            raise ValueError("Async input is incomplete, stale, or inconsistent; collect fresh evidence.")
        target = Path(target).absolute()
        rows, counts = collect(target if target.is_dir() else target.parent, facts, base)
        report.update(status="complete", functions=rows, summary=counts)
    except (OSError, SyntaxError, UnicodeError, ValueError, RecursionError, KeyError, TypeError) as error:
        report["diagnostics"].append({"code": "async-adapter-analysis", "message": str(error)})
    return report


def analyze(target, *, run_id=None, audit_id=None, **selection):
    facts = usage.source_facts.analyze(target, **selection)
    base = usage.review(target, facts, run_id=run_id, audit_id=audit_id)
    return review(target, facts, base)


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
        print(f"Cannot produce async adapter review: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
