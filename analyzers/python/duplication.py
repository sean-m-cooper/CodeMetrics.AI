"""Offline exact-body duplication review. Parse target source; never execute it."""

import argparse
import ast
from collections import Counter, defaultdict
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import sys
import tokenize
import uuid

import source_facts


POLICY = "python/exact-function-body-v1"
MAX_NORMALIZED_BYTES = 64 * 1024 * 1024
MAX_OWNERS = 100000


def digest(value):
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def body_signature(node):
    """Ignore only the owner's docstring and AST locations, not names/literals."""
    if isinstance(node, ast.Lambda):
        return None, None, "lambda"
    body = node.body
    if body and isinstance(body[0], ast.Expr) and isinstance(body[0].value, ast.Constant) and isinstance(body[0].value.value, str):
        body = body[1:]
    tree = ast.Module(body=body, type_ignores=[])
    if any(isinstance(child, source_facts.OWNERS + (ast.ClassDef,)) for child in ast.walk(tree)):
        return None, None, "nested-definition"
    statements = sum(isinstance(child, ast.stmt) and not isinstance(child, (ast.Pass, ast.Global, ast.Nonlocal))
                     and not (isinstance(child, ast.AnnAssign) and child.value is None) for child in ast.walk(tree))
    return ast.dump(tree, annotate_fields=True, include_attributes=False), statements, None


def read_verified(root, record):
    path = root / record["file"]
    if source_facts.linked(path) or not path.resolve().is_relative_to(root.resolve()):
        raise ValueError("Selected source became linked or left the root.")
    if path.stat().st_size > source_facts.MAX_FILE_BYTES:
        raise ValueError("Selected source exceeds the analyzer file budget.")
    with tokenize.open(path) as handle:
        source = handle.read()
    if digest(source) != record["contentSha256"]:
        raise ValueError("Selected source changed after population measurement.")
    return source


def collect(root, facts, minimum_statements):
    by_file = defaultdict(dict)
    for function in facts["functions"]:
        by_file[function["file"]][function["id"]] = function
    groups, exclusions = defaultdict(list), []
    eligible = 0
    normalized_bytes = 0
    if len(facts["functions"]) > MAX_OWNERS:
        raise ValueError("Selected function population exceeds the duplication owner budget.")
    for record in facts["selection"]["files"]:
        if record["disposition"] != "analyzed":
            continue
        source = read_verified(root, record)
        seen = set()
        for node in ast.walk(ast.parse(source, filename=record["file"])):
            if not isinstance(node, source_facts.OWNERS):
                continue
            key = digest(f"{record['file']}|{node.lineno}|{node.col_offset}|{type(node).__name__}")
            function = by_file[record["file"]].get(key)
            if function is None:
                continue
            seen.add(key)
            signature, statements, reason = body_signature(node)
            location = {key: function[key] for key in ("id", "file", "member", "line", "population")}
            location.update(endLine=node.end_lineno, statementCount=statements,
                            sourceSha256=record["contentSha256"])
            if reason or statements < minimum_statements:
                exclusions.append({**location, "reason": reason or "below-statement-minimum"})
                continue
            normalized_bytes += len(signature.encode("utf-8"))
            if normalized_bytes > MAX_NORMALIZED_BYTES:
                raise ValueError("Normalized bodies exceed the duplication byte budget.")
            eligible += 1
            # Full structural text is the equality key; a hash collision cannot
            # group different bodies. Async/sync and populations stay separate.
            groups[(function["population"], type(node).__name__, signature)].append(location)
        if seen != set(by_file[record["file"]]):
            raise ValueError("Selected function identities changed during duplication analysis.")
    findings = []
    for (population, kind, signature), occurrences in groups.items():
        if len(occurrences) < 2:
            continue
        occurrences.sort(key=lambda item: (item["file"], item["line"], item["id"]))
        findings.append({"id": digest(f"{POLICY}|{population}|{kind}|{signature}"),
                         "population": population, "functionKind": kind, "bodySha256": digest(signature),
                         "statementCount": occurrences[0]["statementCount"], "occurrences": occurrences,
                         "disposition": "review-only"})
    findings.sort(key=lambda item: (-item["statementCount"], -len(item["occurrences"]), item["id"]))
    exclusions.sort(key=lambda item: (item["file"], item["line"], item["id"]))
    summary = {"selectedFunctions": len(facts["functions"]), "eligibleFunctions": eligible,
               "excludedFunctions": len(exclusions), "exclusionReasons": dict(sorted(Counter(item["reason"] for item in exclusions).items())),
               "duplicateGroups": len(findings), "matchedFunctions": sum(len(item["occurrences"]) for item in findings)}
    return findings, exclusions, summary


