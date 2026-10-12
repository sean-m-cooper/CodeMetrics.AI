"""Offline coverage.py line-coverage/CRAP review; never execute target code."""

import argparse
import ast
from bisect import bisect_left, bisect_right
from collections import Counter, defaultdict
from datetime import datetime, timezone
from fractions import Fraction
import hashlib
import json
from pathlib import Path, PurePosixPath
import sys
import uuid

from duplication import digest, read_verified
import source_facts


POLICY = "python/coveragepy-line-crap-review-v1"
MAX_REPORT_BYTES = 32 * 1024 * 1024
MAX_OWNERS = 100000
FORMULA = "CC^2 * (1 - coveredLines / executableLines)^3 + CC"


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate JSON key: {key}")
        result[key] = value
    return result


def invalid_constant(value):
    raise ValueError(f"Non-finite JSON number: {value}")


def report_path(value):
    """Coverage paths are relative to the selected source root, on either OS."""
    path = value.replace("\\", "/")
    if not path or path.startswith("/") or ":" in path or ".." in path.split("/"):
        raise ValueError(f"Coverage path must be root-relative without traversal: {value}")
    normalized = PurePosixPath(path).as_posix()
    if normalized == ".":
        raise ValueError("Coverage path must identify a file.")
    return normalized


def line_set(record, key):
    lines = record.get(key)
    if not isinstance(lines, list) or any(type(line) is not int or line < 1 for line in lines):
        raise ValueError(f"{key} must contain positive integer line numbers.")
    result = set(lines)
    if len(result) != len(lines):
        raise ValueError(f"{key} contains duplicate lines.")
    return result


def load_coverage(path):
    if path is None:
        return {}, None
    with Path(path).open("rb") as handle:
        raw = handle.read(MAX_REPORT_BYTES + 1)
    if len(raw) > MAX_REPORT_BYTES:
        raise ValueError("Coverage JSON exceeds the report byte budget.")
    data = json.loads(raw, object_pairs_hook=unique_object, parse_constant=invalid_constant)
    if not isinstance(data, dict) or not isinstance(data.get("meta"), dict):
        raise ValueError("Expected a coverage.py JSON report with metadata.")
    meta = data["meta"]
    if type(meta.get("format")) is not int or meta["format"] not in (2, 3):
        raise ValueError("Only coverage.py JSON formats 2 and 3 are supported.")
    if not isinstance(meta.get("version"), str) or not meta["version"]:
        raise ValueError("Coverage producer version is required.")
    if not isinstance(meta.get("timestamp"), str) or type(meta.get("branch_coverage")) is not bool:
        raise ValueError("Coverage timestamp and branch_coverage metadata are required.")
    files = data.get("files")
    if not isinstance(files, dict) or len(files) > source_facts.MAX_FILES:
        raise ValueError("Coverage files must be an object within the file-count budget.")
    normalized = {}
    for file, record in files.items():
        name = report_path(file)
        if name in normalized or not isinstance(record, dict):
            raise ValueError("Duplicate normalized coverage path or invalid file record.")
        executed = line_set(record, "executed_lines")
        missing = line_set(record, "missing_lines")
        excluded = line_set(record, "excluded_lines")
        if missing & (executed | excluded):
            raise ValueError(f"Conflicting executed/missing/excluded lines: {file}")
        normalized[name] = {"executed": sorted(executed - excluded), "missing": sorted(missing), "excluded": sorted(excluded)}
    provenance = {"path": str(Path(path).absolute()), "sha256": hashlib.sha256(raw).hexdigest(),
                  "producer": "coverage.py", "version": meta["version"], "format": meta["format"],
                  "reportedAt": meta["timestamp"], "branchDataPresent": meta["branch_coverage"],
                  "sourceBinding": "unverified", "freshness": "unverified"}
    return normalized, provenance


def body_region(node, lines):
    if isinstance(node, ast.Lambda):
        return None, "lambda-line-attribution"
    first = node.body[0]
    # Definition execution cannot prove body execution on a shared header line,
    # including a multi-line signature whose closing line also starts the body.
    if lines[first.lineno - 1].encode("utf-8")[:first.col_offset].strip():
        return None, "body-shares-definition-line"
    body = node.body
    if isinstance(first, ast.Expr) and isinstance(first.value, ast.Constant) and isinstance(first.value.value, str):
        body = body[1:]
    if not body:
        return None, "no-reportable-body-lines"
    tree = ast.Module(body=body, type_ignores=[])
    if any(isinstance(child, source_facts.OWNERS + (ast.ClassDef,)) for child in ast.walk(tree)):
        return None, "nested-definition"
    if body[0].lineno <= first.end_lineno and body[0] is not first:
        return None, "body-shares-docstring-line"
    return (body[0].lineno, node.end_lineno), None


def measure_function(function, node, lines, coverage, supplied):
    result = {key: function[key] for key in ("id", "file", "member", "line", "population")}
    result.update(endLine=node.end_lineno, complexity=function["complexity"]["value"],
                  coverage={"status": "unavailable"}, crapEstimate=None)
    region, reason = body_region(node, lines)
    if not supplied:
        reason = "coverage-not-supplied"
    elif coverage is None:
        reason = "file-not-in-report"
    if reason:
        result["coverage"]["reason"] = reason
        return result
    start, end = region
    owned = {key: values[bisect_left(values, start):bisect_right(values, end)] for key, values in coverage.items()}
    covered = len(owned["executed"])
    total = covered + len(owned["missing"])
    if total == 0:
        result["coverage"]["reason"] = "no-reportable-body-lines"
        result["coverage"]["excludedLines"] = owned["excluded"]
        return result
    result["coverage"] = {"status": "reported-unverified", "basis": "executable-line",
                          "coveredLines": covered, "executableLines": total,
                          "executedLines": owned["executed"], "missingLines": owned["missing"],
                          "excludedLines": owned["excluded"], "fraction": covered / total}
    cc = result["complexity"]
    value = Fraction(cc * cc) * Fraction(total - covered, total) ** 3 + cc
    result["crapEstimate"] = {"value": float(value), "disposition": "conditional-review-only",
                              "basis": "line-coverage-proxy", "formula": FORMULA}
    return result


