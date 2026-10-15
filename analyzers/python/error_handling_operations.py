"""Owned handler call/return syntax and unresolved-shape inventory; no score."""

import argparse
import ast
from collections import Counter
from datetime import datetime, timezone
import json
from pathlib import Path
import sys
import tokenize

import error_handling_transfers as transfers
from duplication import digest, read_verified

context = transfers.context
POLICY = "python/exception-call-return-v1"
MAX_OPERATIONS = 100000
MAX_ANCESTORS = 32
LOGGING_NAMES = frozenset({"debug", "info", "warning", "warn", "error", "exception", "critical", "fatal", "log"})


def owned_operations(handler, parents, file):
    calls, returns = [], []

    def location(node):
        ancestors, parent = [], parents[node]
        while parent is not handler:
            if isinstance(parent, (ast.stmt, ast.expr)):
                ancestors.append({"kind": type(parent).__name__, "line": parent.lineno})
            parent = parents[parent]
        return {"id": digest(f"{file}|{node.lineno}|{node.col_offset}|{node.end_lineno}|{node.end_col_offset}|{type(node).__name__}"),
                "line": node.lineno, "column": node.col_offset, "endLine": node.end_lineno, "endColumn": node.end_col_offset,
                "ancestors": ancestors[:MAX_ANCESTORS], "ancestorsTruncated": len(ancestors) > MAX_ANCESTORS}

    def visit(node):
        if isinstance(node, context.handlers.source_facts.OWNERS + (ast.ClassDef, ast.ExceptHandler)):
            return
        if isinstance(node, ast.Call):
            parent = parents[node]
            awaited = isinstance(parent, ast.Await)
            statement = parents[parent] if awaited else parent
            symbol = transfers.symbolic_path(node.func)
            # Lexical hint only: neither imports nor receiver identity are resolved.
            terminal = symbol.split(".")[-1] if symbol else None
            calls.append({**location(node), "callee": transfers.expression(node.func, handler.name),
                          "loggingNameSyntax": terminal if terminal in LOGGING_NAMES else None,
                          "directStatement": isinstance(statement, ast.Expr) and statement in handler.body,
                          "immediatelyAwaited": awaited, "positionalArguments": len(node.args),
                          "keywordArguments": len(node.keywords),
                          "starredArguments": sum(isinstance(item, ast.Starred) for item in node.args),
                          "keywordUnpacks": sum(item.arg is None for item in node.keywords),
                          "containsNamedExpression": any(isinstance(item, ast.NamedExpr) for item in ast.walk(node))})
        elif isinstance(node, ast.Return):
            returns.append({**location(node), "direct": node in handler.body,
                            "expression": transfers.expression(node.value, handler.name),
                            "awaitedExpression": transfers.expression(node.value.value, handler.name) if isinstance(node.value, ast.Await) else None,
                            "containsNamedExpression": any(isinstance(item, ast.NamedExpr) for item in ast.walk(node))})
        if len(calls) + len(returns) > MAX_OPERATIONS:
            raise ValueError("Call/return observation budget exceeded.")
        for child in ast.iter_child_nodes(node):
            visit(child)

    for statement in handler.body:
        visit(statement)
    order = lambda row: (row["line"], row["column"])
    return sorted(calls, key=order), sorted(returns, key=order)


