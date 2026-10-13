"""Offline owned-statement and block-nesting evidence; no scores or target execution."""

import argparse
import ast
from collections import Counter, defaultdict
from datetime import datetime, timezone
import json
from pathlib import Path
import sys
import uuid

from duplication import digest, read_verified
import source_facts


POLICY = "python/owned-statements-block-nesting-v1"
MAX_OWNERS = 100000
BLOCKS = (ast.If, ast.For, ast.AsyncFor, ast.While, ast.With, ast.AsyncWith, ast.Try, ast.TryStar, ast.Match)


def is_docstring(node):
    return isinstance(node, ast.Expr) and isinstance(node.value, ast.Constant) and isinstance(node.value.value, str)


class BodyMeasure:
    """A function owns its statement tree, stopping at every nested definition."""

    def __init__(self):
        self.kinds = Counter()
        self.excluded = Counter()
        self.max_nesting = 0

    def visit(self, node, depth=0):
        if isinstance(node, source_facts.OWNERS + (ast.ClassDef,)):
            self.excluded[type(node).__name__] += 1
            return
        if isinstance(node, (ast.Pass, ast.Global, ast.Nonlocal, ast.TypeAlias)) or (
            isinstance(node, ast.AnnAssign) and node.value is None
        ) or (isinstance(node, ast.Expr) and isinstance(node.value, ast.Constant) and node.value.value is Ellipsis):
            self.excluded[type(node).__name__] += 1
            return
        if isinstance(node, ast.stmt):
            self.kinds[type(node).__name__] += 1
        if isinstance(node, BLOCKS):
            # Sibling arms (else/except/finally/case) share their construct's depth.
            branch_depth = depth + 1
            self.max_nesting = max(self.max_nesting, branch_depth)
            for field, value in ast.iter_fields(node):
                children = value if isinstance(value, list) else [value]
                for child in children:
                    if not isinstance(child, ast.AST):
                        continue
                    # CPython represents elif as an If in orelse. A real nested
                    # if in an else suite has greater indentation, even though
                    # the unpositioned AST shape can otherwise be identical.
                    is_elif = (isinstance(node, ast.If) and field == "orelse" and
                               isinstance(child, ast.If) and child.col_offset == node.col_offset)
                    self.visit(child, depth if is_elif else branch_depth)
            return
        for child in ast.iter_child_nodes(node):
            self.visit(child, depth)

    def measure(self, node):
        body = node.body
        if body and is_docstring(body[0]):
            self.excluded["docstring"] += 1
            body = body[1:]
        for statement in body:
            self.visit(statement)
        return {"ownedStatements": sum(self.kinds.values()), "maxBlockNesting": self.max_nesting,
                "statementKinds": dict(sorted(self.kinds.items())), "excludedSyntax": dict(sorted(self.excluded.items()))}


def distribution(values):
    """Nearest-rank quantiles; empty populations are unavailable, never zero."""
    values = sorted(values)
    if not values:
        return None
    return {"count": len(values), "p50": values[(50 * len(values) + 99) // 100 - 1],
            "p90": values[(90 * len(values) + 99) // 100 - 1],
            "p95": values[(95 * len(values) + 99) // 100 - 1], "max": values[-1]}


def summarize(rows):
    measured = [row for row in rows if row["status"] == "measured"]
    populations = {}
    for population in sorted({row["population"] for row in rows}):
        selected = [row for row in rows if row["population"] == population]
        eligible = [row for row in measured if row["population"] == population]
        populations[population] = {"selectedFunctions": len(selected), "measuredFunctions": len(eligible),
                                   "unavailableFunctions": len(selected) - len(eligible),
                                   **{key: distribution(row["measurement"][key] for row in eligible)
                                      for key in ("ownedStatements", "maxBlockNesting")}}
    return {"selectedFunctions": len(rows), "measuredFunctions": len(measured), "unavailableFunctions": len(rows) - len(measured),
            "unavailableReasons": dict(sorted(Counter(row["reason"] for row in rows if row["status"] == "unavailable").items())),
            "populations": populations}


def collect(root, facts):
    if len(facts["functions"]) > MAX_OWNERS:
        raise ValueError("Selected function population exceeds the decomposition owner budget.")
    by_file = defaultdict(dict)
    for function in facts["functions"]:
        if function["id"] in by_file[function["file"]]:
            raise ValueError("Repeated selected function identity.")
        by_file[function["file"]][function["id"]] = function
    rows = []
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
            row = {name: function[name] for name in ("id", "file", "member", "line", "population")}
            row.update(endLine=node.end_lineno, sourceSha256=record["contentSha256"], functionKind=type(node).__name__,
                       complexity=function["complexity"]["value"], disposition="review-only")
            if isinstance(node, ast.Lambda):
                row.update(status="unavailable", reason="lambda-expression-body", measurement=None)
            else:
                row.update(status="measured", measurement=BodyMeasure().measure(node))
            rows.append(row)
        if seen != set(by_file[record["file"]]):
            raise ValueError("Selected function identities changed during decomposition analysis.")
    if len(rows) != len(facts["functions"]):
        raise ValueError("Selected function population changed during decomposition analysis.")
    rows.sort(key=lambda row: (row["file"], row["line"], row["id"]))
    return rows, summarize(rows)


def analyze(target, *, run_id=None, audit_id=None, **selection):
    return review(target, source_facts.analyze(target, **selection), run_id=run_id, audit_id=audit_id)


def review(target, facts, *, run_id=None, audit_id=None):
    target = Path(target).absolute()
    root = target if target.is_dir() else target.parent
    configuration = {"policy": POLICY, "selectionFingerprint": facts["selection"]["configurationFingerprint"],
                     "parserVersion": facts["producer"]["parserVersion"], "maxOwners": MAX_OWNERS,
                     "quantiles": "nearest-rank", "complexityPolicy": facts["measurementPolicy"]}
    report = {"schemaVersion": 1, "artifactKind": "experimental-python-decomposition-review",
              "producer": {"name": "codemetrics-ai/python-decomposition", "version": "0.1.0-experimental"},
              "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
              "runId": str(uuid.UUID(run_id)) if run_id else str(uuid.uuid4()),
              "auditId": str(uuid.UUID(audit_id)) if audit_id else str(uuid.uuid4()),
              "policy": POLICY, "configuration": configuration,
              "configurationFingerprint": digest(json.dumps(configuration, sort_keys=True, separators=(",", ":"))),
              "subject": {"root": str(root), "entryPoint": str(target)},
              "sourceFactsProducer": facts["producer"], "sourceFactsPolicy": facts["measurementPolicy"],
              "selection": facts["selection"], "status": facts["status"], "diagnostics": list(facts["diagnostics"]),
              "scope": "Owned AST statements and block nesting, separately measured. Nested definitions and declaration syntax excluded. Lambdas unavailable. No expression-size measurement, score, threshold, or safe-extraction claim.",
              "functions": [], "summary": None}
    if facts["status"] != "complete":
        return report
    try:
        report["functions"], report["summary"] = collect(root, facts)
    except (OSError, SyntaxError, UnicodeError, ValueError, RecursionError) as error:
        report["status"] = "incomplete"
        report["diagnostics"].append({"code": "decomposition-analysis", "message": str(error)})
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", help="Python source file or directory")
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
            with args.output.open("x", encoding="utf-8") as handle:
                handle.write(payload)
        else:
            sys.stdout.write(payload)
        return 0 if report["status"] == "complete" else 2
    except (OSError, ValueError) as error:
        print(f"Cannot produce decomposition review: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
