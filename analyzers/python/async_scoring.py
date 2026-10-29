"""Standalone Async/Blocking Usage scoring study; no target execution or v3 gates."""

import ast
from collections import Counter
from datetime import datetime, timezone
import io
from pathlib import Path
import re
import tokenize

import async_helper_paths as helpers
from review_cli import run

usage = helpers.usage
POLICY = "python/async-blocking-owner-rate-v1"
INTENT_POLICY = "adjacent-simple-statement-declared-blocking-prose-v1"
MAX_NODES = 1000000
LADDER = [
    {"percent": 1, "inclusive": False, "score": 10},
    {"percent": 2, "inclusive": False, "score": 9},
    {"percent": 5, "inclusive": False, "score": 8},
    {"percent": 10, "inclusive": True, "score": 6},
    {"percent": 15, "inclusive": True, "score": 4},
    {"percent": 20, "inclusive": True, "score": 2},
]
SIMPLE = (ast.Expr, ast.Return, ast.Assign, ast.AnnAssign, ast.AugAssign, ast.Raise, ast.Assert)
BOUNDARIES = (ast.FunctionDef, ast.AsyncFunctionDef, ast.Lambda, ast.ClassDef)
DIRECTIVE = re.compile(r"^(?:noqa\b|type:|pragma:|fmt:|pylint:|ruff:)", re.I)
KEYWORD = re.compile(r"\b(?:blocking|synchronous|sync-over-async|sleep)\b", re.I)
MARKER = re.compile(r"\b(?:TODO|FIXME|HACK|XXX)\b", re.I)
INTENT = re.compile(r"\b(?:intentional(?:ly)?|deliberate(?:ly)?|required|necessary|unavoidable)\b", re.I)
CAUTION = re.compile(r"\b(?:not|never|avoid|remove|replace|shouldn't|mustn't|unnecessary)\b", re.I)


def owner_score(affected, eligible):
    """Exact comparisons; displayed percentages never select a band."""
    if type(eligible) is not int or type(affected) is not int or not 0 <= affected <= eligible:
        raise ValueError("Owner counts must be integers with 0 <= affected <= eligible.")
    if not eligible:
        return None
    for band in LADDER:
        left, right = 100 * affected, band["percent"] * eligible
        if left < right or (band["inclusive"] and left == right):
            return band["score"]
    return 0


def rationale(text):
    if (DIRECTIVE.match(text) or MARKER.search(text) or not KEYWORD.search(text)
            or not INTENT.search(text) or CAUTION.search(text)):
        return False
    if len(re.findall(r"[A-Za-z]+", text)) < 2:
        return False
    try:
        ast.parse(text)
    except SyntaxError:
        return True
    return False  # Commented-out code is not an explanation.


class IntentIndex:
    """Attach actual comment tokens only to a single simple statement."""

    def __init__(self, source, budget):
        self.lines = source.splitlines()
        tree = ast.parse(source)
        self.parents, self.calls, self.statements = {}, {}, []
        for node in ast.walk(tree):
            budget[0] += 1
            if budget[0] > MAX_NODES:
                raise ValueError("Async scoring source-index budget exceeded.")
            for child in ast.iter_child_nodes(node):
                self.parents[child] = node
            if isinstance(node, ast.Call):
                self.calls[self.key(usage.extent(node))] = node
            if isinstance(node, ast.stmt):
                self.statements.append(node)
        self.starts = Counter(node.lineno for node in self.statements)
        self.ends = Counter(node.end_lineno for node in self.statements if isinstance(node, SIMPLE))
        self.comments = {}
        for token in tokenize.generate_tokens(io.StringIO(source).readline):
            if token.type == tokenize.COMMENT:
                self.comments[token.start[0]] = token

    @staticmethod
    def key(location):
        return tuple(location[name] for name in ("line", "column", "endLine", "endColumn"))

    def documents(self, location):
        node = self.calls.get(self.key(location))
        if node is None:
            raise ValueError("Blocking path has no matching source call.")
        while not isinstance(node, ast.stmt):
            node = self.parents.get(node)
            if node is None or isinstance(node, BOUNDARIES):
                return []
        if not isinstance(node, SIMPLE):
            return []
        # Do not let a semicolon, one-line compound suite, or sibling inherit prose.
        if self.starts[node.lineno] != 1 or self.ends[node.end_lineno] != 1:
            return []
        prefix = self.lines[node.lineno - 1].encode("utf-8")[:node.col_offset].decode("utf-8")
        if prefix.strip():
            return []
        found = []
        for line in (node.lineno - 1, node.end_lineno):
            token = self.comments.get(line)
            if token is None:
                continue
            before = self.lines[line - 1][:token.start[1]]
            if line == node.lineno - 1:
                attached = before == prefix
            else:
                end = len(self.lines[line - 1].encode("utf-8")[:node.end_col_offset].decode("utf-8"))
                attached = token.start[1] >= end and not self.lines[line - 1][end:token.start[1]].strip()
            text = token.string[1:].strip()
            if attached and rationale(text):
                found.append({"line": line, "text": text})
        return found


