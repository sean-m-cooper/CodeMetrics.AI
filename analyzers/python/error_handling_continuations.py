"""Bounded post-handler continuations and retained-default syntax, without scores."""

import ast
from collections import Counter
from datetime import datetime, timezone
from itertools import islice
from pathlib import Path
import tokenize

import error_handling_dispositions as dispositions
from review_cli import run

usage = dispositions.usage
POLICY = "python/exception-continuations-review-v1"
MAX_ENCLOSING = 8
MAX_NODES = 256
MAX_TOTAL_NODES = 100000


class ContinuationPaths(dispositions.Paths):
    def __init__(self):
        super().__init__()
        self.returns = []

    def statement(self, node, active, depth):
        if isinstance(node, ast.Raise) and node.exc is None:
            return {("unresolved", "bare-raise-after-handler")}
        if isinstance(node, ast.Return):
            self.returns.append(node)
        return super().statement(node, active, depth)


def location(node):
    return {"kind": type(node).__name__, **usage.extent(node)}


def blocker(visitor, node, original):
    if original["exceptionGroup"]:
        return "exception-group-semantics"
    if original["enclosingFinallyLines"]:
        return "enclosing-finally"
    for ancestor in visitor.ancestors(visitor.parents[node]):
        if isinstance(ancestor, (ast.FunctionDef, ast.AsyncFunctionDef)):
            if any(dispositions.has_yield(statement) for statement in ancestor.body):
                return "generator-owner"
            break
        if not isinstance(ancestor, (ast.If, ast.Module)):
            return "enclosing-" + type(ancestor).__name__
    return None


def continuation(visitor, attempt, paths, total):
    """Skip the handled try's else/siblings; climb only through completed if suites."""
    cursor, crossed, statements, locations = attempt, [], [], []
    states = {("fallthrough", "no-action")}
    count = 0
    for _ in range(MAX_ENCLOSING + 1):
        _, index, suite = visitor.suites[cursor]
        for statement in suite[index + 1:]:
            # Stop collecting after terminal paths; unreachable code cannot supply
            # fallback evidence or invalidate an already established continuation.
            if not any(kind == "fallthrough" for kind, _ in states):
                return states, statements, locations, crossed, None
            size = sum(1 for _ in islice(ast.walk(statement), MAX_NODES + 1))
            count += size
            total[0] += size
            if total[0] > MAX_TOTAL_NODES:
                raise ValueError("Continuation report exceeds its node budget.")
            if count > MAX_NODES:
                return states, statements, locations, crossed, "continuation-node-limit"
            statements.append(statement)
            locations.append(location(statement))
            states = paths.suite([statement], states)
        if not any(kind == "fallthrough" for kind, _ in states):
            return states, statements, locations, crossed, None
        parent = visitor.parents[cursor]
        if isinstance(parent, (ast.FunctionDef, ast.AsyncFunctionDef, ast.Module)):
            return states, statements, locations, crossed, "scope-end-without-explicit-terminal"
        if not isinstance(parent, ast.If):
            return states, statements, locations, crossed, "continuation-boundary-" + type(parent).__name__
        crossed.append(location(parent))
        cursor = parent
    return states, statements, locations, crossed, "continuation-enclosing-limit"


def simple_assignment(node, name=None):
    if not isinstance(node, ast.Assign) or len(node.targets) != 1 or not isinstance(node.targets[0], ast.Name):
        return False
    if name is not None and node.targets[0].id != name:
        return False
    return isinstance(node.value, ast.Constant) or dispositions.operations.transfers.symbolic_path(node.value) is not None


def writes_name(node, name):
    for child in ast.walk(node):
        if isinstance(child, ast.Name) and child.id == name and isinstance(child.ctx, (ast.Store, ast.Del)):
            return True
        if isinstance(child, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)) and child.name == name:
            return True
        if isinstance(child, ast.ExceptHandler) and child.name == name:
            return True
        if isinstance(child, (ast.Import, ast.ImportFrom)) and any((item.asname or item.name.split(".")[0]) == name for item in child.names):
            return True
        if isinstance(child, (ast.MatchAs, ast.MatchStar)) and child.name == name:
            return True
        if isinstance(child, ast.MatchMapping) and child.rest == name:
            return True
    return False