def collect(root, facts, coverage, supplied):
    if len(facts["functions"]) > MAX_OWNERS:
        raise ValueError("Selected functions exceed the coverage owner budget.")
    by_file = defaultdict(dict)
    for function in facts["functions"]:
        by_file[function["file"]][function["id"]] = function
    rows = []
    selected_files = set()
    for record in facts["selection"]["files"]:
        if record["disposition"] != "analyzed":
            continue
        file = record["file"]
        selected_files.add(file)
        source = read_verified(root, record)
        # AST line numbers use newlines, not every separator str.splitlines accepts.
        lines = source.split("\n")
        if lines[-1] == "":
            lines.pop()
        observed = coverage.get(file)
        if observed and any(values and values[-1] > len(lines) for values in observed.values()):
            raise ValueError(f"Coverage line exceeds current source length: {file}")
        seen = set()
        for node in ast.walk(ast.parse(source, filename=file)):
            if not isinstance(node, source_facts.OWNERS):
                continue
            key = digest(f"{file}|{node.lineno}|{node.col_offset}|{type(node).__name__}")
            function = by_file[file].get(key)
            if function is not None:
                seen.add(key)
                row = measure_function(function, node, lines, observed, supplied)
                row["sourceSha256"] = record["contentSha256"]
                rows.append(row)
        if seen != set(by_file[file]):
            raise ValueError("Selected function identities changed during coverage analysis.")
    rows.sort(key=lambda item: (item["file"], item["line"], item["id"]))
    reasons = Counter(row["coverage"]["reason"] for row in rows if row["crapEstimate"] is None)
    summary = {"selectedFunctions": len(rows), "conditionalEstimates": sum(row["crapEstimate"] is not None for row in rows),
               "unavailableFunctions": sum(reasons.values()), "unavailableReasons": dict(sorted(reasons.items())),
               "matchedFiles": len(selected_files & coverage.keys()), "unmatchedReportFiles": sorted(coverage.keys() - selected_files)}
    return rows, summary


def analyze(target, *, coverage_json=None, run_id=None, audit_id=None, **selection):
    facts = source_facts.analyze(target, **selection)
    target = Path(target).absolute()
    root = target if target.is_dir() else target.parent
    configuration = {"policy": POLICY, "selectionFingerprint": facts["selection"]["configurationFingerprint"],
                     "complexityPolicy": facts["measurementPolicy"], "parserVersion": facts["producer"]["parserVersion"],
                     "maxReportBytes": MAX_REPORT_BYTES, "maxOwners": MAX_OWNERS}
    report = {"schemaVersion": 1, "artifactKind": "experimental-python-coverage-review",
              "producer": {"name": "codemetrics-ai/python-coverage-review", "version": "0.1.0-experimental"},
              "generatedAtUtc": datetime.now(timezone.utc).isoformat(),
              "runId": str(uuid.UUID(run_id)) if run_id else str(uuid.uuid4()),
              "auditId": str(uuid.UUID(audit_id)) if audit_id else str(uuid.uuid4()),
              "policy": POLICY, "configuration": configuration,
              "configurationFingerprint": digest(json.dumps(configuration, sort_keys=True, separators=(",", ":"))),
              "status": facts["status"], "subject": {"root": str(root), "entryPoint": str(target)},
              "sourceFactsProducer": facts["producer"], "selection": facts["selection"],
              "coverageInput": None, "diagnostics": list(facts["diagnostics"]),
              "scope": "Conditional line-coverage CRAP estimates, not scores or defect predictions. Coverage source binding and freshness are unverified; current source hashes and new run IDs do not authenticate historical coverage. Line coverage is not basis-path coverage or test effectiveness.",
              "functions": [], "summary": None}
    if facts["status"] != "complete":
        return report
    try:
        coverage, report["coverageInput"] = load_coverage(coverage_json)
        report["functions"], report["summary"] = collect(root, facts, coverage, coverage_json is not None)
    except (OSError, SyntaxError, UnicodeError, ValueError, RecursionError) as error:
        report["status"] = "incomplete"
        report["diagnostics"].append({"code": "coverage-review", "message": str(error)})
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target", help="Python file or directory; same selection as source_facts.py")
    parser.add_argument("--coverage-json", type=Path, help="coverage.py JSON with paths relative to the selected root")
    parser.add_argument("--run-id")
    parser.add_argument("--audit-id")
    parser.add_argument("--output", type=Path, help="New .json file; never overwrite previous results")
    for option in ("include", "exclude", "test-path", "production-path"):
        parser.add_argument(f"--{option}", action="append", default=[])
    for option in ("include-tests", "include-generated", "include-support"):
        parser.add_argument(f"--{option}", action="store_true")
    args = parser.parse_args()
    try:
        report = analyze(args.target, coverage_json=args.coverage_json, run_id=args.run_id, audit_id=args.audit_id,
                         include=args.include, exclude=args.exclude, test_paths=args.test_path, production_paths=args.production_path,
                         include_tests=args.include_tests, include_generated=args.include_generated, include_support=args.include_support)
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
        print(f"Cannot produce coverage review: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
