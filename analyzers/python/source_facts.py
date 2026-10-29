"""Experimental Python function facts. Parse source only; never import target code."""

import argparse
import ast
import hashlib
import json
import os
from pathlib import Path
import platform
import sys
import tokenize
from source_selection import (declaration_reason, file_record, generated_source, import_bindings,
                              inventory, resolve, selection_options)


POLICY = "python/owned-function-complexity-v2"
EXCLUDED_DIRECTORIES = {".git", ".venv", "venv", "env", "__pycache__", "node_modules", "build", "dist", ".tox", ".mypy_cache", ".pytest_cache"}
OWNERS = (ast.FunctionDef, ast.AsyncFunctionDef, ast.Lambda)
MAX_FILE_BYTES = 2 * 1024 * 1024
MAX_FILES = 10000


class Decisions(ast.NodeVisitor):
    """Count decisions in one body, excluding nested executable owners."""

    def __init__(self):
        self.counts = {key: 0 for key in (
            "ifStatements", "loops", "exceptHandlers", "conditionalExpressions",
            "booleanOperators", "chainedComparisons", "comprehensionLoops",
            "comprehensionFilters", "matchCases", "matchGuards", "matchAlternatives")}

    def visit_AnnAssign(self, node):
        self.visit(node.target)
        if node.value is not None:
            self.visit(node.value)

    def visit(self, node):
        if isinstance(node, OWNERS + (ast.ClassDef,)):
            return
        if isinstance(node, ast.If):
            self.counts["ifStatements"] += 1
        elif isinstance(node, (ast.For, ast.AsyncFor, ast.While)):
            self.counts["loops"] += 1
        elif isinstance(node, ast.ExceptHandler):
            self.counts["exceptHandlers"] += 1
        elif isinstance(node, ast.IfExp):
            self.counts["conditionalExpressions"] += 1
        elif isinstance(node, ast.BoolOp):
            self.counts["booleanOperators"] += len(node.values) - 1
        elif isinstance(node, ast.Compare):
            self.counts["chainedComparisons"] += len(node.ops) - 1
        elif isinstance(node, ast.comprehension):
            self.counts["comprehensionLoops"] += 1
            self.counts["comprehensionFilters"] += len(node.ifs)
        elif isinstance(node, ast.match_case):
            if not irrefutable(node.pattern):
                self.counts["matchCases"] += 1
            self.counts["matchGuards"] += int(node.guard is not None)
            membership = membership_pattern(node.pattern)
            if membership is not None:
                # Only the whole-case OR is one membership decision. Keep all
                # guard/body decisions and nested-owner boundaries unchanged.
                self.generic_visit(membership)
                if node.guard is not None:
                    self.visit(node.guard)
                for statement in node.body:
                    self.visit(statement)
                return
        elif isinstance(node, ast.MatchOr):
            self.counts["matchAlternatives"] += len(node.patterns) - 1
        super().visit(node)


def membership_pattern(pattern):
    """Recognize a whole-case non-destructuring OR, optionally as-bound."""
    while isinstance(pattern, ast.MatchAs) and pattern.pattern is not None:
        pattern = pattern.pattern
    if isinstance(pattern, ast.MatchOr) and all(
        isinstance(child, (ast.MatchValue, ast.MatchSingleton)) or (
            isinstance(child, ast.MatchClass) and not child.patterns and not child.kwd_patterns)
        for child in pattern.patterns
    ):
        return pattern
    return None


def irrefutable(pattern):
    """A capture or wildcard (possibly as-bound) is the default match branch."""
    if isinstance(pattern, ast.MatchAs):
        return pattern.pattern is None or irrefutable(pattern.pattern)
    if isinstance(pattern, ast.MatchOr):
        return any(irrefutable(child) for child in pattern.patterns)
    return False


