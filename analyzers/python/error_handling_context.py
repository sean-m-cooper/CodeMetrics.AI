"""Bounded exception context and syntactic patterns; no score or target execution."""

import argparse
import ast
from collections import Counter
from datetime import datetime, timezone
import json
from pathlib import Path
import sys
import tokenize

import error_handling as handlers
from duplication import digest, read_verified


POLICY = "python/exception-context-patterns-v1"
MAX_STATEMENTS = 32
MAX_FOLLOWING = 3
MAX_DOCUMENTATION_CHARS = 2000


def fingerprint(value):
    return digest(json.dumps(value, sort_keys=True, separators=(",", ":")))


def literal_kind(value):
    if value is None:
        return "none"
    if isinstance(value, ast.Constant) and value.value is not Ellipsis:
        return "none" if value.value is None else type(value.value).__name__
    if isinstance(value, ast.UnaryOp) and isinstance(value.op, (ast.USub, ast.UAdd)) and \
            isinstance(value.operand, ast.Constant) and type(value.operand.value) in (int, float, complex):
        return type(value.operand.value).__name__
    return None


def literal_return(node):
    return literal_kind(node.value) if isinstance(node, ast.Return) else None


def literal_assignment(node):
    if isinstance(node, ast.Assign) and all(isinstance(item, ast.Name) for item in node.targets) and literal_kind(node.value):
        return {"targets": [item.id for item in node.targets], "valueKind": literal_kind(node.value)}
    return None


def statement(node):
    return {"kind": type(node).__name__, "line": node.lineno, "endLine": node.end_lineno,
            "literalReturnKind": literal_return(node), "literalAssignment": literal_assignment(node)}


def suite(nodes, maximum=MAX_STATEMENTS):
    return {"statementCount": len(nodes), "statements": [statement(node) for node in nodes[:maximum]],
            "truncated": len(nodes) > maximum}


def import_names(node):
    if isinstance(node, ast.Name):
        return [node.id] if node.id in ("ImportError", "ModuleNotFoundError") else []
    if isinstance(node, ast.Tuple):
        parts = [import_names(item) for item in node.elts]
        return [name for part in parts for name in part] if parts and all(parts) else []
    return []


def bound_names(tree):
    """Whole-file syntactic veto, deliberately conservative across unrelated scopes."""
    names = set()
    dynamic = False
    for node in ast.walk(tree):
        if isinstance(node, ast.Name) and isinstance(node.ctx, (ast.Store, ast.Del)):
            names.add(node.id)
        elif isinstance(node, ast.arg):
            names.add(node.arg)
        elif isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
            names.add(node.name)
        elif isinstance(node, ast.ExceptHandler) and node.name:
            names.add(node.name)
        elif isinstance(node, (ast.Import, ast.ImportFrom)):
            names.update(item.asname or item.name.split(".")[0] for item in node.names)
            dynamic |= any(item.name == "*" for item in node.names)
        elif isinstance(node, (ast.MatchAs, ast.MatchStar)) and node.name:
            names.add(node.name)
        elif isinstance(node, ast.MatchMapping) and node.rest:
            names.add(node.rest)
        elif isinstance(node, ast.Call) and isinstance(node.func, ast.Name) and node.func.id in ("exec", "eval", "globals", "locals"):
            dynamic = True
    return names, dynamic


