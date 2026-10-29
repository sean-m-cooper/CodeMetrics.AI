"""Validate unscored behavior extensions against pinned projects and saved evidence."""

import argparse
import json
from pathlib import Path
import uuid

import async_helper_paths as helpers
import async_usage as usage
import error_handling_dispositions as dispositions
import error_handling_operations as operations
from corpus import REPOSITORY, git, write


def stable(value):
    if isinstance(value, list):
        return [stable(item) for item in value]
    if isinstance(value, dict):
        return {key: stable(item) for key, item in value.items() if key not in {"generatedAtUtc", "runId", "auditId"}}
    return value


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repos", type=Path, required=True)
    parser.add_argument("--baseline", type=Path, required=True, help="Installed-package corpus evidence directory")
    parser.add_argument("--output", type=Path, required=True, help="New directory")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    load = lambda path: json.loads(path.read_text(encoding="utf-8"))
    manifest = load(REPOSITORY / "shared/calibration/corpus/python-selection.json")
    modules = ["behavior_review_corpus.py", "error_handling_dispositions.py", "async_helper_paths.py", "review_cli.py",
        "error_handling_operations.py", "error_handling_transfers.py", "error_handling_context.py", "error_handling.py",
        "async_adapters.py", "async_provenance.py", "async_usage.py", "async_bindings.py", "source_facts.py",
        "source_selection.py", "duplication.py"]
    implementation = {name: usage.digest(Path(__file__).with_name(name).read_text(encoding="utf-8")) for name in modules}
    result = {"schemaVersion": 1, "auditId": str(uuid.uuid4()), "policies": [dispositions.POLICY, helpers.POLICY],
        "implementationSha256": usage.fingerprint(implementation), "implementationFiles": implementation,
        "scope": "Unscored source review. Unresolved is not defective; candidate is not confirmed defect. No targets executed.", "samples": []}
    for entry in manifest["samples"]:
        root = args.repos / entry["repository"]
        if git(root, "rev-parse", "HEAD") != entry["revision"] or git(root, "status", "--porcelain"):
            raise RuntimeError("Corpus must be pinned and clean: " + entry["id"])
        old = load(args.baseline / f"{entry['id']}-evidence.json")
        facts = usage.source_facts.analyze(root, include=entry["include"], production_paths=entry["productionPaths"])
        if facts != old["dimensions"]["codeQuality"]["sourceFacts"]:
            raise RuntimeError("Source facts or selection changed: " + entry["id"])
        run_id = str(uuid.uuid4())
        base_operations = operations.analyze(root, include=entry["include"], production_paths=entry["productionPaths"],
            run_id=run_id, audit_id=result["auditId"])
        base_async = usage.review(root, facts, run_id=run_id, audit_id=result["auditId"])
        if stable(base_operations) != stable(old["dimensions"]["errorHandling"]["operationReview"]):
            raise RuntimeError("Prior exception evidence changed: " + entry["id"])
        if stable(base_async) != stable(old["dimensions"]["performanceAsync"]["review"]):
            raise RuntimeError("Prior async evidence changed: " + entry["id"])
        before = usage.fingerprint([facts, base_operations, base_async])
        handler_report = dispositions.review(root, facts, base_operations)
        helper_report = helpers.review(root, facts, base_async)
        for name, report in (("dispositions", handler_report), ("helpers", helper_report)):
            if report["status"] != "complete":
                raise RuntimeError(str(report["diagnostics"]))
            write(args.output / f"{entry['id']}-{name}.json", report)
        if before != usage.fingerprint([facts, base_operations, base_async]):
            raise RuntimeError("Extension mutated prior evidence.")
        if git(root, "rev-parse", "HEAD") != entry["revision"] or git(root, "status", "--porcelain"):
            raise RuntimeError("Corpus changed during review.")
        examples = {}
        for row in handler_report["handlers"]:
            bucket = examples.setdefault(row["classification"], [])
            if len(bucket) < 3:
                bucket.append(row)
        sample = {"id": entry["id"], "revision": entry["revision"], "runId": run_id,
            "functionFactsSha256": usage.fingerprint(facts["functions"]),
            "selectionFingerprint": facts["selection"]["configurationFingerprint"],
            "unchangedSourceFactsAndPriorExceptionAndAsyncEvidence": True,
            "dispositionRowsSha256": usage.fingerprint(handler_report["handlers"]),
            "helperRowsSha256": usage.fingerprint(helper_report["functions"]),
            "handlerSummary": handler_report["summary"], "asyncSummary": helper_report["summary"],
            "handlerExamples": examples,
            "blockingExamples": [{"file": row["file"], "member": row["member"], **trace}
                for row in helper_report["functions"] for trace in row["traces"] if trace["status"] == "blocking-candidate"][:20]}
        result["samples"].append(sample)
        print(entry["id"], json.dumps({"handlers": handler_report["summary"]["byClassification"],
            "async": helper_report["summary"]}), flush=True)
    write(args.output / "summary.json", result)


if __name__ == "__main__":
    main()
