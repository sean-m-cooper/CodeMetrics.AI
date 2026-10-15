"""Source-only exception-handler observations; no handling score or target execution."""

import argparse
import ast
from collections import Counter
from datetime import datetime, timezone
import io
import json
from pathlib import Path
import re
import sys
import tokenize
import uuid

from duplication import digest, read_verified
import source_facts


POLICY = "python/exception-handler-observations-v1"
MAX_HANDLERS = 100000
DOCUMENTATION_POLICY = "suite-comments-or-string-statements/tool-directives-excluded/content-not-adjudicated-v1"
DIRECTIVE = re.compile(r"^(?:noqa\b|type:\s*ignore\b|pragma:\s*no cover\b|fmt:|pylint:|ruff:)", re.I)


def inert(statement):
    return isinstance(statement, ast.Pass) or (
        isinstance(statement, ast.Expr) and isinstance(statement.value, ast.Constant) and
        (statement.value.value is Ellipsis or isinstance(statement.value.value, str)))


def observations(body):
    """Owned syntax only; nested definitions and handlers cannot credit the enclosing handler."""
    result = {"raises": [], "returns": [], "loopTransfers": [], "calls": 0, "nestedHandlersExcluded": 0}

    def visit(node):
        if isinstance(node, source_facts.OWNERS + (ast.ClassDef,)):
            return
        if isinstance(node, ast.ExceptHandler):
            result["nestedHandlersExcluded"] += 1
            return
        if isinstance(node, ast.Raise):
            result["raises"].append({"line": node.lineno, "bare": node.exc is None, "explicitCause": node.cause is not None})
        elif isinstance(node, ast.Return):
            result["returns"].append({"line": node.lineno, "hasValue": node.value is not None})
        elif isinstance(node, (ast.Break, ast.Continue)):
            result["loopTransfers"].append({"line": node.lineno, "kind": type(node).__name__.lower()})
        elif isinstance(node, ast.Call):
            result["calls"] += 1
        for child in ast.iter_child_nodes(node):
            visit(child)

    for statement in body:
        visit(statement)
    return result


class HandlerVisitor(ast.NodeVisitor):
    def __init__(self, source, record, owners):
        self.lines = source.splitlines()
        self.tokens = list(tokenize.generate_tokens(io.StringIO(source).readline))
        self.record, self.owners = record, owners
        self.rows = []
        self.scope = {"kind": "module", "member": "<module>", "functionId": None}
        self.finally_lines = []

    def scoped_body(self, scope, body, *, deferred=True):
        previous = self.scope, self.finally_lines
        self.scope, self.finally_lines = scope, [] if deferred else self.finally_lines
        for statement in body:
            self.visit(statement)
        self.scope, self.finally_lines = previous

    def visit_FunctionDef(self, node):
        key = digest(f"{self.record['file']}|{node.lineno}|{node.col_offset}|{type(node).__name__}")
        owner = self.owners.get(key)
        if owner:
            self.scoped_body({"kind": "function", "member": owner["member"], "functionId": key}, node.body)

    visit_AsyncFunctionDef = visit_FunctionDef

    def visit_ClassDef(self, node):
        prefix = "" if self.scope["kind"] == "module" else self.scope["member"] + "."
        self.scoped_body({"kind": "class-body", "member": prefix + node.name, "functionId": None}, node.body, deferred=False)

    def visit_Try(self, node):
        previous = self.finally_lines
        self.finally_lines = previous + ([node.lineno] if node.finalbody else [])
        for statement in node.body + node.orelse:
            self.visit(statement)
        for handler in node.handlers:
            self.handler(handler, isinstance(node, ast.TryStar))
        # A try's own finally does not run after a handler located in that finally.
        self.finally_lines = previous
        for statement in node.finalbody:
            self.visit(statement)

    visit_TryStar = visit_Try

    def documentation(self, node):
        first = node.body[0]
        # AST columns are UTF-8 byte offsets; tokenize columns are character offsets.
        column = len(self.lines[first.lineno - 1].encode("utf-8")[:first.col_offset].decode("utf-8"))
        header = [token.start for token in self.tokens if token.string == ":" and
                  (node.lineno, node.col_offset) <= token.start < (first.lineno, column)]
        start = header[-1]
        end = node.end_lineno
        for index in range(end, len(self.lines)):
            line = self.lines[index]
            if not line.strip():
                continue
            if line.lstrip().startswith("#") and len(line) - len(line.lstrip()) > node.col_offset:
                end = index + 1
            else:
                break
        documents, directives = [], []
        for token in self.tokens:
            if token.type != tokenize.COMMENT or not (start < token.start and token.start[0] <= end):
                continue
            text = token.string[1:].strip()
            if not any(character.isalnum() for character in text):
                continue
            item = {"kind": "comment", "line": token.start[0], "text": text}
            (directives if DIRECTIVE.match(text) else documents).append(item)
        for statement in node.body:
            if isinstance(statement, ast.Expr) and isinstance(statement.value, ast.Constant) and isinstance(statement.value.value, str):
                text = statement.value.value.strip()
                if any(character.isalnum() for character in text):
                    documents.append({"kind": "string-statement", "line": statement.lineno, "text": text})
        return documents, directives

    def handler(self, node, group):
        if len(self.rows) >= MAX_HANDLERS:
            raise ValueError("Exception-handler population exceeds the handler budget.")
        documents, directives = self.documentation(node) if all(inert(item) for item in node.body) else ([], [])
        terminal = type(node.body[-1]).__name__
        if all(inert(item) for item in node.body):
            classification = "documented-no-op" if documents else "undocumented-no-op"
        else:
            classification = {"Raise": "terminal-raise", "Return": "terminal-return",
                              "Break": "terminal-loop-control", "Continue": "terminal-loop-control"}.get(terminal, "other-body")
        exception = ast.unparse(node.type) if node.type else None
        self.rows.append({"id": digest(f"{self.record['file']}|{node.lineno}|{node.col_offset}|ExceptHandler"),
                          "file": self.record["file"], "line": node.lineno, "endLine": node.end_lineno,
                          "sourceSha256": self.record["contentSha256"], "population": self.record["category"],
                          "owner": dict(self.scope), "exceptionTypeSyntax": exception, "bareExcept": node.type is None,
                          "exceptionGroup": group, "enclosingFinallyLines": list(self.finally_lines),
                          "classification": classification, "documentation": documents, "directiveComments": directives,
                          "bodyObservations": observations(node.body), "disposition": "review-only"})
        for statement in node.body:
            self.visit(statement)