class ContextVisitor(handlers.HandlerVisitor):
    def __init__(self, source, record, owners, tree):
        super().__init__(source, record, owners)
        self.contexts, self.parents, self.suites = [], {}, {}
        self.bindings, self.dynamic_bindings = bound_names(tree)
        for parent in ast.walk(tree):
            for field, value in ast.iter_fields(parent):
                children = value if isinstance(value, list) else [value]
                for index, child in enumerate(children):
                    if isinstance(child, ast.AST):
                        self.parents[child] = parent
                        if isinstance(value, list) and isinstance(child, ast.stmt):
                            self.suites[child] = (field, index, value)

    def ancestors(self, node):
        result = []
        while node in self.parents:
            node = self.parents[node]
            result.append(node)
            if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
                break
        return result

    def surrounding_documentation(self, attempt, ancestors):
        comments = []
        for index in range(attempt.lineno - 2, -1, -1):
            line = self.lines[index]
            if not line.strip().startswith("#") or len(line) - len(line.lstrip()) != attempt.col_offset:
                break
            comments.append((index + 1, line.lstrip()[1:].strip()))
        comments.reverse()
        result = []
        for line, text in comments:
            if text and not handlers.DIRECTIVE.match(text):
                result.append({"kind": "preceding-try-comment", "line": line, "text": text})
        owner = next((item for item in ancestors if isinstance(item, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef))), None)
        if owner:
            text = ast.get_docstring(owner, clean=False)
            if text:
                result.append({"kind": "owner-docstring", "line": owner.body[0].lineno, "text": text})
        remaining, bounded = MAX_DOCUMENTATION_CHARS, []
        for item in result:
            if remaining <= 0:
                break
            text = item["text"][:remaining]
            bounded.append({**item, "text": text, "truncated": len(text) < len(item["text"])})
            remaining -= len(text)
        return {"items": bounded, "truncated": len(bounded) < len(result) or any(item["truncated"] for item in bounded)}

    def classify(self, node, attempt, following, row, ancestors):
        inert = all(handlers.inert(item) for item in node.body)
        # Documentation is observable even when surrounding execution is unresolved.
        if inert and row["documentation"]:
            return {"status": "recognized-shape", "pattern": "locally-documented-no-op",
                    "evidenceLines": [item["line"] for item in row["documentation"]], "unresolvedReasons": []}
        blockers = []
        if row["exceptionGroup"]:
            blockers.append("exception-group-semantics")
        if row["enclosingFinallyLines"]:
            blockers.append("enclosing-finally")
        if any(isinstance(item, (ast.With, ast.AsyncWith)) for item in ancestors):
            blockers.append("enclosing-context-manager")
        if any(isinstance(item, ast.ExceptHandler) for item in ancestors):
            blockers.append("nested-handler")
        if blockers:
            return {"status": "unresolved", "pattern": None, "evidenceLines": [node.lineno], "unresolvedReasons": blockers}
        pattern, evidence = None, [node.lineno]
        if len(node.body) == 1 and literal_return(node.body[0]):
            pattern, evidence = "direct-literal-fallback-return", evidence + [node.body[0].lineno]
        elif len(node.body) == 1 and isinstance(node.body[0], ast.Raise) and node.body[0].exc is None:
            pattern, evidence = "direct-bare-rethrow", evidence + [node.body[0].lineno]
        elif len(attempt.body) == 1 and isinstance(attempt.body[0], (ast.Import, ast.ImportFrom)) and import_names(node.type):
            imported = {item.asname or item.name.split(".")[0] for item in attempt.body[0].names}
            default = literal_assignment(node.body[0]) if len(node.body) == 1 else None
            if self.dynamic_bindings or any(name in self.bindings for name in import_names(node.type)):
                blockers.append("import-exception-binding-unresolved")
            elif inert:
                pattern, evidence = "guarded-import-shape", [attempt.body[0].lineno, node.lineno]
            elif default and set(default["targets"]).issubset(imported):
                pattern, evidence = "guarded-import-literal-default", [attempt.body[0].lineno, node.lineno, node.body[0].lineno]
        elif inert and following and literal_return(following[0]):
            pattern, evidence = "no-op-then-literal-return", evidence + [following[0].lineno]
        if pattern:
            return {"status": "recognized-shape", "pattern": pattern, "evidenceLines": sorted(set(evidence)), "unresolvedReasons": []}
        return {"status": "unresolved", "pattern": None, "evidenceLines": evidence,
                "unresolvedReasons": blockers or ["no-supported-local-shape"]}

    def handler(self, node, group):
        offset = len(self.rows)
        super().handler(node, group)
        row = self.rows[offset]
        attempt = self.parents[node]
        field, index, siblings = self.suites[attempt]
        following = siblings[index + 1:]
        ancestors = self.ancestors(attempt)
        self.contexts.append({"handlerId": row["id"], "file": row["file"], "line": row["line"],
                              "sourceSha256": row["sourceSha256"], "population": row["population"], "owner": row["owner"],
                              "tryStatement": {"line": attempt.lineno, "endLine": attempt.end_lineno, "kind": type(attempt).__name__,
                                               "body": suite(attempt.body), "else": suite(attempt.orelse), "finally": suite(attempt.finalbody),
                                               "handlerLines": [item.lineno for item in attempt.handlers]},
                              "handlerBody": suite(node.body),
                              "followingInSameSuite": {"suite": field, **suite(following, MAX_FOLLOWING)},
                              "enclosingConstructs": [{"kind": type(item).__name__, "line": item.lineno} for item in ancestors if hasattr(item, "lineno")],
                              "surroundingDocumentation": self.surrounding_documentation(attempt, ancestors),
                              "classification": self.classify(node, attempt, following, row, ancestors), "disposition": "review-only"})