def analyze(target, *, minimum_statements=5, run_id=None, audit_id=None, **selection):
    facts = source_facts.analyze(target, **selection)
    return review(target, facts, minimum_statements=minimum_statements, run_id=run_id, audit_id=audit_id)


def review(target, facts, *, minimum_statements=5, run_id=None, audit_id=None):
    """Use the same measured population as packaged complexity evidence."""
    if type(minimum_statements) is not int or minimum_statements < 2:
        raise ValueError("Minimum statements must be an integer of at least two.")
    run_id = str(uuid.UUID(run_id)) if run_id else str(uuid.uuid4())
    audit_id = str(uuid.UUID(audit_id)) if audit_id else str(uuid.uuid4())
    target = Path(target).absolute()
    root = target if target.is_dir() else target.parent
    configuration = {"policy": POLICY, "minimumStatements": minimum_statements,
                     "selectionFingerprint": facts["selection"]["configurationFingerprint"],
                     "parserVersion": facts["producer"]["parserVersion"],
                     "maxOwners": MAX_OWNERS, "maxNormalizedBytes": MAX_NORMALIZED_BYTES}
    report = {"schemaVersion": 1, "artifactKind": "experimental-python-duplication-review",
              "producer": {"name": "codemetrics-ai/python-duplication", "version": "0.1.0-experimental"},
              "generatedAtUtc": datetime.now(timezone.utc).isoformat(), "runId": run_id, "auditId": audit_id,
              "status": facts["status"], "policy": POLICY, "configuration": configuration,
              "configurationFingerprint": digest(json.dumps(configuration, sort_keys=True, separators=(",", ":"))),
              "subject": {"root": str(root), "entryPoint": str(target)},
              "sourceFactsProducer": facts["producer"], "sourceFactsPolicy": facts["measurementPolicy"],
              "selection": facts["selection"], "diagnostics": list(facts["diagnostics"]),
              "scope": "Exact whole-function AST bodies; names/literals retained; signatures, decorators, leading docstrings and locations excluded. Populations and async/sync kinds compared separately. Not semantic equivalence, partial-block detection, a duplication percentage, or a score.",
              "groups": [], "excludedFunctions": [], "summary": None}
    if facts["status"] != "complete":
        return report
    try:
        report["groups"], report["excludedFunctions"], report["summary"] = collect(root, facts, minimum_statements)
    except (OSError, SyntaxError, UnicodeError, ValueError, RecursionError) as error:
        report["status"] = "incomplete"
        report["diagnostics"].append({"code": "duplication-analysis", "message": str(error)})
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", help="Python file or directory; same source selection as source_facts.py")
    parser.add_argument("--minimum-statements", type=int, default=5)
    parser.add_argument("--run-id")
    parser.add_argument("--audit-id")
    parser.add_argument("--output", type=Path, help="New .json file; never overwrite prior output")
    for option in ("include", "exclude", "test-path", "production-path"):
        parser.add_argument(f"--{option}", action="append", default=[])
    for option in ("include-tests", "include-generated", "include-support"):
        parser.add_argument(f"--{option}", action="store_true")
    args = parser.parse_args()
    try:
        report = analyze(args.target, minimum_statements=args.minimum_statements, run_id=args.run_id, audit_id=args.audit_id,
                         include=args.include, exclude=args.exclude, test_paths=args.test_path, production_paths=args.production_path,
                         include_tests=args.include_tests, include_generated=args.include_generated, include_support=args.include_support)
        payload = json.dumps(report, indent=2, ensure_ascii=True) + "\n"
        if args.output:
            if args.output.suffix != ".json":
                raise ValueError("Output must be a new .json file.")
            with args.output.open("x", encoding="utf-8") as handle:
                handle.write(payload)
        else:
            sys.stdout.write(payload)
        return 0 if report["status"] == "complete" else 2
    except (OSError, ValueError) as error:
        print(f"Cannot produce duplication review: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
