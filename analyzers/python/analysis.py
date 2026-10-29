"""Packaged Python measurements from one selected source population."""

import argparse
import json

import async_usage
import async_context
import async_adapters
import async_helper_paths
import async_scoring
import error_handling
import error_handling_context
import error_handling_transfers
import error_handling_operations
import decomposition
import duplication
import coverage_review
import source_facts


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("target")
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--audit-id", required=True)
    parser.add_argument("--coverage-json")
    parser.add_argument("--collection-manifest")
    for option in ("include", "exclude", "test-path", "production-path"):
        parser.add_argument(f"--{option}", action="append", default=[])
    for option in ("include-tests", "include-generated", "include-support"):
        parser.add_argument(f"--{option}", action="store_true")
    args = parser.parse_args()
    facts = source_facts.analyze(args.target, include=args.include, exclude=args.exclude, test_paths=args.test_path,
                                 production_paths=args.production_path, include_tests=args.include_tests,
                                 include_generated=args.include_generated, include_support=args.include_support)
    duplicates = duplication.review(args.target, facts, run_id=args.run_id, audit_id=args.audit_id)
    coverage = coverage_review.review(args.target, facts, coverage_json=args.coverage_json, collection_manifest=args.collection_manifest,
                                     run_id=args.run_id, audit_id=args.audit_id)
    bodies = decomposition.review(args.target, facts, run_id=args.run_id, audit_id=args.audit_id)
    handlers = error_handling.review(args.target, facts, run_id=args.run_id, audit_id=args.audit_id)
    context = error_handling_context.review(args.target, facts, handlers)
    transfers = error_handling_transfers.review(args.target, facts, context)
    operations = error_handling_operations.review(args.target, facts, transfers)
    async_review = async_usage.review(args.target, facts, run_id=args.run_id, audit_id=args.audit_id)
    async_context_review = async_context.review(args.target, facts, async_review)
    async_adapter_review = async_adapters.review(args.target, facts, async_review)
    async_helpers = async_helper_paths.review(args.target, facts, async_review)
    async_score = async_scoring.review(args.target, facts, async_helpers)
    print(json.dumps({"sourceFacts": facts, "duplication": duplicates, "coverageReview": coverage, "decomposition": bodies, "errorHandlingReview": handlers, "errorHandlingContext": context, "errorHandlingTransfers": transfers, "errorHandlingOperations": operations, "asyncUsageReview": async_review, "asyncContextReview": async_context_review, "asyncAdapterReview": async_adapter_review, "asyncHelperReview": async_helpers, "asyncScoring": async_score}, ensure_ascii=True))
    return 0 if facts["status"] == duplicates["status"] == coverage["status"] == bodies["status"] == handlers["status"] == context["status"] == transfers["status"] == operations["status"] == async_review["status"] == async_context_review["status"] == async_adapter_review["status"] == async_helpers["status"] == async_score["status"] == "complete" else 2


if __name__ == "__main__":
    raise SystemExit(main())
