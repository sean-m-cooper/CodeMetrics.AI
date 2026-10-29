"""Shared CLI for standalone, unscored source reviewers."""

import argparse
import json
from pathlib import Path
import sys


def run(analyze, description):
    parser = argparse.ArgumentParser(description=description)
    parser.add_argument("target")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--run-id")
    parser.add_argument("--audit-id")
    for name in ("include", "exclude", "test-path", "production-path"):
        parser.add_argument(f"--{name}", action="append", default=[])
    for name in ("include-tests", "include-generated", "include-support"):
        parser.add_argument(f"--{name}", action="store_true")
    args = parser.parse_args()
    try:
        report = analyze(args.target, run_id=args.run_id, audit_id=args.audit_id,
            include=args.include, exclude=args.exclude, test_paths=args.test_path,
            production_paths=args.production_path, include_tests=args.include_tests,
            include_generated=args.include_generated, include_support=args.include_support)
        payload = json.dumps(report, indent=2, ensure_ascii=True) + "\n"
        if args.output:
            if args.output.suffix != ".json":
                raise ValueError("Output must be a new .json file with an existing parent.")
            with args.output.open("x", encoding="utf-8") as stream:
                stream.write(payload)
        else:
            sys.stdout.write(payload)
        return 0 if report["status"] == "complete" else 2
    except (OSError, ValueError) as error:
        print(f"Cannot produce review: {error}", file=sys.stderr)
        return 2