def classify(trace, indexes):
    result = {**trace, "documentation": []}
    if trace["executionContext"] != "async-body":
        return {**result, "classification": "excluded-execution-context"}
    documents = []
    for step in trace["path"]:
        documents.extend({"file": step["file"], "callId": step["callId"], **item}
                         for item in indexes[step["file"]].documents(step))
    return {**result, "documentation": documents,
            "classification": "documented-choice" if documents else "scored-signal"}


def contributions(base, indexes):
    rows = []
    for owner in base["functions"]:
        observations = [classify(trace, indexes) for trace in owner["traces"] if trace["status"] == "blocking-candidate"]
        eligible = owner["population"] == "production"
        unresolved = [trace for trace in owner["traces"] if trace["status"] == "unresolved"]
        rows.append({**{key: owner[key] for key in ("id", "file", "member", "line", "population", "sourceSha256")},
            "eligible": eligible, "eligibilityReason": "production-async-implementation" if eligible else "nonproduction",
            "affected": eligible and any(item["classification"] == "scored-signal" for item in observations),
            "observations": observations, "unresolvedPaths": unresolved})
    return rows


def summarize(rows):
    eligible = [row for row in rows if row["eligible"]]
    affected = sum(row["affected"] for row in eligible)
    observations = [item for row in eligible for item in row["observations"]]
    unresolved = [item for row in eligible for item in row["unresolvedPaths"]]
    return {"availability": "available" if eligible else "unavailable", "eligibleOwners": len(eligible),
        "affectedOwners": affected, "score": owner_score(affected, len(eligible)),
        "affectedRate": {"numerator": affected, "denominator": len(eligible)} if eligible else None,
        "excludedOwnersByPopulation": dict(sorted(Counter(row["population"] for row in rows if not row["eligible"]).items())),
        "observationPathsByClassification": dict(sorted(Counter(item["classification"] for item in observations).items())),
        "distinctScoredSourceCalls": len({item["path"][-1]["callId"] for item in observations if item["classification"] == "scored-signal"}),
        "ownersWithUnresolvedPaths": sum(bool(row["unresolvedPaths"]) for row in eligible),
        "unresolvedPathsByReason": dict(sorted(Counter(item["reason"] for item in unresolved).items()))}


def review(target, facts, base):
    configuration = {"policy": POLICY, "helperConfigurationFingerprint": base["configurationFingerprint"],
        "intentPolicy": INTENT_POLICY, "maxSourceIndexNodes": MAX_NODES, "ladder": LADDER,
        "population": "selected-production-async-implementations", "unresolvedTreatment": "visible-without-deduction"}
    report = {"schemaVersion": 1, "artifactKind": "experimental-python-async-scoring",
        "producer": {"name": "codemetrics-ai/python-async-scoring", "version": "0.1.0-experimental"},
        "generatedAtUtc": datetime.now(timezone.utc).isoformat(), "runId": base["runId"], "auditId": base["auditId"],
        "policy": POLICY, "configuration": configuration, "configurationFingerprint": usage.fingerprint(configuration),
        "helperRowsSha256": usage.fingerprint(base["functions"]), "subject": base["subject"], "selection": facts["selection"],
        "sourceFactsProducer": facts["producer"], "sourceFactsPolicy": facts["measurementPolicy"],
        "status": "incomplete", "diagnostics": [], "functions": [], "summary": None,
        "scope": "Partial static blocking-catalogue usage score. Import spelling and bounded helper paths, not runtime identity, reachability, throughput, latency, or proof of safe async behavior. Standalone study; no scorecard v3 aggregate or gates. Error Handling remains unscored."}
    try:
        original = usage.review(target, facts, run_id=base["runId"], audit_id=base["auditId"])
        fresh = helpers.review(target, facts, original)
        stable = lambda value: {key: item for key, item in value.items() if key != "generatedAtUtc"}
        if fresh["status"] != "complete" or stable(fresh) != stable(base):
            raise ValueError("Async helper input is incomplete, stale, or inconsistent.")
        target = Path(target).absolute()
        root = target if target.is_dir() else target.parent
        needed = {step["file"] for row in base["functions"] for trace in row["traces"]
                  if trace["status"] == "blocking-candidate" and trace["executionContext"] == "async-body" for step in trace["path"]}
        indexes, budget = {}, [0]
        for record in facts["selection"]["files"]:
            if record["file"] in needed:
                indexes[record["file"]] = IntentIndex(usage.read_verified(root, record), budget)
        rows = contributions(base, indexes)
        report.update(status="complete", functions=rows, summary=summarize(rows))
    except (OSError, SyntaxError, UnicodeError, ValueError, RecursionError, KeyError, TypeError, tokenize.TokenError) as error:
        report["diagnostics"].append({"code": "async-scoring-analysis", "message": str(error)})
    return report


def analyze(target, *, run_id=None, audit_id=None, **selection):
    facts = usage.source_facts.analyze(target, **selection)
    original = usage.review(target, facts, run_id=run_id, audit_id=audit_id)
    base = helpers.review(target, facts, original)
    return review(target, facts, base)


if __name__ == "__main__":
    raise SystemExit(run(analyze, __doc__))
