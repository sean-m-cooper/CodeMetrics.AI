"""Review decomposition evidence against clean pinned Python source selections."""

import argparse
import hashlib
import json
from pathlib import Path
import uuid

from corpus import REPOSITORY, git, write
import decomposition


def ranked(rows, metric, limit=10):
    return sorted(rows, key=lambda row: (-row["measurement"][metric], row["id"]))[:limit]


def sample(root, entry, expected, output, audit_id):
    if git(root, "rev-parse", "HEAD") != entry["revision"] or git(root, "status", "--porcelain"):
        raise RuntimeError("Pinned clean checkout required: " + str(root))
    facts = decomposition.source_facts.analyze(root, include=entry["include"], production_paths=entry["productionPaths"])
    frozen_facts = json.dumps(facts, sort_keys=True)
    report = decomposition.review(root, facts, audit_id=audit_id)
    if json.dumps(facts, sort_keys=True) != frozen_facts:
        raise RuntimeError("Decomposition changed source facts.")
    write(output / f"{entry['id']}.json", report)
    if report["status"] != "complete":
        raise RuntimeError(str(report["diagnostics"]))
    selection = report["selection"]
    source_hash = decomposition.digest(json.dumps([(row["file"], row["contentSha256"]) for row in selection["files"] if row["contentSha256"]], separators=(",", ":")))
    function_hash = decomposition.digest(json.dumps(sorted(facts["functions"], key=lambda row: row["id"]), sort_keys=True, separators=(",", ":")))
    if (source_hash != expected["selectedSourceSha256"] or function_hash != expected["functionFactsSha256"] or
            selection["configurationFingerprint"] != expected["configurationFingerprint"] or
            report["summary"]["selectedFunctions"] != expected["implementedFunctions"]):
        raise RuntimeError("Selected source/population/complexity drift: " + entry["id"])
    if git(root, "status", "--porcelain") or git(root, "rev-parse", "HEAD") != entry["revision"]:
        raise RuntimeError("Checkout changed during review: " + entry["id"])
    measured = [row for row in report["functions"] if row["status"] == "measured"]
    row_hash = decomposition.digest(json.dumps(report["functions"], sort_keys=True, separators=(",", ":")))
    return {**entry, "runId": report["runId"], "configurationFingerprint": report["configurationFingerprint"],
            "producer": report["producer"], "sourceFactsProducer": report["sourceFactsProducer"],
            "selectedSourceSha256": source_hash, "unchangedFunctionFactsSha256": function_hash,
            "decompositionRowsSha256": row_hash, "summary": report["summary"],
            "reviewThresholdCounts": {
                "statementsGreaterThan": {str(limit): sum(row["measurement"]["ownedStatements"] > limit for row in measured) for limit in (20, 30, 40)},
                "nestingAtLeast": {str(limit): sum(row["measurement"]["maxBlockNesting"] >= limit for row in measured) for limit in (4, 5, 6)}},
            "largestBodies": ranked(measured, "ownedStatements"), "deepestBlocks": ranked(measured, "maxBlockNesting"),
            "largestLowComplexityBodies": ranked([row for row in measured if row["complexity"] <= 5], "ownedStatements")}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repos", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True, help="New output directory")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    manifest = json.loads((REPOSITORY / "shared/calibration/corpus/python-selection.json").read_text())
    baseline = json.loads((REPOSITORY / "shared/calibration/baselines/python-selection.json").read_text())
    sources = [Path(__file__), Path(decomposition.__file__), Path(__file__).with_name("duplication.py"),
               Path(decomposition.source_facts.__file__), Path(__file__).with_name("source_selection.py")]
    implementation = hashlib.sha256(b"\0".join(path.name.encode() + b"\0" + path.read_bytes().replace(b"\r\n", b"\n") for path in sources)).hexdigest()
    summary = {"schemaVersion": 1, "policy": decomposition.POLICY, "auditId": str(uuid.uuid4()),
               "implementationSha256": implementation,
               "scope": "Review-only per-project distributions, diagnostic threshold counts, and ranked examples. Low complexity means the existing CC 1-5 band. Counts do not activate a threshold or scoring ladder. No target execution or scores.",
               "samples": []}
    for entry in manifest["samples"]:
        expected = next(row for row in baseline["samples"] if row["id"] == entry["id"])
        result = sample(args.repos / entry["repository"], entry, expected, args.output, summary["auditId"])
        summary["samples"].append(result)
        print(entry["id"], json.dumps(result["summary"]))
    write(args.output / "summary.json", summary)


if __name__ == "__main__":
    main()