def retained_default(visitor, handler, attempt, statements, returns, total):
    # A single simple RHS assignment to a local name retains its old binding if
    # RHS evaluation raises. This records syntax, not object state or semantics.
    if len(attempt.body) != 1 or not simple_assignment(attempt.body[0]):
        return None
    guarded = attempt.body[0]
    name = guarded.targets[0].id
    if handler.name == name:
        return None  # Python clears the exception target after the handler.
    prefixes, cursor, owner = [], attempt, None
    for _ in range(MAX_ENCLOSING + 1):
        _, index, suite = visitor.suites[cursor]
        prefixes.append(suite[:index])
        parent = visitor.parents[cursor]
        if isinstance(parent, (ast.FunctionDef, ast.AsyncFunctionDef)):
            owner = parent
            break
        if not isinstance(parent, ast.If):
            return None
        if writes_name(parent.test, name):
            return None
        cursor = parent
    if owner is None:
        return None
    owner_nodes = list(islice(ast.walk(owner), MAX_NODES + 1))
    total[0] += len(owner_nodes)
    if total[0] > MAX_TOTAL_NODES:
        raise ValueError("Continuation report exceeds its node budget.")
    if len(owner_nodes) > MAX_NODES:
        return None
    for node in owner_nodes:
        if isinstance(node, (ast.Global, ast.Nonlocal)) and name in node.names:
            return None
        if isinstance(node, ast.Call) and isinstance(node.func, ast.Name) and node.func.id in {"exec", "eval", "globals", "locals"}:
            return None
    initializer = None
    for suite in reversed(prefixes):
        for statement in suite:
            if simple_assignment(statement, name):
                initializer = statement
            elif writes_name(statement, name):
                initializer = None
    if initializer is None or any(writes_name(statement, name) for statement in statements):
        return None
    uses = []
    for node in returns:
        if isinstance(node.value, ast.Name) and node.value.id == name:
            uses.append({"line": node.lineno, "kind": "returned-name"})
        elif isinstance(node.value, ast.Call) and isinstance(node.value.func, ast.Name) and node.value.func.id == name:
            uses.append({"line": node.lineno, "kind": "returned-call-through-name"})
    if not uses:
        return None
    return {"status": "retained-default-syntax", "name": name,
        "initializer": location(initializer), "initializerValueKind": type(initializer.value).__name__,
        "initializerSymbol": dispositions.operations.transfers.symbolic_path(initializer.value),
        "guardedAssignment": location(guarded), "uses": uses}


class ContinuationVisitor(dispositions.DispositionVisitor):
    def __init__(self, source, record, owners, tree, paths, continuation_paths, total):
        super().__init__(source, record, owners, tree, paths)
        self.extensions, self.continuation_paths, self.total = [], continuation_paths, total

    def handler(self, node, group):
        offset = len(self.rows)
        super().handler(node, group)
        original, prior = self.rows[offset], self.dispositions[-1]
        row = {key: prior[key] for key in ("handlerId", "file", "line", "sourceSha256", "population", "owner")}
        row.update(originalClassification=prior["classification"], classification=prior["classification"],
            originalReasons=prior["reasons"], disposition="review-only", continuation={"status": "not-applicable",
                "reason": "handler-already-explicit-or-non-inert", "statements": [], "crossedBranches": [], "paths": [], "retainedDefault": None})
        if original["classification"] == "undocumented-no-op" and prior["classification"] in {"unresolved", "unexplained-swallowing-candidate"}:
            self.extend(node, original, row)
        self.extensions.append(row)

    def extend(self, node, original, row):
        evidence = row["continuation"]
        reason = blocker(self, node, original)
        if reason:
            evidence.update(status="unresolved", reason=reason)
            return
        attempt = self.parents[node]
        return_start = len(self.continuation_paths.returns)
        states, statements, locations, crossed, reason = continuation(self, attempt, self.continuation_paths, self.total)
        evidence.update(status="unresolved", reason=reason, statements=locations, crossedBranches=crossed,
            paths=[{"outcome": kind, "detail": detail} for kind, detail in sorted(states)])
        kinds = {kind for kind, _ in states}
        if reason is None and kinds and kinds <= {"propagation", "explicit-fallback"}:
            evidence.update(status="explicit-continuation", reason="all-supported-paths-reach-explicit-terminal",
                retainedDefault=retained_default(self, node, attempt, statements,
                    self.continuation_paths.returns[return_start:], self.total))
            row["classification"] = next(iter(kinds)) if len(kinds) == 1 else "mixed-explicit-disposition"
        elif reason is None:
            evidence["reason"] = "unsupported-continuation-path"


