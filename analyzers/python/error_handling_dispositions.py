"""Bounded handler disposition paths; observations, never handling-success scores."""

import ast
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path
import tokenize

import async_usage as usage
import error_handling_operations as operations
from review_cli import run

context = operations.context
POLICY = "python/exception-disposition-paths-review-v1"
MAX_STATEMENTS = 100000
MAX_DEPTH = 32


class Paths:
    """Finite union of syntactic paths, conditional on expressions completing normally."""
    def __init__(self):
        self.statements = 0

    def suite(self, body, states=None, depth=0):
        if depth > MAX_DEPTH:
            return {("unresolved", "depth-limit")}
        states = {("fallthrough", "no-action")} if states is None else states
        for node in body:
            if not any(kind == "fallthrough" for kind, _ in states):
                break
            self.statements += 1
            if self.statements > MAX_STATEMENTS:
                raise ValueError("Handler path statement budget exceeded.")
            terminals = {(kind, detail) for kind, detail in states if kind != "fallthrough"}
            active = {(kind, detail) for kind, detail in states if kind == "fallthrough"}
            states = terminals | self.statement(node, active, depth)
        return states

    def statement(self, node, active, depth):
        if has_yield(node):
            return {("unresolved", "generator-suspension")}
        if isinstance(node, ast.If):
            # No truthiness evaluation or path feasibility claim.
            return self.suite(node.body, active, depth + 1) | self.suite(node.orelse, active, depth + 1)
        if isinstance(node, ast.Raise):
            return {("propagation", "bare-raise" if node.exc is None else "raise-expression")}
        if isinstance(node, ast.Return):
            return {("explicit-fallback", "return-expression" if node.value else "bare-return")}
        if isinstance(node, (ast.Break, ast.Continue)):
            return {("loop-transfer", type(node).__name__.lower())}
        if context.handlers.inert(node):
            return active
        if isinstance(node, (ast.Assign, ast.AnnAssign, ast.AugAssign)):
            if isinstance(node, ast.AnnAssign) and node.value is None:
                return active
            return {("fallthrough", "state-update")}
        if isinstance(node, ast.Expr) and isinstance(node.value, (ast.Call, ast.Await)):
            return {("fallthrough", "call-action")}
        # Nested exception handling, with, loops, match, declarations and arbitrary
        # expressions cannot be made safe by a later terminal statement.
        return {("unresolved", "unsupported-" + type(node).__name__)}


def has_yield(node):
    if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef, ast.Lambda)):
        return False
    return isinstance(node, (ast.Yield, ast.YieldFrom)) or any(has_yield(child) for child in ast.iter_child_nodes(node))


def classify(node, original, lexical, paths, attempt):
    shape = lexical["classification"]
    if original["classification"] == "documented-no-op":
        return "documented-suppression", []
    blockers = [reason for reason in shape["unresolvedReasons"] if reason != "no-supported-local-shape"]
    if blockers:
        return "unresolved", blockers
    if shape["pattern"] in {"guarded-import-shape", "guarded-import-literal-default", "no-op-then-literal-return"}:
        return "explicit-fallback", [shape["pattern"]]
    if any(kind == "unresolved" for kind, _ in paths):
        return "unresolved", sorted({detail for kind, detail in paths if kind == "unresolved"})
    if ("fallthrough", "no-action") in paths:
        if original["classification"] != "undocumented-no-op":
            return "unresolved", ["conditional-no-action-path"]
        # Prose outside the suite, else/probe logic and following work may explain
        # a no-op. Withhold a swallowing label until that context is understood.
        if lexical["surroundingDocumentation"]["items"] or lexical["surroundingDocumentation"]["truncated"]:
            return "unresolved", ["surrounding-rationale-needs-review"]
        if lexical["tryStatement"]["else"]["statementCount"] or lexical["followingInSameSuite"]["statementCount"]:
            return "unresolved", ["surrounding-fallback-needs-review"]
        if any(item["kind"] in {"For", "AsyncFor", "While"} for item in lexical["enclosingConstructs"]):
            return "unresolved", ["loop-continuation-needs-review"]
        if any(has_yield(item) for item in attempt.body):
            return "unresolved", ["generator-protocol-needs-review"]
        if len(attempt.handlers) > 1:
            return "unresolved", ["selective-sibling-handlers-need-review"]
        return "unexplained-swallowing-candidate", ["no-local-action-or-rationale-observed"]
    kinds = {"recovery-action" if kind == "fallthrough" else kind for kind, _ in paths}
    return next(iter(kinds)) if len(kinds) == 1 else "mixed-explicit-disposition", []