def classify(handler, prior):
    # Never relax earlier blockers or rewrite any previously recognized pattern.
    if prior["status"] == "recognized-shape" or prior["unresolvedReasons"] != ["no-supported-local-shape"]:
        return dict(prior)
    if any(isinstance(item, ast.NamedExpr) for statement in handler.body for item in ast.walk(statement)):
        return dict(prior)
    body, pattern = handler.body, None
    if len(body) == 1 and isinstance(body[0], ast.Return):
        value = body[0].value
        if isinstance(value, (ast.Name, ast.Attribute, ast.Call)) and transfers.expression(value, handler.name)["symbol"]:
            pattern = "direct-symbol-or-call-return"
    else:
        rethrow = len(body) > 1 and isinstance(body[-1], ast.Raise) and body[-1].exc is None
        prefix = body[:-1] if rethrow else body
        if prefix and all(isinstance(item, ast.Expr) and isinstance(item.value, ast.Call) and
                          transfers.symbolic_path(item.value.func) is not None for item in prefix):
            pattern = "direct-calls-then-bare-raise" if rethrow else "direct-call-statement-sequence"
    if not pattern:
        return dict(prior)
    return {"status": "recognized-shape", "pattern": pattern,
            "evidenceLines": sorted({handler.lineno, *(statement.lineno for statement in body)}), "unresolvedReasons": []}


class OperationVisitor(transfers.TransferVisitor):
    def __init__(self, source, record, owners, tree):
        super().__init__(source, record, owners, tree)
        self.operations = []

    def handler(self, node, group):
        super().handler(node, group)
        original = self.transfers[-1]
        calls, returns = owned_operations(node, self.parents, original["file"])
        self.operations.append({key: original[key] for key in ("handlerId", "file", "line", "sourceSha256", "population", "owner")} |
                               {"calls": calls, "returns": returns, "suite": context.suite(node.body),
                                "originalClassification": original["classification"],
                                "classification": classify(node, original["classification"]), "disposition": "review-only"})


def inventory(rows):
    """Inventory the input's unresolved population before any new classification."""
    items = [row for row in rows if row["originalClassification"]["status"] == "unresolved"]
    shapes = Counter((tuple(value["kind"] for value in row["suite"]["statements"]), row["suite"]["truncated"]) for row in items)
    return {"handlers": len(items), "singleStatement": sum(row["suite"]["statementCount"] == 1 for row in items),
            "multiStatement": sum(row["suite"]["statementCount"] > 1 for row in items),
            "withCalls": sum(bool(row["calls"]) for row in items), "withReturns": sum(bool(row["returns"]) for row in items),
            "withLoggingNameSyntax": sum(any(call["loggingNameSyntax"] for call in row["calls"]) for row in items),
            "byReason": dict(sorted(Counter(reason for row in items for reason in row["originalClassification"]["unresolvedReasons"]).items())),
            "suiteShapes": [{"kinds": list(shape), "truncated": truncated, "handlers": count}
                            for (shape, truncated), count in sorted(shapes.items(), key=lambda pair: (-pair[1], pair[0]))]}


def summarize(rows):
    def counts(items):
        calls = [call for row in items for call in row["calls"]]
        returns = [value for row in items for value in row["returns"]]
        return {**{key: value for key, value in context.summarize(items).items() if key != "populations"},
                "callExpressions": len(calls), "returnStatements": len(returns),
                "handlersWithCalls": sum(bool(row["calls"]) for row in items),
                "handlersWithReturns": sum(bool(row["returns"]) for row in items),
                "loggingNameSyntaxCalls": sum(call["loggingNameSyntax"] is not None for call in calls),
                "immediatelyAwaitedCalls": sum(call["immediatelyAwaited"] for call in calls),
                "byReturnExpression": dict(sorted(Counter(value["expression"]["kind"] for value in returns).items())),
                "newlyRecognizedShapes": sum(row["originalClassification"]["status"] == "unresolved" and row["classification"]["status"] == "recognized-shape" for row in items),
                "inputUnresolvedInventory": inventory(items)}
    return {**counts(rows), "populations": {kind: counts([row for row in rows if row["population"] == kind]) for kind in sorted({row["population"] for row in rows})}}


