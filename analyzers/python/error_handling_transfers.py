"""Exception raise/chaining syntax and conservative local patterns; no score."""

import argparse
import ast
from collections import Counter
from datetime import datetime, timezone
import json
from pathlib import Path
import sys
import tokenize

import error_handling_context as context
from duplication import digest, read_verified

POLICY = "python/exception-raise-chaining-v1"
MAX_RAISES = 100000
MAX_PATH_CHARACTERS = 200
MAX_CONTAINERS = 32
MAX_SYMBOL_ATTRIBUTES = 8


def symbolic_path(node):
    """Bounded identifier path; calls/subscripts in a receiver are not simple symbols."""
    parts = []
    while isinstance(node, ast.Attribute) and len(parts) < MAX_SYMBOL_ATTRIBUTES:
        parts.append(node.attr)
        node = node.value
    if not isinstance(node, ast.Name):
        return None
    value = ".".join([node.id, *reversed(parts)])
    return value if len(value) <= MAX_PATH_CHARACTERS else None


def expression(node, handler_name):
    if node is None:
        return {"kind": "absent", "astKind": None, "symbol": None, "matchesHandlerNameSyntax": False}
    symbol = symbolic_path(node.func if isinstance(node, ast.Call) else node)
    kind = "none" if isinstance(node, ast.Constant) and node.value is None else (
        "name" if isinstance(node, ast.Name) else "attribute" if isinstance(node, ast.Attribute) else
        "call" if isinstance(node, ast.Call) else "other")
    return {"kind": kind, "astKind": type(node).__name__, "symbol": symbol,
            "matchesHandlerNameSyntax": isinstance(node, ast.Name) and node.id == handler_name}


def owned_raises(handler, parents):
    rows = []

    def visit(node):
        if isinstance(node, context.handlers.source_facts.OWNERS + (ast.ClassDef, ast.ExceptHandler)):
            return
        if isinstance(node, ast.Raise):
            containers, parent = [], parents[node]
            while parent is not handler:
                if isinstance(parent, ast.stmt):
                    containers.append({"kind": type(parent).__name__, "line": parent.lineno})
                parent = parents[parent]
            rows.append({"line": node.lineno, "column": node.col_offset, "endLine": node.end_lineno,
                         "direct": node in handler.body, "exception": expression(node.exc, handler.name),
                         "cause": expression(node.cause, handler.name),
                         "containsNamedExpression": any(isinstance(item, ast.NamedExpr) for item in ast.walk(node)),
                         "containers": containers[:MAX_CONTAINERS], "containersTruncated": len(containers) > MAX_CONTAINERS})
            if len(rows) > MAX_RAISES:
                raise ValueError("Raise observation budget exceeded.")
        for child in ast.iter_child_nodes(node):
            visit(child)

    for statement in handler.body:
        visit(statement)
    return sorted(rows, key=lambda row: (row["line"], row["column"]))


def classify(handler, original, raises):
    # Retain v1 recognized patterns and conservative execution blockers exactly.
    if original["status"] == "recognized-shape" or original["unresolvedReasons"] != ["no-supported-local-shape"]:
        return dict(original)
    if len(handler.body) != 1 or not isinstance(handler.body[0], ast.Raise):
        return dict(original)
    observation = raises[0]
    exc, cause = observation["exception"], observation["cause"]
    pattern = None
    if not observation["containsNamedExpression"] and exc["symbol"] is not None:
        if cause["kind"] == "none":
            pattern = "direct-raise-from-none"
        elif cause["matchesHandlerNameSyntax"]:
            pattern = "direct-raise-from-handler-name"
        elif cause["kind"] == "absent":
            pattern = "direct-handler-name-raise" if exc["matchesHandlerNameSyntax"] else "direct-symbol-or-call-raise"
    if pattern:
        return {"status": "recognized-shape", "pattern": pattern,
                "evidenceLines": sorted({handler.lineno, observation["line"]}), "unresolvedReasons": []}
    return {"status": "unresolved", "pattern": None, "evidenceLines": [handler.lineno],
            "unresolvedReasons": ["raise-expression-or-cause-unresolved"]}


class TransferVisitor(context.ContextVisitor):
    def __init__(self, source, record, owners, tree):
        super().__init__(source, record, owners, tree)
        self.transfers = []

    def handler(self, node, group):
        super().handler(node, group)
        # ContextVisitor appends the parent after visiting nested handlers.
        original = self.contexts[-1]
        raises = owned_raises(node, self.parents)
        for row in raises:
            row["id"] = digest(f"{original['file']}|{row['line']}|{row['column']}|Raise")
        self.transfers.append({key: original[key] for key in ("handlerId", "file", "line", "sourceSha256", "population", "owner")} |
                              {"handlerNameSyntax": node.name, "raises": raises, "originalClassification": original["classification"],
                               "classification": classify(node, original["classification"], raises), "disposition": "review-only"})