def summarize(rows):
    def counts(items):
        return {"handlers": len(items), "byClassification": dict(sorted(Counter(item["classification"] for item in items).items()))}
    return {**counts(rows), "functionsWithHandlers": len({row["owner"]["functionId"] for row in rows if row["owner"]["functionId"]}),
            "byOwnerKind": dict(sorted(Counter(row["owner"]["kind"] for row in rows).items())),
            "populations": {kind: counts([row for row in rows if row["population"] == kind]) for kind in sorted({row["population"] for row in rows})}}


def collect(root, facts):
    owners = {item["id"]: item for item in facts["functions"]}
    if len(owners) != len(facts["functions"]):
        raise ValueError("Repeated selected function identity.")
    rows, seen = [], set()
    for record in facts["selection"]["files"]:
        if record["disposition"] != "analyzed":
            continue
        source = read_verified(root, record)
        tree = ast.parse(source, filename=record["file"])
        for node in ast.walk(tree):
            if isinstance(node, source_facts.OWNERS):
                key = digest(f"{record['file']}|{node.lineno}|{node.col_offset}|{type(node).__name__}")
                if key in owners:
                    seen.add(key)
        visitor = HandlerVisitor(source, record, owners)
        visitor.visit(tree)
        rows.extend(visitor.rows)
        if len(rows) > MAX_HANDLERS:
            raise ValueError("Exception-handler population exceeds the handler budget.")
    if seen != set(owners):
        raise ValueError("Selected function identities changed during error-handling analysis.")
    rows.sort(key=lambda row: (row["file"], row["line"], row["id"]))
    return rows, summarize(rows)


def analyze(target, *, run_id=None, audit_id=None, **selection):
    return review(target, source_facts.analyze(target, **selection), run_id=run_id, audit_id=audit_id)


def review(target, facts, *, run_id=None, audit_id=None):
    target = Path(target).absolute()
    root = target if target.is_dir() else target.parent
    configuration = {"policy": POLICY, "selectionFingerprint": facts["selection"]["configurationFingerprint"],
                     "parserVersion": facts["producer"]["parserVersion"], "maxHandlers": MAX_HANDLERS,
                     "documentationPolicy": DOCUMENTATION_POLICY}
    report = {"schemaVersion": 1, "artifactKind": "experimental-python-error-handling-review",
              "producer": {"name": "codemetrics-ai/python-error-handling", "version": "0.1.0-experimental"},
              "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
              "runId": str(uuid.UUID(run_id)) if run_id else str(uuid.uuid4()),
              "auditId": str(uuid.UUID(audit_id)) if audit_id else str(uuid.uuid4()),
              "policy": POLICY, "configuration": configuration,
              "configurationFingerprint": digest(json.dumps(configuration, sort_keys=True, separators=(",", ":"))),
              "subject": {"root": str(root), "entryPoint": str(target)},
              "sourceFactsProducer": facts["producer"], "sourceFactsPolicy": facts["measurementPolicy"],
              "selection": facts["selection"], "status": facts["status"], "diagnostics": list(facts["diagnostics"]),
              "scope": "Selected-source exception suites, including module/class bodies and except*. Syntactic observations only; no score, handling-quality judgment, resolved exception types, path completeness or propagation guarantee. Context-manager suppression is outside scope.",
              "handlers": [], "summary": None}
    if facts["status"] != "complete":
        return report
    try:
        report["handlers"], report["summary"] = collect(root, facts)
    except (OSError, SyntaxError, UnicodeError, ValueError, RecursionError, tokenize.TokenError) as error:
        report["status"] = "incomplete"
        report["diagnostics"].append({"code": "error-handling-analysis", "message": str(error)})
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
        print(f"Cannot produce error-handling review: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
