"""Review unresolved async-call shapes and bounded source links, without scores."""

import argparse
import ast
from collections import Counter, defaultdict
from datetime import datetime, timezone
import json
from pathlib import Path
import sys

import async_usage as usage
from async_provenance import ContextCalls, Provenance


POLICY = "python/async-call-context-review-v1"
MAX_EXAMPLES = 3


def inventory(rows, context):
    groups = defaultdict(list)
    for row in rows:
        for call in row["calls"]:
            if call["executionContext"] == context and call["unresolvedReason"] is not None:
                groups[(call["unresolvedReason"], call["shape"])].append({"ownerId": row["id"], "callId": call["callId"]})
    return [{"reason": reason, "shape": shape, "callExpressions": len(items), "examples": items[:MAX_EXAMPLES]}
            for (reason, shape), items in sorted(groups.items())]


def population_summary(rows):
    eligible = [row for row in rows if row["status"] == "observed"]
    calls = [call for row in eligible for call in row["calls"]]
    contexts = {}
    for context in ("async-body", "deferred-generator-body"):
        selected = [call for call in calls if call["executionContext"] == context]
        unmatched = [call for call in selected if call["unresolvedReason"] is not None]
        contexts[context] = None if not eligible else {
            "callExpressions": len(selected), "unresolvedImportCallExpressions": len(unmatched),
            "unresolvedReasons": dict(sorted(Counter(call["unresolvedReason"] for call in unmatched).items())),
            "unresolvedShapes": dict(sorted(Counter(call["shape"] for call in unmatched).items())),
            "unresolvedInventory": inventory(eligible, context),
            "localCalleeLinks": sum(bool(call["localCallee"] and call["localCallee"]["status"] == "local-definition") for call in selected),
            "storedWrapperInvocationLinks": sum(bool(call["storedWrapper"] and call["storedWrapper"]["status"] == "stored-wrapper-syntax") for call in selected),
            "localCallbackReferenceLinks": sum(bool(call["callback"] and call["callback"]["reference"]["status"] == "local-definition") for call in selected)}
    return {"selectedFunctions": len(rows), "asyncFunctions": len(eligible),
            "excludedSynchronousFunctions": len(rows) - len(eligible),
            "availability": "available" if eligible else "unavailable", "contexts": contexts}


def collect(root, facts, base):
    owners = {row["id"]: row for row in facts["functions"]}
    prior = {row["id"]: row for row in base["functions"]}
    rows = []
    for record in facts["selection"]["files"]:
        if record["disposition"] != "analyzed":
            continue
        tree = ast.parse(usage.read_verified(root, record), filename=record["file"])
        bindings = usage.Bindings(tree)
        provenance = Provenance(bindings, owners, record["file"])
        for node in bindings.scopes:
            if not isinstance(node, usage.source_facts.OWNERS):
                continue
            key = usage.digest(f"{record['file']}|{node.lineno}|{node.col_offset}|{type(node).__name__}")
            owner = owners.get(key)
            if owner is None:
                continue
            original = prior[key]
            row = {name: original[name] for name in ("id", "file", "member", "line", "endLine", "population", "sourceSha256", "status", "reason")}
            row.update(disposition="review-only", calls=[])
            if isinstance(node, ast.AsyncFunctionDef):
                measurement, row["calls"] = ContextCalls(bindings, node, owner, provenance).measure(node)
                if measurement != original["measurement"]:
                    raise ValueError("Async ownership/call evidence changed during context review.")
            rows.append(row)
    if len(rows) != len(owners) or {row["id"] for row in rows} != set(owners):
        raise ValueError("Selected owners changed during async context review.")
    rows.sort(key=lambda row: (row["file"], row["line"], row["id"]))
    return rows, {**population_summary(rows), "populations": {
        population: population_summary([row for row in rows if row["population"] == population])
        for population in sorted({row["population"] for row in rows})}}


def review(target, facts, base):
    configuration = {"policy": POLICY, "asyncConfigurationFingerprint": base["configurationFingerprint"],
                     "maxExamplesPerGroup": MAX_EXAMPLES}
    report = {"schemaVersion": 1, "artifactKind": "experimental-python-async-call-context-review",
              "producer": {"name": "codemetrics-ai/python-async-call-context", "version": "0.1.0-experimental"},
              "generatedAtUtc": datetime.now(timezone.utc).isoformat(), "runId": base["runId"], "auditId": base["auditId"],
              "policy": POLICY, "configuration": configuration, "configurationFingerprint": usage.fingerprint(configuration),
              "subject": base["subject"], "selection": facts["selection"], "sourceFactsProducer": facts["producer"],
              "sourceFactsPolicy": facts["measurementPolicy"], "asyncRowsSha256": usage.fingerprint(base["functions"]),
              "status": "incomplete", "diagnostics": [], "functions": [], "summary": None,
              "scope": "Unresolved import-call inventory and bounded lexical source links. No execution, runtime identity, callback-body traversal, offload-success, safety, defect, or score claim. Both Async/Blocking Usage and Error Handling remain unscored."}
    try:
        fresh = usage.review(target, facts, run_id=base["runId"], audit_id=base["auditId"])
        stable = lambda value: {key: item for key, item in value.items() if key != "generatedAtUtc"}
        if fresh["status"] != "complete" or stable(fresh) != stable(base):
            raise ValueError("Async input is incomplete, stale, or inconsistent; collect fresh evidence.")
        target = Path(target).absolute()
        root = target if target.is_dir() else target.parent
        functions, summary = collect(root, facts, base)
        report.update(status="complete", functions=functions, summary=summary)
    except (OSError, SyntaxError, UnicodeError, ValueError, RecursionError, KeyError, TypeError) as error:
        report["diagnostics"].append({"code": "async-context-analysis", "message": str(error)})
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
        print(f"Cannot produce async context review: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