def summarize(rows):
    def counts(items):
        return {"handlers": len(items), "recognizedShapes": sum(item["classification"]["status"] == "recognized-shape" for item in items),
                "unresolved": sum(item["classification"]["status"] == "unresolved" for item in items),
                "byPattern": dict(sorted(Counter(item["classification"]["pattern"] for item in items if item["classification"]["pattern"]).items())),
                "unresolvedReasons": dict(sorted(Counter(reason for item in items for reason in item["classification"]["unresolvedReasons"]).items()))}
    return {**counts(rows), "populations": {kind: counts([row for row in rows if row["population"] == kind]) for kind in sorted({row["population"] for row in rows})}}


def review(target, facts, base):
    configuration = {"policy": POLICY, "handlerPolicy": handlers.POLICY, "maxStatementsPerSuite": MAX_STATEMENTS,
                     "maxFollowingStatements": MAX_FOLLOWING, "maxDocumentationCharacters": MAX_DOCUMENTATION_CHARS,
                     "selectionFingerprint": facts["selection"]["configurationFingerprint"], "parserVersion": facts["producer"]["parserVersion"]}
    report = {"schemaVersion": 1, "artifactKind": "experimental-python-exception-context-review",
              "producer": {"name": "codemetrics-ai/python-exception-context", "version": "0.1.0-experimental"},
              "generatedAtUtc": datetime.now(timezone.utc).isoformat(), "runId": base["runId"], "auditId": base["auditId"],
              "policy": POLICY, "configuration": configuration, "configurationFingerprint": fingerprint(configuration),
              "handlerReview": base, "handlerRowsSha256": fingerprint(base["handlers"]),
              "status": base["status"], "diagnostics": list(base["diagnostics"]), "contexts": [], "summary": None,
              "scope": "Bounded lexical context and syntactic patterns, not resolved behavior, optionality, handling success, defects or scores. Following statements are lexical siblings, not guaranteed execution. Surrounding prose does not automatically explain a particular handler."}
    if facts["status"] != "complete" or base["status"] != "complete":
        report["status"] = "incomplete"
        report["diagnostics"] = list(base["diagnostics"] or facts["diagnostics"])
        return report
    target = Path(target).absolute()
    root = target if target.is_dir() else target.parent
    try:
        if base["selection"] != facts["selection"] or base["sourceFactsProducer"] != facts["producer"] or base["sourceFactsPolicy"] != facts["measurementPolicy"] or base["policy"] != handlers.POLICY or base["subject"] != {"root": str(root), "entryPoint": str(target)}:
            raise ValueError("Handler/source-facts provenance mismatch.")
        owners = {item["id"]: item for item in facts["functions"]}
        if len(owners) != len(facts["functions"]):
            raise ValueError("Repeated selected function identity.")
        rows, regenerated, seen = [], [], set()
        for record in facts["selection"]["files"]:
            if record["disposition"] != "analyzed":
                continue
            source = read_verified(root, record)
            tree = ast.parse(source, filename=record["file"])
            for node in ast.walk(tree):
                if isinstance(node, handlers.source_facts.OWNERS):
                    key = digest(f"{record['file']}|{node.lineno}|{node.col_offset}|{type(node).__name__}")
                    if key in owners:
                        seen.add(key)
            visitor = ContextVisitor(source, record, owners, tree)
            visitor.visit(tree)
            rows.extend(visitor.contexts)
            regenerated.extend(visitor.rows)
            if len(rows) > handlers.MAX_HANDLERS:
                raise ValueError("Exception context population exceeds the handler budget.")
        regenerated.sort(key=lambda row: (row["file"], row["line"], row["id"]))
        if seen != set(owners) or regenerated != base["handlers"] or handlers.summarize(regenerated) != base["summary"]:
            raise ValueError("Handler observations changed during context analysis.")
        rows.sort(key=lambda row: (row["file"], row["line"], row["handlerId"]))
        report["contexts"], report["summary"] = rows, summarize(rows)
    except (OSError, SyntaxError, UnicodeError, ValueError, RecursionError, tokenize.TokenError) as error:
        report["status"] = "incomplete"
        report["diagnostics"].append({"code": "exception-context-analysis", "message": str(error)})
    return report


def analyze(target, *, run_id=None, audit_id=None, **selection):
    facts = handlers.source_facts.analyze(target, **selection)
    base = handlers.review(target, facts, run_id=run_id, audit_id=audit_id)
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
        print(f"Cannot produce exception context review: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
