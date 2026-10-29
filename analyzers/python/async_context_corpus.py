"""Pinned async context validation; preserve the prior async and error evidence."""

import argparse
import hashlib
import json
from pathlib import Path
import uuid

import async_context as context
import async_provenance
import async_usage_corpus as prior
from corpus import REPOSITORY, git, write


def sample(root, entry, expected, frozen, frozen_rows, async_baseline, output, audit_id):
    original = prior.sample(root, entry, expected, frozen, frozen_rows, output, audit_id)
    if (original["asyncRowsSha256"] != async_baseline["asyncRowsSha256"] or
        original["summary"] != async_baseline["summary"]):
        raise RuntimeError("Prior async ownership/call evidence changed: " + entry["id"])
    facts = json.loads((output / f"{entry['id']}-source-facts.json").read_text(encoding="utf-8"))
    base = json.loads((output / f"{entry['id']}.json").read_text(encoding="utf-8"))
    report = context.review(root, facts, base)
    if report["status"] != "complete":
        raise RuntimeError(str(report["diagnostics"]))
    if git(root, "rev-parse", "HEAD") != entry["revision"] or git(root, "status", "--porcelain"):
        raise RuntimeError("Checkout changed during context review.")
    write(output / f"{entry['id']}-context.json", report)
    examples = {}
    for row in report["functions"]:
        for call in row["calls"]:
            for key in ("localCallee", "storedWrapper", "callback"):
                value = call[key]
                if key == "callback" and value:
                    value = value["reference"]
                if value and value["status"] in {"local-definition", "stored-wrapper-syntax"}:
                    if len(examples.setdefault(key, [])) < 5:
                        examples[key].append({"ownerId": row["id"], "file": row["file"], "member": row["member"], **call})
    return {**entry, "runId": report["runId"], "configurationFingerprint": report["configurationFingerprint"],
            "functionFactsSha256": original["functionFactsSha256"], "selectedSourceSha256": original["selectedSourceSha256"],
            "asyncRowsSha256": original["asyncRowsSha256"], "contextRowsSha256": context.usage.fingerprint(report["functions"]),
            "summary": report["summary"], "methodComplexityScore": original["methodComplexityScore"],
            "unchangedMethodComplexityAndErrorHandlingEvidence": True, "unchangedAsyncEvidence": True, "examples": examples}


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
    earlier = load("shared/scorecard-schema/calibration-runs/python-error-operations-2026-10-15.json")
    corrected = load("shared/calibration/baselines/python-async-prior-evidence.json")
    async_baseline = load("shared/scorecard-schema/calibration-runs/python-async-usage-2026-10-15.json")
    paths = [Path(__file__), Path(context.__file__), Path(async_provenance.__file__), Path(prior.__file__),
             Path(context.usage.__file__), Path(__file__).with_name("async_bindings.py"),
             Path(context.usage.source_facts.__file__), Path(__file__).with_name("source_selection.py"),
             Path(__file__).with_name("duplication.py")]
    implementation = hashlib.sha256(b"\0".join(path.name.encode() + b"\0" + path.read_bytes().replace(b"\r\n", b"\n") for path in paths)).hexdigest()
    summary = {"schemaVersion": 1, "policy": context.POLICY, "auditId": str(uuid.uuid4()), "implementationSha256": implementation,
               "scope": "Unresolved-call inventory and lexical links only. Async/Blocking Usage and Error Handling remain unscored.", "samples": []}
    find = lambda rows, id: next(row for row in rows if row["id"] == id)
    for entry in manifest["samples"]:
        id = entry["id"]
        if id not in {"starlette", "django"}:
            continue
        frozen, correction = find(packaged["results"], id), find(corrected["samples"], id)
        old_async = find(async_baseline["samples"], id)
        if correction["revision"] != entry["revision"] or correction["evidenceSha256"] != frozen["evidenceSha256"] or old_async["revision"] != entry["revision"]:
            raise RuntimeError("Prior baseline has inconsistent provenance.")
        frozen_rows = {**find(earlier["samples"], id), "operationRowsSha256": correction["operationRowsSha256"]}
        result = sample(args.repos / entry["repository"], entry, find(baseline["samples"], id), frozen,
                        frozen_rows, old_async, args.output, summary["auditId"])
        summary["samples"].append(result)
        print(id, json.dumps({"complexity": result["methodComplexityScore"],
              "direct": {key: value for key, value in result["summary"]["contexts"]["async-body"].items() if key != "unresolvedInventory"}}), flush=True)
    write(args.output / "summary.json", summary)


if __name__ == "__main__":
    main()
