"""Run the offline duplication proof against pinned Python selections."""

import argparse
import hashlib
import json
from pathlib import Path
import uuid

from corpus import REPOSITORY, git, write
import duplication


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repos", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True, help="New output directory")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    manifest = json.loads((REPOSITORY / "shared/calibration/corpus/python-selection.json").read_text())
    baseline = json.loads((REPOSITORY / "shared/calibration/baselines/python-selection.json").read_text())
    audit_id = str(uuid.uuid4())
    sources = [Path(__file__), Path(duplication.__file__), Path(duplication.source_facts.__file__),
               Path(__file__).with_name("source_selection.py")]
    implementation = hashlib.sha256(b"\0".join(path.name.encode() + b"\0" + path.read_bytes().replace(b"\r\n", b"\n") for path in sources)).hexdigest()
    summary = {"schemaVersion": 1, "policy": duplication.POLICY, "auditId": audit_id,
               "implementationSha256": implementation,
               "scope": "Offline exact-body review leads only. No scores, partial clones, or semantic equivalence claims.", "samples": []}
    for entry in manifest["samples"]:
        root = args.repos / entry["repository"]
        if git(root, "rev-parse", "HEAD") != entry["revision"] or git(root, "status", "--porcelain"):
            raise RuntimeError("Pinned clean checkout required: " + str(root))
        report = duplication.analyze(root, audit_id=audit_id, include=entry["include"], production_paths=entry["productionPaths"])
        write(args.output / f"{entry['id']}.json", report)
        if report["status"] != "complete":
            raise RuntimeError(str(report["diagnostics"]))
        expected = next(item for item in baseline["samples"] if item["id"] == entry["id"])
        source_hash = duplication.digest(json.dumps([(item["file"], item["contentSha256"]) for item in report["selection"]["files"] if item["contentSha256"]], separators=(",", ":")))
        if (source_hash != expected["selectedSourceSha256"] or
                report["selection"]["configurationFingerprint"] != expected["configurationFingerprint"] or
                report["summary"]["selectedFunctions"] != expected["implementedFunctions"]):
            raise RuntimeError("Selected source/population drift: " + entry["id"])
        if git(root, "status", "--porcelain") or git(root, "rev-parse", "HEAD") != entry["revision"]:
            raise RuntimeError("Checkout changed during review: " + entry["id"])
        summary["samples"].append({"id": entry["id"], "revision": entry["revision"], "url": entry["url"],
                                   "runId": report["runId"], "configurationFingerprint": report["configurationFingerprint"],
                                   "configuration": report["configuration"], "sourceFactsProducer": report["sourceFactsProducer"],
                                   "producer": report["producer"],
                                   "selectedSourceSha256": source_hash, "summary": report["summary"], "groups": report["groups"]})
        print(entry["id"], json.dumps(report["summary"]))
    write(args.output / "summary.json", summary)


if __name__ == "__main__":
    main()
