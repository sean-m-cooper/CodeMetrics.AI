"""Inventory unresolved handlers and validate call/return syntax on pinned sources."""

import argparse
import hashlib
import json
from pathlib import Path
import uuid

from corpus import REPOSITORY, git, write
import error_handling_corpus
import error_handling_operations as operations

context = operations.context


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repos", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True, help="New output directory")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    base_output = args.output / "handlers"
    base_output.mkdir()
    load = lambda path: json.loads(path.read_text(encoding="utf-8"))
    manifest = load(REPOSITORY / "shared/calibration/corpus/python-selection.json")
    baseline = load(REPOSITORY / "shared/calibration/baselines/python-selection.json")
    frozen = load(REPOSITORY / "shared/scorecard-schema/calibration-runs/python-error-transfers-2026-10-15.json")
    sources = [Path(__file__), Path(operations.__file__), Path(operations.transfers.__file__), Path(context.__file__),
               Path(context.handlers.__file__), Path(context.handlers.source_facts.__file__), Path(error_handling_corpus.__file__),
               Path(__file__).with_name("duplication.py"), Path(__file__).with_name("source_selection.py")]
    implementation = hashlib.sha256(b"\0".join(path.name.encode() + b"\0" + path.read_bytes().replace(b"\r\n", b"\n") for path in sources)).hexdigest()
    summary = {"schemaVersion": 1, "policy": operations.POLICY, "auditId": str(uuid.uuid4()), "implementationSha256": implementation,
               "scope": "Source syntax inventory and local patterns, not handling-success rates, runtime correctness, defects, or scores.", "samples": []}
    for entry in manifest["samples"]:
        root = args.repos / entry["repository"]
        expected = next(row for row in baseline["samples"] if row["id"] == entry["id"])
        frozen_sample = next(row for row in frozen["samples"] if row["id"] == entry["id"])
        result = error_handling_corpus.sample(root, entry, expected, base_output, summary["auditId"])
        facts = context.handlers.source_facts.analyze(root, include=entry["include"], production_paths=entry["productionPaths"])
        handlers = load(base_output / f"{entry['id']}.json")
        prior_context = context.review(root, facts, handlers)
        prior = operations.transfers.review(root, facts, prior_context)
        for actual, key in [(context.fingerprint(handlers["handlers"]), "handlerRowsSha256"),
                            (context.fingerprint(prior_context["contexts"]), "contextRowsSha256"),
                            (context.fingerprint(prior["handlers"]), "transferRowsSha256"),
                            (result["selectedSourceSha256"], "selectedSourceSha256"),
                            (result["unchangedFunctionFactsSha256"], "functionFactsSha256")]:
            if actual != frozen_sample[key]:
                raise RuntimeError(f"Frozen {key} changed: {entry['id']}")
        before = context.fingerprint([facts, prior])
        report = operations.review(root, facts, prior)
        write(args.output / f"{entry['id']}.json", report)
        if report["status"] != "complete":
            raise RuntimeError(str(report["diagnostics"]))
        if context.fingerprint([facts, prior]) != before:
            raise RuntimeError("Call/return review mutated inputs.")
        if git(root, "status", "--porcelain") or git(root, "rev-parse", "HEAD") != entry["revision"]:
            raise RuntimeError("Checkout changed: " + entry["id"])
        prior_by_id = {row["handlerId"]: row for row in prior["handlers"]}
        base_by_id = {row["id"]: row for row in handlers["handlers"]}
        for row in report["handlers"]:
            old = prior_by_id[row["handlerId"]]["classification"]
            if row["originalClassification"] != old or old["status"] == "recognized-shape" and row["classification"] != old:
                raise RuntimeError("Existing classification changed.")
            original = base_by_id[row["handlerId"]]["bodyObservations"]
            projected = [{"line": value["line"], "hasValue": value["expression"]["kind"] != "absent"} for value in row["returns"]]
            if len(row["calls"]) != original["calls"] or projected != original["returns"]:
                raise RuntimeError("Original call/return population changed.")
        patterns = sorted({row["classification"]["pattern"] for row in report["handlers"] if row["classification"]["pattern"]})
        summary["samples"].append({**entry, "runId": report["runId"], "configurationFingerprint": report["configurationFingerprint"],
                                   "operationRowsSha256": context.fingerprint(report["handlers"]),
                                   **{key: frozen_sample[key] for key in ("selectedSourceSha256", "functionFactsSha256", "handlerRowsSha256", "contextRowsSha256", "transferRowsSha256")},
                                   "summary": report["summary"], "unchangedSourceFunctionsHandlersContextAndTransfers": True,
                                   "ownedCallsAndReturnsMatchBaseObservations": True,
                                   "examples": {pattern: [row for row in report["handlers"] if row["classification"]["pattern"] == pattern][:2] for pattern in patterns}})
        print(entry["id"], json.dumps({key: report["summary"][key] for key in ("handlers", "callExpressions", "returnStatements", "recognizedShapes", "newlyRecognizedShapes", "unresolved")}), flush=True)
    write(args.output / "summary.json", summary)


if __name__ == "__main__":
    main()
