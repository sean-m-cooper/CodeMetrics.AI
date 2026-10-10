"""Offline calibration candidates. Not imported by the production analyzer."""

import ast
from copy import deepcopy
import hashlib

import source_facts


class MembershipProbe(source_facts.Decisions):
    """Candidate: treat a whole-case non-destructuring OR list as membership."""

    def __init__(self):
        super().__init__()
        self.discount = 0

    def visit(self, node):
        if isinstance(node, ast.match_case):
            pattern = node.pattern
            while isinstance(pattern, ast.MatchAs) and pattern.pattern is not None:
                pattern = pattern.pattern
            if isinstance(pattern, ast.MatchOr) and all(simple_pattern(child) for child in pattern.patterns):
                self.discount += len(pattern.patterns) - 1
        return super().visit(node)


def simple_pattern(pattern):
    return isinstance(pattern, (ast.MatchValue, ast.MatchSingleton)) or (
        isinstance(pattern, ast.MatchClass) and not pattern.patterns and not pattern.kwd_patterns)


def membership_discounts(source, file):
    """Keep the production identity and exclusive-body boundary; do not run source."""
    result = {}
    for node in ast.walk(ast.parse(source, filename=file)):
        if not isinstance(node, source_facts.OWNERS):
            continue
        identity = f"{file}|{node.lineno}|{node.col_offset}|{type(node).__name__}"
        key = hashlib.sha256(identity.encode("utf-8")).hexdigest()
        probe = MembershipProbe()
        for child in [node.body] if isinstance(node, ast.Lambda) else node.body:
            probe.visit(child)
        if probe.discount:
            result[key] = probe.discount
    return result


def candidate(facts, mode, discounts=None):
    if mode not in {"membership", "without-except", "without-comprehensions"}:
        raise ValueError("Unknown calibration experiment.")
    document = deepcopy(facts)
    document["measurementPolicy"] = f"experimental/python/calibration/{mode}"
    changes = []
    for function in document["functions"]:
        counts = function["complexity"]["decisions"]
        before = function["complexity"]["value"]
        if mode == "membership":
            removed = (discounts or {}).get(function["id"], 0)
            if not 0 <= removed <= counts["matchAlternatives"]:
                raise ValueError("Membership discount exceeds measured alternatives.")
            counts["matchAlternatives"] -= removed
        else:
            keys = ["exceptHandlers"] if mode == "without-except" else ["comprehensionLoops", "comprehensionFilters"]
            for key in keys:
                counts[key] = 0
        function["complexity"]["value"] = 1 + sum(counts.values())
        after = function["complexity"]["value"]
        if before != after:
            changes.append({"id": function["id"], "file": function["file"], "member": function["member"],
                            "line": function["line"], "before": before, "after": after})
    return document, changes
