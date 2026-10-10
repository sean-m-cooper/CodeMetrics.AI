"""Packaged Python measurements from one selected source population."""

import argparse
import json

import duplication
import source_facts


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target")
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--audit-id", required=True)
    for option in ("include", "exclude", "test-path", "production-path"):
        parser.add_argument(f"--{option}", action="append", default=[])
    for option in ("include-tests", "include-generated", "include-support"):
        parser.add_argument(f"--{option}", action="store_true")
    args = parser.parse_args()
    facts = source_facts.analyze(args.target, include=args.include, exclude=args.exclude, test_paths=args.test_path,
                                 production_paths=args.production_path, include_tests=args.include_tests,
                                 include_generated=args.include_generated, include_support=args.include_support)
    duplicates = duplication.review(args.target, facts, run_id=args.run_id, audit_id=args.audit_id)
    print(json.dumps({"sourceFacts": facts, "duplication": duplicates}, ensure_ascii=True))
    return 0 if facts["status"] == duplicates["status"] == "complete" else 2


if __name__ == "__main__":
    raise SystemExit(main())
