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


POLICY = "python/owned-function-complexity-v1"
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
        elif isinstance(node, ast.MatchOr):
            self.counts["matchAlternatives"] += len(node.patterns) - 1
        super().visit(node)


def irrefutable(pattern):
    """A capture or wildcard (possibly as-bound) is the default match branch."""
    if isinstance(pattern, ast.MatchAs):
        return pattern.pattern is None or irrefutable(pattern.pattern)
    if isinstance(pattern, ast.MatchOr):
        return any(irrefutable(child) for child in pattern.patterns)
    return False


class Functions(ast.NodeVisitor):
    def __init__(self, file):
        self.file = file
        self.parents = []
        self.functions = []

    def visit_AnnAssign(self, node):
        self.visit(node.target)
        if node.value is not None:
            self.visit(node.value)

    def visit_ClassDef(self, node):
        self.parents.append(node.name)
        for child in node.body:
            self.visit(child)
        self.parents.pop()

    def visit_FunctionDef(self, node):
        name = getattr(node, "name", f"<lambda@{node.lineno}:{node.col_offset}>")
        member = ".".join(self.parents + [name])
        identity = f"{self.file}|{node.lineno}|{node.col_offset}|{type(node).__name__}"
        decisions = Decisions()
        body = [node.body] if isinstance(node, ast.Lambda) else node.body
        for child in body:
            decisions.visit(child)
        self.functions.append({
            "id": hashlib.sha256(identity.encode("utf-8")).hexdigest(),
            "file": self.file, "member": member, "line": node.lineno,
            "ownership": "exclusive-function-body",
            "complexity": {"value": 1 + sum(decisions.counts.values()), "baseline": 1, "decisions": decisions.counts},
        })
        self.parents.append(name)
        for child in body:
            self.visit(child)
        self.parents.pop()

    visit_AsyncFunctionDef = visit_FunctionDef
    visit_Lambda = visit_FunctionDef


def measure_source(source, file):
    tree = ast.parse(source, filename=file)
    collector = Functions(file)
    collector.visit(tree)
    return collector.functions


def new_document():
    return {
        "schemaVersion": 1, "language": "python",
        "producer": {"name": "codemetrics-ai/python-source-facts", "version": "0.1.0-experimental",
                     "parser": "python-ast", "parserVersion": platform.python_version()},
        "measurementPolicy": POLICY, "status": "complete",
        "capabilities": {"functionComplexity": "available", "coverage": "unsupported", "maintainability": "unsupported"},
        "scope": {
            "description": "All .py files under the selected root, including tests. Native parser grammar; no imports or execution.",
            "excludes": ["nested-function-and-class-bodies-from-parent", "decorators", "class-headers", "annotations", "parameter-initializers",
                         "module-and-class-initializer-decisions", "symlinks", "directory-junctions", *sorted(EXCLUDED_DIRECTORIES)],
        },
        "diagnostics": [], "functions": [],
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
        if target.suffix != ".py":
            diagnostic(document, "unsupported-file", "The input file must have a .py extension.")
            return []
        return [target]
    if not target.is_dir():
        diagnostic(document, "missing-target", "The input path does not exist.")
        return []
    files = []
    def on_error(error):
        diagnostic(document, "discovery-error", str(error))
    for directory, directories, names in os.walk(target, followlinks=False, onerror=on_error):
        directories[:] = sorted(name for name in directories if name not in EXCLUDED_DIRECTORIES and not linked(Path(directory) / name))
        for name in sorted(names):
            path = Path(directory) / name
            if path.suffix == ".py" and not linked(path):
                files.append(path)
                if len(files) > MAX_FILES:
                    diagnostic(document, "file-limit", f"More than {MAX_FILES} Python files; select a smaller root.")
                    return []
    return files


def analyze(target):
    document = new_document()
    if sys.version_info[:2] != (3, 12):
        diagnostic(document, "unsupported-parser", "This experimental policy is tested with Python 3.12 only.")
        return document
    target = Path(os.path.abspath(target))
    root = target if target.is_dir() else target.parent
    for path in discover(target, document):
        file = path.relative_to(root).as_posix()
        try:
            if not path.resolve().is_relative_to(root.resolve()):
                raise ValueError("File resolves outside the selected root.")
            if path.stat().st_size > MAX_FILE_BYTES:
                raise ValueError(f"File exceeds {MAX_FILE_BYTES} bytes.")
            with tokenize.open(path) as source:
                document["functions"].extend(measure_source(source.read(), file))
        except (SyntaxError, UnicodeError, OSError, ValueError, RecursionError) as error:
            diagnostic(document, "parse-error", str(error), file)
    document["functions"].sort(key=lambda item: (item["file"], item["line"], item["id"]))
    return document


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", help="Python file or directory (tests included)")
    parser.add_argument("--output", type=Path, help="New .json file; existing files are never overwritten")
    args = parser.parse_args()
    document = analyze(args.target)
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
