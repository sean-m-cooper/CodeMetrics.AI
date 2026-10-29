"""Validate continuation evidence against pinned source and frozen disposition rows."""

import argparse
import json
from pathlib import Path
import uuid

import error_handling_continuations as continuation
from behavior_review_corpus import stable
from corpus import REPOSITORY, git, write

usage = continuation.usage


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repos", type=Path, required=True)
    parser.add_argument("--baseline", type=Path, required=True, help="Installed package evidence directory")
    parser.add_argument("--dispositions", type=Path, required=True, help="Frozen disposition corpus directory")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    load = lambda path: json.loads(path.read_text(encoding="utf-8"))
    manifest = load(REPOSITORY / "shared/calibration/corpus/python-selection.json")
    files = ["error_handling_continuations.py", "continuation_corpus.py", "error_handling_dispositions.py",
        "error_handling_operations.py", "error_handling_transfers.py", "error_handling_context.py", "error_handling.py",
        "source_facts.py", "source_selection.py", "duplication.py", "async_usage.py", "review_cli.py", "behavior_review_corpus.py"]
    hashes = {name: usage.digest(Path(__file__).with_name(name).read_text(encoding="utf-8")) for name in files}
    summary = {"schemaVersion": 1, "policy": continuation.POLICY, "auditId": str(uuid.uuid4()),
        "implementationFiles": hashes, "implementationSha256": usage.fingerprint(hashes),
        "scope": "Unscored source continuations. Prior facts and disposition classifications preserved; no target code executed.", "samples": []}
    for entry in manifest["samples"]:
        root = args.repos / entry["repository"]
        if git(root, "rev-parse", "HEAD") != entry["revision"] or git(root, "status", "--porcelain"):
            raise RuntimeError("Corpus must be pinned and clean: " + entry["id"])
        old = load(args.baseline / f"{entry['id']}-evidence.json")
        prior = load(args.dispositions / f"{entry['id']}-dispositions.json")
        facts = usage.source_facts.analyze(root, include=entry["include"], production_paths=entry["productionPaths"])
        if facts != old["dimensions"]["codeQuality"]["sourceFacts"]:
            raise RuntimeError("Source facts changed: " + entry["id"])
        run_id = str(uuid.uuid4())
        base = continuation.dispositions.operations.analyze(root, include=entry["include"], production_paths=entry["productionPaths"],
            run_id=run_id, audit_id=summary["auditId"])
        if stable(base) != stable(old["dimensions"]["errorHandling"]["operationReview"]):
            raise RuntimeError("Prior exception evidence changed: " + entry["id"])
        before = usage.fingerprint([facts, base])
        report = continuation.review(root, facts, base)
        if report["status"] != "complete":
            raise RuntimeError(str(report["diagnostics"]))
        if report["dispositionRowsSha256"] != usage.fingerprint(prior["handlers"]):
            raise RuntimeError("Original disposition rows changed: " + entry["id"])
        if before != usage.fingerprint([facts, base]):
            raise RuntimeError("Continuation review mutated inputs.")
        if git(root, "rev-parse", "HEAD") != entry["revision"] or git(root, "status", "--porcelain"):
            raise RuntimeError("Corpus changed during review.")
        write(args.output / f"{entry['id']}.json", report)
        changed = [row for row in report["handlers"] if row["originalClassification"] != row["classification"]]
        defaults = [row for row in report["handlers"] if row["continuation"]["retainedDefault"]]
        result = {"id": entry["id"], "revision": entry["revision"], "runId": run_id,
            "sourceFactsSha256": usage.fingerprint(facts), "priorDispositionRowsSha256": report["dispositionRowsSha256"],
            "continuationRowsSha256": usage.fingerprint(report["handlers"]), "summary": report["summary"],
            "unchangedSourceFactsExceptionEvidenceAndDispositionRows": True, "changes": changed, "defaultExamples": defaults,
            "remainingCandidates": [{key: row[key] for key in ("handlerId", "file", "line", "owner", "classification", "continuation")}
                for row in report["handlers"] if row["classification"] == "unexplained-swallowing-candidate"]}
        summary["samples"].append(result)
        print(entry["id"], json.dumps(report["summary"]), flush=True)
    write(args.output / "summary.json", summary)


if __name__ == "__main__":
    main()
