"""Validate exception-handler observations against clean pinned Python selections."""

import argparse
import hashlib
import json
from pathlib import Path
import uuid

from corpus import REPOSITORY, git, write
import error_handling


def sample(root, entry, expected, output, audit_id):
    if git(root, "rev-parse", "HEAD") != entry["revision"] or git(root, "status", "--porcelain"):
        raise RuntimeError("Pinned clean checkout required: " + str(root))
    facts = error_handling.source_facts.analyze(root, include=entry["include"], production_paths=entry["productionPaths"])
    original = json.dumps(facts, sort_keys=True)
    report = error_handling.review(root, facts, audit_id=audit_id)
    write(output / f"{entry['id']}.json", report)
    if report["status"] != "complete":
        raise RuntimeError(str(report["diagnostics"]))
    if json.dumps(facts, sort_keys=True) != original:
        raise RuntimeError("Error-handling review changed source facts.")
    source_hash = error_handling.digest(json.dumps([(row["file"], row["contentSha256"]) for row in report["selection"]["files"] if row["contentSha256"]], separators=(",", ":")))
    function_hash = error_handling.digest(json.dumps(sorted(facts["functions"], key=lambda row: row["id"]), sort_keys=True, separators=(",", ":")))
    if (source_hash != expected["selectedSourceSha256"] or function_hash != expected["functionFactsSha256"] or
            report["selection"]["configurationFingerprint"] != expected["configurationFingerprint"] or
            len(facts["functions"]) != expected["implementedFunctions"]):
        raise RuntimeError("Source/population/complexity drift: " + entry["id"])
    if git(root, "status", "--porcelain") or git(root, "rev-parse", "HEAD") != entry["revision"]:
        raise RuntimeError("Checkout changed during review: " + entry["id"])
    return {**entry, "runId": report["runId"], "configurationFingerprint": report["configurationFingerprint"],
            "selectedSourceSha256": source_hash, "unchangedFunctionFactsSha256": function_hash,
            "handlerRowsSha256": error_handling.digest(json.dumps(report["handlers"], sort_keys=True, separators=(",", ":"))),
            "selectedFunctions": len(facts["functions"]), "summary": report["summary"],
            "examples": {kind: [row for row in report["handlers"] if row["classification"] == kind][:3]
                         for kind in report["summary"]["byClassification"]}}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repos", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True, help="New output directory")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    manifest = json.loads((REPOSITORY / "shared/calibration/corpus/python-selection.json").read_text())
    baseline = json.loads((REPOSITORY / "shared/calibration/baselines/python-selection.json").read_text())
    sources = [Path(__file__), Path(error_handling.__file__), Path(__file__).with_name("duplication.py"),
               Path(error_handling.source_facts.__file__), Path(__file__).with_name("source_selection.py")]
    implementation = hashlib.sha256(b"\0".join(path.name.encode() + b"\0" + path.read_bytes().replace(b"\r\n", b"\n") for path in sources)).hexdigest()
    summary = {"schemaVersion": 1, "policy": error_handling.POLICY, "auditId": str(uuid.uuid4()),
               "implementationSha256": implementation,
               "scope": "Review-only handler classifications and owned syntax. No target execution, handling score, failure-rate estimate, or classification of business correctness.",
               "samples": []}
    for entry in manifest["samples"]:
        expected = next(row for row in baseline["samples"] if row["id"] == entry["id"])
        result = sample(args.repos / entry["repository"], entry, expected, args.output, summary["auditId"])
        summary["samples"].append(result)
        print(entry["id"], json.dumps(result["summary"]))
    write(args.output / "summary.json", summary)


if __name__ == "__main__":
    main()