def review(target, facts, base):
    configuration = {"policy": POLICY, "transferPolicy": transfers.POLICY, "transferConfigurationFingerprint": base["configurationFingerprint"],
                     "maxOperations": MAX_OPERATIONS, "maxAncestors": MAX_ANCESTORS, "maxSuiteStatements": context.MAX_STATEMENTS,
                     "maxSymbolCharacters": transfers.MAX_PATH_CHARACTERS, "maxSymbolAttributes": transfers.MAX_SYMBOL_ATTRIBUTES,
                     "loggingNames": sorted(LOGGING_NAMES)}
    report = {"schemaVersion": 1, "artifactKind": "experimental-python-exception-operation-review",
              "producer": {"name": "codemetrics-ai/python-exception-operations", "version": "0.2.0-experimental"},
              "generatedAtUtc": datetime.now(timezone.utc).isoformat(), "runId": base["runId"], "auditId": base["auditId"],
              "policy": POLICY, "configuration": configuration, "configurationFingerprint": context.fingerprint(configuration),
              "transferReview": base, "transferRowsSha256": context.fingerprint(base["handlers"]),
              "status": base["status"], "diagnostics": list(base["diagnostics"]), "handlers": [], "summary": None,
              "scope": "Owned call/return syntax and input unresolved-shape inventory; not resolved logging, delegation, recovery, runtime paths, defects, or scores."}
    if facts["status"] != "complete" or base["status"] != "complete":
        report["status"] = "incomplete"
        report["diagnostics"] = list(base["diagnostics"] or facts["diagnostics"])
        return report
    target = Path(target).absolute()
    root = target if target.is_dir() else target.parent
    try:
        regenerated = transfers.review(target, facts, base["contextReview"])
        stable = lambda value: {key: item for key, item in value.items() if key != "generatedAtUtc"}
        if regenerated["status"] != "complete" or stable(regenerated) != stable(base):
            raise ValueError("Raise/chaining evidence changed or has inconsistent provenance.")
        owners = {item["id"]: item for item in facts["functions"]}
        rows, prior, contexts, handlers, count = [], [], [], [], 0
        for record in facts["selection"]["files"]:
            if record["disposition"] != "analyzed":
                continue
            source = read_verified(root, record)
            tree = ast.parse(source, filename=record["file"])
            visitor = OperationVisitor(source, record, owners, tree)
            visitor.visit(tree)
            rows.extend(visitor.operations)
            prior.extend(visitor.transfers)
            contexts.extend(visitor.contexts)
            handlers.extend(visitor.rows)
            count += sum(len(row["calls"]) + len(row["returns"]) for row in visitor.operations)
            if count > MAX_OPERATIONS or len(rows) > context.handlers.MAX_HANDLERS:
                raise ValueError("Call/return observation population exceeds its budget.")
        order = lambda row: (row["file"], row["line"], row["handlerId"])
        rows.sort(key=order); prior.sort(key=order); contexts.sort(key=order)
        handlers.sort(key=lambda row: (row["file"], row["line"], row["id"]))
        if prior != base["handlers"] or contexts != base["contextReview"]["contexts"] or handlers != base["contextReview"]["handlerReview"]["handlers"]:
            raise ValueError("Earlier observations changed during call/return analysis.")
        for row, original in zip(rows, handlers):
            projected = [{"line": value["line"], "hasValue": value["expression"]["kind"] != "absent"} for value in row["returns"]]
            if len(row["calls"]) != original["bodyObservations"]["calls"] or projected != original["bodyObservations"]["returns"]:
                raise ValueError("Owned calls/returns disagree with original observations.")
        report["handlers"], report["summary"] = rows, summarize(rows)
    except (OSError, SyntaxError, UnicodeError, ValueError, RecursionError, tokenize.TokenError) as error:
        report["status"] = "incomplete"
        report["diagnostics"].append({"code": "exception-operation-analysis", "message": str(error)})
    return report


def analyze(target, *, run_id=None, audit_id=None, **selection):
    facts = context.handlers.source_facts.analyze(target, **selection)
    handlers = context.handlers.review(target, facts, run_id=run_id, audit_id=audit_id)
    base_context = context.review(target, facts, handlers)
    return review(target, facts, transfers.review(target, facts, base_context))


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
        print(f"Cannot produce exception call/return review: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