def summarize(rows):
    def counts(items):
        raises = [value for row in items for value in row["raises"]]
        return {**{key: value for key, value in context.summarize(items).items() if key != "populations"},
                "raiseStatements": len(raises), "handlersWithRaises": sum(bool(row["raises"]) for row in items),
                "newlyRecognizedShapes": sum(row["originalClassification"]["status"] == "unresolved" and row["classification"]["status"] == "recognized-shape" for row in items),
                "byExceptionSyntax": dict(sorted(Counter(row["exception"]["kind"] for row in raises).items())),
                "handlerNameRaiseSyntax": sum(row["exception"]["matchesHandlerNameSyntax"] for row in raises),
                "handlerNameCauseSyntax": sum(row["cause"]["matchesHandlerNameSyntax"] for row in raises),
                "byCauseSyntax": dict(sorted(Counter(row["cause"]["kind"] for row in raises).items()))}
    return {**counts(rows), "populations": {kind: counts([row for row in rows if row["population"] == kind]) for kind in sorted({row["population"] for row in rows})}}


def review(target, facts, base):
    configuration = {"policy": POLICY, "contextPolicy": context.POLICY, "contextConfigurationFingerprint": base["configurationFingerprint"],
                     "maxRaises": MAX_RAISES, "maxSymbolCharacters": MAX_PATH_CHARACTERS, "maxContainers": MAX_CONTAINERS, "maxSymbolAttributes": MAX_SYMBOL_ATTRIBUTES}
    report = {"schemaVersion": 1, "artifactKind": "experimental-python-exception-transfer-review",
              "producer": {"name": "codemetrics-ai/python-exception-transfers", "version": "0.1.0-experimental"},
              "generatedAtUtc": datetime.now(timezone.utc).isoformat(), "runId": base["runId"], "auditId": base["auditId"],
              "policy": POLICY, "configuration": configuration, "configurationFingerprint": context.fingerprint(configuration),
              "contextReview": base, "contextRowsSha256": context.fingerprint(base["contexts"]),
              "status": base["status"], "diagnostics": list(base["diagnostics"]), "handlers": [], "summary": None,
              "scope": "Owned raise and cause syntax with conservative local shapes; not resolved exception identity, propagation, handling quality, defects, or scores."}
    if facts["status"] != "complete" or base["status"] != "complete":
        report["status"] = "incomplete"
        report["diagnostics"] = list(base["diagnostics"] or facts["diagnostics"])
        return report
    target = Path(target).absolute()
    root = target if target.is_dir() else target.parent
    try:
        regenerated = context.review(target, facts, base["handlerReview"])
        stable = lambda value: {key: item for key, item in value.items() if key != "generatedAtUtc"}
        if regenerated["status"] != "complete" or stable(regenerated) != stable(base):
            raise ValueError("Context evidence changed or has inconsistent provenance.")
        owners = {item["id"]: item for item in facts["functions"]}
        rows, contexts, handlers, raise_count = [], [], [], 0
        for record in facts["selection"]["files"]:
            if record["disposition"] != "analyzed":
                continue
            source = read_verified(root, record)
            tree = ast.parse(source, filename=record["file"])
            visitor = TransferVisitor(source, record, owners, tree)
            visitor.visit(tree)
            rows.extend(visitor.transfers)
            contexts.extend(visitor.contexts)
            handlers.extend(visitor.rows)
            raise_count += sum(len(row["raises"]) for row in visitor.transfers)
            if raise_count > MAX_RAISES or len(rows) > context.handlers.MAX_HANDLERS:
                raise ValueError("Transfer observation population exceeds its budget.")
        order = lambda row: (row["file"], row["line"], row["handlerId"])
        rows.sort(key=order)
        contexts.sort(key=order)
        handlers.sort(key=lambda row: (row["file"], row["line"], row["id"]))
        if contexts != base["contexts"] or handlers != base["handlerReview"]["handlers"]:
            raise ValueError("Handler/context observations changed during transfer analysis.")
        report["handlers"], report["summary"] = rows, summarize(rows)
    except (OSError, SyntaxError, UnicodeError, ValueError, RecursionError, tokenize.TokenError) as error:
        report["status"] = "incomplete"
        report["diagnostics"].append({"code": "exception-transfer-analysis", "message": str(error)})
    return report


def analyze(target, *, run_id=None, audit_id=None, **selection):
    facts = context.handlers.source_facts.analyze(target, **selection)
    handlers = context.handlers.review(target, facts, run_id=run_id, audit_id=audit_id)
    base = context.review(target, facts, handlers)
    return review(target, facts, base)


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
        print(f"Cannot produce exception transfer review: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