class Functions(ast.NodeVisitor):
    def __init__(self, file, tree=None):
        self.file = file
        self.parents = []
        self.functions = []
        self.excluded_functions = []
        self.bindings = import_bindings(tree) if tree is not None else {}
        self.contexts = []

    def visit_AnnAssign(self, node):
        self.visit(node.target)
        if node.value is not None:
            self.visit(node.value)

    def visit_ClassDef(self, node):
        self.parents.append(node.name)
        protocol = any(resolve(base, self.bindings) in {"typing.Protocol", "typing_extensions.Protocol"} for base in node.bases)
        self.contexts.append(("class", protocol))
        for child in node.body:
            self.visit(child)
        self.contexts.pop()
        self.parents.pop()

    def visit_FunctionDef(self, node):
        name = getattr(node, "name", f"<lambda@{node.lineno}:{node.col_offset}>")
        member = ".".join(self.parents + [name])
        identity = f"{self.file}|{node.lineno}|{node.col_offset}|{type(node).__name__}"
        location = {"id": hashlib.sha256(identity.encode("utf-8")).hexdigest(),
                    "file": self.file, "member": member, "line": node.lineno}
        protocol_method = bool(self.contexts and self.contexts[-1] == ("class", True))
        reason = declaration_reason(node, self.bindings, protocol_method)
        if reason:
            self.excluded_functions.append({**location, "reason": reason})
            return
        decisions = Decisions()
        body = [node.body] if isinstance(node, ast.Lambda) else node.body
        for child in body:
            decisions.visit(child)
        self.functions.append({
            **location,
            "ownership": "exclusive-function-body",
            "complexity": {"value": 1 + sum(decisions.counts.values()), "baseline": 1, "decisions": decisions.counts},
        })
        self.parents.append(name)
        self.contexts.append(("function", False))
        for child in body:
            self.visit(child)
        self.parents.pop()
        self.contexts.pop()

    visit_AsyncFunctionDef = visit_FunctionDef
    visit_Lambda = visit_FunctionDef


def measure_source(source, file):
    tree = ast.parse(source, filename=file)
    collector = Functions(file, tree)
    collector.visit(tree)
    return collector.functions


def new_document(options=None):
    return {
        "schemaVersion": 2, "language": "python",
        "producer": {"name": "codemetrics-ai/python-source-facts", "version": "0.4.0-experimental",
                     "parser": "python-ast", "parserVersion": platform.python_version()},
        "measurementPolicy": POLICY, "status": "complete",
        "capabilities": {"functionComplexity": "available", "coverage": "unsupported", "maintainability": "unsupported"},
        "scope": {
            "description": "Selected Python implementation bodies. File roles are explicit scope conventions, not runtime reachability or defect judgments. See selection inventory.",
            "excludes": ["nested-function-and-class-bodies-from-parent", "decorators", "class-headers", "annotations", "parameter-initializers",
                         "module-and-class-initializer-decisions", "symlinks", "directory-junctions", *sorted(EXCLUDED_DIRECTORIES)],
        },
        "diagnostics": [], "functions": [], "selection": inventory(options or selection_options()),
    }


def diagnostic(document, code, message, file=None):
    document["status"] = "incomplete"
    entry = {"code": code, "message": message}
    if file:
        entry["file"] = str(file)
    document["diagnostics"].append(entry)


def linked(path):
    return path.is_symlink() or path.is_junction()


