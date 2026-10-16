"""Validate async review against pinned Starlette/Django and frozen prior evidence."""

import argparse
import hashlib
import json
from pathlib import Path
import uuid

import async_usage as usage
from corpus import REPOSITORY, git, preview, write
import error_handling_operations as operations


def sample(root, entry, expected, frozen, frozen_rows, output, audit_id):
    if git(root, "rev-parse", "HEAD") != entry["revision"] or git(root, "status", "--porcelain"):
        raise RuntimeError("Pinned checkout is not clean: " + entry["id"])
    facts = usage.source_facts.analyze(root, include=entry["include"], production_paths=entry["productionPaths"])
    if facts["status"] != "complete":
        raise RuntimeError(str(facts["diagnostics"]))
    before = usage.fingerprint(facts)
    functions_hash = usage.fingerprint(sorted(facts["functions"], key=lambda row: row["id"]))
    sources = [(row["file"], row["contentSha256"]) for row in facts["selection"]["files"] if row["contentSha256"]]
    source_hash = usage.digest(json.dumps(sources, separators=(",", ":")))
    if functions_hash != expected["functionFactsSha256"] or source_hash != expected["selectedSourceSha256"]:
        raise RuntimeError("Frozen source/function population drift: " + entry["id"])
    if (facts["selection"]["configurationFingerprint"] != expected["configurationFingerprint"] or
        usage.fingerprint(facts["selection"]["excludedFunctions"]) != expected["excludedDeclarationsSha256"]):
        raise RuntimeError("Frozen selection/declaration population drift: " + entry["id"])
    facts_path = output / f"{entry['id']}-source-facts.json"
    write(facts_path, facts)
    score = preview(facts_path)["score"]
    if score != frozen["methodComplexityScore"]:
        raise RuntimeError("Method Complexity score changed.")
    report = usage.review(root, facts, audit_id=audit_id)
    if report["status"] != "complete":
        raise RuntimeError(str(report["diagnostics"]))
    write(output / f"{entry['id']}.json", report)
    # Regenerate the existing evidence chain; no scores or patterns may change.
    handlers = operations.context.handlers.review(root, facts)
    context = operations.context.review(root, facts, handlers)
    transfers = operations.transfers.review(root, facts, context)
    calls = operations.review(root, facts, transfers)
    for key, earlier in [("handlers", handlers), ("context", context), ("transfers", transfers), ("operations", calls)]:
        if earlier["status"] != "complete" or earlier["summary"] != frozen[key]:
            raise RuntimeError("Existing Error Handling evidence changed: " + key)
    for key, rows in [("handlerRowsSha256", handlers["handlers"]), ("contextRowsSha256", context["contexts"]),
                      ("transferRowsSha256", transfers["handlers"]), ("operationRowsSha256", calls["handlers"])]:
        if usage.fingerprint(rows) != frozen_rows[key]:
            raise RuntimeError("Existing Error Handling rows changed: " + key)
    if usage.fingerprint(facts) != before:
        raise RuntimeError("Async review mutated source facts.")
    if git(root, "rev-parse", "HEAD") != entry["revision"] or git(root, "status", "--porcelain"):
        raise RuntimeError("Checkout changed during review.")
    candidates = []
    by_category = {}
    reasons = {}
    for function in report["functions"]:
        if function["measurement"] is None:
            continue
        for call in function["measurement"]["calls"]:
            example = {"file": function["file"], "member": function["member"], **call}
            category = call["category"]
            if category == "blocking-call-candidate":
                candidates.append(example)
            if len(by_category.setdefault(category, [])) < 3:
                by_category[category].append(example)
            if call["binding"]["status"] == "unresolved":
                reason = call["binding"]["reason"]
                reasons[reason] = reasons.get(reason, 0) + 1
    return {**entry, "runId": report["runId"], "configurationFingerprint": report["configurationFingerprint"],
            "functionFactsSha256": functions_hash, "selectedSourceSha256": source_hash,
            "asyncRowsSha256": usage.fingerprint(report["functions"]), "summary": report["summary"],
            "methodComplexityScore": score, "unchangedMethodComplexityAndErrorHandlingEvidence": True,
            "unresolvedReasons": dict(sorted(reasons.items())), "blockingCandidates": candidates, "examples": by_category}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repos", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True, help="New output directory")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    load = lambda path: json.loads((REPOSITORY / path).read_text(encoding="utf-8"))
    manifest = load("shared/calibration/corpus/python-selection.json")
    baseline = load("shared/calibration/baselines/python-selection.json")
    packaged = load("shared/scorecard-schema/calibration-runs/python-error-operations-package-2026-10-15.json")
    earlier_rows = load("shared/scorecard-schema/calibration-runs/python-error-operations-2026-10-15.json")
    corrected_rows = load("shared/calibration/baselines/python-async-prior-evidence.json")
    paths = [Path(__file__), Path(usage.__file__), Path(__file__).with_name("async_bindings.py"),
             Path(usage.source_facts.__file__), Path(__file__).with_name("source_selection.py"), Path(__file__).with_name("duplication.py")]
    implementation = hashlib.sha256(b"\0".join(path.name.encode() + b"\0" + path.read_bytes().replace(b"\r\n", b"\n") for path in paths)).hexdigest()
    summary = {"schemaVersion": 1, "policy": usage.POLICY, "auditId": str(uuid.uuid4()), "implementationSha256": implementation,
               "scope": "Static import-matched call syntax; Async/Blocking Usage and Error Handling remain unscored.", "samples": []}
    for entry in manifest["samples"]:
        if entry["id"] not in {"starlette", "django"}:
            continue
        expected = next(row for row in baseline["samples"] if row["id"] == entry["id"])
        frozen = next(row for row in packaged["results"] if row["id"] == entry["id"])
        frozen_rows = next(row for row in earlier_rows["samples"] if row["id"] == entry["id"])
        corrected = next(row for row in corrected_rows["samples"] if row["id"] == entry["id"])
        if corrected["revision"] != entry["revision"] or corrected["evidenceSha256"] != frozen["evidenceSha256"]:
            raise RuntimeError("Corrected operation baseline has inconsistent provenance.")
        frozen_rows = {**frozen_rows, "operationRowsSha256": corrected["operationRowsSha256"]}
        result = sample(args.repos / entry["repository"], entry, expected, frozen, frozen_rows, args.output, summary["auditId"])
        summary["samples"].append(result)
        print(entry["id"], json.dumps(result["summary"]), flush=True)
    write(args.output / "summary.json", summary)


if __name__ == "__main__":
    main()
