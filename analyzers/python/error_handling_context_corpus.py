"""Compare contextual handler evidence with frozen source and handler populations."""

import argparse
import hashlib
import json
from pathlib import Path
import uuid

from corpus import REPOSITORY, git, write
import error_handling_context as context
import error_handling_corpus


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repos", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True, help="New output directory")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    base_output = args.output / "handlers"
    base_output.mkdir()
    manifest = json.loads((REPOSITORY / "shared/calibration/corpus/python-selection.json").read_text())
    baseline = json.loads((REPOSITORY / "shared/calibration/baselines/python-selection.json").read_text())
    handlers = json.loads((REPOSITORY / "shared/scorecard-schema/calibration-runs/python-error-handling-2026-10-13.json").read_text())
    sources = [Path(__file__), Path(context.__file__), Path(error_handling_corpus.__file__), Path(context.handlers.__file__),
               Path(__file__).with_name("duplication.py"), Path(context.handlers.source_facts.__file__), Path(__file__).with_name("source_selection.py")]
    implementation = hashlib.sha256(b"\0".join(path.name.encode() + b"\0" + path.read_bytes().replace(b"\r\n", b"\n") for path in sources)).hexdigest()
    summary = {"schemaVersion": 1, "policy": context.POLICY, "auditId": str(uuid.uuid4()), "implementationSha256": implementation,
               "scope": "Syntactic pattern review, not error-handling scoring or business-correctness classification.", "samples": []}
    for entry in manifest["samples"]:
        root = args.repos / entry["repository"]
        expected = next(row for row in baseline["samples"] if row["id"] == entry["id"])
        base_result = error_handling_corpus.sample(root, entry, expected, base_output, summary["auditId"])
        frozen_handlers = next(row for row in handlers["samples"] if row["id"] == entry["id"])
        if base_result["handlerRowsSha256"] != frozen_handlers["handlerRowsSha256"]:
            raise RuntimeError("Frozen handler observations changed: " + entry["id"])
        facts = context.handlers.source_facts.analyze(root, include=entry["include"], production_paths=entry["productionPaths"])
        base = json.loads((base_output / f"{entry['id']}.json").read_text())
        before = context.fingerprint([facts, base])
        report = context.review(root, facts, base)
        write(args.output / f"{entry['id']}.json", report)
        if report["status"] != "complete":
            raise RuntimeError(str(report["diagnostics"]))
        if context.fingerprint([facts, base]) != before:
            raise RuntimeError("Context review mutated its inputs.")
        if git(root, "status", "--porcelain") or git(root, "rev-parse", "HEAD") != entry["revision"]:
            raise RuntimeError("Checkout changed during context analysis: " + entry["id"])
        example_keys = sorted({row["classification"]["pattern"] or "unresolved" for row in report["contexts"]})
        result = {**entry, "runId": report["runId"], "configurationFingerprint": report["configurationFingerprint"],
                  "handlerRowsSha256": report["handlerRowsSha256"], "contextRowsSha256": context.fingerprint(report["contexts"]),
                  "selectedSourceSha256": base_result["selectedSourceSha256"], "functionFactsSha256": base_result["unchangedFunctionFactsSha256"],
                  "summary": report["summary"], "unchangedSourceFunctionsAndHandlers": True,
                  "examples": {key: [row for row in report["contexts"] if (row["classification"]["pattern"] or "unresolved") == key][:2] for key in example_keys}}
        summary["samples"].append(result)
        print(entry["id"], json.dumps(result["summary"]), flush=True)
    write(args.output / "summary.json", summary)


if __name__ == "__main__":
    main()