class DispositionVisitor(context.ContextVisitor):
    def __init__(self, source, record, owners, tree, paths):
        super().__init__(source, record, owners, tree)
        self.dispositions, self.paths = [], paths

    def handler(self, node, group):
        offset = len(self.rows)
        super().handler(node, group)
        original, lexical = self.rows[offset], self.contexts[-1]
        # A generator suspension inside this handler is not a completed action.
        paths = self.paths.suite(node.body)
        classification, reasons = classify(node, original, lexical, paths, self.parents[node])
        self.dispositions.append({key: lexical[key] for key in
            ("handlerId", "file", "line", "sourceSha256", "population", "owner")} |
            {"classification": classification, "reasons": reasons,
             "paths": [{"outcome": kind, "detail": detail} for kind, detail in sorted(paths)],
             "disposition": "review-only"})


def summarize(rows):
    def counts(items):
        return {"handlers": len(items), "byClassification": dict(sorted(Counter(row["classification"] for row in items).items()))}
    return {**counts(rows), "populations": {population: counts([row for row in rows if row["population"] == population])
        for population in sorted({row["population"] for row in rows})}}


def review(target, facts, base):
    configuration = {"policy": POLICY, "operationConfigurationFingerprint": base["configurationFingerprint"],
                     "maxStatements": MAX_STATEMENTS, "maxDepth": MAX_DEPTH}
    report = {"schemaVersion": 1, "artifactKind": "experimental-python-handler-dispositions",
        "producer": {"name": "codemetrics-ai/python-handler-dispositions", "version": "0.1.0-experimental"},
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(), "runId": base["runId"], "auditId": base["auditId"],
        "policy": POLICY, "configuration": configuration, "configurationFingerprint": usage.fingerprint(configuration),
        "operationRowsSha256": usage.fingerprint(base["handlers"]), "selection": facts["selection"],
        "sourceFactsProducer": facts["producer"], "sourceFactsPolicy": facts["measurementPolicy"],
        "status": "incomplete", "diagnostics": [], "handlers": [], "summary": None,
        "scope": "Bounded lexical paths conditional on normal expression completion; actions are not proven recovery. Candidates are not defects. Error Handling and Async/Blocking Usage remain unscored."}
    try:
        usage.verify_facts(target, facts)
        fresh = operations.review(target, facts, base["transferReview"])
        stable = lambda value: {key: item for key, item in value.items() if key != "generatedAtUtc"}
        if fresh["status"] != "complete" or stable(fresh) != stable(base):
            raise ValueError("Handler operations are incomplete, stale, or inconsistent.")
        target = Path(target).absolute()
        root = target if target.is_dir() else target.parent
        owners = {row["id"]: row for row in facts["functions"]}
        rows, originals, contexts, paths = [], [], [], Paths()
        for record in facts["selection"]["files"]:
            if record["disposition"] != "analyzed":
                continue
            source = usage.read_verified(root, record)
            tree = ast.parse(source)
            visitor = DispositionVisitor(source, record, owners, tree, paths)
            visitor.visit(tree)
            rows.extend(visitor.dispositions)
            originals.extend(visitor.rows)
            contexts.extend(visitor.contexts)
        order = lambda row: (row["file"], row["line"], row["handlerId"])
        rows.sort(key=order); contexts.sort(key=order)
        originals.sort(key=lambda row: (row["file"], row["line"], row["id"]))
        prior = base["transferReview"]["contextReview"]
        if contexts != prior["contexts"] or originals != prior["handlerReview"]["handlers"]:
            raise ValueError("Earlier handler evidence changed during disposition review.")
        report.update(status="complete", subject=prior["handlerReview"]["subject"], handlers=rows, summary=summarize(rows))
    except (OSError, SyntaxError, UnicodeError, ValueError, RecursionError, KeyError, TypeError, tokenize.TokenError) as error:
        report["diagnostics"].append({"code": "handler-disposition-analysis", "message": str(error)})
    return report


def analyze(target, *, run_id=None, audit_id=None, **selection):
    facts = usage.source_facts.analyze(target, **selection)
    base = operations.analyze(target, run_id=run_id, audit_id=audit_id, **selection)
    return review(target, facts, base)


if __name__ == "__main__":
    raise SystemExit(run(analyze, __doc__))
