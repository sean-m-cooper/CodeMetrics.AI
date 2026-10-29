"""Validate standalone async scores against clean pinned source and prior evidence."""

import argparse
import json
from pathlib import Path
import uuid

import async_scoring as scoring
from behavior_review_corpus import stable
from corpus import REPOSITORY, git, write

usage = scoring.usage


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repos", type=Path, required=True)
    parser.add_argument("--baseline", type=Path, required=True, help="Installed package evidence directory")
    parser.add_argument("--helpers", type=Path, required=True, help="Frozen helper corpus directory")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    load = lambda path: json.loads(path.read_text(encoding="utf-8"))
    manifest = load(REPOSITORY / "shared/calibration/corpus/python-selection.json")
    modules = ["async_scoring.py", "async_scoring_corpus.py", "async_helper_paths.py", "async_adapters.py",
        "async_provenance.py", "async_context.py", "async_usage.py", "async_bindings.py", "source_facts.py",
        "source_selection.py", "duplication.py", "review_cli.py"]
    implementation = {name: usage.digest(Path(__file__).with_name(name).read_text(encoding="utf-8")) for name in modules}
    result = {"schemaVersion": 1, "auditId": str(uuid.uuid4()), "policy": scoring.POLICY,
        "implementationFiles": implementation, "implementationSha256": usage.fingerprint(implementation),
        "scope": "Standalone blocking-catalogue scores; no runtime performance assessment, packaged scores or gates changed.",
        "samples": []}
    for entry in manifest["samples"]:
        root = args.repos / entry["repository"]
        if git(root, "rev-parse", "HEAD") != entry["revision"] or git(root, "status", "--porcelain"):
            raise RuntimeError("Corpus must be pinned and clean: " + entry["id"])
        old = load(args.baseline / f"{entry['id']}-evidence.json")
        prior = load(args.helpers / f"{entry['id']}-helpers.json")
        facts = usage.source_facts.analyze(root, include=entry["include"], production_paths=entry["productionPaths"])
        if facts != old["dimensions"]["codeQuality"]["sourceFacts"]:
            raise RuntimeError("Source facts changed: " + entry["id"])
        run_id = str(uuid.uuid4())
        original = usage.review(root, facts, run_id=run_id, audit_id=result["auditId"])
        if stable(original) != stable(old["dimensions"]["performanceAsync"]["review"]):
            raise RuntimeError("Prior async evidence changed: " + entry["id"])
        base = scoring.helpers.review(root, facts, original)
        if stable(base) != stable(prior):
            raise RuntimeError("Prior helper evidence changed: " + entry["id"])
        before = usage.fingerprint([facts, original, base])
        report = scoring.review(root, facts, base)
        if report["status"] != "complete":
            raise RuntimeError(str(report["diagnostics"]))
        if before != usage.fingerprint([facts, original, base]):
            raise RuntimeError("Scoring mutated inputs.")
        if git(root, "rev-parse", "HEAD") != entry["revision"] or git(root, "status", "--porcelain"):
            raise RuntimeError("Corpus changed during scoring.")
        write(args.output / f"{entry['id']}.json", report)
        result["samples"].append({"id": entry["id"], "revision": entry["revision"], "runId": run_id,
            "sourceFactsSha256": usage.fingerprint(facts), "priorHelperRowsSha256": report["helperRowsSha256"],
            "scoringRowsSha256": usage.fingerprint(report["functions"]), "summary": report["summary"],
            "unchangedSourceFactsPriorAsyncAndHelperEvidence": True})
        print(entry["id"], json.dumps(report["summary"]), flush=True)
    write(args.output / "summary.json", result)


if __name__ == "__main__":
    main()