def summarize(rows):
    def counts(items):
        return {"handlers": len(items), "byClassification": dict(sorted(Counter(row["classification"] for row in items).items())),
            "explicitContinuations": sum(row["continuation"]["status"] == "explicit-continuation" for row in items),
            "retainedDefaultLinks": sum(row["continuation"]["retainedDefault"] is not None for row in items),
            "candidateReclassifications": sum(row["originalClassification"] == "unexplained-swallowing-candidate" and
                row["classification"] != "unexplained-swallowing-candidate" for row in items),
            "unresolvedContinuationReasons": dict(sorted(Counter(row["continuation"]["reason"] for row in items
                if row["continuation"]["status"] == "unresolved").items()))}
    return {**counts(rows), "populations": {kind: counts([row for row in rows if row["population"] == kind])
        for kind in sorted({row["population"] for row in rows})}}


def review(target, facts, operations_base):
    base = dispositions.review(target, facts, operations_base)
    configuration = {"policy": POLICY, "dispositionConfigurationFingerprint": base["configurationFingerprint"],
        "maxEnclosing": MAX_ENCLOSING, "maxNodesPerContinuation": MAX_NODES, "maxTotalNodes": MAX_TOTAL_NODES}
    report = {"schemaVersion": 1, "artifactKind": "experimental-python-handler-continuations",
        "producer": {"name": "codemetrics-ai/python-handler-continuations", "version": "0.1.0-experimental"},
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(), "runId": base["runId"], "auditId": base["auditId"],
        "policy": POLICY, "configuration": configuration, "configurationFingerprint": usage.fingerprint(configuration),
        "dispositionRowsSha256": usage.fingerprint(base["handlers"]), "selection": facts["selection"],
        "sourceFactsProducer": facts["producer"], "sourceFactsPolicy": facts["measurementPolicy"],
        "status": "incomplete", "diagnostics": list(base["diagnostics"]), "handlers": [], "summary": None,
        "scope": "Bounded post-handler lexical paths conditional on normal expression completion; no recovery-success, defect or scoring claim. Both Error Handling and Async/Blocking Usage remain unscored."}
    if base["status"] != "complete":
        return report
    try:
        target = Path(target).absolute()
        root = target if target.is_dir() else target.parent
        owners = {row["id"]: row for row in facts["functions"]}
        rows, prior, paths, after, total = [], [], dispositions.Paths(), ContinuationPaths(), [0]
        for record in facts["selection"]["files"]:
            if record["disposition"] != "analyzed":
                continue
            source = usage.read_verified(root, record)
            tree = ast.parse(source)
            visitor = ContinuationVisitor(source, record, owners, tree, paths, after, total)
            visitor.visit(tree)
            rows.extend(visitor.extensions)
            prior.extend(visitor.dispositions)
        order = lambda row: (row["file"], row["line"], row["handlerId"])
        rows.sort(key=order); prior.sort(key=order)
        if prior != base["handlers"]:
            raise ValueError("Disposition evidence changed during continuation analysis.")
        report.update(status="complete", subject=base["subject"], handlers=rows, summary=summarize(rows))
    except (OSError, SyntaxError, UnicodeError, ValueError, RecursionError, KeyError, TypeError, tokenize.TokenError) as error:
        report["diagnostics"].append({"code": "handler-continuation-analysis", "message": str(error)})
    return report


def analyze(target, *, run_id=None, audit_id=None, **selection):
    facts = usage.source_facts.analyze(target, **selection)
    base = dispositions.operations.analyze(target, run_id=run_id, audit_id=audit_id, **selection)
    return review(target, facts, base)


if __name__ == "__main__":
    raise SystemExit(run(analyze, __doc__))