def discover(target, document):
    if linked(target):
        diagnostic(document, "linked-target", "An explicit symlink/junction target is not analyzed.")
        return []
    if target.is_file():
        if target.suffix not in {".py", ".pyi"}:
            diagnostic(document, "unsupported-file", "The input file must have a .py or .pyi extension.")
            return []
        return [target]
    if not target.is_dir():
        diagnostic(document, "missing-target", "The input path does not exist.")
        return []
    files = []
    def on_error(error):
        diagnostic(document, "discovery-error", str(error))
    for directory, directories, names in os.walk(target, followlinks=False, onerror=on_error):
        retained = []
        for name in sorted(directories):
            path = Path(directory) / name
            if name in EXCLUDED_DIRECTORIES or linked(path):
                document["selection"]["prunedDirectories"].append({"path": path.relative_to(target).as_posix(), "reason": "linked-directory" if linked(path) else "dependency-cache-or-build-directory"})
            else:
                retained.append(name)
        directories[:] = retained
        for name in sorted(names):
            path = Path(directory) / name
            if path.suffix in {".py", ".pyi"}:
                files.append(path)
                if len(files) > MAX_FILES:
                    diagnostic(document, "file-limit", f"More than {MAX_FILES} Python files; select a smaller root.")
                    return []
    return files


def analyze(target, **configuration):
    try:
        options = selection_options(**configuration)
    except ValueError as error:
        document = new_document()
        diagnostic(document, "invalid-selection", str(error))
        return document
    document = new_document(options)
    if sys.version_info[:2] != (3, 12):
        diagnostic(document, "unsupported-parser", "This experimental policy is tested with Python 3.12 only.")
        return document
    target = Path(os.path.abspath(target))
    root = target if target.is_dir() else target.parent
    for path in discover(target, document):
        file = path.relative_to(root).as_posix()
        record = file_record(file, options)
        document["selection"]["files"].append(record)
        if linked(path):
            record.update(disposition="excluded", reason="linked-file")
        if record["disposition"] == "excluded":
            continue
        try:
            if not path.resolve().is_relative_to(root.resolve()):
                raise ValueError("File resolves outside the selected root.")
            if path.stat().st_size > MAX_FILE_BYTES:
                raise ValueError(f"File exceeds {MAX_FILE_BYTES} bytes.")
            with tokenize.open(path) as source:
                text = source.read()
            record["contentSha256"] = hashlib.sha256(text.encode("utf-8")).hexdigest()
            if generated_source(text):
                record.update(category="generated", reason="generated-header-marker")
                if not options["includeGenerated"]:
                    record["disposition"] = "excluded"
                    continue
            tree = ast.parse(text, filename=file)
            collector = Functions(file, tree)
            collector.visit(tree)
            document["functions"].extend({**item, "population": record["category"]} for item in collector.functions)
            document["selection"]["excludedFunctions"].extend(collector.excluded_functions)
        except (SyntaxError, UnicodeError, OSError, ValueError, RecursionError) as error:
            record.update(disposition="failed", reason="parse-error")
            diagnostic(document, "parse-error", str(error), file)
    document["functions"].sort(key=lambda item: (item["file"], item["line"], item["id"]))
    return document


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", help="Python file or directory (production candidates by default)")
    for option in ("include", "exclude", "test-path", "production-path"):
        parser.add_argument(f"--{option}", action="append", default=[], help="Root-relative POSIX pattern; repeatable")
    for option in ("include-tests", "include-generated", "include-support"):
        parser.add_argument(f"--{option}", action="store_true")
    parser.add_argument("--output", type=Path, help="New .json file; existing files are never overwritten")
    args = parser.parse_args()
    document = analyze(args.target, include=args.include, exclude=args.exclude, test_paths=args.test_path,
                       production_paths=args.production_path, include_tests=args.include_tests,
                       include_generated=args.include_generated, include_support=args.include_support)
    payload = json.dumps(document, indent=2, ensure_ascii=True) + "\n"
    try:
        if args.output:
            if args.output.suffix != ".json":
                raise ValueError("Output must be a new .json file.")
            with args.output.open("x", encoding="utf-8") as output:
                output.write(payload)
        else:
            sys.stdout.write(payload)
    except (OSError, ValueError) as error:
        print(f"Cannot write source facts: {error}", file=sys.stderr)
        return 2
    return 0 if document["status"] == "complete" else 2


if __name__ == "__main__":
    raise SystemExit(main())
